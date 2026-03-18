using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Threading;

namespace VSIXProject1
{
    public class SummaryToolWindow : ToolWindowPane
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

        public SummaryToolWindow() : base(null)
        {
            this.Caption = "代码问题汇总";
            this.Content = new SummaryWindowControl();
            Control.RefreshRequested += Control_RefreshRequested;
            // 注册 MyToolWindowCommand 实例初始化完成事件
            MyToolWindowCommand.InstanceInitialized += MyToolWindowCommand_InstanceInitialized;
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
                // 注册 RefreshRequested 事件的处理器
                RefreshRequested += SummaryToolWindow_RefreshRequested;
                // 窗口显示时检查并执行代码分析，确保显示最新结果
                CheckAndExecuteAnalysis();
            }
        }

        // RefreshRequested 事件处理程序
        private void SummaryToolWindow_RefreshRequested(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("SummaryToolWindow_RefreshRequested 被调用");
            ExecuteCodeAnalysis();
        }

        private void ExecuteCodeAnalysis()
        {
            AsyncPackage package = _package ?? (Package as AsyncPackage);
            System.Diagnostics.Debug.WriteLine($"ExecuteCodeAnalysis 开始执行，package: {package != null}");
            if (package != null)
            {
                System.Diagnostics.Debug.WriteLine($"ExecuteCodeAnalysis: MyToolWindowCommand.Instance: {MyToolWindowCommand.Instance != null}");
                package.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        // 检查MyToolWindowCommand.Instance是否已经初始化
                        if (MyToolWindowCommand.Instance != null)
                        {
                            System.Diagnostics.Debug.WriteLine("ExecuteCodeAnalysis: 开始执行代码分析");
                            // 执行代码分析
                            var diagnostics = await MyToolWindowCommand.Instance.AnalyzeSolutionAsync();
                            System.Diagnostics.Debug.WriteLine($"ExecuteCodeAnalysis: 代码分析完成，发现 {diagnostics.Count()} 个问题");
                            // 更新工具窗口显示
                            await UpdateAnalysisResultsAsync(diagnostics);
                            System.Diagnostics.Debug.WriteLine("ExecuteCodeAnalysis: 工具窗口显示更新完成");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("ExecuteCodeAnalysis: MyToolWindowCommand.Instance 为 null");
                        }
                    }
                    catch (Exception ex)
                    {
                        // 处理异常
                        System.Diagnostics.Debug.WriteLine($"执行代码分析时出错: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"异常堆栈: {ex.StackTrace}");
                    }
                });
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ExecuteCodeAnalysis: package 为 null");
            }
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
            if (MyToolWindowCommand.Instance != null)
            {
                MyToolWindowCommand.Instance.GitRepositoryChanged += OnGitRepositoryChanged;
            }
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
    }
}
