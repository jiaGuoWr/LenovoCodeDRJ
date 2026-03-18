using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VSIXProject1
{
    public class SharpGitHelper
    {
        private static SharpGitHelper _instance;
        public static SharpGitHelper Instance => _instance ?? (_instance = new SharpGitHelper());

        // 检测是否为 Git 仓库
        public bool IsGitRepository(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory))
                {
                    System.Diagnostics.Debug.WriteLine("SharpGitHelper.IsGitRepository: directory 为空");
                    return false;
                }

                // 检查 .git 目录是否存在
                string gitDir = Path.Combine(directory, ".git");
                if (Directory.Exists(gitDir))
                {
                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.IsGitRepository: 找到 .git 目录: {directory}");
                    return true;
                }

                // 尝试使用 Git 命令行检测
                try
                {
                    using (var process = new System.Diagnostics.Process())
                    {
                        process.StartInfo.FileName = "git";
                        process.StartInfo.Arguments = "rev-parse --is-inside-work-tree";
                        process.StartInfo.WorkingDirectory = directory;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"SharpGitHelper.IsGitRepository: Git 命令行检测成功: {directory}");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.IsGitRepository: Git 命令行检测失败: {ex.Message}");
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SharpGitHelper.IsGitRepository: 异常: {ex.Message}");
                return false;
            }
        }

        // 获取 Git 仓库根目录
        public string GetGitRoot(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory))
                {
                    System.Diagnostics.Debug.WriteLine("SharpGitHelper.GetGitRoot: directory 为空");
                    return null;
                }

                // 检查 .git 目录是否存在
                string gitDir = Path.Combine(directory, ".git");
                if (Directory.Exists(gitDir))
                {
                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetGitRoot: 找到 .git 目录: {directory}");
                    return directory;
                }

                // 尝试使用 Git 命令行获取根目录
                try
                {
                    using (var process = new System.Diagnostics.Process())
                    {
                        process.StartInfo.FileName = "git";
                        process.StartInfo.Arguments = "rev-parse --show-toplevel";
                        process.StartInfo.WorkingDirectory = directory;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                        {
                            string gitRoot = output.Trim();
                            System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetGitRoot: Git 命令行找到仓库: {gitRoot}");
                            return gitRoot;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetGitRoot: Git 命令行执行失败: {ex.Message}");
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetGitRoot: 异常: {ex.Message}");
                return null;
            }
        }

        // 获取更改状态的文件列表
        public IEnumerable<string> GetChangedFiles(string gitRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(gitRoot))
                {
                    System.Diagnostics.Debug.WriteLine("SharpGitHelper.GetChangedFiles: gitRoot 为空");
                    return Enumerable.Empty<string>();
                }

                // 尝试使用 Git 命令行获取更改文件
                try
                {
                    using (var process = new System.Diagnostics.Process())
                    {
                        process.StartInfo.FileName = "git";
                        process.StartInfo.Arguments = "status --porcelain";
                        process.StartInfo.WorkingDirectory = gitRoot;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0)
                        {
                            var changedFiles = new List<string>();
                            var lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                            
                            System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetChangedFiles: 总文件数={lines.Length}");
                            
                            foreach (var line in lines)
                            {
                                if (line.Length < 3)
                                    continue;
                                
                                string status = line.Substring(0, 2).Trim();
                                string filePath = line.Substring(3).Trim();
                                
                                // 过滤掉被忽略的文件和非 .cs 文件
                                if (status == "!!" || !filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                                    continue;
                                
                                // 构建完整的文件路径
                                string fullPath = Path.Combine(gitRoot, filePath);
                                if (File.Exists(fullPath))
                                {
                                    changedFiles.Add(fullPath);
                                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.添加更改文件: {fullPath}");
                                }
                            }
                            
                            System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetChangedFiles: 更改文件数={changedFiles.Count}");
                            return changedFiles;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetChangedFiles: Git 命令行执行失败: {ex.Message}");
                }

                return Enumerable.Empty<string>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetChangedFiles: 异常: {ex.Message}");
                return Enumerable.Empty<string>();
            }
        }

        // 获取当前分支名称
        public string GetCurrentBranch(string gitRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(gitRoot))
                {
                    System.Diagnostics.Debug.WriteLine("SharpGitHelper.GetCurrentBranch: gitRoot 为空");
                    return null;
                }

                // 尝试使用 Git 命令行获取当前分支
                try
                {
                    using (var process = new System.Diagnostics.Process())
                    {
                        process.StartInfo.FileName = "git";
                        process.StartInfo.Arguments = "rev-parse --abbrev-ref HEAD";
                        process.StartInfo.WorkingDirectory = gitRoot;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                        {
                            string branchName = output.Trim();
                            System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetCurrentBranch: 当前分支: {branchName}");
                            return branchName;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetCurrentBranch: Git 命令行执行失败: {ex.Message}");
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetCurrentBranch: 异常: {ex.Message}");
                return null;
            }
        }

        // 获取仓库状态
        public object GetRepositoryStatus(string gitRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(gitRoot))
                {
                    System.Diagnostics.Debug.WriteLine("SharpGitHelper.GetRepositoryStatus: gitRoot 为空");
                    return null;
                }

                // 尝试使用 Git 命令行获取状态
                try
                {
                    using (var process = new System.Diagnostics.Process())
                    {
                        process.StartInfo.FileName = "git";
                        process.StartInfo.Arguments = "status";
                        process.StartInfo.WorkingDirectory = gitRoot;
                        process.StartInfo.RedirectStandardOutput = true;
                        process.StartInfo.RedirectStandardError = true;
                        process.StartInfo.UseShellExecute = false;
                        process.StartInfo.CreateNoWindow = true;
                        
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        
                        if (process.ExitCode == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetRepositoryStatus: 获取状态成功");
                            return output;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetRepositoryStatus: Git 命令行执行失败: {ex.Message}");
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SharpGitHelper.GetRepositoryStatus: 异常: {ex.Message}");
                return null;
            }
        }
    }
}
