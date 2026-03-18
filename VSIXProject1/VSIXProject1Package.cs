using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace VSIXProject1
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(VSIXProject1Package.PackageGuidString)]
    [ProvideMenuResource("Menus.ctmenu", 1)] 
    [ProvideToolWindow(typeof(SummaryToolWindow), Style = Microsoft.VisualStudio.Shell.VsDockStyle.Tabbed, Window = Microsoft.VisualStudio.Shell.Interop.ToolWindowGuids.SolutionExplorer)]
    public sealed class VSIXProject1Package : AsyncPackage
    {
        public const string PackageGuidString = "89D89890-7000-4008-8000-000000000001";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await MyToolWindowCommand.InitializeAsync(this);

            // 尝试在包初始化时显示工具窗口，保持其状态
            await RestoreToolWindowStateAsync(cancellationToken);

            // 注册解决方案事件，在解决方案打开后自动执行代码分析
            IVsSolution solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            if (solution != null)
            {
                solution.AdviseSolutionEvents(new SolutionOpenEventHandler(this), out _);
            }
        }

        private async Task RestoreToolWindowStateAsync(CancellationToken cancellationToken)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            try
            {
                // 显示工具窗口但不激活，保持其之前的状态
                // 第三个参数为 false，表示如果窗口不存在则创建，但不强制显示
                ToolWindowPane window = await ShowToolWindowAsync(typeof(SummaryToolWindow), 0, false, cancellationToken);
                if ((null != window) && (null != window.Frame))
                {
                    // 窗口已存在或已创建，不需要额外操作
                    // Visual Studio 会自动恢复其之前的停靠状态
                    // 执行代码分析并更新窗口内容
                    await UpdateToolWindowContentAsync(window);
                }
            }
            catch (Exception ex)
            {
                // 忽略异常，确保包初始化不会失败
                System.Diagnostics.Debug.WriteLine($"恢复工具窗口状态时出错: {ex.Message}");
            }
        }

        private async Task UpdateToolWindowContentAsync(ToolWindowPane window)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            
            // 检查MyToolWindowCommand.Instance是否已经初始化
            if (MyToolWindowCommand.Instance == null)
            {
                return;
            }
            
            // 执行代码分析
            var diagnostics = await MyToolWindowCommand.Instance.AnalyzeSolutionAsync();

            // 更新工具窗口显示
            if (window is SummaryToolWindow summaryWindow)
            {
                await summaryWindow.UpdateAnalysisResultsAsync(diagnostics);
            }
        }

        // 解决方案打开事件处理程序
        private class SolutionOpenEventHandler : IVsSolutionEvents
        {
            private VSIXProject1Package _package;

            public SolutionOpenEventHandler(VSIXProject1Package package)
            {
                _package = package;
            }

            public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
            {
                return VSConstants.S_OK;
            }

            public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
            {
                return VSConstants.S_OK;
            }

            public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
            {
                // 解决方案打开后，延迟执行代码分析，确保所有项目都已加载
                _package.JoinableTaskFactory.RunAsync(async () =>
                {
                    await Task.Delay(1000); // 延迟1秒，确保项目加载完成
                    await ExecuteAnalysisAsync();
                });
                return VSConstants.S_OK;
            }

            public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
            {
                return VSConstants.S_OK;
            }

            public int OnBeforeCloseSolution(object pUnkReserved)
            {
                return VSConstants.S_OK;
            }

            public int OnAfterCloseSolution(object pUnkReserved)
            {
                return VSConstants.S_OK;
            }

            private async Task ExecuteAnalysisAsync()
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                
                // 检查MyToolWindowCommand.Instance是否已经初始化
                if (MyToolWindowCommand.Instance == null)
                {
                    return;
                }
                
                // 获取工具窗口（不强制显示）
                ToolWindowPane window = await _package.ShowToolWindowAsync(typeof(SummaryToolWindow), 0, false, _package.DisposalToken);
                if ((null == window) || (null == window.Frame))
                {
                    return;
                }

                // 执行代码分析
                var diagnostics = await MyToolWindowCommand.Instance.AnalyzeSolutionAsync();

                // 更新工具窗口显示
                if (window is SummaryToolWindow summaryWindow)
                {
                    await summaryWindow.UpdateAnalysisResultsAsync(diagnostics);
                }
            }
        }

        protected override WindowPane CreateToolWindow(Type toolWindowType, int id)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (toolWindowType == typeof(SummaryToolWindow))
            {
                return new SummaryToolWindow(this);
            }

            return base.CreateToolWindow(toolWindowType, id);
        }
    }
}