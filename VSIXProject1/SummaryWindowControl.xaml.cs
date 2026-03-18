using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.PlatformUI;
using System.Diagnostics;

namespace VSIXProject1
{
    /// <summary>
    /// SummaryWindowControl.xaml 的交互逻辑
    /// </summary>
    public partial class SummaryWindowControl : UserControl
    {
        private IVsSolution _solution;
        private AsyncPackage _package;

        public event EventHandler RefreshRequested;

        public SummaryWindowControl()
        {
            InitializeComponent();
        }

        public void Initialize(AsyncPackage package)
        {
            if (_package != package)
            {
                _package = package;
                InitializeEventHandlers();
                // 注册双击事件已在 XAML 中绑定到 DiagnosticsTree_MouseDoubleClick
            }
        }

        private void InitializeEventHandlers()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 获取解决方案服务
            _solution = _package.GetService<SVsSolution, IVsSolution>();
            if (_solution != null)
            {
                // 注册解决方案事件
                _solution.AdviseSolutionEvents(new SolutionEventsHandler(this), out _);
            }

            // 获取运行文档表服务，用于监听文档变更
            var runningDocumentTable = _package.GetService<SVsRunningDocumentTable, IVsRunningDocumentTable>();
            if (runningDocumentTable != null)
            {
                // 使用正确的方法注册文档变更事件
                var rdtEvents = new RunningDocumentTableEventsHandler(this);
                runningDocumentTable.AdviseRunningDocTableEvents(rdtEvents, out _);
            }
        }

        // 运行文档表事件处理程序，用于监听文档变更
        private class RunningDocumentTableEventsHandler : IVsRunningDocTableEvents
        {
            private SummaryWindowControl _control;
            private static DateTime _lastSaveTime = DateTime.MinValue;
            private static readonly TimeSpan SAVE_THROTTLE_INTERVAL = TimeSpan.FromSeconds(3);

            public RunningDocumentTableEventsHandler(SummaryWindowControl control)
            {
                _control = control;
            }

            public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterSave(uint docCookie)
            {
                // 文档保存后触发刷新，但限制频率
                var now = DateTime.Now;
                if (now - _lastSaveTime > SAVE_THROTTLE_INTERVAL)
                {
                    _lastSaveTime = now;
                    _control.RefreshRequested?.Invoke(_control, EventArgs.Empty);
                }
                return VSConstants.S_OK;
            }

            public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
            {
                // 移除编辑解锁时的刷新，避免过于频繁的分析
                // 只在保存时触发分析
                return VSConstants.S_OK;
            }
        }

        // 存储诊断信息的字典，用于双击导航
        private Dictionary<string, Diagnostic> _diagnosticMap = new Dictionary<string, Diagnostic>();

        // 最大显示的诊断数量，避免UI卡顿
        private const int MAX_DISPLAY_DIAGNOSTICS = 500;
        // 每批处理的诊断数量（基础值，实际使用动态计算）
        private const int BATCH_SIZE = 50;

        // 动态批次大小计算参数
        private const int MIN_BATCH_SIZE = 20;
        private const int MAX_BATCH_SIZE = 200;

        /// <summary>
        /// 根据诊断总数动态计算批次大小
        /// </summary>
        private int CalculateBatchSize(int totalDiagnostics)
        {
            if (totalDiagnostics <= 100)
                return MIN_BATCH_SIZE;  // 小数据量，减少让出次数

            if (totalDiagnostics <= 500)
                return BATCH_SIZE;  // 默认批次大小

            if (totalDiagnostics <= 1000)
                return 100;  // 中等数据量，增加批次

            return MAX_BATCH_SIZE;  // 大数据量，最大批次
        }

        // 诊断消息缓存，避免重复生成
        private readonly Dictionary<string, string> _messageCache = new Dictionary<string, string>();

        /// <summary>
        /// 使用 StringBuilder 获取诊断消息（带缓存）
        /// </summary>
        private string GetDiagnosticMessage(Diagnostic diagnostic)
        {
            string key = diagnostic.Id + ":" + diagnostic.GetMessage();

            if (!_messageCache.TryGetValue(key, out string message))
            {
                var sb = new System.Text.StringBuilder(256);
                sb.Append(diagnostic.Id);
                sb.Append(": ");
                sb.Append(diagnostic.GetMessage());
                message = sb.ToString();
                _messageCache[key] = message;
            }

            return message;
        }

        public async Task UpdateAnalysisResultsAsync(IEnumerable<Diagnostic> diagnostics)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            Debug.WriteLine("开始更新分析结果");
            DiagnosticsTree.Items.Clear();
            _diagnosticMap.Clear();

            if (diagnostics == null)
            {
                Debug.WriteLine("UpdateAnalysisResults: diagnostics 为空");
                DiagnosticsTree.Items.Add(new TreeViewItem { Header = "未检测到代码问题" });
                return;
            }

            // 使用 HashSet 去重
            var uniqueMessages = new HashSet<string>();
            var uniqueDiagnostics = new List<Diagnostic>();

            foreach (var diagnostic in diagnostics)
            {
                try
                {
                    string message = GetDiagnosticMessage(diagnostic);
                    if (uniqueMessages.Add(message))
                    {
                        uniqueDiagnostics.Add(diagnostic);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"处理诊断时出错: {ex.Message}");
                }
            }

            // 动态计算批次大小
            int batchSize = CalculateBatchSize(uniqueDiagnostics.Count);
            Debug.WriteLine($"动态批次大小: {batchSize} (总诊断数: {uniqueDiagnostics.Count})");

            Debug.WriteLine($"UpdateAnalysisResults: 去重后诊断数量={uniqueDiagnostics.Count}");

            if (uniqueDiagnostics.Count == 0)
            {
                DiagnosticsTree.Items.Add(new TreeViewItem { Header = "未检测到代码问题" });
                return;
            }

            // 如果诊断数量过多，显示提示信息
            bool hasMoreDiagnostics = uniqueDiagnostics.Count > MAX_DISPLAY_DIAGNOSTICS;
            var diagnosticsToShow = hasMoreDiagnostics
                ? uniqueDiagnostics.Take(MAX_DISPLAY_DIAGNOSTICS).ToList()
                : uniqueDiagnostics;

            if (hasMoreDiagnostics)
            {
                Debug.WriteLine($"诊断数量超过 {MAX_DISPLAY_DIAGNOSTICS}，仅显示前 {MAX_DISPLAY_DIAGNOSTICS} 个");
            }

            // 按文件路径分组
            var groupedDiagnostics = diagnosticsToShow
                .GroupBy(d =>
                {
                    try
                    {
                        var path = d.Location?.GetLineSpan().Path;
                        return string.IsNullOrEmpty(path) ? "未知文件" : path;
                    }
                    catch
                    {
                        return "未知文件";
                    }
                })
                .OrderBy(g => g.Key)
                .ToList();

            // 分批处理，避免UI阻塞
            var rootNodes = new List<TreeViewItem>();
            int totalProcessed = 0;

            foreach (var group in groupedDiagnostics)
            {
                // 创建文件分组节点
                var fileNode = new TreeViewItem
                {
                    Header = $"{group.Key} ({group.Count()})",
                    IsExpanded = false // 默认不展开，减少初始渲染开销
                };

                foreach (var diagnostic in group)
                {
                    try
                    {
                        var lineSpan = diagnostic.Location.GetLineSpan();
                        int line = lineSpan.StartLinePosition.Line + 1;
                        string message = $"[行 {line}] {diagnostic.Id}: {diagnostic.GetMessage()}";

                        var itemNode = new TreeViewItem
                        {
                            Header = message,
                            Tag = diagnostic
                        };
                        fileNode.Items.Add(itemNode);
                        _diagnosticMap[message] = diagnostic;
                        totalProcessed++;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"处理诊断时出错: {ex.Message}");
                    }
                }

                rootNodes.Add(fileNode);

                // 每处理 batchSize 个诊断，让出时间片给UI线程
                if (totalProcessed % batchSize == 0)
                {
                    await Task.Yield();
                }
            }

            // 批量添加到树控件
            foreach (var node in rootNodes)
            {
                DiagnosticsTree.Items.Add(node);
            }

            // 如果有更多诊断，添加提示节点
            if (hasMoreDiagnostics)
            {
                var moreNode = new TreeViewItem
                {
                    Header = $"... 还有 {uniqueDiagnostics.Count - MAX_DISPLAY_DIAGNOSTICS} 个诊断未显示",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    IsEnabled = false
                };
                DiagnosticsTree.Items.Add(moreNode);
            }

            Debug.WriteLine($"UpdateAnalysisResults: 完成更新，{groupedDiagnostics.Count} 个文件分组，共 {_diagnosticMap.Count} 个诊断");
        }

        /// <summary>
        /// 显示分析进度
        /// </summary>
        public async Task ShowProgressAsync(int completedFiles, int totalFiles, string currentFile)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (ProgressBar.Visibility != Visibility.Visible)
            {
                ProgressBar.Visibility = Visibility.Visible;
                ProgressText.Visibility = Visibility.Visible;
                ToggleModeButton.Visibility = Visibility.Collapsed;
            }

            ProgressBar.Maximum = totalFiles;
            ProgressBar.Value = completedFiles;

            string fileName = System.IO.Path.GetFileName(currentFile);
            if (fileName.Length > 30)
            {
                fileName = fileName.Substring(0, 27) + "...";
            }

            ProgressText.Text = $"分析中... ({completedFiles}/{totalFiles}) {fileName}";
        }

        /// <summary>
        /// 隐藏分析进度
        /// </summary>
        public async Task HideProgressAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            ProgressBar.Visibility = Visibility.Collapsed;
            ProgressText.Visibility = Visibility.Collapsed;
            ToggleModeButton.Visibility = Visibility.Visible;
        }

        private void DiagnosticsTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (DiagnosticsTree.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is Diagnostic diagnostic)
            {
                NavigateToErrorLocation(diagnostic);
            }
        }

        private void NavigateToErrorLocation(Diagnostic diagnostic)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var location = diagnostic.Location;
                if (location == null || location.IsInMetadata)
                    return;

                string filePath = location.SourceTree?.FilePath;
                if (string.IsNullOrEmpty(filePath))
                    return;

                var lineSpan = location.GetLineSpan();
                int lineNumber = lineSpan.StartLinePosition.Line + 1;
                int columnNumber = lineSpan.StartLinePosition.Character + 1;

                // 使用 VsShellUtilities 打开文档，避免 DTE COM 的 AccessViolationException
                IVsUIHierarchy hierarchy;
                uint itemId;
                IVsWindowFrame windowFrame;
                Microsoft.VisualStudio.Text.Editor.IWpfTextView textView = null;

                VsShellUtilities.OpenDocument(
                    _package,
                    filePath,
                    Guid.Empty,
                    out hierarchy,
                    out itemId,
                    out windowFrame);

                if (windowFrame != null)
                {
                    windowFrame.Show();

                    // 通过 IVsTextView 导航到指定行
                    var textViewHost = VsShellUtilities.GetTextView(windowFrame);
                    if (textViewHost != null)
                    {
                        textViewHost.SetCaretPos(lineNumber - 1, columnNumber - 1);
                        textViewHost.CenterLines(lineNumber - 1, 1);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导航到错误位置时出错: {ex.Message}");
            }
        }

        public void ClearResults()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DiagnosticsTree.Items.Clear();
            _diagnosticMap.Clear();
            DiagnosticsTree.Items.Add(new TreeViewItem { Header = "未检测到代码问题" });
        }

        // 更新 Git 状态显示
        public void UpdateGitStatus(bool isGitRepository, string branch, int changedFilesCount)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 更新 Git 状态
                GitStatusText.Text = isGitRepository ? "Git: 已连接" : "Git: 未检测到";
                BranchText.Text = isGitRepository && !string.IsNullOrEmpty(branch) ? $"分支: {branch}" : "分支: -";
                ChangedFilesText.Text = isGitRepository ? $"更改: {changedFilesCount}" : "更改: 0";

                // 更新分析模式
                var toolWindow = GetParentToolWindow();
                if (toolWindow != null)
                {
                    AnalysisModeText.Text = toolWindow.IsIncrementalAnalysisMode ? "模式: 增量" : "模式: 全量";
                }
                else
                {
                    AnalysisModeText.Text = "模式: -";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateGitStatus 错误: {ex.Message}");
            }
        }

        // 切换模式按钮点击事件
        private void ToggleModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var toolWindow = GetParentToolWindow();
                if (toolWindow != null)
                {
                    toolWindow.ToggleAnalysisMode();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ToggleModeButton_Click 错误: {ex.Message}");
            }
        }

        // 获取父工具窗口
        private SummaryToolWindow GetParentToolWindow()
        {
            try
            {
                // 通过Package获取工具窗口
                if (_package != null)
                {
                    var window = _package.FindToolWindow(typeof(SummaryToolWindow), 0, false);
                    return window as SummaryToolWindow;
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetParentToolWindow 错误: {ex.Message}");
                return null;
            }
        }

        // 解决方案事件处理程序
        private class SolutionEventsHandler : IVsSolutionEvents
        {
            private SummaryWindowControl _control;
            private static DateTime _lastRefreshTime = DateTime.MinValue;
            private static readonly TimeSpan REFRESH_THROTTLE_INTERVAL = TimeSpan.FromSeconds(5);

            public SolutionEventsHandler(SummaryWindowControl control)
            {
                _control = control;
            }

            private bool ShouldThrottle()
            {
                var now = DateTime.Now;
                if (now - _lastRefreshTime < REFRESH_THROTTLE_INTERVAL)
                {
                    return true;
                }
                _lastRefreshTime = now;
                return false;
            }

            public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
            {
                // 项目打开后刷新，但限制频率
                if (!ShouldThrottle())
                {
                    _control.RefreshRequested?.Invoke(_control, EventArgs.Empty);
                }
                return VSConstants.S_OK;
            }

            public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
            { return VSConstants.S_OK; }

            public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
            { return VSConstants.S_OK; }

            public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
            { return VSConstants.S_OK; }

            public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
            { return VSConstants.S_OK; }

            public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
            { return VSConstants.S_OK; }

            public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
            {
                // 解决方案打开后刷新
                if (!ShouldThrottle())
                {
                    _control.RefreshRequested?.Invoke(_control, EventArgs.Empty);
                }
                return VSConstants.S_OK;
            }

            public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
            { return VSConstants.S_OK; }

            public int OnBeforeCloseSolution(object pUnkReserved)
            { return VSConstants.S_OK; }

            public int OnAfterCloseSolution(object pUnkReserved)
            { return VSConstants.S_OK; }
        }
    }
}
