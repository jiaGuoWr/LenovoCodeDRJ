using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VSIXProject1
{
    public class GitHelper
    {
        private static GitHelper _instance;
        public static GitHelper Instance => _instance ?? (_instance = new GitHelper());

        // 检测是否为 Git 仓库
        public bool IsGitRepository(string solutionPath)
        {
            try
            {
                if (string.IsNullOrEmpty(solutionPath))
                {
                    System.Diagnostics.Debug.WriteLine("IsGitRepository: solutionPath 为空");
                    return false;
                }

                // 路径规范化
                string normalizedPath = Path.GetFullPath(solutionPath);
                string solutionDir = Path.GetDirectoryName(normalizedPath);
                if (string.IsNullOrEmpty(solutionDir))
                {
                    System.Diagnostics.Debug.WriteLine("IsGitRepository: solutionDir 为空");
                    return false;
                }

                // 场景一：检查当前目录
                if (CheckGitDirectory(solutionDir))
                {
                    return true;
                }

                // 场景二：检查父目录
                string parentDir = solutionDir;
                while (!string.IsNullOrEmpty(parentDir))
                {
                    parentDir = Path.GetDirectoryName(parentDir);
                    if (!string.IsNullOrEmpty(parentDir) && CheckGitDirectory(parentDir))
                    {
                        return true;
                    }
                }

                // 场景四：使用 LibGit2Sharp 检测
                if (TryDetectWithLibGit2Sharp(solutionDir))
                {
                    return true;
                }

                // 场景三：检查子目录
                if (CheckGitInSubdirectories(solutionDir))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"IsGitRepository 错误: {ex.Message}");
                return false;
            }
        }

        private bool CheckGitDirectory(string directory)
        {
            try
            {
                string gitDir = Path.Combine(directory, ".git");
                bool exists = Directory.Exists(gitDir);
                System.Diagnostics.Debug.WriteLine($"CheckGitDirectory: {directory}, exists={exists}");
                return exists;
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckGitDirectory: 无权限访问目录 {directory}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckGitDirectory: 异常 {directory}: {ex.Message}");
                return false;
            }
        }

        private bool TryDetectWithLibGit2Sharp(string directory)
        {
            try
            {
                // 使用 SharpGit 检测仓库
                bool isRepo = SharpGitHelper.Instance.IsGitRepository(directory);
                System.Diagnostics.Debug.WriteLine($"TryDetectWithLibGit2Sharp: 使用 SharpGit 检测结果: {isRepo}");
                return isRepo;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryDetectWithLibGit2Sharp: 异常: {ex.Message}");
                // 异常时使用目录检查作为备选
                return CheckGitDirectory(directory);
            }
        }

        private bool CheckGitInSubdirectories(string directory)
        {
            try
            {
                // 使用递归方法，避免一次性枚举所有目录导致的权限问题
                return CheckGitInSubdirectoriesRecursive(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckGitInSubdirectories: 无权限访问目录 {directory}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckGitInSubdirectories: 异常: {ex.Message}");
                return false;
            }
        }

        // 需要排除的目录（性能优化）
        private static readonly HashSet<string> EXCLUDED_DIRECTORIES = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", "node_modules", ".git", "packages", "testresults", "artifacts", ".vs"
        };

        // 最大递归深度限制
        private const int MAX_DIRECTORY_DEPTH = 5;

        private bool CheckGitInSubdirectoriesRecursive(string directory, int currentDepth = 0)
        {
            // 限制递归深度，避免在大型代码库中性能问题
            if (currentDepth >= MAX_DIRECTORY_DEPTH)
            {
                System.Diagnostics.Debug.WriteLine($"CheckGitInSubdirectoriesRecursive: 达到最大深度限制 {MAX_DIRECTORY_DEPTH}, 停止遍历: {directory}");
                return false;
            }

            try
            {
                // 检查当前目录
                if (CheckGitDirectory(directory))
                {
                    return true;
                }

                // 枚举直接子目录
                var subDirectories = Directory.EnumerateDirectories(directory);
                foreach (var subDir in subDirectories)
                {
                    // 跳过排除的目录
                    string dirName = Path.GetFileName(subDir);
                    if (EXCLUDED_DIRECTORIES.Contains(dirName))
                    {
                        continue;
                    }

                    // 递归检查子目录
                    if (CheckGitInSubdirectoriesRecursive(subDir, currentDepth + 1))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckGitInSubdirectoriesRecursive: 无权限访问目录 {directory}: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CheckGitInSubdirectoriesRecursive: 异常 {directory}: {ex.Message}");
                return false;
            }
        }

        // 获取 Git 仓库的根目录
        public string GetGitRoot(string solutionPath)
        {
            try
            {
                if (string.IsNullOrEmpty(solutionPath))
                {
                    System.Diagnostics.Debug.WriteLine("GetGitRoot: solutionPath 为空");
                    return null;
                }

                // 路径规范化
                string normalizedPath = Path.GetFullPath(solutionPath);
                string solutionDir = Path.GetDirectoryName(normalizedPath);
                if (string.IsNullOrEmpty(solutionDir))
                {
                    System.Diagnostics.Debug.WriteLine("GetGitRoot: solutionDir 为空");
                    return null;
                }

                // 场景一：检查当前目录
                string gitRoot = GetGitRootFromDirectory(solutionDir);
                if (!string.IsNullOrEmpty(gitRoot))
                {
                    return gitRoot;
                }

                // 场景二：检查父目录
                string parentDir = solutionDir;
                while (!string.IsNullOrEmpty(parentDir))
                {
                    parentDir = Path.GetDirectoryName(parentDir);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        gitRoot = GetGitRootFromDirectory(parentDir);
                        if (!string.IsNullOrEmpty(gitRoot))
                        {
                            return gitRoot;
                        }
                    }
                }

                // 场景四：使用 LibGit2Sharp 检测
                gitRoot = GetGitRootWithLibGit2Sharp(solutionDir);
                if (!string.IsNullOrEmpty(gitRoot))
                {
                    return gitRoot;
                }

                // 场景三：检查子目录
                gitRoot = GetGitRootFromSubdirectories(solutionDir);
                if (!string.IsNullOrEmpty(gitRoot))
                {
                    return gitRoot;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRoot 错误: {ex.Message}");
                return null;
            }
        }

        private string GetGitRootFromDirectory(string directory)
        {
            try
            {
                string gitDir = Path.Combine(directory, ".git");
                if (Directory.Exists(gitDir))
                {
                    System.Diagnostics.Debug.WriteLine($"GetGitRootFromDirectory: 找到 .git 文件夹: {directory}");
                    return directory;
                }
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootFromDirectory: 无权限访问目录 {directory}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootFromDirectory: 异常 {directory}: {ex.Message}");
                return null;
            }
        }

        private string GetGitRootWithLibGit2Sharp(string directory)
        {
            try
            {
                // 使用 SharpGit 获取 Git 根目录
                string gitRoot = SharpGitHelper.Instance.GetGitRoot(directory);
                if (!string.IsNullOrEmpty(gitRoot))
                {
                    System.Diagnostics.Debug.WriteLine($"GetGitRootWithLibGit2Sharp: 使用 SharpGit 找到仓库: {gitRoot}");
                    return gitRoot;
                }
                
                // 当 SharpGit 不可用时，使用目录检查作为备选
                System.Diagnostics.Debug.WriteLine($"GetGitRootWithLibGit2Sharp: SharpGit 未找到仓库，使用目录检查作为备选");
                return GetGitRootFromDirectory(directory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootWithLibGit2Sharp: 异常: {ex.Message}");
                return null;
            }
        }

        private string GetGitRootFromSubdirectories(string directory)
        {
            try
            {
                // 使用递归方法，避免一次性枚举所有目录导致的权限问题
                return GetGitRootFromSubdirectoriesRecursive(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootFromSubdirectories: 无权限访问目录 {directory}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootFromSubdirectories: 异常: {ex.Message}");
                return null;
            }
        }

        private string GetGitRootFromSubdirectoriesRecursive(string directory, int currentDepth = 0)
        {
            // 限制递归深度，避免在大型代码库中性能问题
            if (currentDepth >= MAX_DIRECTORY_DEPTH)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootFromSubdirectoriesRecursive: 达到最大深度限制 {MAX_DIRECTORY_DEPTH}, 停止遍历: {directory}");
                return null;
            }

            try
            {
                // 检查当前目录
                string gitRoot = GetGitRootFromDirectory(directory);
                if (!string.IsNullOrEmpty(gitRoot))
                {
                    return gitRoot;
                }

                // 枚举直接子目录
                var subDirectories = Directory.EnumerateDirectories(directory);
                foreach (var subDir in subDirectories)
                {
                    // 跳过排除的目录
                    string dirName = Path.GetFileName(subDir);
                    if (EXCLUDED_DIRECTORIES.Contains(dirName))
                    {
                        continue;
                    }

                    // 递归检查子目录
                    gitRoot = GetGitRootFromSubdirectoriesRecursive(subDir, currentDepth + 1);
                    if (!string.IsNullOrEmpty(gitRoot))
                    {
                        return gitRoot;
                    }
                }

                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootFromSubdirectoriesRecursive: 无权限访问目录 {directory}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetGitRootFromSubdirectoriesRecursive: 异常 {directory}: {ex.Message}");
                return null;
            }
        }

        // 获取更改状态的文件列表（使用 SharpGit）
        public IEnumerable<string> GetChangedFiles(string solutionPath)
        {
            try
            {
                string gitRoot = GetGitRoot(solutionPath);
                if (string.IsNullOrEmpty(gitRoot))
                {
                    System.Diagnostics.Debug.WriteLine("GetChangedFiles: 未找到 Git 根目录");
                    return Enumerable.Empty<string>();
                }

                try
                {
                    // 使用 SharpGit 获取更改文件
                    var changedFiles = SharpGitHelper.Instance.GetChangedFiles(gitRoot);
                    System.Diagnostics.Debug.WriteLine($"GetChangedFiles: 更改文件数={changedFiles.Count()}");
                    return changedFiles;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GetChangedFiles: SharpGit 异常，直接返回空列表: {ex.Message}");
                    // SharpGit 异常，直接返回空列表
                    return Enumerable.Empty<string>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetChangedFiles 错误: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        // 获取仓库状态
        public object GetRepositoryStatus(string solutionPath)
        {
            try
            {
                string gitRoot = GetGitRoot(solutionPath);
                if (string.IsNullOrEmpty(gitRoot))
                    return null;

                // 使用 SharpGit 获取仓库状态
                try
                {
                    var status = SharpGitHelper.Instance.GetRepositoryStatus(gitRoot);
                    System.Diagnostics.Debug.WriteLine($"GetRepositoryStatus: 使用 SharpGit 获取状态成功");
                    return status;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GetRepositoryStatus: SharpGit 异常: {ex.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetRepositoryStatus 错误: {ex.Message}");
                return null;
            }
        }

        // 监听 Git 事件（提交、推送、文件修改等）
        public IDisposable MonitorGitEvents(string solutionPath, Action onGitChange)
        {
            try
            {
                string gitRoot = GetGitRoot(solutionPath);
                if (string.IsNullOrEmpty(gitRoot))
                    return null;

                // 创建一个复合的 Disposable，包含多个 FileSystemWatcher
                var disposables = new List<IDisposable>();

                // 1. 监听 Git 的 refs/heads 目录（分支变化）
                string refsPath = Path.Combine(gitRoot, ".git", "refs", "heads");
                try
                {
                    if (Directory.Exists(refsPath))
                    {
                        var refsWatcher = new FileSystemWatcher(refsPath)
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                            EnableRaisingEvents = true
                        };

                        refsWatcher.Changed += (s, e) => onGitChange?.Invoke();
                        refsWatcher.Created += (s, e) => onGitChange?.Invoke();

                        disposables.Add(refsWatcher);
                        System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 已添加 refs/heads 目录监听器: {refsPath}");
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 无权限访问目录 {refsPath}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 创建 refs/heads 监听器异常: {ex.Message}");
                }

                // 2. 监听 Git 的 index 文件（暂存区变化，用于检测重置操作）
                string indexPath = Path.Combine(gitRoot, ".git", "index");
                try
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 检查 Git index 文件: {indexPath}");
                    string indexDir = Path.GetDirectoryName(indexPath);
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: Git index 目录: {indexDir}");
                    
                    if (Directory.Exists(indexDir))
                    {
                        var indexWatcher = new FileSystemWatcher(indexDir)
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Attributes | NotifyFilters.Security,
                            Filter = "index",
                            IncludeSubdirectories = false,
                            EnableRaisingEvents = true,
                            InternalBufferSize = 8192 // 增加缓冲区大小，减少事件丢失
                        };

                        // 避免频繁触发，使用节流
                        DateTime lastIndexTriggerTime = DateTime.MinValue;
                        TimeSpan indexThrottleInterval = TimeSpan.FromSeconds(1); // 1秒的节流间隔

                        indexWatcher.Changed += (s, e) => 
                        {
                            if (DateTime.Now - lastIndexTriggerTime > indexThrottleInterval)
                            {
                                lastIndexTriggerTime = DateTime.Now;
                                System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 检测到 Git index 文件变化（可能是重置操作）: {e.FullPath}");
                                onGitChange?.Invoke();
                            }
                        };

                        // 也监听Created事件，以防文件被重建
                        indexWatcher.Created += (s, e) => 
                        {
                            if (DateTime.Now - lastIndexTriggerTime > indexThrottleInterval)
                            {
                                lastIndexTriggerTime = DateTime.Now;
                                System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 检测到 Git index 文件创建（可能是重置操作）: {e.FullPath}");
                                onGitChange?.Invoke();
                            }
                        };

                        // 监听Deleted事件，以防文件被删除
                        indexWatcher.Deleted += (s, e) => 
                        {
                            if (DateTime.Now - lastIndexTriggerTime > indexThrottleInterval)
                            {
                                lastIndexTriggerTime = DateTime.Now;
                                System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 检测到 Git index 文件删除: {e.FullPath}");
                                onGitChange?.Invoke();
                            }
                        };

                        // 监听Renamed事件，以防文件被重命名
                        indexWatcher.Renamed += (s, e) => 
                        {
                            if (DateTime.Now - lastIndexTriggerTime > indexThrottleInterval)
                            {
                                lastIndexTriggerTime = DateTime.Now;
                                System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 检测到 Git index 文件重命名: {e.OldFullPath} -> {e.FullPath}");
                                onGitChange?.Invoke();
                            }
                        };

                        disposables.Add(indexWatcher);
                        System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 已添加 Git index 文件监听器: {indexPath}");
                        System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 监听器配置: NotifyFilter={indexWatcher.NotifyFilter}, Filter={indexWatcher.Filter}, IncludeSubdirectories={indexWatcher.IncludeSubdirectories}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: Git index 目录不存在: {indexDir}");
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 无权限访问 Git index 文件: {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 创建 Git index 监听器异常: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 异常堆栈: {ex.StackTrace}");
                }

                // 3. 监听 Git 的 HEAD 文件（HEAD 变化，用于检测 checkout 操作）
                string headPath = Path.Combine(gitRoot, ".git", "HEAD");
                try
                {
                    if (File.Exists(headPath))
                    {
                        var headWatcher = new FileSystemWatcher(Path.GetDirectoryName(headPath))
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                            Filter = "HEAD",
                            EnableRaisingEvents = true
                        };

                        // 避免频繁触发，使用节流
                        DateTime lastHeadTriggerTime = DateTime.MinValue;
                        TimeSpan headThrottleInterval = TimeSpan.FromSeconds(1); // 1秒的节流间隔

                        headWatcher.Changed += (s, e) => 
                        {
                            if (DateTime.Now - lastHeadTriggerTime > headThrottleInterval)
                            {
                                lastHeadTriggerTime = DateTime.Now;
                                System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 检测到 Git HEAD 文件变化（可能是 checkout 操作）: {e.FullPath}");
                                onGitChange?.Invoke();
                            }
                        };

                        disposables.Add(headWatcher);
                        System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 已添加 Git HEAD 文件监听器: {headPath}");
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 无权限访问 Git HEAD 文件: {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 创建 Git HEAD 监听器异常: {ex.Message}");
                }

                // 4. 监听工作目录中的文件变化
                try
                {
                    // 查找解决方案文件所在的目录
                    string solutionDir = Path.GetDirectoryName(solutionPath);
                    if (!string.IsNullOrEmpty(solutionDir) && Directory.Exists(solutionDir))
                    {
                        var workDirWatcher = new FileSystemWatcher(solutionDir)
                        {
                            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size,
                            Filter = "*.cs", // 只监听 C# 文件
                            IncludeSubdirectories = true,
                            EnableRaisingEvents = true
                        };

                        // 避免频繁触发，使用节流
                        DateTime lastTriggerTime = DateTime.MinValue;
                        TimeSpan throttleInterval = TimeSpan.FromSeconds(2); // 增加节流间隔到2秒
                        object triggerLock = new object();

                        // 统一的事件处理方法
                        void HandleFileEvent(string eventType, string filePath)
                        {
                            lock (triggerLock)
                            {
                                if (DateTime.Now - lastTriggerTime > throttleInterval)
                                {
                                    lastTriggerTime = DateTime.Now;
                                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 检测到文件{eventType}: {filePath}");
                                    onGitChange?.Invoke();
                                }
                            }
                        }

                        workDirWatcher.Changed += (s, e) => HandleFileEvent("变化", e.FullPath);
                        workDirWatcher.Created += (s, e) => HandleFileEvent("创建", e.FullPath);
                        workDirWatcher.Deleted += (s, e) => HandleFileEvent("删除", e.FullPath);

                        disposables.Add(workDirWatcher);
                        System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 已添加工作目录监听器: {solutionDir}");
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 无权限访问工作目录 {solutionPath}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"MonitorGitEvents: 创建工作目录监听器异常: {ex.Message}");
                }

                // 如果没有添加任何监听器，返回 null
                if (disposables.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("MonitorGitEvents: 未添加任何监听器");
                    return null;
                }

                // 返回一个复合的 Disposable
                return new CompositeDisposable(disposables);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MonitorGitEvents 错误: {ex.Message}");
                return null;
            }
        }

        // 安全的 FileSystemWatcher 包装类，确保事件正确取消订阅
        private class SafeFileSystemWatcher : IDisposable
        {
            private FileSystemWatcher _watcher;
            private FileSystemEventHandler _changedHandler;
            private FileSystemEventHandler _createdHandler;
            private FileSystemEventHandler _deletedHandler;
            private RenamedEventHandler _renamedHandler;
            private readonly object _lock = new object();
            private bool _disposed;

            public SafeFileSystemWatcher(string path, string filter = "*.*")
            {
                _watcher = new FileSystemWatcher(path, filter)
                {
                    EnableRaisingEvents = false // 先禁用，等事件订阅完成后再启用
                };
            }

            public FileSystemWatcher Watcher => _watcher;

            public void SubscribeChanged(FileSystemEventHandler handler)
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _changedHandler = handler;
                    _watcher.Changed += _changedHandler;
                }
            }

            public void SubscribeCreated(FileSystemEventHandler handler)
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _createdHandler = handler;
                    _watcher.Created += _createdHandler;
                }
            }

            public void SubscribeDeleted(FileSystemEventHandler handler)
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _deletedHandler = handler;
                    _watcher.Deleted += _deletedHandler;
                }
            }

            public void SubscribeRenamed(RenamedEventHandler handler)
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _renamedHandler = handler;
                    _watcher.Renamed += _renamedHandler;
                }
            }

            public void Start()
            {
                lock (_lock)
                {
                    if (!_disposed && _watcher != null)
                    {
                        _watcher.EnableRaisingEvents = true;
                    }
                }
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _disposed = true;

                    if (_watcher != null)
                    {
                        _watcher.EnableRaisingEvents = false;

                        // 显式取消事件订阅
                        if (_changedHandler != null)
                            _watcher.Changed -= _changedHandler;
                        if (_createdHandler != null)
                            _watcher.Created -= _createdHandler;
                        if (_deletedHandler != null)
                            _watcher.Deleted -= _deletedHandler;
                        if (_renamedHandler != null)
                            _watcher.Renamed -= _renamedHandler;

                        _watcher.Dispose();
                        _watcher = null;
                    }

                    _changedHandler = null;
                    _createdHandler = null;
                    _deletedHandler = null;
                    _renamedHandler = null;
                }
            }
        }

        // 复合的 Disposable 类，用于管理多个 IDisposable
        private class CompositeDisposable : IDisposable
        {
            private readonly List<IDisposable> _disposables;
            private readonly object _lock = new object();
            private bool _disposed;

            public CompositeDisposable(List<IDisposable> disposables)
            {
                _disposables = disposables ?? new List<IDisposable>();
            }

            public void Dispose()
            {
                lock (_lock)
                {
                    if (_disposed) return;
                    _disposed = true;
                }

                foreach (var disposable in _disposables)
                {
                    try
                    {
                        disposable?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"CompositeDisposable: 清理资源异常: {ex.Message}");
                    }
                }
                _disposables.Clear();
            }
        }

        // 获取当前分支名称
        public string GetCurrentBranch(string solutionPath)
        {
            try
            {
                string gitRoot = GetGitRoot(solutionPath);
                if (string.IsNullOrEmpty(gitRoot))
                    return null;

                // 使用 SharpGit 获取当前分支
                try
                {
                    string branchName = SharpGitHelper.Instance.GetCurrentBranch(gitRoot);
                    System.Diagnostics.Debug.WriteLine($"GetCurrentBranch: 使用 SharpGit 获取分支成功: {branchName}");
                    return branchName;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"GetCurrentBranch: SharpGit 异常: {ex.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetCurrentBranch 错误: {ex.Message}");
                return null;
            }
        }

        // 获取相对路径（替代 Path.GetRelativePath，兼容 .NET Framework 4.8）
        private string GetRelativePath(string basePath, string targetPath)
        {
            Uri baseUri = new Uri(basePath + Path.DirectorySeparatorChar);
            Uri targetUri = new Uri(targetPath);
            return baseUri.MakeRelativeUri(targetUri).ToString().Replace('/', Path.DirectorySeparatorChar);
        }

        // 基于文件系统的更改文件检测（备选方案）
        private IEnumerable<string> GetChangedFilesUsingFileSystem(string gitRoot)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"GetChangedFilesUsingFileSystem: 开始扫描 {gitRoot}");
                var changedFiles = new List<string>();
                
                // 定义要忽略的目录
                var ignoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {".git", "bin", "obj", "packages", "node_modules"};
                
                // 遍历 Git 根目录下的所有 .cs 文件
                foreach (var csFile in Directory.EnumerateFiles(gitRoot, "*.cs", SearchOption.AllDirectories))
                {
                    // 检查文件是否在忽略的目录中
                    string relativePath = GetRelativePath(gitRoot, csFile);
                    string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);
                    
                    bool isInIgnoredDirectory = false;
                    foreach (var part in pathParts)
                    {
                        if (ignoredDirectories.Contains(part))
                        {
                            isInIgnoredDirectory = true;
                            break;
                        }
                    }
                    
                    if (isInIgnoredDirectory)
                    {
                        System.Diagnostics.Debug.WriteLine($"GetChangedFilesUsingFileSystem: 忽略文件: {csFile}");
                        continue;
                    }
                    
                    // 检查文件的最后修改时间（最近 5 秒内修改的文件视为更改）
                    // 进一步缩短时间窗口，确保还原的文件不会被错误识别为更改文件
                    DateTime lastWriteTime = File.GetLastWriteTime(csFile);
                    TimeSpan timeSinceLastWrite = DateTime.Now - lastWriteTime;
                    
                    if (timeSinceLastWrite.TotalSeconds <= 5)
                    {
                        changedFiles.Add(csFile);
                        System.Diagnostics.Debug.WriteLine($"GetChangedFilesUsingFileSystem: 添加更改文件: {csFile} (修改时间: {lastWriteTime}, 时间差: {timeSinceLastWrite.TotalSeconds}秒)");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"GetChangedFilesUsingFileSystem: 文件未更改: {csFile} (修改时间: {lastWriteTime}, 时间差: {timeSinceLastWrite.TotalSeconds}秒)");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"GetChangedFilesUsingFileSystem: 更改文件数={changedFiles.Count}");
                return changedFiles;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetChangedFilesUsingFileSystem 错误: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        // 辅助类，用于释放资源
        private class DisposableAction : IDisposable
        {
            private readonly Action _action;
            public DisposableAction(Action action) => _action = action;
            public void Dispose() => _action?.Invoke();
        }
    }
}
