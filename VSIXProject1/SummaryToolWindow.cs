using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using VSIXProject1.Localization;
using System.Windows;

namespace VSIXProject1
{
    public class SummaryToolWindow : ToolWindowPane, IDisposable
    {
        public SummaryWindowControl Control {
            get { return (SummaryWindowControl)this.Content; }
        }

        public event EventHandler RefreshRequested;

        // 保存 AsyncPackage 实例
        private AsyncPackage _package;

        // Git 仓库状态
        public bool IsGitRepository { get; private set; }
        public string CurrentBranch { get; private set; }
        public int ChangedFilesCount { get; private set; }

        // 事件订阅状态跟踪
        private bool _isInstanceInitializedSubscribed = false;
        private bool _isGitRepositoryChangedSubscribed = false;
        private bool _isDisposed = false;
        private bool _isControlLifecycleSubscribed = false;
        private bool _isRefreshHandlerSubscribed = false;
        private bool _hasRequestedInitialRefresh = false;

        public bool IsDisposed => _isDisposed;

        public void MarkInitialRefreshRequested()
        {
            _hasRequestedInitialRefresh = true;
        }

        public SummaryToolWindow() : base(null)
        {
            this.Caption = LocalizationService.GetString("ToolWindow_Title");
            this.Content = new SummaryWindowControl();
            Control.RefreshRequested += Control_RefreshRequested;
            // 注册 MyToolWindowCommand 实例初始化完成事件
            MyToolWindowCommand.InstanceInitialized += MyToolWindowCommand_InstanceInitialized;
            _isInstanceInitializedSubscribed = true;
            LocalizationService.LanguageChanged += OnLanguageChanged;
        }

        public SummaryToolWindow(AsyncPackage package) : this()
        {
            _package = package;
            // 初始化控件
            Control.Initialize(package);
        }

        // 当窗口框架初始化时调用
        public override void OnToolWindowCreated()
        {
            base.OnToolWindowCreated();
            System.Diagnostics.Debug.WriteLine("OnToolWindowCreated 被调用");

            // 重置首次加载标志，确保重启后能显示数据
            if (MyToolWindowCommand.Instance != null)
            {
                // 通过反射或公共方法重置，但由于私有，这里通过触发全量分析实现
                System.Diagnostics.Debug.WriteLine("OnToolWindowCreated: 窗口已恢复，准备执行分析");
            }

            // 确保控件初始化，即使是在窗口自动恢复的情况下
            AsyncPackage package = _package ?? (Package as AsyncPackage);
            if (package != null)
            {
                System.Diagnostics.Debug.WriteLine("OnToolWindowCreated: 初始化控件");
                Control.Initialize(package);

                // 确保事件订阅只发生一次
                if (!_isControlLifecycleSubscribed)
                {
                    Control.Loaded += Control_Loaded;
                    Control.Unloaded += Control_Unloaded;
                    _isControlLifecycleSubscribed = true;
                }

                if (!_isRefreshHandlerSubscribed)
                {
                    RefreshRequested += SummaryToolWindow_RefreshRequested;
                    _isRefreshHandlerSubscribed = true;
                }

                // 强制刷新Git状态和分析结果
                UpdateGitStatus();
                if (MyToolWindowCommand.Instance != null && !_hasRequestedInitialRefresh)
                {
                    _hasRequestedInitialRefresh = true;
                    CheckAndExecuteAnalysis(immediate: true);
                }
            }
        }

        private void Control_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            UpdateGitStatus();
            if (!_hasRequestedInitialRefresh && MyToolWindowCommand.Instance != null)
            {
                _hasRequestedInitialRefresh = true;
                CheckAndExecuteAnalysis(immediate: true);
            }
        }

        private void Control_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Removed hotkey unregister logic
        }

        // RefreshRequested 事件处理程序
        private void SummaryToolWindow_RefreshRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("SummaryToolWindow_RefreshRequested 被调用");
            ExecuteCodeAnalysis();
        }

        private void ExecuteCodeAnalysis(bool immediate = false)
        {
            System.Diagnostics.Debug.WriteLine("ExecuteCodeAnalysis: 通过防抖请求执行代码分析");
            MyToolWindowCommand.Instance?.RequestAnalysisRefresh(immediate);
        }

        private void Control_RefreshRequested(object sender, EventArgs e)
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        // 检查并执行代码分析
        private void CheckAndExecuteAnalysis(bool immediate = false)
        {
            System.Diagnostics.Debug.WriteLine("CheckAndExecuteAnalysis 被调用");
            if (MyToolWindowCommand.Instance != null)
            {
                // 如果 MyToolWindowCommand.Instance 已经初始化，直接执行代码分析
                ExecuteCodeAnalysis(immediate);
            }
            else
            {
                // 如果 MyToolWindowCommand.Instance 尚未初始化，等待 InstanceInitialized 事件
                System.Diagnostics.Debug.WriteLine("CheckAndExecuteAnalysis: 等待 MyToolWindowCommand.Instance 初始化");
            }
        }

        // MyToolWindowCommand 实例初始化完成事件处理程序
        private void MyToolWindowCommand_InstanceInitialized(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MyToolWindowCommand_InstanceInitialized 被调用");
            // 注册 Git 仓库变化事件
            if (MyToolWindowCommand.Instance != null && !_isGitRepositoryChangedSubscribed)
            {
                MyToolWindowCommand.Instance.GitRepositoryChanged += OnGitRepositoryChanged;
                _isGitRepositoryChangedSubscribed = true;
                System.Diagnostics.Debug.WriteLine("SummaryToolWindow: 已订阅 GitRepositoryChanged 事件");
            }

            if (Control.IsLoaded)
            {
                UpdateGitStatus();
                if (!_hasRequestedInitialRefresh)
                {
                    _hasRequestedInitialRefresh = true;
                    ExecuteCodeAnalysis(immediate: true);
                }
            }
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            if (_isDisposed)
            {
                return;
            }

            this.Caption = LocalizationService.GetString("ToolWindow_Title");
        }

        // Git 仓库变化事件处理程序
        private void OnGitRepositoryChanged(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("OnGitRepositoryChanged 被调用");
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (!_isDisposed)
                {
                    UpdateGitStatus();
                }
            });
        }

        // 更新 Git 状态
        public void UpdateGitStatus()
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                AsyncPackage package = _package ?? (Package as AsyncPackage);
                if (package == null)
                    return;

                // 获取当前解决方案路径
                var solution = package.GetService<Microsoft.VisualStudio.Shell.Interop.SVsSolution, Microsoft.VisualStudio.Shell.Interop.IVsSolution>();
                if (solution == null)
                    return;

                string solutionPath = string.Empty;
                solution.GetSolutionInfo(out solutionPath, out _, out _);

                if (string.IsNullOrEmpty(solutionPath))
                    return;

                // 检查 Git 仓库状态
                IsGitRepository = GitHelper.Instance.IsGitRepository(solutionPath);
                CurrentBranch = GitHelper.Instance.GetCurrentBranch(solutionPath);
                ChangedFilesCount = GitHelper.Instance.GetChangedFiles(solutionPath).Count();

                System.Diagnostics.Debug.WriteLine($"Git 状态更新: IsGitRepository={IsGitRepository}, Branch={CurrentBranch}, ChangedFiles={ChangedFilesCount}");

                // 更新控件显示
                if (Control != null)
                {
                    Control.UpdateGitStatus(IsGitRepository, CurrentBranch, ChangedFilesCount);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateGitStatus 错误: {ex.Message}");
            }
        }

        // 切换分析模式
        public void ToggleAnalysisMode()
        {
            if (MyToolWindowCommand.Instance != null)
            {
                bool newMode = !MyToolWindowCommand.Instance.IsIncrementalAnalysis;
                MyToolWindowCommand.Instance.IsIncrementalAnalysis = newMode;
                System.Diagnostics.Debug.WriteLine($"分析模式切换为: {(newMode ? "增量" : "全量")}");

                // 重新执行分析（强制刷新）
                ExecuteCodeAnalysis(immediate: true);

                // 更新 Git 状态和UI
                UpdateGitStatus();
            }
        }

        // 获取当前分析模式
        public bool IsIncrementalAnalysisMode
        {
            get { return MyToolWindowCommand.Instance?.IsIncrementalAnalysis ?? false; }
        }



        public async Task UpdateAnalysisResultsAsync(IEnumerable<Diagnostic> diagnostics)
        {
            if (_isDisposed)
            {
                return;
            }

            if (Control != null)
            {
                // 确保控件被初始化
                AsyncPackage package = _package ?? (Package as AsyncPackage);
                Control.Initialize(package);
                await Control.UpdateAnalysisResultsAsync(diagnostics);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("SummaryToolWindow.UpdateAnalysisResults: Control 为空");
            }
        }

        public void ClearResults()
        {
            if (_isDisposed)
            {
                return;
            }

            if (Control != null)
            {
                Control.ClearResults();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                _isDisposed = true;

                System.Diagnostics.Debug.WriteLine("SummaryToolWindow: Dispose 被调用");

                // 重置状态，以便窗口重新打开时能正确初始化
                _hasRequestedInitialRefresh = false;

                // 取消订阅 InstanceInitialized 静态事件
                if (_isInstanceInitializedSubscribed)
                {
                    MyToolWindowCommand.InstanceInitialized -= MyToolWindowCommand_InstanceInitialized;
                    _isInstanceInitializedSubscribed = false;
                    System.Diagnostics.Debug.WriteLine("SummaryToolWindow: 已取消订阅 InstanceInitialized 事件");
                }

                LocalizationService.LanguageChanged -= OnLanguageChanged;

                // 取消订阅 GitRepositoryChanged 事件
                if (_isGitRepositoryChangedSubscribed && MyToolWindowCommand.Instance != null)
                {
                    MyToolWindowCommand.Instance.GitRepositoryChanged -= OnGitRepositoryChanged;
                    _isGitRepositoryChangedSubscribed = false;
                    System.Diagnostics.Debug.WriteLine("SummaryToolWindow: 已取消订阅 GitRepositoryChanged 事件");
                }

                // 取消订阅 Control 的事件
                if (Control != null)
                {
                    Control.RefreshRequested -= Control_RefreshRequested;
                    if (_isControlLifecycleSubscribed)
                    {
                        Control.Loaded -= Control_Loaded;
                        Control.Unloaded -= Control_Unloaded;
                        _isControlLifecycleSubscribed = false;
                    }

                    if (Control is IDisposable disposableControl)
                    {
                        disposableControl.Dispose();
                    }
                }

                // 取消订阅 RefreshRequested 事件
                if (_isRefreshHandlerSubscribed)
                {
                    RefreshRequested -= SummaryToolWindow_RefreshRequested;
                    _isRefreshHandlerSubscribed = false;
                }

                System.Diagnostics.Debug.WriteLine("SummaryToolWindow: Dispose 完成");
            }

            base.Dispose(disposing);
        }
    }
}
