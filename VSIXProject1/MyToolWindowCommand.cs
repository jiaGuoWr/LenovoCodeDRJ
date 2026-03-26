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
using System.Collections.Concurrent;
using Microsoft.VisualStudio;
using LenovoAnalyzer;
using System.Runtime.InteropServices;
using System.Diagnostics;
using VSIXProject1.Localization;
using Microsoft.VisualStudio.Threading;

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
        public const int LanguageCommandId = 0x0101;
        public const int TopLevelMenuId = 0x1021;
        public static readonly Guid CommandSet = new Guid("89D89890-7000-4008-8000-000000000001");

        private readonly AsyncPackage package;

        private MyToolWindowCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var topMenuCommandID = new CommandID(CommandSet, TopLevelMenuId);
            var topMenuItem = new OleMenuCommand(null, topMenuCommandID);
            topMenuItem.BeforeQueryStatus += (s, e) =>
            {
                if (s is OleMenuCommand cmd)
                {
                    cmd.Text = LocalizationService.GetString("Menu_CodeIssueSummary");
                }
            };
            commandService.AddCommand(topMenuItem);

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new OleMenuCommand(this.Execute, menuCommandID);
            menuItem.BeforeQueryStatus += (s, e) =>
            {
                if (s is OleMenuCommand cmd)
                {
                    cmd.Text = LocalizationService.GetString("Menu_CodeIssueSummary");
                }
            };
            commandService.AddCommand(menuItem);

            var langCommandID = new CommandID(CommandSet, LanguageCommandId);
            var langMenuItem = new OleMenuCommand(this.ExecuteLanguageSettings, langCommandID);
            langMenuItem.BeforeQueryStatus += (s, e) =>
            {
                if (s is OleMenuCommand cmd)
                {
                    cmd.Text = LocalizationService.GetString("Menu_LanguageSettings");
                }
            };
            commandService.AddCommand(langMenuItem);
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
        private const int DEBOUNCE_DELAY_MS = 1200; // 1200毫秒防抖延迟，加速刷新响应
        private readonly SemaphoreSlim _refreshExecutionGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _refreshAnalysisCts;
        private int _pendingRefreshRequests = 0;

        // 最大文件大小限制 (10MB)
        private const long MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024;

        // 分析超时时间（秒）
        private const int ANALYSIS_TIMEOUT_SECONDS = 30;

        // 缓存条目结构 - 使用不可变设计避免并发修改问题
        private class CacheEntry
        {
            public DateTime LastModified { get; }
            public DateTime LastAccessed { get; }
            public SyntaxTree SyntaxTree { get; }

            public CacheEntry(DateTime lastModified, SyntaxTree syntaxTree)
            {
                LastModified = lastModified;
                LastAccessed = DateTime.UtcNow;
                SyntaxTree = syntaxTree;
            }
        }

        // 使用 ConcurrentDictionary 替代 Dictionary + 锁，提高并发性能
        private readonly ConcurrentDictionary<string, CacheEntry> _syntaxTreeCache = new ConcurrentDictionary<string, CacheEntry>();
        private const int MAX_CACHE_SIZE = 100;

        // 缓存过期和清理配置
        private static readonly TimeSpan CACHE_EXPIRATION = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan CACHE_CLEANUP_INTERVAL = TimeSpan.FromMinutes(5);
        private DateTime _lastCacheCleanup = DateTime.MinValue;
        private readonly object _cleanupLock = new object();

        // 分析失败的文件列表 - 用于错误报告
        private readonly ConcurrentBag<string> _failedAnalysisFiles = new ConcurrentBag<string>();
        private int _failedAnalysisCount = 0;

        // 需要排除的目录
        private static readonly HashSet<string> EXCLUDED_DIRECTORIES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", "packages", "testresults", "artifacts"
        };

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            LocalizationService.Initialize(package);
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

        public async Task<SummaryToolWindow> GetSummaryToolWindowAsync(bool createIfNeeded, bool showWindow, CancellationToken cancellationToken)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // 改进窗口查找逻辑：优先查找已存在窗口，处理已关闭但未完全Dispose的情况
            var existingWindow = package.FindToolWindow(typeof(SummaryToolWindow), 0, false) as SummaryToolWindow;
            if (existingWindow != null)
            {
                if (existingWindow.IsDisposed)
                {
                    System.Diagnostics.Debug.WriteLine("GetSummaryToolWindowAsync: 找到已Dispose的窗口，重新创建");
                }
                else
                {
                    if (showWindow && (existingWindow.Frame == null || existingWindow.Frame is IVsWindowFrame frame && frame.IsVisible() == 0))
                    {
                        System.Diagnostics.Debug.WriteLine("GetSummaryToolWindowAsync: 显示现有窗口");
                        return await package.ShowToolWindowAsync(typeof(SummaryToolWindow), 0, true, cancellationToken) as SummaryToolWindow;
                    }

                    if (showWindow && existingWindow.Frame is IVsWindowFrame existingFrame)
                    {
                        ErrorHandler.ThrowOnFailure(existingFrame.Show());
                    }

                    // 确保控件已初始化
                    existingWindow.Control?.Initialize(package);
                    return existingWindow;
                }
            }

            if (!createIfNeeded)
            {
                return null;
            }

            System.Diagnostics.Debug.WriteLine("GetSummaryToolWindowAsync: 创建新的工具窗口");
            var createdWindow = await package.ShowToolWindowAsync(typeof(SummaryToolWindow), 0, true, cancellationToken) as SummaryToolWindow;
            if (createdWindow != null && showWindow && createdWindow.Frame is IVsWindowFrame createdFrame)
            {
                ErrorHandler.ThrowOnFailure(createdFrame.Show());
            }

            return createdWindow;
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine("开始执行命令");
                    
                    // 统一通过窗口解析逻辑获取实例，避免拿到已释放的旧窗口
                    System.Diagnostics.Debug.WriteLine("尝试显示工具窗口");
                    SummaryToolWindow window = await GetSummaryToolWindowAsync(createIfNeeded: true, showWindow: true, package.DisposalToken);
                    
                    System.Diagnostics.Debug.WriteLine($"工具窗口显示结果: {window != null}, 窗口框架: {window?.Frame != null}");
                    
                    if (window == null)
                    {
                        throw new NotSupportedException("无法创建工具窗口");
                    }

                    window.MarkInitialRefreshRequested();

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
                    if (window != null)
                    {
                        System.Diagnostics.Debug.WriteLine("更新工具窗口显示");
                        // 隐藏进度条
                        await window.Control.HideProgressAsync();
                        // 确保控件被初始化
                        window.Control.Initialize(package);
                        await window.UpdateAnalysisResultsAsync(diagnostics);
                        // 注意：此处不再注册刷新事件，由 SummaryToolWindow 内部触发防抖请求
                    }

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

        private void ExecuteLanguageSettings(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var dialog = new LanguageSelectDialog(LocalizationService.CurrentLanguage);
                // We show dialog without explicit owner since we are invoked from menu, or we could resolve main window
                bool? result = dialog.ShowDialog();
                if (result == true)
                {
                    LocalizationService.SetLanguage(dialog.SelectedLanguage, package);
                    
                    // Trigger refresh on active tool window if it exists
                    var existingWindow = package.FindToolWindow(typeof(SummaryToolWindow), 0, false) as SummaryToolWindow;
                    if (existingWindow != null && !existingWindow.IsDisposed)
                    {
                        // The UI texts automatically update due to TranslationProvider Refresh
                        // We also need to refresh Git status which contains dynamic localized text
                        existingWindow.UpdateGitStatus();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExecuteLanguageSettings 出错: {ex.Message}");
            }
        }

        public void RequestAnalysisRefresh(bool immediate = false)
        {
            if (immediate)
            {
                lock (_debounceLock)
                {
                    _debounceTimer?.Dispose();
                    _debounceTimer = null;
                }

                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await QueueDebouncedRefreshAsync();
                });
                return;
            }

            // 使用防抖机制，延迟执行分析
            lock (_debounceLock)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = new System.Threading.Timer(_ =>
                {
                    ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await QueueDebouncedRefreshAsync();
                    });
                }, null, DEBOUNCE_DELAY_MS, Timeout.Infinite);
            }
        }

        private async Task QueueDebouncedRefreshAsync()
        {
            Interlocked.Increment(ref _pendingRefreshRequests);

            if (!await _refreshExecutionGate.WaitAsync(0))
            {
                // 已有刷新任务在执行，当前请求仅标记为 pending，由正在运行的循环合并处理
                return;
            }

            try
            {
                while (Interlocked.Exchange(ref _pendingRefreshRequests, 0) > 0)
                {
                    CancellationTokenSource currentCts = null;
                    lock (_debounceLock)
                    {
                        _refreshAnalysisCts?.Cancel();
                        _refreshAnalysisCts?.Dispose();
                        _refreshAnalysisCts = CancellationTokenSource.CreateLinkedTokenSource(package.DisposalToken);
                        currentCts = _refreshAnalysisCts;
                    }

                    await ExecuteRefreshWithDebounceAsync(currentCts.Token);
                }
            }
            finally
            {
                _refreshExecutionGate.Release();
            }
        }

        private async Task ExecuteRefreshWithDebounceAsync(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 获取当前打开的解决方案
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
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

                // 再次切回后台线程，避免阻塞 UI
                await TaskScheduler.Default;

                // 保存/自动刷新路径默认禁用逐文件进度UI，避免大文件保存后主线程频繁刷新导致卡顿
                var diagnostics = await AnalyzeSolutionAsync(progress: null, cancellationToken: cancellationToken);

                // 调试：打印诊断信息数量和类型
                Debug.WriteLine($"分析完成，共发现 {diagnostics.Count()} 个问题");

                // 更新工具窗口显示（切换到UI线程）
                var summaryWindow = await GetSummaryToolWindowAsync(createIfNeeded: false, showWindow: false, cancellationToken);
                if (summaryWindow != null)
                {
                    // 隐藏进度条
                    await summaryWindow.Control.HideProgressAsync();
                    // 确保控件被初始化
                    summaryWindow.Control.Initialize(package);
                    await summaryWindow.UpdateAnalysisResultsAsync(diagnostics);
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("ExecuteRefreshWithDebounceAsync: 刷新分析已取消（收到更新请求或窗口关闭）");
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
            
            // 必须在UI线程上收集所有需要分析的文件路径，避免在后台线程调用 COM 对象
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            IVsSolution solution = package.GetService<SVsSolution, IVsSolution>();
            if (solution == null)
            {
                System.Diagnostics.Debug.WriteLine("AnalyzeSolutionAsync: solution 为空");
                return diagnostics;
            }

            string solutionPath = string.Empty;
            solution.GetSolutionInfo(out solutionPath, out _, out _);
            System.Diagnostics.Debug.WriteLine($"AnalyzeSolutionAsync: solutionPath={solutionPath}");

            // 检查 Git 根目录等信息，不需要是COM调用但在此处执行很快
            bool isGitRepository = GitHelper.Instance.IsGitRepository(solutionPath);
            string gitRoot = GitHelper.Instance.GetGitRoot(solutionPath);
            bool useIncrementalAnalysis = IsIncrementalAnalysis;

            List<string> filesToAnalyze = new List<string>();

            if (isGitRepository && !string.IsNullOrEmpty(gitRoot) && _gitEventMonitor == null)
            {
                StartGitEventMonitoring(solutionPath);
            }

            if (useIncrementalAnalysis && isGitRepository && !string.IsNullOrEmpty(gitRoot))
            {
                // 获取更改的文件可能涉及 IO，可以切到后台
                await TaskScheduler.Default;
                var changedFiles = GitHelper.Instance.GetChangedFiles(solutionPath);
                Debug.WriteLine($"AnalyzeSolutionAsync: 增量分析，更改文件数={changedFiles.Count()}");
                if (changedFiles.Any())
                {
                    filesToAnalyze = changedFiles.Where(f => !ShouldExcludeDirectory(f)).ToList();
                }
                else
                {
                    return Enumerable.Empty<Diagnostic>();
                }
            }
            else
            {
                // 全量分析模式：必须在UI线程上遍历 COM 项目层次结构
                System.Diagnostics.Debug.WriteLine("使用全量分析模式");
                IEnumHierarchies enumHierarchies;
                Guid guid = Guid.Empty;
                solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref guid, out enumHierarchies);
                if (enumHierarchies != null)
                {
                    IVsHierarchy[] hierarchies = new IVsHierarchy[1];
                    uint fetched;
                    while (enumHierarchies.Next(1, hierarchies, out fetched) == 0 && fetched == 1)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string projectPath = string.Empty;
                        hierarchies[0].GetCanonicalName((uint)VSConstants.VSITEMID.Root, out projectPath);
                        if (!string.IsNullOrEmpty(projectPath))
                        {
                            string projectDirectory = Path.GetDirectoryName(projectPath);
                            if (Directory.Exists(projectDirectory))
                            {
                                var csFiles = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
                                    .Where(f => !ShouldExcludeDirectory(f));
                                filesToAnalyze.AddRange(csFiles);
                            }
                        }
                    }
                }
                
                // 去重
                filesToAnalyze = filesToAnalyze.Distinct().ToList();
            }

            // 现在我们有了一个纯字符串列表，安全切换到真正的后台线程执行繁重的分析任务
            return await Task.Run(() =>
            {
                int completedFiles = 0;
                int totalFiles = filesToAnalyze.Count;
                
                if (totalFiles == 0) return diagnostics;
                
                progress?.Report(new AnalysisProgress { CompletedFiles = 0, TotalFiles = totalFiles, CurrentFile = "准备分析..." });

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
                    CancellationToken = cancellationToken
                };

                var fileDiagnosticsList = new List<Diagnostic>[totalFiles];
                var progressLock = new object();

                var tasks = new Task[totalFiles];
                Parallel.For(0, totalFiles, options, i =>
                {
                    int index = i; // capture loop variable
                    tasks[index] = Task.Run(async () =>
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            lock (progressLock)
                            {
                                progress?.Report(new AnalysisProgress
                                {
                                    CompletedFiles = completedFiles,
                                    TotalFiles = totalFiles,
                                    CurrentFile = filesToAnalyze[index]
                                });
                            }

                            var fileDiagnostics = await AnalyzeFileAsync(filesToAnalyze[index], cancellationToken);
                            fileDiagnosticsList[index] = fileDiagnostics.ToList();

                            Interlocked.Increment(ref completedFiles);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"分析文件失败 {filesToAnalyze[index]}: {ex.Message}");
                            fileDiagnosticsList[index] = new List<Diagnostic>();
                            Interlocked.Increment(ref completedFiles);
                        }
                    }, cancellationToken);
                });
                
                // Wait for all async file analysis tasks to complete
                Task.WaitAll(tasks);

                foreach (var fileDiagnostics in fileDiagnosticsList)
                {
                    if (fileDiagnostics != null)
                    {
                        diagnostics.AddRange(fileDiagnostics);
                    }
                }

                progress?.Report(new AnalysisProgress { CompletedFiles = totalFiles, TotalFiles = totalFiles, CurrentFile = "分析完成", IsCompleted = true });

                var uniqueDiagnostics = diagnostics.Distinct(new DiagnosticEqualityComparer()).ToList();

                return uniqueDiagnostics;
            }, cancellationToken);
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

        /// <summary>
        /// 同步分析文件 - 避免在 Parallel.For 中使用 .GetAwaiter().GetResult()
        /// </summary>
        private async Task<IEnumerable<Diagnostic>> AnalyzeFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var diagnostics = new List<Diagnostic>();

            // 创建超时取消令牌源
            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(ANALYSIS_TIMEOUT_SECONDS)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                var linkedToken = linkedCts.Token;

                try
                {
                    // 定期清理缓存
                    CleanupCache();

                    if (!File.Exists(filePath))
                    {
                        Debug.WriteLine($"AnalyzeFile: 文件不存在: {filePath}");
                        return diagnostics;
                    }

                    // 检查文件大小，跳过大文件
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                    {
                        Debug.WriteLine($"AnalyzeFile: 文件过大，跳过分析: {filePath} ({fileInfo.Length / 1024 / 1024}MB)");
                        return diagnostics;
                    }

                    // 检查是否需要排除的目录
                    if (ShouldExcludeDirectory(filePath))
                    {
                        return diagnostics;
                    }

                    // 检查缓存 - 使用 ConcurrentDictionary
                    DateTime currentLastModified = fileInfo.LastWriteTimeUtc;
                    SyntaxTree syntaxTree = null;

                    if (_syntaxTreeCache.TryGetValue(filePath, out var cached) && cached.LastModified == currentLastModified)
                    {
                        syntaxTree = cached.SyntaxTree;
                        // 使用新的缓存条目更新访问时间（原子操作）
                        _syntaxTreeCache[filePath] = new CacheEntry(currentLastModified, syntaxTree);
                        Debug.WriteLine($"AnalyzeFile: 使用缓存的语法树: {filePath}");
                    }

                    // 如果没有缓存，解析文件
                    if (syntaxTree == null)
                    {
                        linkedToken.ThrowIfCancellationRequested();

                        // 同步读取文件
                        string code = await Task.Run(() => File.ReadAllText(filePath), linkedToken);
                        if (string.IsNullOrWhiteSpace(code))
                        {
                            return diagnostics;
                        }

                        linkedToken.ThrowIfCancellationRequested();

                        syntaxTree = CSharpSyntaxTree.ParseText(code, path: filePath, cancellationToken: linkedToken);

                        // 更新缓存 - 使用 ConcurrentDictionary
                        EnforceCacheSizeLimit();
                        _syntaxTreeCache[filePath] = new CacheEntry(currentLastModified, syntaxTree);
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
                        // 使用同步方法获取诊断
                        var fileDiagnostics = await compilation
                            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer), cancellationToken: linkedToken)
                            .GetAnalyzerDiagnosticsAsync(linkedToken)
                            .ConfigureAwait(false);
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
                    // 记录失败但不抛出，返回已收集的诊断
                    RecordAnalysisFailure(filePath, "分析超时");
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"分析被取消: {filePath}");
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"分析文件时出错: {ex.Message}");
                    // 记录分析失败，供错误报告使用
                    RecordAnalysisFailure(filePath, ex.Message);
                }

                return diagnostics;
            }
        }

        /// <summary>
        /// 记录分析失败的文件
        /// </summary>
        private void RecordAnalysisFailure(string filePath, string errorMessage)
        {
            _failedAnalysisFiles.Add($"{filePath}: {errorMessage}");
            Interlocked.Increment(ref _failedAnalysisCount);
            Debug.WriteLine($"记录分析失败: {filePath} - {errorMessage}");
        }

        /// <summary>
        /// 获取并清空分析失败的文件列表
        /// </summary>
        public IEnumerable<string> GetAndClearFailedAnalysisFiles()
        {
            var failedFiles = _failedAnalysisFiles.ToArray();
            // ConcurrentBag 没有 Clear 方法，使用 TryTake 循环清空
            while (_failedAnalysisFiles.TryTake(out _)) { }
            Interlocked.Exchange(ref _failedAnalysisCount, 0);
            return failedFiles;
        }

        /// <summary>
        /// 获取分析失败计数
        /// </summary>
        public int GetFailedAnalysisCount()
        {
            return _failedAnalysisCount;
        }

        /// <summary>
        /// 强制执行缓存大小限制
        /// </summary>
        private void EnforceCacheSizeLimit()
        {
            // 如果缓存大小超过限制，移除最久未访问的条目
            while (_syntaxTreeCache.Count >= MAX_CACHE_SIZE)
            {
                var oldestKey = _syntaxTreeCache.OrderBy(x => x.Value.LastAccessed).FirstOrDefault().Key;
                if (oldestKey != null)
                {
                    _syntaxTreeCache.TryRemove(oldestKey, out _);
                }
                else
                {
                    break;
                }
            }
        }


        /// <summary>
        /// 清理过期缓存条目 - 使用 ConcurrentDictionary 无需额外锁
        /// </summary>
        private void CleanupCache()
        {
            var now = DateTime.UtcNow;

            // 检查是否需要清理 - 使用独立锁保护最后清理时间
            lock (_cleanupLock)
            {
                if (now - _lastCacheCleanup < CACHE_CLEANUP_INTERVAL)
                    return;

                _lastCacheCleanup = now;
            }

            // 移除过期条目 - ConcurrentDictionary 支持并发遍历
            var expiredKeys = _syntaxTreeCache
                .Where(kvp => now - kvp.Value.LastAccessed > CACHE_EXPIRATION)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                _syntaxTreeCache.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                Debug.WriteLine($"缓存清理完成: 移除了 {expiredKeys.Count} 个过期条目，当前缓存 {_syntaxTreeCache.Count} 项");
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

                    // 自动重新分析（通过防抖触发）
                    RequestAnalysisRefresh();
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
