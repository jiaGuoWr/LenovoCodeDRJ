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
using System.Windows.Threading;
using VSIXProject1.Localization;

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

        // 延迟分析定时器 - 用于保存后防抖
        private DispatcherTimer _analysisDelayTimer;
        // 保存后触发分析使用更保守延迟，优先保证编辑器流畅性，尤其是大文件保存场景
        private const int ANALYSIS_DELAY_MS = 2500;
        private bool _isLanguageChangedSubscribed;
        private List<Diagnostic> _latestDiagnostics = new List<Diagnostic>();

        public SummaryWindowControl()
        {
            InitializeComponent();

            // 初始化延迟分析定时器
            _analysisDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ANALYSIS_DELAY_MS)
            };
            _analysisDelayTimer.Tick += (s, e) =>
            {
                _analysisDelayTimer.Stop();
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            };
        }

        public void Initialize(AsyncPackage package)
        {
            if (_package != package)
            {
                _package = package;
                LocalizationService.Initialize(package);
                InitializeEventHandlers();
                SubscribeLanguageChanged();
                // 注册双击事件已在 XAML 中绑定到 DiagnosticsTree_MouseDoubleClick
            }
        }

        private void SubscribeLanguageChanged()
        {
            if (_isLanguageChangedSubscribed)
            {
                return;
            }

            LocalizationService.LanguageChanged += OnLanguageChanged;
            _isLanguageChangedSubscribed = true;
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                GetParentToolWindow()?.UpdateGitStatus();
                await UpdateAnalysisResultsAsync(_latestDiagnostics);
            });
        }

        private string GetLineLabel(int line)
        {
            return LocalizationService.CurrentLanguage == SupportedLanguage.English
                ? $"[Line {line}]"
                : $"[行 {line}]";
        }

        private string GetUnknownFileText()
        {
            return LocalizationService.CurrentLanguage == SupportedLanguage.English
                ? "Unknown file"
                : "未知文件";
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
            private static readonly TimeSpan SAVE_THROTTLE_INTERVAL = TimeSpan.FromSeconds(5); // 增加到5秒

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
                // 文档保存后使用延迟定时器触发刷新，避免保存卡顿
                // 如果在延迟期间再次保存，定时器会重置，确保只在用户停止输入后分析
                var now = DateTime.Now;
                if (now - _lastSaveTime > SAVE_THROTTLE_INTERVAL)
                {
                    _lastSaveTime = now;
                    // 使用延迟定时器而不是立即触发，避免保存卡顿
                    _control.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _control._analysisDelayTimer.Stop();
                        _control._analysisDelayTimer.Start();
                    }));
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

        // 去重使用的比较器 - 使用已定义的 DiagnosticEqualityComparer
        private static readonly DiagnosticEqualityComparer _diagnosticComparer = new DiagnosticEqualityComparer();

        // 每批处理的诊断数量（基础值，实际使用动态计算）
        private const int BATCH_SIZE = 50;

        /// <summary>
        /// 获取文件分组节点的画刷 - 使用 VS 主题颜色（强调色）
        /// </summary>
        private Brush GetFileGroupBrush()
        {
            // 尝试使用 VS 主题中的强调色，如果不存在则使用默认颜色
            try
            {
                // 使用 ToolWindowText 颜色，但稍微调整亮度作为强调
                var brush = Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey;
                var resource = System.Windows.Application.Current.TryFindResource(brush);
                if (resource is SolidColorBrush solidColorBrush)
                {
                    // 基于当前文本颜色创建一个稍微更亮的版本作为强调
                    var color = solidColorBrush.Color;
                    // 增加亮度（适用于暗色和亮色主题）
                    byte r = (byte)Math.Min(255, color.R + (color.R < 128 ? 40 : -20));
                    byte g = (byte)Math.Min(255, color.G + (color.G < 128 ? 40 : -20));
                    byte b = (byte)Math.Min(255, color.B + (color.B < 128 ? 40 : -20));
                    return new SolidColorBrush(Color.FromArgb(color.A, r, g, b));
                }
            }
            catch { }
            // 回退到默认颜色
            return SystemColors.ControlTextBrush;
        }

        /// <summary>
        /// 获取诊断项的画刷 - 使用 VS 主题普通文本颜色
        /// </summary>
        private Brush GetDiagnosticItemBrush()
        {
            try
            {
                // 使用 ToolWindowText 颜色
                var brush = Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey;
                var resource = System.Windows.Application.Current.TryFindResource(brush);
                if (resource is SolidColorBrush solidColorBrush)
                {
                    return new SolidColorBrush(solidColorBrush.Color);
                }
            }
            catch { }
            // 回退到默认黑色/白色
            return SystemColors.ControlTextBrush;
        }

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
            _latestDiagnostics = diagnostics?.ToList() ?? new List<Diagnostic>();

            if (diagnostics == null)
            {
                Debug.WriteLine("UpdateAnalysisResults: diagnostics 为空");
                return;
            }

            // 使用 DiagnosticEqualityComparer 去重 - 确保不同位置的问题都能显示
            var uniqueDiagnostics = diagnostics.Distinct(_diagnosticComparer).ToList();

            // 动态计算批次大小
            int batchSize = CalculateBatchSize(uniqueDiagnostics.Count);
            Debug.WriteLine($"动态批次大小: {batchSize} (总诊断数: {uniqueDiagnostics.Count})");

            Debug.WriteLine($"UpdateAnalysisResults: 去重后诊断数量={uniqueDiagnostics.Count}");

            if (uniqueDiagnostics.Count == 0)
            {
                return;
            }

            // 显示所有诊断（TreeView 已配置虚拟化，可高效处理大量数据）
            var diagnosticsToShow = uniqueDiagnostics;

            // 按文件路径分组
            var groupedDiagnostics = diagnosticsToShow
                .GroupBy(d =>
                {
                    try
                    {
                        var path = d.Location?.GetLineSpan().Path;
                        return string.IsNullOrEmpty(path) ? GetUnknownFileText() : path;
                    }
                    catch
                    {
                        return GetUnknownFileText();
                    }
                })
                .OrderBy(g => g.Key)
                .ToList();

            // 分批处理，避免UI阻塞
            var rootNodes = new List<TreeViewItem>();
            int totalProcessed = 0;

            foreach (var group in groupedDiagnostics)
            {
                // 创建文件分组节点 - 使用 TextBlock 以便设置颜色
                var fileHeader = new TextBlock
                {
                    Text = $"{group.Key} ({group.Count()})",
                    FontWeight = FontWeights.Bold,
                    Foreground = GetFileGroupBrush()
                };

                var fileNode = new TreeViewItem
                {
                    Header = fileHeader,
                    IsExpanded = true // 默认展开，方便查看所有诊断
                };

                foreach (var diagnostic in group)
                {
                    try
                    {
                        var lineSpan = diagnostic.Location.GetLineSpan();
                        int line = lineSpan.StartLinePosition.Line + 1;
                        string lineLabel = GetLineLabel(line);
                        string localizedMessage = DiagnosticMessageLocalizer.GetDisplayMessage(diagnostic);

                        // 创建带颜色区分的诊断项文本
                        var itemText = new TextBlock
                        {
                            Text = $"{lineLabel} {diagnostic.Id}: {localizedMessage}",
                            Foreground = GetDiagnosticItemBrush()
                        };

                        var itemNode = new TreeViewItem
                        {
                            Header = itemText,
                            Tag = diagnostic,
                            IsExpanded = false
                        };
                        fileNode.Items.Add(itemNode);
                        _diagnosticMap[itemText.Text] = diagnostic;
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
        }

        // 更新 Git 状态显示
        public void UpdateGitStatus(bool isGitRepository, string branch, int changedFilesCount)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // 更新 Git 状态
                GitStatusText.Text = isGitRepository
                    ? LocalizationService.GetString("Status_GitConnected")
                    : LocalizationService.GetString("Status_GitNotDetected");
                BranchText.Text = isGitRepository && !string.IsNullOrEmpty(branch)
                    ? LocalizationService.GetString("Status_Branch", branch)
                    : LocalizationService.GetString("Status_BranchNone");
                ChangedFilesText.Text = LocalizationService.GetString("Status_Changes", isGitRepository ? changedFilesCount : 0);

                // 更新分析模式
                var toolWindow = GetParentToolWindow();
                if (toolWindow != null)
                {
                    AnalysisModeText.Text = toolWindow.IsIncrementalAnalysisMode
                        ? LocalizationService.GetString("Status_ModeIncremental")
                        : LocalizationService.GetString("Status_ModeFull");
                }
                else
                {
                    AnalysisModeText.Text = LocalizationService.GetString("Status_ModeNone");
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

        public void ShowLanguageSelectionDialog()
        {
            try
            {
                if (_package == null)
                {
                    return;
                }
                var dialog = new LanguageSelectDialog(LocalizationService.CurrentLanguage)
                {
                    Owner = Window.GetWindow(this)
                };
                bool? result = dialog.ShowDialog();
                if (result == true)
                {
                    LocalizationService.SetLanguage(dialog.SelectedLanguage, _package);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowLanguageSelectionDialog 错误: {ex.Message}");
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
