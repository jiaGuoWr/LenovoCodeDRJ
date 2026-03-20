using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using Microsoft.CodeAnalysis.Text;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class LenovoQiraCodeAnalyzerAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "CHN001";
    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        DiagnosticId,
        "注释中包含中文内容",
        "注释内容 '{0}' 包含中文",
        "CodeStyle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "代码注释中禁止包含中文文本");

    public const string MissingDllImportSearchPathsId = "DLL002";
    private static readonly DiagnosticDescriptor MissingDllImportSearchPathsRule = new DiagnosticDescriptor(
        MissingDllImportSearchPathsId,
        "DllImport缺少DefaultDllImportSearchPaths属性",
        "DllImport未指定DefaultDllImportSearchPaths属性",
        "CodeStyle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "使用DllImport时应指定DefaultDllImportSearchPaths属性");

    public const string InvalidStackTraceUsageId = "EXC001";
    private static readonly DiagnosticDescriptor InvalidStackTraceUsageRule = new DiagnosticDescriptor(
        InvalidStackTraceUsageId,
        "catch块中不应打印具体堆栈信息",
        "catch块中使用了{0}获取堆栈信息，可能导致敏感信息泄露",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "异常捕获时不应打印完整堆栈信息，建议仅记录必要的错误消息");
    public const string UnsafeDllSignatureId = "DLL003";
    private static readonly DiagnosticDescriptor UnsafeDllSignatureRule = new DiagnosticDescriptor(
        UnsafeDllSignatureId,
        "DLL签名不安全",
        "DLL文件 '{0}'",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "使用DllImport引用的DLL必须具有有效的数字签名");
    public const string InvalidCommentedCodeId = "CODE001";
    private static readonly DiagnosticDescriptor InvalidCommentedCodeRule = new DiagnosticDescriptor(
        InvalidCommentedCodeId,
        "注释中包含无效代码",
        "注释内容包含无效代码: {0}",
        "CodeStyle",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "不应保留被注释掉的无效代码片段");
    public const string SensitiveInfoInCodeId = "SEC001";
    private static readonly DiagnosticDescriptor SensitiveInfoInCodeRule = new DiagnosticDescriptor(
        SensitiveInfoInCodeId,
        "代码中存在明文存储的敏感信息",
        "检测到敏感信息: {0} = \"{1}\"",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "不应在代码中明文存储密码、Token、密钥等敏感信息，建议使用配置文件或环境变量");

    public const string PathTraversalId = "SEC002";
    private static readonly DiagnosticDescriptor PathTraversalRule = new DiagnosticDescriptor(
        PathTraversalId,
        "不受控制的搜索路径（路径遍历风险）",
        "检测到不受控制的搜索路径: {0}",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "永远不要直接使用用户输入作为路径，可能导致路径遍历攻击。应验证路径是否在允许的目录内");

    public const string SqlInjectionId = "SEC003";
    private static readonly DiagnosticDescriptor SqlInjectionRule = new DiagnosticDescriptor(
        SqlInjectionId,
        "潜在的SQL注入风险",
        "检测到通过{0}动态构建SQL语句，存在SQL注入风险: {1}",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "不应使用字符串拼接或插值动态构建SQL语句，建议使用参数化查询或ORM框架");

    public const string UnsafeDeserializationId = "SEC004";
    private static readonly DiagnosticDescriptor UnsafeDeserializationRule = new DiagnosticDescriptor(
        UnsafeDeserializationId,
        "使用了不安全的反序列化方式",
        "检测到不安全的反序列化操作: {0}，可能导致远程代码执行（RCE）漏洞",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "BinaryFormatter和JavaScriptSerializer存在反序列化漏洞，建议使用System.Text.Json，若使用Newtonsoft.Json须设置TypeNameHandling.None");

    public const string InsecureRandomId = "SEC005";
    private static readonly DiagnosticDescriptor InsecureRandomRule = new DiagnosticDescriptor(
        InsecureRandomId,
        "使用了密码学不安全的随机数生成器",
        "检测到使用 System.Random {0}，不适用于安全场景（如令牌、密钥生成），建议改用 RandomNumberGenerator",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "System.Random使用伪随机算法，不具备密码学安全性，在生成令牌、密钥、验证码等安全敏感场景下必须使用RandomNumberGenerator");

    public const string RegexDosId = "SEC006";
    private static readonly DiagnosticDescriptor RegexDosRule = new DiagnosticDescriptor(
        RegexDosId,
        "正则表达式缺少超时参数（ReDoS风险）",
        "检测到 {0} 未设置超时参数，复杂正则表达式可能导致正则表达式拒绝服务（ReDoS）攻击",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "使用Regex时应始终指定matchTimeout参数，防止恶意输入导致回溯爆炸式增长造成DoS攻击");

    public const string ResourceLeakId = "SEC007";
    private static readonly DiagnosticDescriptor ResourceLeakRule = new DiagnosticDescriptor(
        ResourceLeakId,
        "IDisposable资源未使用using语句管理（资源泄漏风险）",
        "'{0}' 实现了IDisposable接口，创建后应使用using语句或using声明确保资源被释放",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "数据库连接、文件流、HttpClient等资源未正确释放会导致连接池耗尽、文件句柄泄漏等问题");

    public const string InsecureTempFileId = "SEC008";
    private static readonly DiagnosticDescriptor InsecureTempFileRule = new DiagnosticDescriptor(
        InsecureTempFileId,
        "使用了可预测的临时文件名（竞争条件/文件枚举风险）",
        "检测到使用可预测方式({0})构造临时文件名，攻击者可预测文件路径发动竞争条件或符号链接攻击",
        "Security",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "应使用Path.GetTempFileName()或Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())生成不可预测的临时文件名");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(
            Rule, MissingDllImportSearchPathsRule, InvalidStackTraceUsageRule,
            UnsafeDllSignatureRule, InvalidCommentedCodeRule, SensitiveInfoInCodeRule,
            PathTraversalRule,
            SqlInjectionRule,
            UnsafeDeserializationRule,
            InsecureRandomRule,
            RegexDosRule,
            ResourceLeakRule,
            InsecureTempFileRule);

    private static readonly HashSet<string> SensitiveKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "token", "secret", "key", "apikey",
        "accesskey", "privatekey", "secretkey", "credential",
         "auth", "authorization", "passcode", "certificate", "secretid"
    };

    private static readonly HashSet<string> SqlKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "INSERT", "UPDATE", "DELETE", "DROP", "CREATE",
        "ALTER", "FROM", "WHERE", "JOIN", "UNION", "EXEC", "EXECUTE"
    };

    private static readonly HashSet<string> UnsafeSerializerTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "BinaryFormatter", "JavaScriptSerializer", "LosFormatter",
        "NetDataContractSerializer", "SoapFormatter"
    };

    private static readonly HashSet<string> UnsafeTypeNameHandlingValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "All", "Objects", "Arrays", "Auto"
    };

    private static readonly HashSet<string> InsecureRandomMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "Next", "NextBytes", "NextDouble"
    };

    private static readonly HashSet<string> RegexStaticMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "IsMatch", "Match", "Matches", "Replace", "Split"
    };


    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTreeForChineseComments);
        context.RegisterSyntaxNodeAction(AnalyzeDllImportSearchPaths, SyntaxKind.MethodDeclaration);

        context.RegisterSyntaxNodeAction(AnalyzeCatchClauseForStackTrace, SyntaxKind.CatchClause);

        context.RegisterSyntaxTreeAction(AnalyzeSyntaxTreeForInvalidCommentedCode);

        context.RegisterSyntaxNodeAction(AnalyzeVariableDeclaration, SyntaxKind.VariableDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeAssignmentExpression, SyntaxKind.SimpleAssignmentExpression);

        // 注册路径遍历检测
        context.RegisterSyntaxNodeAction(AnalyzePathTraversal, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzePathTraversalInBinaryExpression, SyntaxKind.AddExpression);
        context.RegisterSyntaxNodeAction(AnalyzePathTraversalInInterpolation, SyntaxKind.InterpolatedStringExpression);

        // SEC003: SQL注入
        context.RegisterSyntaxNodeAction(AnalyzeSqlInjection, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeSqlCommandTextAssignment, SyntaxKind.SimpleAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeSqlQueryMethods, SyntaxKind.InvocationExpression);

        // SEC004: 不安全反序列化
        context.RegisterSyntaxNodeAction(AnalyzeUnsafeDeserialization, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeTypeNameHandlingAssignment, SyntaxKind.SimpleAssignmentExpression);

        // SEC005: 不安全随机数
        context.RegisterSyntaxNodeAction(AnalyzeInsecureRandom, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInsecureRandomMethodCall, SyntaxKind.InvocationExpression);

        // SEC006: ReDoS
        context.RegisterSyntaxNodeAction(AnalyzeRegexWithoutTimeout, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeStaticRegexWithoutTimeout, SyntaxKind.InvocationExpression);

        // SEC007: 资源泄漏
        context.RegisterSyntaxNodeAction(AnalyzeResourceLeak, SyntaxKind.LocalDeclarationStatement);

        // SEC008: 不安全临时文件
        context.RegisterSyntaxNodeAction(AnalyzeInsecureTempFile, SyntaxKind.InvocationExpression);
    }

    private void AnalyzeSyntaxTreeForChineseComments(SyntaxTreeAnalysisContext context)
    {
        if (context.CancellationToken.IsCancellationRequested) return;

        var root = context.Tree.GetRoot();
        var chineseRegex = new Regex(@"[\u4e00-\u9fa5]", RegexOptions.Compiled);

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            string rawText = trivia.ToString().Trim();
            if (string.IsNullOrWhiteSpace(rawText)) continue;

            string commentText = null;
            var location = Location.Create(context.Tree, trivia.Span);

            if (Regex.IsMatch(rawText, @"^\s*///", RegexOptions.Multiline))
            {
                commentText = Regex.Replace(rawText, @"^\s*///\s*", "", RegexOptions.Multiline).Trim();
            }
            else if (Regex.IsMatch(rawText, @"^\s*//", RegexOptions.Multiline) && !Regex.IsMatch(rawText, @"^\s*///", RegexOptions.Multiline))
            {
                commentText = Regex.Replace(rawText, @"^\s*//\s*", "", RegexOptions.Multiline).Trim();
            }
            else if (rawText.StartsWith("/*") && rawText.EndsWith("*/"))
            {
                commentText = Regex.Replace(rawText, @"^\s*/\*|\*/\s*$", "", RegexOptions.Multiline).Trim();
            }

            if (!string.IsNullOrWhiteSpace(commentText) && chineseRegex.IsMatch(commentText))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, commentText));
            }
        }
    }

    private void AnalyzeDllImportSearchPaths(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        var cancellationToken = context.CancellationToken;

        var dllImportAttrs = method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Where(attr => attr.Name.ToString().EndsWith("DllImport", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!dllImportAttrs.Any()) return;

        bool hasSearchPathsAttr = method.AttributeLists
            .SelectMany(attrList => attrList.Attributes)
            .Any(attr => attr.Name.ToString().EndsWith("DefaultDllImportSearchPaths", StringComparison.OrdinalIgnoreCase));

        if (!hasSearchPathsAttr)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                MissingDllImportSearchPathsRule,
                dllImportAttrs.First().GetLocation()));
        }

        foreach (var dllImportAttr in dllImportAttrs)
        {
            var dllName = GetDllNameFromAttribute(dllImportAttr);

            if (string.IsNullOrEmpty(dllName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsafeDllSignatureRule,
                    dllImportAttr.GetLocation(),
                    "未知DLL名称"));
                continue;
            }

            var searchPath = GetSearchPathFromAttributes(method, context);

            // 严格模式查找：只在指定路径中查找，不回退到系统目录
            var dllPath = FindDllPathStrict(dllName, searchPath);

            if (dllPath == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsafeDllSignatureRule,
                    dllImportAttr.GetLocation(),
                    $"{dllName}（在指定搜索路径中未找到）"));
                continue;
            }

            if (!IsDllSigned(dllPath))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsafeDllSignatureRule,
                    dllImportAttr.GetLocation(),
                    $"{dllName}（签名无效/未签名）"));
            }
        }
    }

    private bool IsDllSigned(string dllPath)
    {
        if (!File.Exists(dllPath))
            return false;

        try
        {
            // 严格模式验证：验证证书链完整性和有效期
            X509Certificate cert = X509Certificate.CreateFromSignedFile(dllPath);
            if (cert == null)
                return false;

            // 转换为 X509Certificate2 以支持链验证
            X509Certificate2 cert2 = new X509Certificate2(cert);

            // 验证证书是否在有效期内
            DateTime now = DateTime.Now;
            if (cert2.NotBefore > now || cert2.NotAfter < now)
            {
                return false; // 证书已过期或尚未生效
            }

            // 构建并验证证书链
            X509Chain chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // 不检查吊销，避免网络依赖
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            bool isChainValid = chain.Build(cert2);
            if (!isChainValid)
            {
                return false; // 证书链验证失败
            }

            return true;
        }
        catch (CryptographicException)
        {
            // 文件没有签名或签名格式无效
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string GetSearchPathFromAttributes(MethodDeclarationSyntax method, SyntaxNodeAnalysisContext context)
    {
        try
        {
            var searchPathAttr = method.AttributeLists
                .SelectMany(attrList => attrList.Attributes)
                .FirstOrDefault(attr => attr.Name.ToString().EndsWith("DefaultDllImportSearchPaths", StringComparison.OrdinalIgnoreCase));

            if (searchPathAttr == null || searchPathAttr.ArgumentList?.Arguments.Count == 0)
                return null;

            var searchPathValues = searchPathAttr.ArgumentList.Arguments
                .Select(arg => arg.Expression.ToString())
                .Select(expr =>
                {
                    var enumValue = expr.Split('.').Last();
                    return Enum.TryParse<DllImportSearchPath>(enumValue, out var result) ? result : (DllImportSearchPath?)null;
                })
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .ToList();

            if (!searchPathValues.Any())
                return null;

            var paths = new List<string>();
            foreach (var value in searchPathValues)
            {
                switch (value)
                {
                    case DllImportSearchPath.ApplicationDirectory:
                        string codeFilePath = context.Node.SyntaxTree.FilePath;
                        if (!string.IsNullOrEmpty(codeFilePath))
                        {
                            string projectDir = FindProjectTopDirectory(codeFilePath);
                            if (!string.IsNullOrEmpty(projectDir))
                            {
                                // 探测多个可能的输出路径
                                var possiblePaths = GetPossibleOutputPaths(projectDir);
                                foreach (var dllDir in possiblePaths)
                                {
                                    if (Directory.Exists(dllDir))
                                        paths.Add(dllDir);
                                }
                            }
                        }
                        break;
                    case DllImportSearchPath.System32:
                        paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
                        break;
                    case DllImportSearchPath.UserDirectories:
                        paths.Add(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                        break;
                }
            }

            return paths.Count > 0 ? string.Join(";", paths.Distinct()) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string FindProjectTopDirectory(string codeFilePath)
    {
        try
        {
            string currentDir = Path.GetDirectoryName(codeFilePath);

            // 向上搜索目录树，查找包含 .csproj 文件的目录
            for (int i = 0; i < 15; i++)
            {
                if (string.IsNullOrEmpty(currentDir)) break;

                // 查找项目文件 (.csproj)
                var projectFiles = Directory.GetFiles(currentDir, "*.csproj");
                if (projectFiles.Length > 0)
                {
                    return currentDir;
                }

                currentDir = Directory.GetParent(currentDir)?.FullName;
            }

            // 回退：返回代码文件所在目录
            return Path.GetDirectoryName(codeFilePath);
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<string> GetPossibleOutputPaths(string projectDir)
    {
        var paths = new List<string>();

        // 常见构建配置
        var configurations = new[] { "Debug", "Release" };

        // 常见目标框架 - .NET Core/5/6/7/8+
        var targetFrameworks = new[]
        {
            "net9.0-windows",
            "net9.0",
            "net8.0-windows",
            "net8.0",
            "net7.0-windows",
            "net7.0",
            "net6.0-windows",
            "net6.0",
            "net5.0-windows",
            "net5.0",
            "netcoreapp3.1",
            "netcoreapp3.0",
            "netcoreapp2.2",
            "netcoreapp2.1",
            "netcoreapp2.0",
            "netcoreapp1.1",
            "netcoreapp1.0"
        };

        // .NET Framework
        var netFrameworks = new[]
        {
            "net48",
            "net472",
            "net471",
            "net47",
            "net462",
            "net461",
            "net46",
            "net452",
            "net451",
            "net45",
            "net40",
            "net35",
            "net20"
        };

        foreach (var config in configurations)
        {
            // 添加 .NET Core/5+ 路径
            foreach (var tfm in targetFrameworks)
            {
                paths.Add(Path.Combine(projectDir, "bin", config, tfm));
            }

            // 添加 .NET Framework 路径
            foreach (var tfm in netFrameworks)
            {
                paths.Add(Path.Combine(projectDir, "bin", config, tfm));
            }

            // 也尝试没有子目录的传统路径
            paths.Add(Path.Combine(projectDir, "bin", config));
        }

        // 添加 obj 目录（某些项目可能在此生成 DLL）
        paths.Add(Path.Combine(projectDir, "obj"));

        return paths;
    }

    /// <summary>
    /// 严格模式查找 DLL：只在指定路径中查找，不回退到系统目录
    /// </summary>
    private string FindDllPathStrict(string dllName, string searchPath)
    {
        if (string.IsNullOrEmpty(dllName)) return null;

        // 如果没有指定搜索路径，使用默认行为（系统目录）
        if (string.IsNullOrEmpty(searchPath))
        {
            string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string systemDllPath = Path.Combine(systemPath, dllName);
            if (File.Exists(systemDllPath)) return systemDllPath;
            return null;
        }

        // 只在指定路径中查找，不回退到系统目录
        foreach (var path in searchPath.Split(';'))
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;
            string fullPath = Path.Combine(path, dllName);
            if (File.Exists(fullPath)) return fullPath;
            string archDllPath = Path.Combine(path, $"{Path.GetFileNameWithoutExtension(dllName)}.x64{Path.GetExtension(dllName)}");
            if (File.Exists(archDllPath)) return archDllPath;
        }

        // 在指定路径中未找到，返回 null（不回退到系统目录）
        return null;
    }

    private string FindDllPath(string dllName, string searchPath)
    {
        if (string.IsNullOrEmpty(dllName)) return null;

        if (!string.IsNullOrEmpty(searchPath))
        {
            foreach (var path in searchPath.Split(';'))
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;
                string fullPath = Path.Combine(path, dllName);
                if (File.Exists(fullPath)) return fullPath;
                string archDllPath = Path.Combine(path, $"{Path.GetFileNameWithoutExtension(dllName)}.x64{Path.GetExtension(dllName)}");
                if (File.Exists(archDllPath)) return archDllPath;
            }
        }
        string systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string systemDllPath = Path.Combine(systemPath, dllName);
        if (File.Exists(systemDllPath)) return systemDllPath;

        return null;
    }

    private string GetDllNameFromAttribute(AttributeSyntax dllImportAttr)
    {
        if (dllImportAttr.ArgumentList?.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax literal)
        {
            return literal.Token.Value?.ToString();
        }
        return null;
    }

    private void AnalyzeCatchClauseForStackTrace(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        if (catchClause.Block == null) return;

        // 尝试使用语义分析检测
        var stackTraceUsages = catchClause.Block.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(ma =>
                (ma.Name.Identifier.Text == "StackTrace" && IsExceptionType(context, ma.Expression)) ||
                (ma.Name.Identifier.Text == "ToString" && IsExceptionType(context, ma.Expression)));

        // 如果语义分析没有检测到，尝试使用语法分析检测
        if (!stackTraceUsages.Any())
        {
            // 语法分析：检测catch块中的StackTrace或ToString使用
            stackTraceUsages = catchClause.Block.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(ma =>
                    ma.Name.Identifier.Text == "StackTrace" ||
                    ma.Name.Identifier.Text == "ToString");
        }

        foreach (var usage in stackTraceUsages)
        {
            string usageText = usage.ToString();
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidStackTraceUsageRule,
                usage.GetLocation(),
                usageText));
        }
    }

    private bool IsExceptionType(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        try
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
            return typeInfo.Type != null &&
                   (typeInfo.Type.ToString() == "System.Exception" ||
                    typeInfo.Type.BaseType?.ToString() == "System.Exception");
        }
        catch
        {
            // 语义分析失败，返回false，让语法分析来处理
            return false;
        }
    }

    private void AnalyzeSyntaxTreeForInvalidCommentedCode(SyntaxTreeAnalysisContext context)
    {
        if (context.CancellationToken.IsCancellationRequested) return;

        var root = context.Tree.GetRoot();
        var allTrivias = root.DescendantTrivia(descendIntoTrivia: true).ToList();
        var processedSpans = new HashSet<int>();

        for (int i = 0; i < allTrivias.Count; i++)
        {
            var trivia = allTrivias[i];

            // 跳过已处理的trivia
            if (processedSpans.Contains(trivia.Span.Start))
                continue;

            string rawText = trivia.ToString().Trim();
            if (string.IsNullOrWhiteSpace(rawText)) continue;

            // 处理多行块注释 /* */
            if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            {
                string commentText = ExtractCommentText(rawText, trivia.Kind());
                // 块注释按1行处理，主要检查长度
                if (IsLargeCodeBlock(commentText, 1))
                {
                    var location = Location.Create(context.Tree, trivia.Span);
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidCommentedCodeRule,
                        location,
                        TruncateLongText(commentText, 50)));
                }
                processedSpans.Add(trivia.Span.Start);
                continue;
            }

            // 跳过文档注释
            if (trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia))
            {
                processedSpans.Add(trivia.Span.Start);
                continue;
            }

            // 处理单行注释 // 以及连续的多行注释块
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
            {
                // 收集连续的单行注释
                var commentGroup = new List<SyntaxTrivia> { trivia };
                processedSpans.Add(trivia.Span.Start);

                // 获取当前注释的行号
                int currentLine = trivia.GetLocation().GetLineSpan().StartLinePosition.Line;

                // 向后查找连续的单行注释（按行号判断连续性）
                int j = i + 1;
                while (j < allTrivias.Count)
                {
                    var nextTrivia = allTrivias[j];

                    // 跳过空白和换行
                    if (nextTrivia.IsKind(SyntaxKind.EndOfLineTrivia) ||
                        nextTrivia.IsKind(SyntaxKind.WhitespaceTrivia))
                    {
                        j++;
                        continue;
                    }

                    // 如果是另一个单行注释，检查行号是否连续
                    if (nextTrivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
                        !processedSpans.Contains(nextTrivia.Span.Start))
                    {
                        int nextLine = nextTrivia.GetLocation().GetLineSpan().StartLinePosition.Line;

                        // 只有行号连续（相差1）才算连续注释
                        if (nextLine == currentLine + 1)
                        {
                            commentGroup.Add(nextTrivia);
                            processedSpans.Add(nextTrivia.Span.Start);
                            currentLine = nextLine;
                            j++;
                        }
                        else
                        {
                            // 行号不连续，断开
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                // 合并注释文本
                var combinedText = string.Join("\n", commentGroup.Select(t =>
                    ExtractCommentText(t.ToString().Trim(), SyntaxKind.SingleLineCommentTrivia)));

                if (IsLargeCodeBlock(combinedText, commentGroup.Count))
                {
                    var spanStart = commentGroup.First().Span.Start;
                    var spanEnd = commentGroup.Last().Span.End;
                    var location = Location.Create(context.Tree, new TextSpan(spanStart, spanEnd - spanStart));
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidCommentedCodeRule,
                        location,
                        TruncateLongText(combinedText, 50)));
                }
            }
        }
    }

    private bool IsLargeCodeBlock(string text, int lineCount)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // 条件1: 单行超过80字符
        if (text.Length >= 180)
            return true;

        // 条件2: 连续大于3行（即4行及以上）的注释代码
        if (lineCount > 10)
            return true;

        return false;
    }

    private string ExtractCommentText(string rawText, SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.SingleLineCommentTrivia:
                return rawText.TrimStart('/').Trim();
            case SyntaxKind.MultiLineCommentTrivia:
            case SyntaxKind.DocumentationCommentExteriorTrivia:
                return rawText.TrimStart('/').TrimEnd('/').Replace("*", "").Trim();
            default:
                return rawText;
        }
    }

    private string TruncateLongText(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }

    private void AnalyzeVariableDeclaration(SyntaxNodeAnalysisContext context)
    {
        var variableDeclaration = (VariableDeclarationSyntax)context.Node;

        foreach (var variable in variableDeclaration.Variables)
        {
            string varName = variable.Identifier.Text;
            string plainTextValue = string.Empty;
            if (variable.Initializer?.Value is LiteralExpressionSyntax literal &&
                literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                plainTextValue = literal.Token.ValueText ?? string.Empty;
            }

            if (IsSensitiveVariableName(varName) &&
                variable.Initializer?.Value is LiteralExpressionSyntax literalExpr &&
                literalExpr.IsKind(SyntaxKind.StringLiteralExpression))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SensitiveInfoInCodeRule,
                    variable.GetLocation(),
                    varName,
                    plainTextValue));
            }
        }
    }

    private void AnalyzeAssignmentExpression(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        if (assignment.Left is IdentifierNameSyntax identifierName &&
            IsSensitiveVariableName(identifierName.Identifier.Text) &&
            assignment.Right is LiteralExpressionSyntax literal &&
            literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            string varName = identifierName.Identifier.Text;
            string plainTextValue = literal.Token.ValueText ?? string.Empty;

            context.ReportDiagnostic(Diagnostic.Create(
                SensitiveInfoInCodeRule,
                assignment.GetLocation(),
                varName,
                plainTextValue));
        }
    }

    private bool IsSensitiveVariableName(string variableName)
    {
        return SensitiveKeywords.Any(keyword => variableName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    // 路径遍历检测相关关键字
    private static readonly HashSet<string> PathTraversalKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "input", "userinput", "request", "query", "param", "parameter",
        "arg", "argument", "data", "content", "text", "value",
        "path", "filepath", "filename", "file", "url", "uri"
    };

    /// <summary>
    /// 检测 Path.Combine 等路径拼接方法调用中的路径遍历风险
    /// </summary>
    private void AnalyzePathTraversal(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // 检测 Path.Combine, Path.Join 等方法
        var methodName = GetMethodName(invocation);
        if (string.IsNullOrEmpty(methodName))
            return;

        // 检查是否是路径相关方法
        if (!IsPathRelatedMethod(methodName))
            return;

        // 检查参数中是否包含可疑输入
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (IsSuspiciousPathArgument(arg.Expression))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PathTraversalRule,
                    arg.GetLocation(),
                    $"方法 {methodName} 使用了可能包含用户输入的参数，存在路径遍历风险"));
                return;
            }
        }
    }

    /// <summary>
    /// 检测字符串拼接中的路径遍历风险
    /// </summary>
    private void AnalyzePathTraversalInBinaryExpression(SyntaxNodeAnalysisContext context)
    {
        var binaryExpr = (BinaryExpressionSyntax)context.Node;

        // 检查是否是字符串拼接
        if (!binaryExpr.OperatorToken.IsKind(SyntaxKind.PlusToken))
            return;

        // 检查是否包含路径相关关键字或变量
        if (ContainsPathVariable(binaryExpr.Left) || ContainsPathVariable(binaryExpr.Right))
        {
            // 检查另一侧是否包含可疑输入
            if (IsSuspiciousPathArgument(binaryExpr.Left) || IsSuspiciousPathArgument(binaryExpr.Right))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PathTraversalRule,
                    binaryExpr.GetLocation(),
                    "字符串拼接构造路径时使用了可能包含用户输入的值，存在路径遍历风险"));
            }
        }
    }

    /// <summary>
    /// 检测插值字符串中的路径遍历风险
    /// </summary>
    private void AnalyzePathTraversalInInterpolation(SyntaxNodeAnalysisContext context)
    {
        var interpolation = (InterpolatedStringExpressionSyntax)context.Node;

        // 检查插值字符串是否包含路径相关上下文
        var fullText = interpolation.ToString();
        if (!fullText.Contains("\\") && !fullText.Contains("/") &&
            !fullText.Contains("Path") && !fullText.Contains("path"))
            return;

        // 检查插值表达式中是否包含可疑输入
        foreach (var content in interpolation.Contents)
        {
            if (content is InterpolationSyntax interp &&
                IsSuspiciousPathArgument(interp.Expression))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    PathTraversalRule,
                    interp.GetLocation(),
                    "字符串插值构造路径时使用了可能包含用户输入的值，存在路径遍历风险"));
                return;
            }
        }
    }

    /// <summary>
    /// 获取方法调用名称
    /// </summary>
    private string GetMethodName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }
        else if (invocation.Expression is IdentifierNameSyntax identifier)
        {
            return identifier.Identifier.Text;
        }
        return null;
    }

    /// <summary>
    /// 判断是否是路径相关的方法
    /// </summary>
    private bool IsPathRelatedMethod(string methodName)
    {
        return methodName.Equals("Combine", StringComparison.OrdinalIgnoreCase) ||
               methodName.Equals("Join", StringComparison.OrdinalIgnoreCase) ||
               methodName.Equals("GetFullPath", StringComparison.OrdinalIgnoreCase) ||
               methodName.Equals("GetTempPath", StringComparison.OrdinalIgnoreCase) ||
               methodName.Equals("ChangeExtension", StringComparison.OrdinalIgnoreCase) ||
               methodName.Equals("GetFileName", StringComparison.OrdinalIgnoreCase) ||
               methodName.Equals("GetDirectoryName", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查表达式是否包含路径相关变量
    /// </summary>
    private bool ContainsPathVariable(ExpressionSyntax expression)
    {
        var text = expression.ToString();
        return text.Contains("Path") ||
               text.Contains("path") ||
               text.Contains("dir") ||
               text.Contains("file") ||
               text.Contains("\\") ||
               text.Contains("/");
    }

    /// <summary>
    /// 判断参数是否可疑（可能包含用户输入或路径遍历特征）
    /// </summary>
    private bool IsSuspiciousPathArgument(ExpressionSyntax expression)
    {
        var text = expression.ToString();

        // 检查是否包含路径遍历特征
        if (text.Contains("..") || text.Contains("~") || text.Contains("%"))
            return true;

        // 检查是否是用户输入相关变量
        if (expression is IdentifierNameSyntax identifier)
        {
            var varName = identifier.Identifier.Text;
            if (PathTraversalKeywords.Any(keyword =>
                varName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }

        // 检查成员访问表达式（如 request.QueryString, user.Input 等）
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberText = memberAccess.ToString();
            if (PathTraversalKeywords.Any(keyword =>
                memberText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }

        // 检查索引器访问（如 request["file"]）
        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            return true; // 数组/索引器访问通常来自用户输入
        }

        // 检查方法调用（如 Console.ReadLine(), stream.Read() 等）
        if (expression is InvocationExpressionSyntax invocation)
        {
            var invocText = invocation.ToString().ToLowerInvariant();
            if (invocText.Contains("read") || invocText.Contains("input") ||
                invocText.Contains("get") || invocText.Contains("receive"))
            {
                return true;
            }
        }

        return false;
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC003 ~ SEC008 新增安全规则实现
    // ═════════════════════════════════════════════════════════════════

    // 已知SQL命令类型名（仅后缀匹配，避免命名空间污染误判）
    private static readonly HashSet<string> SqlCommandTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SqlCommand", "DbCommand", "SqliteCommand", "MySqlCommand",
        "NpgsqlCommand", "OracleCommand", "OleDbCommand", "OdbcCommand"
    };

    // 已知需要 using 管理的 IDisposable 类型
    private static readonly HashSet<string> DisposableTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SqlConnection", "SqlCommand", "SqlDataReader", "SqlTransaction",
        "DbConnection", "DbCommand", "DbDataReader", "DbTransaction",
        "SqliteConnection", "SqliteCommand",
        "MySqlConnection", "MySqlCommand",
        "NpgsqlConnection", "NpgsqlCommand",
        "FileStream", "StreamReader", "StreamWriter", "BinaryReader", "BinaryWriter",
        "MemoryStream", "BufferedStream", "GZipStream", "DeflateStream",
        "HttpClient", "HttpClientHandler", "HttpMessageHandler",
        "TcpClient", "UdpClient", "NetworkStream", "Socket",
        "DbContext",
        "SqlBulkCopy",
        "CancellationTokenSource",
        "SemaphoreSlim", "Mutex", "Semaphore"
    };

    /// <summary>
    /// 从类型语法中提取最后一个简单名称（处理带命名空间的全限定名）
    /// </summary>
    private string GetSimpleTypeName(TypeSyntax type)
    {
        if (type is IdentifierNameSyntax idName)
            return idName.Identifier.Text;
        if (type is QualifiedNameSyntax qualName)
            return qualName.Right.Identifier.Text;
        if (type is GenericNameSyntax genName)
            return genName.Identifier.Text;
        return type.ToString().Split('.').Last();
    }

    /// <summary>
    /// 判断表达式是否为字符串拼接或插值，返回构建方式描述，否则返回null
    /// </summary>
    private string GetStringBuildMethod(ExpressionSyntax expression)
    {
        // 字符串插值: $"SELECT {id}"
        if (expression is InterpolatedStringExpressionSyntax)
            return "字符串插值($\"\")";

        // 字符串拼接: "SELECT " + userInput
        if (expression is BinaryExpressionSyntax binary &&
            binary.OperatorToken.IsKind(SyntaxKind.PlusToken))
        {
            // 至少一侧是字符串字面量，另一侧是变量/调用（减少误报：两侧都是字面量则忽略）
            bool leftIsLiteral = binary.Left is LiteralExpressionSyntax le1 && le1.IsKind(SyntaxKind.StringLiteralExpression);
            bool rightIsLiteral = binary.Right is LiteralExpressionSyntax le2 && le2.IsKind(SyntaxKind.StringLiteralExpression);
            if (leftIsLiteral && rightIsLiteral)
                return null; // 两侧都是字面量，是安全的常量拼接
            if (leftIsLiteral || rightIsLiteral)
                return "字符串拼接(+)";
        }

        return null;
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC003: SQL注入防护
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// SEC003: 检测SQL命令构造函数中直接使用字符串插值或拼接
    /// </summary>
    private void AnalyzeSqlInjection(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;

        // 提取类型名（处理 new SqlCommand / new System.Data.SqlClient.SqlCommand 两种形式）
        string typeName = GetSimpleTypeName(objectCreation.Type);
        if (!SqlCommandTypeNames.Contains(typeName))
            return;

        // 检查构造函数参数列表，第一个参数是命令文本
        if (objectCreation.ArgumentList == null || objectCreation.ArgumentList.Arguments.Count == 0)
            return;

        var firstArg = objectCreation.ArgumentList.Arguments[0].Expression;
        string buildMethod = GetStringBuildMethod(firstArg);
        if (buildMethod == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            SqlInjectionRule,
            firstArg.GetLocation(),
            typeName,
            buildMethod));
    }

    /// <summary>
    /// SEC003: 检测 cmd.CommandText = $"..." 或 cmd.CommandText = "..." + var 的赋值
    /// </summary>
    private void AnalyzeSqlCommandTextAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // 左侧必须是 xxx.CommandText 的成员访问
        if (!(assignment.Left is MemberAccessExpressionSyntax memberAccess))
            return;
        if (!memberAccess.Name.Identifier.Text.Equals("CommandText", StringComparison.Ordinal))
            return;

        string buildMethod = GetStringBuildMethod(assignment.Right);
        if (buildMethod == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            SqlInjectionRule,
            assignment.Right.GetLocation(),
            "CommandText",
            buildMethod));
    }

    /// <summary>
    /// SEC003: 检测 EntityFramework 或 Dapper 等ORM的FromSqlRaw/ExecuteSqlRaw等危险方法
    /// </summary>
    private void AnalyzeSqlQueryMethods(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        string methodName = GetMethodName(invocation);
        if (string.IsNullOrEmpty(methodName))
            return;

        // 检测各种危险的SQL执行方法
        var dangerousMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FromSqlRaw", "FromSqlInterpolated",
            "ExecuteSqlRaw", "ExecuteSqlRawAsync",
            "ExecuteSqlCommand", "ExecuteSqlCommandAsync",
            "SqlQuery", "SqlQueryAsync",
            "Query", "QueryAsync", "Execute", "ExecuteAsync"
        };

        if (!dangerousMethods.Contains(methodName))
            return;

        // 检查参数是否是字符串插值或拼接
        if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count == 0)
            return;

        var firstArg = invocation.ArgumentList.Arguments[0].Expression;
        string buildMethod = GetStringBuildMethod(firstArg);
        if (buildMethod == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            SqlInjectionRule,
            firstArg.GetLocation(),
            methodName,
            buildMethod));
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC004: 不安全的反序列化
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// SEC004: 检测 new BinaryFormatter() 等不安全序列化器
    /// </summary>
    private void AnalyzeUnsafeDeserialization(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        string typeName = GetSimpleTypeName(objectCreation.Type);

        if (UnsafeSerializerTypes.Contains(typeName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsafeDeserializationRule,
                objectCreation.GetLocation(),
                $"new {typeName}() — 该类型存在已知RCE漏洞"));
        }
    }

    /// <summary>
    /// SEC004: 检测 settings.TypeNameHandling = TypeNameHandling.All/Objects/Arrays/Auto
    /// </summary>
    private void AnalyzeTypeNameHandlingAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // 左侧必须是 xxx.TypeNameHandling 形式
        if (!(assignment.Left is MemberAccessExpressionSyntax memberAccess))
            return;
        if (!memberAccess.Name.Identifier.Text.Equals("TypeNameHandling", StringComparison.Ordinal))
            return;

        // 右值：检查是否是 TypeNameHandling.None（安全），其他值都不安全
        var rightText = assignment.Right.ToString().Trim();

        // 安全值：TypeNameHandling.None 或 0（整数0等价于None）
        if (rightText.EndsWith(".None", StringComparison.Ordinal) ||
            rightText.Equals("0", StringComparison.Ordinal) ||
            rightText.Equals("TypeNameHandling.None", StringComparison.Ordinal))
            return;

        // 检查是否是不安全的值
        foreach (var unsafeValue in UnsafeTypeNameHandlingValues)
        {
            if (rightText.EndsWith($".{unsafeValue}", StringComparison.Ordinal) ||
                rightText.Equals(unsafeValue, StringComparison.Ordinal))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsafeDeserializationRule,
                    assignment.GetLocation(),
                    $"TypeNameHandling = {rightText}（仅TypeNameHandling.None是安全的）"));
                return;
            }
        }

        // 如果是其他值（可能是变量），也报告
        if (!rightText.Contains("None"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                UnsafeDeserializationRule,
                assignment.GetLocation(),
                $"TypeNameHandling = {rightText}（建议显式设置为TypeNameHandling.None）"));
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC005: 不安全的随机数生成
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// SEC005: 检测 new Random() 的创建
    /// </summary>
    private void AnalyzeInsecureRandom(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        string typeName = GetSimpleTypeName(objectCreation.Type);

        // 精确匹配 "Random"，不匹配 SecureRandom/CryptoRandom 等
        if (!typeName.Equals("Random", StringComparison.Ordinal))
            return;

        // 排除全限定名不是 System.Random 的情况（如第三方库的 Random）
        // 在纯语法分析下，我们通过检查是否有 "Crypto"/"Secure"/"Strong" 前缀排除已知安全替代品
        var fullText = objectCreation.Type.ToString();
        if (fullText.IndexOf("Crypto", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fullText.IndexOf("Secure", StringComparison.OrdinalIgnoreCase) >= 0 ||
            fullText.IndexOf("Strong", StringComparison.OrdinalIgnoreCase) >= 0)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            InsecureRandomRule,
            objectCreation.GetLocation(),
            "构造"));
    }

    /// <summary>
    /// SEC005: 检测 Random.Next/NextBytes/NextDouble 方法调用
    /// </summary>
    private void AnalyzeInsecureRandomMethodCall(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            return;

        string methodName = memberAccess.Name.Identifier.Text;
        if (!InsecureRandomMethods.Contains(methodName))
            return;

        // 检查调用者是否是 Random 类型
        var callerText = memberAccess.Expression.ToString();
        if (callerText.EndsWith("Random", StringComparison.Ordinal) &&
            !callerText.Contains("Crypto") &&
            !callerText.Contains("Secure") &&
            !callerText.Contains("Security"))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InsecureRandomRule,
                invocation.GetLocation(),
                $"方法调用 .{methodName}()"));
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC006: ReDoS（正则表达式拒绝服务）
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// SEC006: 检测 new Regex(pattern) 或 new Regex(pattern, options) 未传入超时参数
    /// Regex构造函数签名:
    ///   Regex(string pattern)
    ///   Regex(string pattern, RegexOptions options)
    ///   Regex(string pattern, RegexOptions options, TimeSpan matchTimeout)  ← 安全
    /// </summary>
    private void AnalyzeRegexWithoutTimeout(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        string typeName = GetSimpleTypeName(objectCreation.Type);

        if (!typeName.Equals("Regex", StringComparison.Ordinal))
            return;

        // 参数少于3个则缺少 matchTimeout
        int argCount = objectCreation.ArgumentList?.Arguments.Count ?? 0;
        if (argCount >= 3)
            return; // 传入了超时参数，安全

        // 必须至少有1个参数（pattern），空参数列表不是Regex的有效用法
        if (argCount == 0)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            RegexDosRule,
            objectCreation.GetLocation(),
            "new Regex(...)"));
    }

    /// <summary>
    /// SEC006: 检测 Regex.IsMatch/Match/Replace 等静态方法未设置超时参数
    /// </summary>
    private void AnalyzeStaticRegexWithoutTimeout(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            return;

        // 检查是否是 Regex.XXX 调用
        var callerText = memberAccess.Expression.ToString();
        if (!callerText.Equals("Regex", StringComparison.Ordinal))
            return;

        string methodName = memberAccess.Name.Identifier.Text;
        if (!RegexStaticMethods.Contains(methodName))
            return;

        // 检查是否传入了 timeout 参数（最后一个参数是 TimeSpan 类型）
        if (invocation.ArgumentList == null)
            return;

        var args = invocation.ArgumentList.Arguments;

        // 检查最后一个参数是否是 TimeSpan 相关
        if (args.Count > 0)
        {
            var lastArg = args[args.Count - 1].Expression.ToString();
            if (lastArg.Contains("TimeSpan") ||
                lastArg.Contains("FromSeconds") ||
                lastArg.Contains("FromMilliseconds"))
                return; // 已设置超时
        }

        // Regex静态方法通常有带timeout参数的重载
        // 如果参数数量少于带timeout版本，则认为缺少超时参数
        // Regex.IsMatch(input, pattern) - 2参数，无超时
        // Regex.IsMatch(input, pattern, options, matchTimeout) - 4参数，有超时
        bool hasTimeoutOverload = methodName switch
        {
            "IsMatch" => args.Count >= 4,
            "Match" => args.Count >= 4,
            "Matches" => args.Count >= 4,
            "Replace" => args.Count >= 5 || (args.Count >= 4 && args[3].Expression.ToString().Contains("TimeSpan")),
            "Split" => args.Count >= 4,
            _ => false
        };

        if (!hasTimeoutOverload)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                RegexDosRule,
                invocation.GetLocation(),
                $"Regex.{methodName}(...)"));
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC007: 资源泄漏
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// 检查 LocalDeclarationStatementSyntax 是否带有 using 修饰符（C# 8.0 using declaration）
    /// </summary>
    private bool HasUsingModifier(LocalDeclarationStatementSyntax localDecl)
    {
        var nodeText = localDecl.ToString().TrimStart();
        return nodeText.StartsWith("using ", StringComparison.Ordinal);
    }

    /// <summary>
    /// 检查节点是否在传统 using(){} 语句内部
    /// </summary>
    private bool IsInsideUsingStatement(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is UsingStatementSyntax usingStmt)
            {
                // 检查该声明是否就是 using 的声明部分
                if (usingStmt.Declaration != null)
                {
                    // 这是一个 using(var x = ...) { } 语句
                    return true;
                }
            }
            // 如果遇到方法/lambda/匿名方法，不再向上查找
            if (parent is MethodDeclarationSyntax ||
                parent is AnonymousFunctionExpressionSyntax ||
                parent is LocalFunctionStatementSyntax)
                break;
            parent = parent.Parent;
        }
        return false;
    }

    /// <summary>
    /// SEC007: 检测 IDisposable 资源创建未使用 using 管理
    /// </summary>
    private void AnalyzeResourceLeak(SyntaxNodeAnalysisContext context)
    {
        var localDecl = (LocalDeclarationStatementSyntax)context.Node;

        // 检查是否已经是 using var 声明
        if (HasUsingModifier(localDecl))
            return;

        // 检查父节点是否是 using 语句
        if (IsInsideUsingStatement(localDecl))
            return;

        var declaration = localDecl.Declaration;

        foreach (var variable in declaration.Variables)
        {
            // 只检测初始化为 new Xxx(...) 的情况，避免误报 conn = GetConnection() 等工厂方法
            if (!(variable.Initializer?.Value is ObjectCreationExpressionSyntax objectCreation))
                continue;

            string typeName = GetSimpleTypeName(objectCreation.Type);
            if (!DisposableTypeNames.Contains(typeName))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                ResourceLeakRule,
                variable.GetLocation(),
                typeName));
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC008: 不安全临时文件创建
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// 检查是否是安全的随机文件名生成方式
    /// </summary>
    private bool IsSafeRandomFileName(ExpressionSyntax expression)
    {
        string text = expression.ToString();
        // Path.GetRandomFileName() — 密码学安全的随机文件名
        if (text.Contains("GetRandomFileName"))
            return true;
        // Guid.NewGuid().ToString() — 密码学安全的UUID
        if (text.Contains("Guid.NewGuid") || text.Contains("NewGuid()"))
            return true;
        // Path.GetTempFileName() — 系统生成的安全临时文件
        if (text.Contains("GetTempFileName"))
            return true;
        return false;
    }

    /// <summary>
    /// 检查文本是否包含DateTime相关的可预测表达式
    /// </summary>
    private bool IsDateTimeExpression(string text)
    {
        return text.IndexOf("DateTime.Now", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("DateTime.UtcNow", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("DateTime.Today", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf(".Ticks", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("Environment.TickCount", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 检查表达式是否是顺序性/可预测的ID（如计数器、进程ID等）
    /// </summary>
    private bool IsSequentialIdExpression(ExpressionSyntax expression)
    {
        string text = expression.ToString();
        return text.IndexOf("Process.GetCurrentProcess().Id", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("Environment.ProcessId", StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("Thread.CurrentThread.ManagedThreadId", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 识别可预测的临时文件名构造模式，返回模式描述；若是安全模式则返回null
    /// </summary>
    private string GetPredictableTempFilePattern(ExpressionSyntax expression)
    {
        // 先排除安全的随机文件名生成方式
        if (IsSafeRandomFileName(expression))
            return null;

        // 模式1：字符串插值 $"..."
        if (expression is InterpolatedStringExpressionSyntax interpolated)
        {
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolationSyntax interp)
                {
                    string interpText = interp.Expression.ToString();
                    if (IsDateTimeExpression(interpText) || IsSequentialIdExpression(interp.Expression))
                        return $"字符串插值包含可预测值({TruncateLongText(interpText, 30)})";
                }
            }
            return "字符串插值($\"\")构造的文件名结构可预测";
        }

        // 模式2：字符串拼接 "prefix_" + something
        if (expression is BinaryExpressionSyntax binary &&
            binary.OperatorToken.IsKind(SyntaxKind.PlusToken))
        {
            string binaryText = expression.ToString();
            if (IsDateTimeExpression(binaryText))
                return $"字符串拼接包含时间戳({TruncateLongText(binaryText, 30)})";
            return "字符串拼接(+)构造的文件名结构可预测";
        }

        // 模式3：string.Format(...)
        if (expression is InvocationExpressionSyntax invoc)
        {
            string invocText = expression.ToString();
            if (invocText.StartsWith("string.Format", StringComparison.OrdinalIgnoreCase) ||
                invocText.StartsWith("String.Format", StringComparison.OrdinalIgnoreCase))
                return "string.Format()构造的文件名结构可预测";

            if (IsDateTimeExpression(invocText))
                return "使用了时间戳(" + TruncateLongText(invocText, 30) + ")，时间戳可预测";
        }

        // 模式4：直接的成员访问表达式（DateTime.Now.Ticks 等）
        if (expression is MemberAccessExpressionSyntax)
        {
            string maText = expression.ToString();
            if (IsDateTimeExpression(maText))
                return "使用了时间戳(" + TruncateLongText(maText, 30) + ")，时间戳可预测";
        }

        return null;
    }

    /// <summary>
    /// 判断表达式是否是 Path.GetTempPath() 调用
    /// </summary>
    private bool IsGetTempPathCall(ExpressionSyntax expression)
    {
        if (!(expression is InvocationExpressionSyntax invocation))
            return false;
        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            return false;

        return memberAccess.Name.Identifier.Text.Equals("GetTempPath", StringComparison.Ordinal);
    }

    /// <summary>
    /// SEC008: 检测 Path.Combine(Path.GetTempPath(), predictable_name) 模式
    /// </summary>
    private void AnalyzeInsecureTempFile(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // 检查是否是 Path.Combine 调用
        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            return;

        string methodName = memberAccess.Name.Identifier.Text;
        if (!methodName.Equals("Combine", StringComparison.Ordinal))
            return;

        // 检查接收者是否是 Path（或全限定的 System.IO.Path）
        string receiverText = memberAccess.Expression.ToString();
        if (!receiverText.Equals("Path", StringComparison.Ordinal) &&
            !receiverText.EndsWith(".Path", StringComparison.Ordinal))
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count < 2)
            return;

        // 检查第一个参数是否是 Path.GetTempPath() 调用
        if (!IsGetTempPathCall(args[0].Expression))
            return;

        // 检查后续参数中是否有可预测的文件名构造方式
        for (int i = 1; i < args.Count; i++)
        {
            string predictablePattern = GetPredictableTempFilePattern(args[i].Expression);
            if (predictablePattern != null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InsecureTempFileRule,
                    args[i].GetLocation(),
                    predictablePattern));
                return; // 每个 Path.Combine 调用只报告一次
            }
        }
    }
}