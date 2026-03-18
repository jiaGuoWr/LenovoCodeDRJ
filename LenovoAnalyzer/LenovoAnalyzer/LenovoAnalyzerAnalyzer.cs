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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(Rule, MissingDllImportSearchPathsRule, InvalidStackTraceUsageRule, UnsafeDllSignatureRule,
            InvalidCommentedCodeRule,
            SensitiveInfoInCodeRule);

    private static readonly HashSet<string> SensitiveKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "token", "secret", "key", "apikey",
        "accesskey", "privatekey", "secretkey", "credential",
         "auth", "authorization", "passcode", "certificate", "secretid"
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

            var dllPath = FindDllPath(dllName, searchPath);

            if (dllPath == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    UnsafeDllSignatureRule,
                    dllImportAttr.GetLocation(),
                    $"{dllName}（未找到该DLL文件）"));
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
            X509Certificate cert = X509Certificate.CreateFromSignedFile(dllPath);
            if (cert == null)
                return false;

            X509Certificate2 cert2 = new X509Certificate2(cert);
            if (cert2 == null)
                return false;

            X509Chain chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return chain.Build(cert2);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private string GetSearchPathFromAttributes(MethodDeclarationSyntax method, SyntaxNodeAnalysisContext context)
    {
        const string PROJECT_TOP_FOLDER_NAME = "WpfApp1";
        const string RELATIVE_OUTPUT_PATH = "bin\\Debug\\net8.0-windows";

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
                            string projectTopDir = FindProjectTopDirectory(codeFilePath, PROJECT_TOP_FOLDER_NAME);
                            if (!string.IsNullOrEmpty(projectTopDir))
                            {
                                string dllDir = Path.Combine(projectTopDir, RELATIVE_OUTPUT_PATH);
                                if (Directory.Exists(dllDir)) paths.Add(dllDir);
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

    private string FindProjectTopDirectory(string codeFilePath, string topFolderName)
    {
        try
        {
            string currentDir = Path.GetDirectoryName(codeFilePath);
            for (int i = 0; i < 10; i++)
            {
                if (string.IsNullOrEmpty(currentDir)) break;
                if (new DirectoryInfo(currentDir).Name.Equals(topFolderName, StringComparison.OrdinalIgnoreCase))
                {
                    return currentDir;
                }
                currentDir = Directory.GetParent(currentDir)?.FullName;
            }
            return Path.GetDirectoryName(codeFilePath);
        }
        catch
        {
            return null;
        }
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
        var codePattern = new Regex(
            @"\b(if|else|for|while|public|private|protected|class|void|int|string|bool|object|var|return)\b" +
            @"|[\{\}\(\);=\+\-\*\/<>]|;=",
            RegexOptions.Compiled);

        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            string rawText = trivia.ToString().Trim();
            if (string.IsNullOrWhiteSpace(rawText)) continue;

            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
                !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) &&
                !trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia))
                continue;

            string commentText = ExtractCommentText(rawText, trivia.Kind());

            if (codePattern.IsMatch(commentText))
            {
                var matches = codePattern.Matches(commentText);
                if (matches.Count >= 2)
                {
                    var location = Location.Create(context.Tree, trivia.Span);
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidCommentedCodeRule,
                        location,
                        TruncateLongText(commentText, 50)));
                }
            }
        }
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
}