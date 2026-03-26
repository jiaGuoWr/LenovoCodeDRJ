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
        private const int HotKeyId = 0x2301;
        private const int WmHotKey = 0x0312;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint VkL = 0x4C;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
        private HwndSource _hwndSource;
        private bool _isHotKeyRegistered;

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
            // 确保控件初始化，即使是在窗口自动恢复的情况下
            AsyncPackage package = _package ?? (Package as AsyncPackage);
            if (package != null)
            {
                System.Diagnostics.Debug.WriteLine("OnToolWindowCreated: 初始化控件");
                Control.Initialize(package);
                Control.Loaded += Control_Loaded;
                Control.Unloaded += Control_Unloaded;
                // 注册 RefreshRequested 事件的处理器
                RefreshRequested += SummaryToolWindow_RefreshRequested;
                // 窗口显示时检查并执行代码分析，确保显示最新结果
                CheckAndExecuteAnalysis();
            }
        }

        private void Control_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            EnsureHotKeyRegistered();
        }

        private void Control_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            UnregisterSystemHotKey();
        }

        private void EnsureHotKeyRegistered()
        {
            try
            {
                if (_isHotKeyRegistered)
                {
                    return;
                }

                _hwndSource = PresentationSource.FromVisual(Control) as HwndSource;
                if (_hwndSource == null || _hwndSource.Handle == IntPtr.Zero)
                {
                    return;
                }

                _hwndSource.AddHook(WndProc);
                _isHotKeyRegistered = RegisterHotKey(_hwndSource.Handle, HotKeyId, ModControl | ModAlt, VkL);
                System.Diagnostics.Debug.WriteLine($"SummaryToolWindow: RegisterHotKey result={_isHotKeyRegistered}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EnsureHotKeyRegistered 错误: {ex.Message}");
            }
        }

        private void UnregisterSystemHotKey()
        {
            try
            {
                if (_hwndSource != null)
                {
                    if (_isHotKeyRegistered)
                    {
                        UnregisterHotKey(_hwndSource.Handle, HotKeyId);
                        _isHotKeyRegistered = false;
                    }
                    _hwndSource.RemoveHook(WndProc);
                    _hwndSource = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UnregisterSystemHotKey 错误: {ex.Message}");
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotKey && wParam.ToInt32() == HotKeyId)
            {
                handled = true;
                Control.ShowLanguageSelectionDialog();
            }

            return IntPtr.Zero;
        }

        // RefreshRequested 事件处理程序
        private void SummaryToolWindow_RefreshRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("SummaryToolWindow_RefreshRequested 被调用");
            ExecuteCodeAnalysis();
        }

        private void ExecuteCodeAnalysis()
        {
            System.Diagnostics.Debug.WriteLine("ExecuteCodeAnalysis: 通过防抖请求执行代码分析");
            MyToolWindowCommand.Instance?.RequestAnalysisRefresh();
        }

        private void Control_RefreshRequested(object sender, EventArgs e)
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        // 检查并执行代码分析
        private void CheckAndExecuteAnalysis()
        {
            System.Diagnostics.Debug.WriteLine("CheckAndExecuteAnalysis 被调用");
            if (MyToolWindowCommand.Instance != null)
            {
                // 如果 MyToolWindowCommand.Instance 已经初始化，直接执行代码分析
                ExecuteCodeAnalysis();
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
            // 当 MyToolWindowCommand.Instance 初始化完成后，执行代码分析
            ExecuteCodeAnalysis();

            // 注册 Git 仓库变化事件
            if (MyToolWindowCommand.Instance != null && !_isGitRepositoryChangedSubscribed)
            {
                MyToolWindowCommand.Instance.GitRepositoryChanged += OnGitRepositoryChanged;
                _isGitRepositoryChangedSubscribed = true;
                System.Diagnostics.Debug.WriteLine("SummaryToolWindow: 已订阅 GitRepositoryChanged 事件");
            }
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            this.Caption = LocalizationService.GetString("ToolWindow_Title");
        }

        // Git 仓库变化事件处理程序
        private void OnGitRepositoryChanged(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("OnGitRepositoryChanged 被调用");
            UpdateGitStatus();
        }

        // 更新 Git 状态
        public void UpdateGitStatus()
        {
            try
            {
                AsyncPackage package = _package ?? (Package as AsyncPackage);
                if (package == null)
                    return;

                // 异步获取 Git 状态，避免在后台线程调用 COM 对象
                package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        // 获取当前解决方案路径 (必须在 UI 线程)
                        var solution = package.GetService<Microsoft.VisualStudio.Shell.Interop.SVsSolution, Microsoft.VisualStudio.Shell.Interop.IVsSolution>();
                        if (solution == null)
                            return;

                        string solutionPath = string.Empty;
                        solution.GetSolutionInfo(out solutionPath, out _, out _);

                        if (string.IsNullOrEmpty(solutionPath))
                            return;

                        // 切换到后台执行耗时的 Git 检查
                        await TaskScheduler.Default;

                        bool isGitRepo = GitHelper.Instance.IsGitRepository(solutionPath);
                        string branch = GitHelper.Instance.GetCurrentBranch(solutionPath);
                        int changedCount = GitHelper.Instance.GetChangedFiles(solutionPath).Count();

                        // 切换回 UI 线程更新控件
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        IsGitRepository = isGitRepo;
                        CurrentBranch = branch;
                        ChangedFilesCount = changedCount;

                        System.Diagnostics.Debug.WriteLine($"Git 状态更新: IsGitRepository={IsGitRepository}, Branch={CurrentBranch}, ChangedFiles={ChangedFilesCount}");

                        // 更新控件显示
                        if (Control != null)
                        {
                            Control.UpdateGitStatus(IsGitRepository, CurrentBranch, ChangedFilesCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateGitStatus 异步执行错误: {ex.Message}");
                    }
                });
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
                MyToolWindowCommand.Instance.IsIncrementalAnalysis = !MyToolWindowCommand.Instance.IsIncrementalAnalysis;
                System.Diagnostics.Debug.WriteLine($"分析模式切换为: {(MyToolWindowCommand.Instance.IsIncrementalAnalysis ? "增量" : "全量")}");
                
                // 重新执行分析
                ExecuteCodeAnalysis();
                
                // 更新 Git 状态
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
            if (Control != null)
            {
                Control.ClearResults();
            }
        }

        // 实现 IDisposable 以正确释放资源
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            System.Diagnostics.Debug.WriteLine("SummaryToolWindow: Dispose 被调用");

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
                Control.Loaded -= Control_Loaded;
                Control.Unloaded -= Control_Unloaded;
            }

            UnregisterSystemHotKey();

            // 取消订阅 RefreshRequested 事件
            RefreshRequested -= SummaryToolWindow_RefreshRequested;

            System.Diagnostics.Debug.WriteLine("SummaryToolWindow: Dispose 完成");
        }
    }
}
