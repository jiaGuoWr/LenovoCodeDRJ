using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.Immutable;
using Microsoft.VisualStudio;
using LenovoAnalyzer;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace VSIXProject1
{
    // 简单的对象池实现
    public class ObjectPool<T> where T : class
    {
        private readonly Queue<T> _pool = new Queue<T>();
        private readonly Func<T> _factory;
        private readonly int _maxSize;
        private readonly object _lock = new object();

        public ObjectPool(Func<T> factory, int maxSize)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _maxSize = maxSize;
        }

        public T Get()
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    return _pool.Dequeue();
                }
            }
            return _factory();
        }

        public void Return(T obj)
        {
            if (obj == null) return;

            lock (_lock)
            {
                if (_pool.Count < _maxSize)
                {
                    _pool.Enqueue(obj);
                }
            }
        }
    }

    // 用于诊断信息去重的比较器
    public class DiagnosticEqualityComparer : IEqualityComparer<Diagnostic>
    {
        public bool Equals(Diagnostic x, Diagnostic y)
        {
            if (x == null || y == null)
                return x == y;
            
            // 比较诊断ID、消息和位置文件路径
            return x.Id == y.Id && 
                   x.GetMessage() == y.GetMessage() && 
                   x.Location.GetLineSpan().Path == y.Location.GetLineSpan().Path &&
                   x.Location.GetLineSpan().StartLinePosition.Line == y.Location.GetLineSpan().StartLinePosition.Line;
        }
        
        public int GetHashCode(Diagnostic obj)
        {
            if (obj == null)
                return 0;
            
            // 基于诊断ID、消息和位置文件路径生成哈希码
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (obj.Id?.GetHashCode() ?? 0);
                hash = hash * 23 + (obj.GetMessage()?.GetHashCode() ?? 0);
                hash = hash * 23 + (obj.Location.GetLineSpan().Path?.GetHashCode() ?? 0);
                hash = hash * 23 + obj.Location.GetLineSpan().StartLinePosition.Line.GetHashCode();
                return hash;
            }
        }
    }
    
    internal sealed class MyToolWindowCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("89D89890-7000-4008-8000-000000000001");

        private readonly AsyncPackage package;

        private MyToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(new Guid("89D89890-7000-4008-8000-000000000001"), 0x0100);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static MyToolWindowCommand Instance { get; private set; }

        // 实例初始化完成事件，用于通知工具窗口执行代码分析
        public static event EventHandler InstanceInitialized;
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => this.package;

        // 分析模式
        private bool _isIncrementalAnalysis = true;
        public bool IsIncrementalAnalysis 
        {
            get 
            { 
                System.Diagnostics.Debug.WriteLine($"Get IsIncrementalAnalysis: {_isIncrementalAnalysis}");
                return _isIncrementalAnalysis; 
            }
            set 
            { 
                System.Diagnostics.Debug.WriteLine($"Set IsIncrementalAnalysis: {value}");
                _isIncrementalAnalysis = value; 
            }
        }

        // Git 事件监听器
        private IDisposable _gitEventMonitor;

        // Git 仓库状态变化事件
        public event EventHandler GitRepositoryChanged;

        // 防抖定时器，用于延迟执行分析
        private System.Threading.Timer _debounceTimer;
        private readonly object _debounceLock = new object();
        private const int DEBOUNCE_DELAY_MS = 1000; // 1秒防抖延迟

        // 缓存已分析文件的语法树，避免重复解析
        private readonly Dictionary<string, (DateTime lastModified, SyntaxTree syntaxTree)> _syntaxTreeCache = new Dictionary<string, (DateTime, SyntaxTree)>();
        private readonly object _cacheLock = new object();
        private const int MAX_CACHE_SIZE = 100;

        // 最大文件大小限制 (10MB)
        private const long MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024;

        // 分析超时时间（秒）
        private const int ANALYSIS_TIMEOUT_SECONDS = 30;

        // 缓存配置
        private static readonly TimeSpan CACHE_EXPIRATION = TimeSpan.FromMinutes(30);  // 30分钟过期
        private static readonly TimeSpan CACHE_CLEANUP_INTERVAL = TimeSpan.FromMinutes(5);  // 每5分钟清理
        private DateTime _lastCacheCleanup = DateTime.MinValue;

        // 缓存条目结构
        private class CacheEntry
        {
            public DateTime LastModified { get; set; }
            public DateTime LastAccessed { get; set; }
            public SyntaxTree SyntaxTree { get; set; }
        }

        // 增强的语法树缓存
        private readonly Dictionary<string, CacheEntry> _syntaxTreeCacheNew = new Dictionary<string, CacheEntry>();

        // 需要排除的目录
        private static readonly HashSet<string> EXCLUDED_DIRECTORIES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", "packages", "testresults", "artifacts"
        };

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new MyToolWindowCommand(package, commandService);
            
            // 初始化时启动 Git 事件监听
            Instance.InitializeGitEventMonitoring();
            
            // 触发实例初始化完成事件
            InstanceInitialized?.Invoke(null, EventArgs.Empty);
        }
        
        // 初始化 Git 事件监听
        private void InitializeGitEventMonitoring()
        {
            try
            {
                // 获取当前打开的解决方案
                IVsSolution solution = package.GetService<SVsSolution, IVsSolution>();
                if (solution == null)
                {
                    System.Diagnostics.Debug.WriteLine("InitializeGitEventMonitoring: solution 为空");
                    return;
                }

                // 获取解决方案路径
                string solutionPath = string.Empty;
                solution.GetSolutionInfo(out solutionPath, out _, out _);
                System.Diagnostics.Debug.WriteLine($"InitializeGitEventMonitoring: solutionPath={solutionPath}");

                // 检查是否为 Git 仓库
                bool isGitRepository = GitHelper.Instance.IsGitRepository(solutionPath);
                System.Diagnostics.Debug.WriteLine($"InitializeGitEventMonitoring: isGitRepository={isGitRepository}");

                // 如果是 Git 仓库，启动事件监听
                if (isGitRepository && _gitEventMonitor == null)
                {
                    StartGitEventMonitoring(solutionPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"InitializeGitEventMonitoring 错误: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("开始执行命令");
                    
                    // 显示工具窗口，第三个参数为true表示窗口关闭后可以重新打开
                    System.Diagnostics.Debug.WriteLine("尝试显示工具窗口");
                    ToolWindowPane window = await package.ShowToolWindowAsync(typeof(SummaryToolWindow), 0, true, package.DisposalToken);
                    
                    System.Diagnostics.Debug.WriteLine($"工具窗口显示结果: {window != null}, 窗口框架: {window?.Frame != null}");
                    
                    if ((null == window) || (null == window.Frame))
                    {
                        throw new NotSupportedException("无法创建工具窗口");
                    }

                    // 执行代码分析
                    System.Diagnostics.Debug.WriteLine("开始执行代码分析");

                    // 创建进度报告器
                    var progress = new Progress<AnalysisProgress>(async p =>
                    {
                        if (window is SummaryToolWindow sw)
                        {
                            if (p.IsCompleted)
                            {
                                await sw.Control.HideProgressAsync();
                            }
                            else
                            {
                                await sw.Control.ShowProgressAsync(p.CompletedFiles, p.TotalFiles, p.CurrentFile);
                            }
                        }
                    });

                    var diagnostics = await AnalyzeSolutionAsync(progress);
                    System.Diagnostics.Debug.WriteLine($"代码分析完成，发现 {diagnostics.Count()} 个问题");

                    // 更新工具窗口显示
                    if (window is SummaryToolWindow summaryWindow)
                    {
                        System.Diagnostics.Debug.WriteLine("更新工具窗口显示");
                        // 隐藏进度条
                        await summaryWindow.Control.HideProgressAsync();
                        // 确保控件被初始化
                        summaryWindow.Control.Initialize(package);
                        await summaryWindow.UpdateAnalysisResultsAsync(diagnostics);
                        // 注册刷新事件
                        summaryWindow.RefreshRequested += SummaryWindow_RefreshRequested;
                    }

                    IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
                    System.Diagnostics.Debug.WriteLine("显示工具窗口");
                    Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
                    System.Diagnostics.Debug.WriteLine("命令执行完成");
                }
                catch (Exception ex)
                {
                    // 处理异常，确保命令执行过程中的错误能够被捕获和处理
                    System.Diagnostics.Debug.WriteLine($"执行命令时出错: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
                    // 显示错误消息给用户
                    System.Windows.MessageBox.Show($"执行命令时出错: {ex.Message}\n\n堆栈跟踪: {ex.StackTrace}", "错误", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            });
        }

        private void SummaryWindow_RefreshRequested(object sender, EventArgs e)
        {
            // 使用防抖机制，延迟执行分析
            lock (_debounceLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new System.Threading.Timer(async _ =>
                {
                    await ExecuteRefreshWithDebounceAsync(sender);
                }, null, DEBOUNCE_DELAY_MS, Timeout.Infinite);
            }
        }

        private async Task ExecuteRefreshWithDebounceAsync(object sender)
        {
            try
            {
                // 获取当前打开的解决方案
                IVsSolution solution = package.GetService<SVsSolution, IVsSolution>();
                if (solution == null)
                {
                    Debug.WriteLine("ExecuteRefreshWithDebounceAsync: solution 为空");
                    return;
                }

                // 获取解决方案路径
                string solutionPath = string.Empty;
                solution.GetSolutionInfo(out solutionPath, out _, out _);
                Debug.WriteLine($"ExecuteRefreshWithDebounceAsync: solutionPath={solutionPath}");

                // 检查是否为 Git 仓库
                bool isGitRepository = GitHelper.Instance.IsGitRepository(solutionPath);
                Debug.WriteLine($"ExecuteRefreshWithDebounceAsync: isGitRepository={isGitRepository}");

                // 如果是 Git 仓库，检查是否有更改文件
                if (isGitRepository)
                {
                    var changedFiles = GitHelper.Instance.GetChangedFiles(solutionPath);
                    Debug.WriteLine($"ExecuteRefreshWithDebounceAsync: 更改文件数={changedFiles.Count()}");

                    // 如果没有更改文件，不执行分析
                    if (!changedFiles.Any())
                    {
                        Debug.WriteLine("ExecuteRefreshWithDebounceAsync: 无更改文件，不执行分析");
                        return;
                    }
                }

                // 创建进度报告器
                var progress = new Progress<AnalysisProgress>(async p =>
                {
                    if (sender is SummaryToolWindow sw)
                    {
                        if (p.IsCompleted)
                        {
                            await sw.Control.HideProgressAsync();
                        }
                        else
                        {
                            await sw.Control.ShowProgressAsync(p.CompletedFiles, p.TotalFiles, p.CurrentFile);
                        }
                    }
                });

                // 执行代码分析
                var diagnostics = await AnalyzeSolutionAsync(progress);

                // 调试：打印诊断信息数量和类型
                Debug.WriteLine($"分析完成，共发现 {diagnostics.Count()} 个问题");

                // 更新工具窗口显示（切换到UI线程）
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (sender is SummaryToolWindow summaryWindow)
                {
                    // 隐藏进度条
                    await summaryWindow.Control.HideProgressAsync();
                    // 确保控件被初始化
                    summaryWindow.Control.Initialize(package);
                    await summaryWindow.UpdateAnalysisResultsAsync(diagnostics);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExecuteRefreshWithDebounceAsync 错误: {ex.Message}");
                Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// 分析进度报告数据
        /// </summary>
        public class AnalysisProgress
        {
            public int CompletedFiles { get; set; }
            public int TotalFiles { get; set; }
            public string CurrentFile { get; set; }
            public bool IsCompleted { get; set; }
        }

        public async Task<IEnumerable<Diagnostic>> AnalyzeSolutionAsync(IProgress<AnalysisProgress> progress = null, CancellationToken cancellationToken = default)
        {
            var diagnostics = new List<Diagnostic>();
            int completedFiles = 0;

            try
            {
                // 获取当前打开的解决方案
                IVsSolution solution = package.GetService<SVsSolution, IVsSolution>();
                if (solution == null)
                {
                    System.Diagnostics.Debug.WriteLine("AnalyzeSolutionAsync: solution 为空");
                    return diagnostics;
                }

                // 获取解决方案路径
                string solutionPath = string.Empty;
                solution.GetSolutionInfo(out solutionPath, out _, out _);
                System.Diagnostics.Debug.WriteLine($"AnalyzeSolutionAsync: solutionPath={solutionPath}");

                // 检查是否为 Git 仓库
                bool isGitRepository = GitHelper.Instance.IsGitRepository(solutionPath);
                System.Diagnostics.Debug.WriteLine($"AnalyzeSolutionAsync: isGitRepository={isGitRepository}");

                // 检查是否找到 Git 根目录
                string gitRoot = GitHelper.Instance.GetGitRoot(solutionPath);
                System.Diagnostics.Debug.WriteLine($"AnalyzeSolutionAsync: gitRoot={gitRoot}");

                // 只在找到 Git 根目录时才启动监控
                if (isGitRepository && !string.IsNullOrEmpty(gitRoot))
                {
                    // 只在监听器未启动时启动
                    if (_gitEventMonitor == null)
                    {
                        // 启动 Git 事件监听
                        StartGitEventMonitoring(solutionPath);
                    }

                    // 获取更改的文件
                    var changedFiles = GitHelper.Instance.GetChangedFiles(solutionPath);
                    System.Diagnostics.Debug.WriteLine($"发现 {changedFiles.Count()} 个更改文件");

                    // 检查是否使用增量分析模式
                    bool useIncrementalAnalysis = IsIncrementalAnalysis;

                    // 有Git仓库的情况
                    if (changedFiles.Any())
                    {
                        // 有更改文件，使用增量分析模式
                        Debug.WriteLine("使用增量分析模式");
                        Debug.WriteLine($"发现 {changedFiles.Count()} 个更改文件");

                        try
                        {
                            // 过滤掉排除目录中的文件
                            var filesToAnalyze = changedFiles
                                .Where(f => !ShouldExcludeDirectory(f))
                                .ToList();

                            Debug.WriteLine($"过滤后待分析文件数: {filesToAnalyze.Count}");
                            int totalFiles = filesToAnalyze.Count;

                            // 报告开始
                            progress?.Report(new AnalysisProgress { CompletedFiles = 0, TotalFiles = totalFiles, CurrentFile = "准备分析..." });

                            // 使用并行处理分析更改的文件
                            var options = new ParallelOptions
                            {
                                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                                CancellationToken = cancellationToken
                            };

                            var fileDiagnosticsList = new List<Diagnostic>[filesToAnalyze.Count];
                            var progressLock = new object();

                            await Task.Run(() =>
                            {
                                Parallel.For(0, filesToAnalyze.Count, options, i =>
                                {
                                    try
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        Debug.WriteLine($"分析文件: {filesToAnalyze[i]}");

                                        // 报告进度
                                        lock (progressLock)
                                        {
                                            progress?.Report(new AnalysisProgress
                                            {
                                                CompletedFiles = completedFiles,
                                                TotalFiles = totalFiles,
                                                CurrentFile = filesToAnalyze[i]
                                            });
                                        }

                                        var fileDiagnostics = AnalyzeFileAsync(filesToAnalyze[i], cancellationToken).GetAwaiter().GetResult();
                                        fileDiagnosticsList[i] = fileDiagnostics.ToList();

                                        Interlocked.Increment(ref completedFiles);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        throw;
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"分析文件失败 {filesToAnalyze[i]}: {ex.Message}");
                                        fileDiagnosticsList[i] = new List<Diagnostic>();
                                        Interlocked.Increment(ref completedFiles);
                                    }
                                });
                            }, cancellationToken);

                            foreach (var fileDiagnostics in fileDiagnosticsList)
                            {
                                if (fileDiagnostics != null)
                                {
                                    diagnostics.AddRange(fileDiagnostics);
                                }
                            }

                            // 报告完成
                            progress?.Report(new AnalysisProgress { CompletedFiles = totalFiles, TotalFiles = totalFiles, CurrentFile = "分析完成", IsCompleted = true });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Git 操作失败: {ex.Message}");
                            Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
                        }
                    }
                    else
                    {
                        // 没有更改文件，返回空列表
                        Debug.WriteLine("无更改文件，返回空诊断列表");
                        return Enumerable.Empty<Diagnostic>();
                    }
                }
                else
                {
                    // 没有找到 Git 根目录，使用全量分析模式
                    System.Diagnostics.Debug.WriteLine("使用全量分析模式");

                    // 遍历解决方案中的所有项目
                    IEnumHierarchies enumHierarchies;
                    Guid guid = Guid.Empty;
                    solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref guid, out enumHierarchies);
                    if (enumHierarchies == null)
                    {
                        System.Diagnostics.Debug.WriteLine("AnalyzeSolutionAsync: enumHierarchies 为空");
                        return diagnostics;
                    }

                    IVsHierarchy[] hierarchies = new IVsHierarchy[1];
                    uint fetched;
                    while (enumHierarchies.Next(1, hierarchies, out fetched) == 0 && fetched == 1)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var projectDiagnostics = await AnalyzeProjectAsync(hierarchies[0], progress, cancellationToken);
                        diagnostics.AddRange(projectDiagnostics);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("AnalyzeSolutionAsync: 分析被取消");
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AnalyzeSolutionAsync 错误: {ex.Message}");
            }

            // 对诊断信息进行去重
            var uniqueDiagnostics = diagnostics.Distinct(new DiagnosticEqualityComparer()).ToList();
            System.Diagnostics.Debug.WriteLine($"AnalyzeSolutionAsync: 去重前诊断数={diagnostics.Count}, 去重后诊断数={uniqueDiagnostics.Count}");
            return uniqueDiagnostics;
        }

        private async Task<IEnumerable<Diagnostic>> AnalyzeProjectAsync(IVsHierarchy hierarchy, IProgress<AnalysisProgress> progress = null, CancellationToken cancellationToken = default)
        {
            var diagnostics = new List<Diagnostic>();
            int completedFiles = 0;

            try
            {
                // 获取项目的完整路径
                string projectPath = string.Empty;
                hierarchy.GetCanonicalName((uint)VSConstants.VSITEMID.Root, out projectPath);
                if (string.IsNullOrEmpty(projectPath))
                {
                    return diagnostics;
                }

                // 分析项目中的所有C#文件
                string projectDirectory = Path.GetDirectoryName(projectPath);
                if (Directory.Exists(projectDirectory))
                {
                    var csFiles = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                        .Where(f => !ShouldExcludeDirectory(f))
                        .ToList();

                    Debug.WriteLine($"AnalyzeProjectAsync: 发现 {csFiles.Count} 个C#文件待分析");
                    int totalFiles = csFiles.Count;

                    // 报告开始
                    progress?.Report(new AnalysisProgress { CompletedFiles = 0, TotalFiles = totalFiles, CurrentFile = "准备分析..." });

                    // 使用并行处理，但限制并发数
                    var options = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                        CancellationToken = cancellationToken
                    };

                    var fileDiagnosticsList = new List<Diagnostic>[csFiles.Count];
                    var progressLock = new object();

                    await Task.Run(() =>
                    {
                        Parallel.For(0, csFiles.Count, options, i =>
                        {
                            try
                            {
                                cancellationToken.ThrowIfCancellationRequested();

                                // 报告进度
                                lock (progressLock)
                                {
                                    progress?.Report(new AnalysisProgress
                                    {
                                        CompletedFiles = completedFiles,
                                        TotalFiles = totalFiles,
                                        CurrentFile = csFiles[i]
                                    });
                                }

                                var fileDiagnostics = AnalyzeFileAsync(csFiles[i], cancellationToken).GetAwaiter().GetResult();
                                fileDiagnosticsList[i] = fileDiagnostics.ToList();

                                Interlocked.Increment(ref completedFiles);
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"分析文件失败 {csFiles[i]}: {ex.Message}");
                                fileDiagnosticsList[i] = new List<Diagnostic>();
                                Interlocked.Increment(ref completedFiles);
                            }
                        });
                    }, cancellationToken);

                    foreach (var fileDiagnostics in fileDiagnosticsList)
                    {
                        if (fileDiagnostics != null)
                        {
                            diagnostics.AddRange(fileDiagnostics);
                        }
                    }

                    // 报告完成
                    progress?.Report(new AnalysisProgress { CompletedFiles = totalFiles, TotalFiles = totalFiles, CurrentFile = "分析完成", IsCompleted = true });
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AnalyzeProjectAsync 错误: {ex.Message}");
            }

            return diagnostics;
        }

        // 共享的编译引用，避免重复创建
        private static readonly Lazy<ImmutableArray<MetadataReference>> _sharedReferences = new Lazy<ImmutableArray<MetadataReference>>(() =>
        {
            var references = new List<MetadataReference>();
            string runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();

            string[] essentialAssemblies = { "mscorlib.dll", "System.dll", "System.Core.dll" };
            foreach (var assembly in essentialAssemblies)
            {
                string path = Path.Combine(runtimeDir, assembly);
                if (File.Exists(path))
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }

            return ImmutableArray.CreateRange(references);
        });

        // 分析器实例池
        private static readonly ObjectPool<LenovoQiraCodeAnalyzerAnalyzer> _analyzerPool = new ObjectPool<LenovoQiraCodeAnalyzerAnalyzer>(
            () => new LenovoQiraCodeAnalyzerAnalyzer(),
            5);

        private async Task<IEnumerable<Diagnostic>> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var diagnostics = new List<Diagnostic>();

            // 创建超时取消令牌源
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ANALYSIS_TIMEOUT_SECONDS));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            var linkedToken = linkedCts.Token;

            try
            {
                // 定期清理缓存
                CleanupCache();

                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"AnalyzeFileAsync: 文件不存在: {filePath}");
                    return diagnostics;
                }

                // 检查文件大小，跳过大文件
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                {
                    Debug.WriteLine($"AnalyzeFileAsync: 文件过大，跳过分析: {filePath} ({fileInfo.Length / 1024 / 1024}MB)");
                    return diagnostics;
                }

                // 检查是否需要排除的目录
                if (ShouldExcludeDirectory(filePath))
                {
                    return diagnostics;
                }

                // 检查缓存
                DateTime currentLastModified = fileInfo.LastWriteTimeUtc;
                SyntaxTree syntaxTree = null;

                lock (_cacheLock)
                {
                    if (_syntaxTreeCacheNew.TryGetValue(filePath, out var cached) && cached.LastModified == currentLastModified)
                    {
                        syntaxTree = cached.SyntaxTree;
                        cached.LastAccessed = DateTime.UtcNow;  // 更新访问时间
                        Debug.WriteLine($"AnalyzeFileAsync: 使用缓存的语法树: {filePath}");
                    }
                }

                // 如果没有缓存，解析文件
                if (syntaxTree == null)
                {
                    linkedToken.ThrowIfCancellationRequested();

                    string code = await ReadFileTextAsync(filePath);
                    if (string.IsNullOrWhiteSpace(code))
                    {
                        return diagnostics;
                    }

                    linkedToken.ThrowIfCancellationRequested();

                    syntaxTree = CSharpSyntaxTree.ParseText(code, path: filePath, cancellationToken: linkedToken);

                    // 更新缓存
                    lock (_cacheLock)
                    {
                        if (_syntaxTreeCacheNew.Count >= MAX_CACHE_SIZE)
                        {
                            // 移除最久未访问的条目
                            var oldest = _syntaxTreeCacheNew.OrderBy(x => x.Value.LastAccessed).First().Key;
                            _syntaxTreeCacheNew.Remove(oldest);
                        }
                        _syntaxTreeCacheNew[filePath] = new CacheEntry
                        {
                            LastModified = currentLastModified,
                            LastAccessed = DateTime.UtcNow,
                            SyntaxTree = syntaxTree
                        };
                    }
                }

                linkedToken.ThrowIfCancellationRequested();

                // 创建编译单元（使用共享引用）
                CSharpCompilation compilation = CSharpCompilation.Create(
                    $"Compilation_{Guid.NewGuid():N}",
                    syntaxTrees: new[] { syntaxTree },
                    references: _sharedReferences.Value);

                // 从对象池获取分析器
                var analyzer = _analyzerPool.Get();
                try
                {
                    var fileDiagnostics = await compilation
                        .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer), cancellationToken: linkedToken)
                        .GetAnalyzerDiagnosticsAsync(linkedToken);
                    diagnostics.AddRange(fileDiagnostics);
                }
                finally
                {
                    _analyzerPool.Return(analyzer);
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                Debug.WriteLine($"分析文件超时: {filePath}");
                // 返回已收集的诊断（可能为空）
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"分析被取消: {filePath}");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"分析文件时出错: {ex.Message}");
            }

            return diagnostics;
        }

        /// <summary>
        /// 清理过期缓存条目
        /// </summary>
        private void CleanupCache()
        {
            lock (_cacheLock)
            {
                var now = DateTime.UtcNow;

                // 检查是否需要清理
                if (now - _lastCacheCleanup < CACHE_CLEANUP_INTERVAL)
                    return;

                _lastCacheCleanup = now;

                // 移除过期条目
                var expiredKeys = _syntaxTreeCacheNew
                    .Where(kvp => now - kvp.Value.LastAccessed > CACHE_EXPIRATION)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _syntaxTreeCacheNew.Remove(key);
                }

                Debug.WriteLine($"缓存清理完成: 移除了 {expiredKeys.Count} 个过期条目，当前缓存 {_syntaxTreeCacheNew.Count} 项");
            }
        }

        private bool ShouldExcludeDirectory(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(directory))
                return false;

            var parts = directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Any(part => EXCLUDED_DIRECTORIES.Contains(part));
        }

        private async Task<string> ReadFileTextAsync(string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true))
            using (var reader = new StreamReader(stream))
            {
                return await reader.ReadToEndAsync();
            }
        }

        // 启动 Git 事件监听
        private void StartGitEventMonitoring(string solutionPath)
        {
            try
            {
                // 停止之前的监听器
                if (_gitEventMonitor != null)
                {
                    _gitEventMonitor.Dispose();
                    _gitEventMonitor = null;
                }

                // 启动新的监听器
                _gitEventMonitor = GitHelper.Instance.MonitorGitEvents(solutionPath, () =>
                {
                    System.Diagnostics.Debug.WriteLine("Git 仓库状态变化，触发分析");
                    
                    // 触发 Git 仓库变化事件
                    GitRepositoryChanged?.Invoke(this, EventArgs.Empty);

                    // 自动重新分析
                    package.JoinableTaskFactory.RunAsync(async () =>
                    {
                        try
                        {
                            // 尝试获取已存在的工具窗口
                            var window = package.FindToolWindow(typeof(SummaryToolWindow), 0, false);
                            
                            if (window is SummaryToolWindow summaryWindow)
                            {
                                // 创建进度报告器
                                var progress = new Progress<AnalysisProgress>(async p =>
                                {
                                    if (p.IsCompleted)
                                    {
                                        await summaryWindow.Control.HideProgressAsync();
                                    }
                                    else
                                    {
                                        await summaryWindow.Control.ShowProgressAsync(p.CompletedFiles, p.TotalFiles, p.CurrentFile);
                                    }
                                });

                                // 执行分析
                                var diagnostics = await AnalyzeSolutionAsync(progress);
                                System.Diagnostics.Debug.WriteLine($"分析完成，诊断数量: {diagnostics.Count()}");

                                // 确保在UI线程上更新
                                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                                // 隐藏进度条
                                await summaryWindow.Control.HideProgressAsync();

                                if (diagnostics.Count() == 0)
                                {
                                    System.Diagnostics.Debug.WriteLine("Git 事件触发，分析结果为空，清空UI");
                                    summaryWindow.ClearResults();
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Git 事件触发，分析结果不为空，更新UI");
                                    await summaryWindow.UpdateAnalysisResultsAsync(diagnostics);
                                }
                                System.Diagnostics.Debug.WriteLine("UI更新完成");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine("工具窗口未打开，不更新");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"文件变化时自动分析错误: {ex.Message}");
                            System.Diagnostics.Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
                        }
                    });
                });

                System.Diagnostics.Debug.WriteLine("Git 事件监听已启动");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartGitEventMonitoring 错误: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
            }
        }
    }
}
