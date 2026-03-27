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
        DiagnosticId, "CHN001", "CHN001", "CodeStyle", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string DLL002Id = "DLL002";
    private static readonly DiagnosticDescriptor MissingDllImportSearchPathsRule = new DiagnosticDescriptor(
        DLL002Id, "DLL002", "DLL002", "CodeStyle", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string EXC001Id = "EXC001";
    private static readonly DiagnosticDescriptor InvalidStackTraceUsageRule = new DiagnosticDescriptor(
        EXC001Id, "EXC001", "EXC001", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string DLL003Id = "DLL003";
    private static readonly DiagnosticDescriptor UnsafeDllSignatureRule = new DiagnosticDescriptor(
        DLL003Id, "DLL003", "DLL003", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string CODE001Id = "CODE001";
    private static readonly DiagnosticDescriptor InvalidCommentedCodeRule = new DiagnosticDescriptor(
        CODE001Id, "CODE001", "CODE001", "CodeStyle", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC001Id = "SEC001";
    private static readonly DiagnosticDescriptor SensitiveInfoInCodeRule = new DiagnosticDescriptor(
        SEC001Id, "SEC001", "SEC001", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC002Id = "SEC002";
    private static readonly DiagnosticDescriptor PathTraversalRule = new DiagnosticDescriptor(
        SEC002Id, "SEC002", "SEC002", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC003Id = "SEC003";
    private static readonly DiagnosticDescriptor SqlInjectionRule = new DiagnosticDescriptor(
        SEC003Id, "SEC003", "SEC003", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC004Id = "SEC004";
    private static readonly DiagnosticDescriptor UnsafeDeserializationRule = new DiagnosticDescriptor(
        SEC004Id, "SEC004", "SEC004", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC005Id = "SEC005";
    private static readonly DiagnosticDescriptor InsecureRandomRule = new DiagnosticDescriptor(
        SEC005Id, "SEC005", "SEC005", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC006Id = "SEC006";
    private static readonly DiagnosticDescriptor RegexDosRule = new DiagnosticDescriptor(
        SEC006Id, "SEC006", "SEC006", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC007Id = "SEC007";
    private static readonly DiagnosticDescriptor ResourceLeakRule = new DiagnosticDescriptor(
        SEC007Id, "SEC007", "SEC007", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC008Id = "SEC008";
    private static readonly DiagnosticDescriptor InsecureTempFileRule = new DiagnosticDescriptor(
        SEC008Id, "SEC008", "SEC008", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC009Id = "SEC009";
    private static readonly DiagnosticDescriptor UnsafeReflectionRule = new DiagnosticDescriptor(
        SEC009Id, "SEC009", "SEC009", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC010Id = "SEC010";
    private static readonly DiagnosticDescriptor RaceConditionRule = new DiagnosticDescriptor(
        SEC010Id, "SEC010", "SEC010", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);
    public const string SEC011Id = "SEC011";
    private static readonly DiagnosticDescriptor InsecureIpcRule = new DiagnosticDescriptor(
        SEC011Id, "SEC011", "SEC011", "Security", DiagnosticSeverity.Warning, isEnabledByDefault: true);

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
            InsecureTempFileRule,
            UnsafeReflectionRule,
            RaceConditionRule,
            InsecureIpcRule);

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

    private static readonly HashSet<string> UnsafeReflectionMethods = new HashSet<string>(StringComparer.Ordinal)
    {
        "GetType", "GetMethod", "GetField", "GetProperty", "GetMember",
        "GetMethods", "GetFields", "GetProperties", "GetMembers"
    };

    private static readonly HashSet<string> NonThreadSafeCollections = new HashSet<string>(StringComparer.Ordinal)
    {
        "Dictionary", "List", "HashSet", "Queue", "Stack",
        "SortedDictionary", "SortedList", "SortedSet", "LinkedList"
    };

    private static readonly HashSet<string> InsecureBindingTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "BasicHttpBinding", "WebHttpBinding"
    };

    
    private void Report(Action<Diagnostic> reportAction, string ruleId, string category, DiagnosticSeverity severity, Location location, params object[] messageArgs)
    {
        string title = AnalyzerI18n.GetString(ruleId + "_Title");
        string messageFormat = AnalyzerI18n.GetString(ruleId + "_MessageFormat");
        string description = AnalyzerI18n.GetString(ruleId + "_Description");

        for (int i = 0; i < messageArgs.Length; i++)
        {
            if (messageArgs[i] is LocalizableArgument la)
            {
                messageArgs[i] = la.ToString(null, null);
            }
        }

        var descriptor = new DiagnosticDescriptor(
            ruleId,
            title,
            messageFormat,
            category,
            severity,
            isEnabledByDefault: true,
            description: description);

        reportAction(Diagnostic.Create(descriptor, location, messageArgs));
    }

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

        // SEC009: 不安全反射
        context.RegisterSyntaxNodeAction(AnalyzeUnsafeReflection, SyntaxKind.InvocationExpression);

        // SEC010: 竞争条件
        context.RegisterSyntaxNodeAction(AnalyzeRaceConditionIncrement, SyntaxKind.PostIncrementExpression);
        context.RegisterSyntaxNodeAction(AnalyzeRaceConditionIncrement, SyntaxKind.PreIncrementExpression);
        context.RegisterSyntaxNodeAction(AnalyzeRaceConditionDecrement, SyntaxKind.PostDecrementExpression);
        context.RegisterSyntaxNodeAction(AnalyzeRaceConditionDecrement, SyntaxKind.PreDecrementExpression);
        context.RegisterSyntaxNodeAction(AnalyzeRaceConditionCompoundAssignment, SyntaxKind.AddAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeRaceConditionCompoundAssignment, SyntaxKind.SubtractAssignmentExpression);
        context.RegisterSyntaxNodeAction(AnalyzeRaceConditionCheckThenUse, SyntaxKind.IfStatement);
        context.RegisterSyntaxNodeAction(AnalyzeNonThreadSafeCollection, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeNonThreadSafeCollectionIndexer, SyntaxKind.SimpleAssignmentExpression);

        // SEC011: 不安全IPC
        context.RegisterSyntaxNodeAction(AnalyzeInsecureBinding, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInsecureEndpoint, SyntaxKind.ObjectCreationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInsecureHttpUrl, SyntaxKind.InvocationExpression);
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
                Report(context.ReportDiagnostic, DiagnosticId, "CodeStyle", DiagnosticSeverity.Warning, location, commentText);
            }
        }

        // 检测字符串字面量中的中文字符
        foreach (var node in root.DescendantNodes())
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var literal = (LiteralExpressionSyntax)node;
                var value = literal.Token.ValueText;
                if (!string.IsNullOrEmpty(value) && chineseRegex.IsMatch(value))
                {
                    var loc = Location.Create(context.Tree, node.Span);
                    Report(context.ReportDiagnostic, DiagnosticId, "CodeStyle", DiagnosticSeverity.Warning, loc, value);
                }
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
            Report(context.ReportDiagnostic, DLL002Id, "CodeStyle", DiagnosticSeverity.Warning, dllImportAttrs.First().GetLocation()));
        }

        foreach (var dllImportAttr in dllImportAttrs)
        {
            var dllName = GetDllNameFromAttribute(dllImportAttr);

            if (string.IsNullOrEmpty(dllName))
            {
                Report(context.ReportDiagnostic, DLL003Id, "Security", DiagnosticSeverity.Warning, dllImportAttr.GetLocation(),
                    new LocalizableArgument("DLL003_Arg_Unknown");
                continue;
            }

            var searchPath = GetSearchPathFromAttributes(method, context);

            // 严格模式查找：只在指定路径中查找，不回退到系统目录
            var dllPath = FindDllPathStrict(dllName, searchPath);

            if (dllPath == null)
            {
                Report(context.ReportDiagnostic, DLL003Id, "Security", DiagnosticSeverity.Warning, dllImportAttr.GetLocation(),
                    new LocalizableArgument("DLL003_Arg_NotFound", dllName));
                continue;
            }

            if (!IsDllSigned(dllPath))
            {
                Report(context.ReportDiagnostic, DLL003Id, "Security", DiagnosticSeverity.Warning, dllImportAttr.GetLocation(),
                    new LocalizableArgument("DLL003_Arg_InvalidSig", dllName));
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
            Report(context.ReportDiagnostic, EXC001Id, "Security", DiagnosticSeverity.Warning, usage.GetLocation(),
                usageText);
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
                    Report(context.ReportDiagnostic, CODE001Id, "CodeStyle", DiagnosticSeverity.Warning, location,
                        TruncateLongText(commentText, 50));
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
                    Report(context.ReportDiagnostic, CODE001Id, "CodeStyle", DiagnosticSeverity.Warning, location,
                        TruncateLongText(combinedText, 50));
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
                Report(context.ReportDiagnostic, SEC001Id, "Security", DiagnosticSeverity.Warning, variable.GetLocation(),
                    varName,
                    plainTextValue);
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

            Report(context.ReportDiagnostic, SEC001Id, "Security", DiagnosticSeverity.Warning, assignment.GetLocation(),
                varName,
                plainTextValue);
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
                Report(context.ReportDiagnostic, SEC002Id, "Security", DiagnosticSeverity.Warning, arg.GetLocation(),
                    new LocalizableArgument("SEC002_Arg_Method", methodName));
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
                Report(context.ReportDiagnostic, SEC002Id, "Security", DiagnosticSeverity.Warning, binaryExpr.GetLocation(),
                    new LocalizableArgument("SEC002_Arg_Concat"));
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
                Report(context.ReportDiagnostic, SEC002Id, "Security", DiagnosticSeverity.Warning, interp.GetLocation(),
                    new LocalizableArgument("SEC002_Arg_Interpolation"));
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
            // 豁免已知安全的系统路径/随机 API，避免误报
            var invocStr = invocation.ToString();
            if (invocStr.Contains("GetTempPath") ||
                invocStr.Contains("GetTempFileName") ||
                invocStr.Contains("GetRandomFileName") ||
                invocStr.Contains("NewGuid") ||
                invocStr.Contains("GetCurrentDirectory") ||
                invocStr.Contains("GetFolderPath"))
                return false;

            var invocText = invocStr.ToLowerInvariant();
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
    private LocalizableArgument GetStringBuildMethod(ExpressionSyntax expression)
    {
        // 字符串插值: $"SELECT {id}"
        if (expression is InterpolatedStringExpressionSyntax)
            return new LocalizableArgument("SEC003_Arg_Interpolation");

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
                return new LocalizableArgument("SEC003_Arg_Concat");
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
        LocalizableArgument buildMethod = GetStringBuildMethod(firstArg);
        if (buildMethod == null)
            return;

        Report(context.ReportDiagnostic, SEC003Id, "Security", DiagnosticSeverity.Warning, firstArg.GetLocation(),
            typeName,
            buildMethod);
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

        LocalizableArgument buildMethod = GetStringBuildMethod(assignment.Right);
        if (buildMethod == null)
            return;

        Report(context.ReportDiagnostic, SEC003Id, "Security", DiagnosticSeverity.Warning, assignment.Right.GetLocation(),
            "CommandText",
            buildMethod);
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
        LocalizableArgument buildMethod = GetStringBuildMethod(firstArg);
        if (buildMethod == null)
            return;

        Report(context.ReportDiagnostic, SEC003Id, "Security", DiagnosticSeverity.Warning, firstArg.GetLocation(),
            methodName,
            buildMethod);
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
            Report(context.ReportDiagnostic, SEC004Id, "Security", DiagnosticSeverity.Warning, objectCreation.GetLocation(),
                new LocalizableArgument("SEC004_Arg_NewType", typeName));
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
                Report(context.ReportDiagnostic, SEC004Id, "Security", DiagnosticSeverity.Warning, assignment.GetLocation(),
                    new LocalizableArgument("SEC004_Arg_HandlingUnsafe", rightText));
                return;
            }
        }

        // 如果是其他值（可能是变量），也报告
        if (!rightText.Contains("None"))
        {
            Report(context.ReportDiagnostic, SEC004Id, "Security", DiagnosticSeverity.Warning, assignment.GetLocation(),
                new LocalizableArgument("SEC004_Arg_HandlingRecommend", rightText));
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

        Report(context.ReportDiagnostic, SEC005Id, "Security", DiagnosticSeverity.Warning, objectCreation.GetLocation(),
            new LocalizableArgument("SEC005_Arg_Construct"));
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
        if (callerText.EndsWith("Random", StringComparison.OrdinalIgnoreCase) &&
            !callerText.Contains("Crypto") &&
            !callerText.Contains("Secure") &&
            !callerText.Contains("Security"))
        {
            Report(context.ReportDiagnostic, SEC005Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                new LocalizableArgument("SEC005_Arg_MethodCall", methodName));
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

        Report(context.ReportDiagnostic, SEC006Id, "Security", DiagnosticSeverity.Warning, objectCreation.GetLocation(),
            new LocalizableArgument("SEC006_Arg_NewRegex"));
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
            Report(context.ReportDiagnostic, SEC006Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                new LocalizableArgument("SEC006_Arg_StaticRegex", methodName));
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

            Report(context.ReportDiagnostic, SEC007Id, "Security", DiagnosticSeverity.Warning, variable.GetLocation(),
                typeName);
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
    private LocalizableArgument GetPredictableTempFilePattern(ExpressionSyntax expression)
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
                        return new LocalizableArgument("SEC008_Arg_InterpPredictable", TruncateLongText(interpText, 30));
                }
            }
            return new LocalizableArgument("SEC008_Arg_InterpStructure");
        }

        // 模式2：字符串拼接 "prefix_" + something
        if (expression is BinaryExpressionSyntax binary &&
            binary.OperatorToken.IsKind(SyntaxKind.PlusToken))
        {
            string binaryText = expression.ToString();
            if (IsDateTimeExpression(binaryText))
                return new LocalizableArgument("SEC008_Arg_ConcatTimestamp", TruncateLongText(binaryText, 30));
            return new LocalizableArgument("SEC008_Arg_ConcatStructure");
        }

        // 模式3：string.Format(...)
        if (expression is InvocationExpressionSyntax invoc)
        {
            string invocText = expression.ToString();
            if (invocText.StartsWith("string.Format", StringComparison.OrdinalIgnoreCase) ||
                invocText.StartsWith("String.Format", StringComparison.OrdinalIgnoreCase))
                return new LocalizableArgument("SEC008_Arg_FormatStructure");

            if (IsDateTimeExpression(invocText))
                return new LocalizableArgument("SEC008_Arg_Timestamp1", TruncateLongText(invocText, 30));
        }

        // 模式4：直接的成员访问表达式（DateTime.Now.Ticks 等）
        if (expression is MemberAccessExpressionSyntax)
        {
            string maText = expression.ToString();
            if (IsDateTimeExpression(maText))
                return new LocalizableArgument("SEC008_Arg_Timestamp2", TruncateLongText(maText, 30));
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
            LocalizableArgument predictablePattern = GetPredictableTempFilePattern(args[i].Expression);
            if (predictablePattern != null)
            {
                Report(context.ReportDiagnostic, SEC008Id, "Security", DiagnosticSeverity.Warning, args[i].GetLocation(),
                    predictablePattern);
                return; // 每个 Path.Combine 调用只报告一次
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC009: 不安全的反射使用
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// SEC009: 检测 Type.GetType/Activator.CreateInstance/GetMethod 等不安全反射调用
    /// </summary>
    private void AnalyzeUnsafeReflection(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        
        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            return;

        string methodName = memberAccess.Name.Identifier.Text;

        // 检测 Type.GetType(userInput)
        if (methodName.Equals("GetType", StringComparison.Ordinal))
        {
            var callerText = memberAccess.Expression.ToString();
            if (callerText.Equals("Type", StringComparison.Ordinal) || 
                callerText.EndsWith(".Type", StringComparison.Ordinal))
            {
                if (invocation.ArgumentList?.Arguments.Count > 0)
                {
                    var arg = invocation.ArgumentList.Arguments[0].Expression;
                    if (!IsStringLiteral(arg))
                    {
                        Report(context.ReportDiagnostic, SEC009Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                            new LocalizableArgument("SEC009_Arg_GetType"));
                    }
                }
            }
        }

        // 检测 GetMethod/GetField/GetProperty 使用 BindingFlags.NonPublic
        if (methodName.Equals("GetMethod", StringComparison.Ordinal) ||
            methodName.Equals("GetField", StringComparison.Ordinal) ||
            methodName.Equals("GetProperty", StringComparison.Ordinal))
        {
            if (invocation.ArgumentList != null)
            {
                foreach (var arg in invocation.ArgumentList.Arguments)
                {
                    var argText = arg.Expression.ToString();
                    if (argText.Contains("NonPublic"))
                    {
                        Report(context.ReportDiagnostic, SEC009Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                            new LocalizableArgument("SEC009_Arg_NonPublic", methodName));
                        return;
                    }
                }
            }
        }

        // 检测 MethodInfo.Invoke 调用
        if (methodName.Equals("Invoke", StringComparison.Ordinal))
        {
            var callerText = memberAccess.Expression.ToString();
            if (callerText.Contains("GetMethod") || callerText.Contains("method"))
            {
                Report(context.ReportDiagnostic, SEC009Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                    new LocalizableArgument("SEC009_Arg_Invoke"));
            }
        }
    }

    private bool IsStringLiteral(ExpressionSyntax expression)
    {
        return expression is LiteralExpressionSyntax literal && 
               literal.IsKind(SyntaxKind.StringLiteralExpression);
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC010: 线程同步/竞争条件
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// SEC010: 检测字段的自增操作是否缺少同步
    /// </summary>
    private void AnalyzeRaceConditionIncrement(SyntaxNodeAnalysisContext context)
    {
        var increment = context.Node;
        ExpressionSyntax operand = null;

        if (increment is PostfixUnaryExpressionSyntax postfix)
            operand = postfix.Operand;
        else if (increment is PrefixUnaryExpressionSyntax prefix)
            operand = prefix.Operand;

        if (operand == null) return;

        // 只检查字段访问
        if (!IsFieldAccess(operand)) return;

        // 检查是否在 lock 语句内
        if (IsInsideLockStatement(increment)) return;

        // 检查是否在 Interlocked 调用中
        if (IsInsideInterlockedCall(increment)) return;

        Report(context.ReportDiagnostic, SEC010Id, "Security", DiagnosticSeverity.Warning, increment.GetLocation(),
            new LocalizableArgument("SEC010_Arg_Increment"));
    }

    /// <summary>
    /// SEC010: 检测字段的自减操作是否缺少同步
    /// </summary>
    private void AnalyzeRaceConditionDecrement(SyntaxNodeAnalysisContext context)
    {
        var decrement = context.Node;
        ExpressionSyntax operand = null;

        if (decrement is PostfixUnaryExpressionSyntax postfix)
            operand = postfix.Operand;
        else if (decrement is PrefixUnaryExpressionSyntax prefix)
            operand = prefix.Operand;

        if (operand == null) return;

        if (!IsFieldAccess(operand)) return;
        if (IsInsideLockStatement(decrement)) return;
        if (IsInsideInterlockedCall(decrement)) return;

        Report(context.ReportDiagnostic, SEC010Id, "Security", DiagnosticSeverity.Warning, decrement.GetLocation(),
            new LocalizableArgument("SEC010_Arg_Decrement"));
    }

    /// <summary>
    /// SEC010: 检测字段的复合赋值操作是否缺少同步
    /// </summary>
    private void AnalyzeRaceConditionCompoundAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        if (!IsFieldAccess(assignment.Left)) return;
        if (IsInsideLockStatement(assignment)) return;

        string opText = assignment.OperatorToken.Text;
        Report(context.ReportDiagnostic, SEC010Id, "Security", DiagnosticSeverity.Warning, assignment.GetLocation(),
            new LocalizableArgument("SEC010_Arg_Compound", opText));
    }

    /// <summary>
    /// SEC010: 检测 check-then-use 模式
    /// </summary>
    private void AnalyzeRaceConditionCheckThenUse(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;

        // 检查是否在 lock 语句内
        if (IsInsideLockStatement(ifStatement)) return;

        // 查找条件中的字段引用
        var conditionFields = GetFieldReferences(ifStatement.Condition);
        if (!conditionFields.Any()) return;

        // 查找语句体中对相同字段的修改
        var bodyModifications = GetFieldModifications(ifStatement.Statement);

        // 检查是否有相同字段在条件和语句体中都被访问
        foreach (var field in conditionFields)
        {
            if (bodyModifications.Contains(field))
            {
                Report(context.ReportDiagnostic, SEC010Id, "Security", DiagnosticSeverity.Warning, ifStatement.GetLocation(),
                    new LocalizableArgument("SEC010_Arg_CheckThenUse", field));
                return;
            }
        }
    }

    /// <summary>
    /// SEC010: 检测非线程安全集合的使用
    /// </summary>
    private void AnalyzeNonThreadSafeCollection(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            return;

        var callerText = memberAccess.Expression.ToString();

        // 检查是否是字段访问
        if (!callerText.StartsWith("_") && !callerText.StartsWith("this._"))
            return;

        // 检查是否在 lock 内
        if (IsInsideLockStatement(invocation)) return;

        string methodName = memberAccess.Name.Identifier.Text;

        // 检查是否是修改操作
        var modifyMethods = new HashSet<string> { "Add", "Remove", "Clear", "Insert", "RemoveAt", "RemoveAll" };
        if (modifyMethods.Contains(methodName))
        {
            // 尝试判断集合类型
            foreach (var collType in NonThreadSafeCollections)
            {
                if (callerText.Contains(collType) || IsNonThreadSafeCollectionField(memberAccess.Expression))
                {
                    Report(context.ReportDiagnostic, SEC010Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                        new LocalizableArgument("SEC010_Arg_CollectionMethod", methodName));
                    return;
                }
            }
        }
    }

    private bool IsFieldAccess(ExpressionSyntax expression)
    {
        var text = expression.ToString();
        // 简单启发式：以 _ 开头或 this._ 开头的标识符视为字段
        return text.StartsWith("_") || text.StartsWith("this._") ||
               (expression is MemberAccessExpressionSyntax ma && 
                ma.Expression is ThisExpressionSyntax);
    }

    private bool IsInsideLockStatement(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is LockStatementSyntax)
                return true;
            if (parent is MethodDeclarationSyntax || parent is ClassDeclarationSyntax)
                break;
            parent = parent.Parent;
        }
        return false;
    }

    private bool IsInsideInterlockedCall(SyntaxNode node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            if (parent is InvocationExpressionSyntax invocation)
            {
                var invocText = invocation.Expression.ToString();
                if (invocText.Contains("Interlocked"))
                    return true;
            }
            if (parent is MethodDeclarationSyntax)
                break;
            parent = parent.Parent;
        }
        return false;
    }

    private HashSet<string> GetFieldReferences(SyntaxNode node)
    {
        var fields = new HashSet<string>();
        foreach (var identifier in node.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.Text;
            if (name.StartsWith("_"))
                fields.Add(name);
        }
        foreach (var memberAccess in node.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            if (memberAccess.Expression is ThisExpressionSyntax)
            {
                fields.Add(memberAccess.Name.Identifier.Text);
            }
        }
        return fields;
    }

    private HashSet<string> GetFieldModifications(SyntaxNode node)
    {
        var fields = new HashSet<string>();
        
        foreach (var assignment in node.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            var leftText = assignment.Left.ToString();
            if (leftText.StartsWith("_"))
                fields.Add(leftText);
            else if (assignment.Left is MemberAccessExpressionSyntax ma && 
                     ma.Expression is ThisExpressionSyntax)
                fields.Add(ma.Name.Identifier.Text);
        }

        foreach (var unary in node.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
        {
            var opText = unary.Operand.ToString();
            if (opText.StartsWith("_"))
                fields.Add(opText);
        }

        foreach (var unary in node.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
        {
            var opText = unary.Operand.ToString();
            if (opText.StartsWith("_"))
                fields.Add(opText);
        }

        return fields;
    }

    private bool IsNonThreadSafeCollectionField(ExpressionSyntax expression)
    {
        return expression is IdentifierNameSyntax id && id.Identifier.Text.StartsWith("_");
    }

    /// <summary>
    /// SEC010: 检测非线程安全集合的索引器赋值 (如 _dict[key] = value)
    /// </summary>
    private void AnalyzeNonThreadSafeCollectionIndexer(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // 检查左侧是否是索引器访问 (ElementAccessExpression)
        if (!(assignment.Left is ElementAccessExpressionSyntax elementAccess))
            return;

        var collectionExpr = elementAccess.Expression.ToString();

        // 检查是否是字段访问
        if (!collectionExpr.StartsWith("_") && !collectionExpr.StartsWith("this._"))
            return;

        // 检查字段是否是 Concurrent 集合类型 (通过查找字段声明)
        if (IsConcurrentCollectionField(elementAccess.Expression, context))
            return;

        // 检查是否在 lock 内
        if (IsInsideLockStatement(assignment)) return;

        Report(context.ReportDiagnostic, SEC010Id, "Security", DiagnosticSeverity.Warning, assignment.GetLocation(),
            new LocalizableArgument("SEC010_Arg_CollectionIndexer"));
    }

    /// <summary>
    /// 检查字段是否是 Concurrent 集合类型
    /// </summary>
    private bool IsConcurrentCollectionField(ExpressionSyntax expression, SyntaxNodeAnalysisContext context)
    {
        string fieldName = expression.ToString().TrimStart('_').Replace("this._", "");
        if (expression is IdentifierNameSyntax id)
            fieldName = id.Identifier.Text;

        // 查找包含此字段的类声明
        var classDecl = expression.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl == null) return false;

        // 在类中查找字段声明
        foreach (var member in classDecl.Members)
        {
            if (member is FieldDeclarationSyntax fieldDecl)
            {
                foreach (var variable in fieldDecl.Declaration.Variables)
                {
                    if (variable.Identifier.Text == fieldName || 
                        "_" + variable.Identifier.Text == fieldName ||
                        variable.Identifier.Text == expression.ToString())
                    {
                        var typeText = fieldDecl.Declaration.Type.ToString();
                        if (typeText.Contains("Concurrent"))
                            return true;
                    }
                }
            }
        }

        return false;
    }

    // ═════════════════════════════════════════════════════════════════
    // SEC011: 不安全的IPC/远程调用
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// SEC011: 检测不安全的绑定类型
    /// </summary>
    private void AnalyzeInsecureBinding(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        string typeName = GetSimpleTypeName(objectCreation.Type);

        // 检测 BasicHttpBinding（默认无加密）
        if (typeName.Equals("BasicHttpBinding", StringComparison.Ordinal))
        {
            // 检查是否传入了安全模式参数
            if (objectCreation.ArgumentList == null || objectCreation.ArgumentList.Arguments.Count == 0)
            {
                Report(context.ReportDiagnostic, SEC011Id, "Security", DiagnosticSeverity.Warning, objectCreation.GetLocation(),
                    new LocalizableArgument("SEC011_Arg_BasicHttp"));
                return;
            }
        }

        // 检测 NetTcpBinding/WSHttpBinding 使用 SecurityMode.None
        if (typeName.Equals("NetTcpBinding", StringComparison.Ordinal) ||
            typeName.Equals("WSHttpBinding", StringComparison.Ordinal))
        {
            if (objectCreation.ArgumentList?.Arguments.Count > 0)
            {
                var argText = objectCreation.ArgumentList.Arguments[0].Expression.ToString();
                if (argText.Contains("SecurityMode.None") || argText.EndsWith(".None"))
                {
                    Report(context.ReportDiagnostic, SEC011Id, "Security", DiagnosticSeverity.Warning, objectCreation.GetLocation(),
                        new LocalizableArgument("SEC011_Arg_SecurityNone", typeName));
                }
            }
        }
    }

    /// <summary>
    /// SEC011: 检测不安全的端点地址
    /// </summary>
    private void AnalyzeInsecureEndpoint(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;
        string typeName = GetSimpleTypeName(objectCreation.Type);

        // 检测 EndpointAddress 或 Uri 使用 HTTP
        if (typeName.Equals("EndpointAddress", StringComparison.Ordinal) ||
            typeName.Equals("Uri", StringComparison.Ordinal))
        {
            if (objectCreation.ArgumentList?.Arguments.Count > 0)
            {
                var arg = objectCreation.ArgumentList.Arguments[0].Expression;
                if (IsInsecureHttpUrl(arg))
                {
                    Report(context.ReportDiagnostic, SEC011Id, "Security", DiagnosticSeverity.Warning, objectCreation.GetLocation(),
                        new LocalizableArgument("SEC011_Arg_HttpEndpoint"));
                }
            }
        }
    }

    /// <summary>
    /// SEC011: 检测方法调用中的不安全 HTTP URL
    /// </summary>
    private void AnalyzeInsecureHttpUrl(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            return;

        string methodName = memberAccess.Name.Identifier.Text;

        // 检测 GrpcChannel.ForAddress
        if (methodName.Equals("ForAddress", StringComparison.Ordinal))
        {
            var callerText = memberAccess.Expression.ToString();
            if (callerText.Contains("GrpcChannel") || callerText.EndsWith("Channel"))
            {
                if (invocation.ArgumentList?.Arguments.Count > 0)
                {
                    var arg = invocation.ArgumentList.Arguments[0].Expression;
                    if (IsInsecureHttpUrl(arg))
                    {
                        Report(context.ReportDiagnostic, SEC011Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                            new LocalizableArgument("SEC011_Arg_GrpcHttp"));
                    }
                }
            }
        }

        // 检测 WebClient.DownloadString 等方法
        if (methodName.Equals("DownloadString", StringComparison.Ordinal) ||
            methodName.Equals("DownloadData", StringComparison.Ordinal) ||
            methodName.Equals("UploadString", StringComparison.Ordinal) ||
            methodName.Equals("UploadData", StringComparison.Ordinal))
        {
            if (invocation.ArgumentList?.Arguments.Count > 0)
            {
                var arg = invocation.ArgumentList.Arguments[0].Expression;
                if (IsInsecureHttpUrl(arg))
                {
                    Report(context.ReportDiagnostic, SEC011Id, "Security", DiagnosticSeverity.Warning, invocation.GetLocation(),
                        new LocalizableArgument("SEC011_Arg_WebClientHttp", methodName));
                }
            }
        }
    }

    private bool IsInsecureHttpUrl(ExpressionSyntax expression)
    {
        string text = expression.ToString().Trim('"');
        
        // 跳过本地地址
        if (text.Contains("localhost") || text.Contains("127.0.0.1") || text.Contains("[::1]"))
            return false;

        // 检测 http:// 开头（但不是 https://）
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return true;

        // 检测变量中包含 http://
        if (expression is IdentifierNameSyntax)
            return false; // 无法在编译时确定变量值，不报告

        return false;
    }

    internal class LocalizableArgument : IFormattable
    {
        private readonly string _key;
        private readonly object[] _args;

        public LocalizableArgument(string key, params object[] args)
        {
            _key = key;
            _args = args;
        }

        public override string ToString() => ToString(null, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            var culture = formatProvider as System.Globalization.CultureInfo;
            string localizedString = LenovoAnalyzer.Resources.ResourceManager.GetString(_key, culture);
            if (string.IsNullOrEmpty(localizedString)) localizedString = _key;
            
            if (_args != null && _args.Length > 0)
            {
                return string.Format(culture, localizedString, _args);
            }
            return localizedString;
        }
    }
}