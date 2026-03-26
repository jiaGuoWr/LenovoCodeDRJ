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

            // 注册解决方案事件，在解决方案打开后自动执行代码分析
            IVsSolution solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            if (solution != null)
            {
                solution.AdviseSolutionEvents(new SolutionOpenEventHandler(this), out _);
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

                // 改进：总是尝试获取或创建窗口，确保重启后能显示数据
                var window = await MyToolWindowCommand.Instance.GetSummaryToolWindowAsync(
                    createIfNeeded: true, showWindow: false, _package.DisposalToken) as SummaryToolWindow;

                if (window == null || window.IsDisposed)
                {
                    System.Diagnostics.Debug.WriteLine("ExecuteAnalysisAsync: 无法获取有效窗口");
                    return;
                }

                window.UpdateGitStatus();
                // 强制首次全量分析
                MyToolWindowCommand.Instance.RequestAnalysisRefresh(immediate: true);
                System.Diagnostics.Debug.WriteLine("ExecuteAnalysisAsync: 已触发解决方案打开后的分析");
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