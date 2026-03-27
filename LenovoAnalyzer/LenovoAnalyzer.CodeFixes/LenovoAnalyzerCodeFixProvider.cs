using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using LenovoAnalyzer;

namespace LenovoQiraCodeAnalyzer
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LenovoQiraCodeAnalyzerCodeFixProvider)), Shared]
    public class LenovoQiraCodeAnalyzerCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds
            => ImmutableArray.Create(
                LenovoQiraCodeAnalyzerAnalyzer.DiagnosticId,
                LenovoQiraCodeAnalyzerAnalyzer.MissingDllImportSearchPathsId,
                LenovoQiraCodeAnalyzerAnalyzer.InvalidStackTraceUsageId,
                LenovoQiraCodeAnalyzerAnalyzer.UnsafeDllSignatureId,
                LenovoQiraCodeAnalyzerAnalyzer.InvalidCommentedCodeId);

        public override FixAllProvider GetFixAllProvider()
            => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            if (root == null) return;
            foreach (var diag in context.Diagnostics)
            {
                if (diag.Id == LenovoQiraCodeAnalyzerAnalyzer.DiagnosticId)
                {
                    var commentTrivia = root.DescendantTrivia(diag.Location.SourceSpan)
                .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                                     t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                                     t.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia));
                    if (commentTrivia != default)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: CodeFixResources.ResourceManager.GetString("CodeFix_DeleteChineseComment") ?? "删除中文注释",
                                createChangedDocument: c => RemoveCommentAsync(context.Document, commentTrivia, c),
                                equivalenceKey: "DeleteChineseComment"),
                            diag);
                    }
                }
                else if (diag.Id == LenovoQiraCodeAnalyzerAnalyzer.MissingDllImportSearchPathsId)
                {
                    var attributeList = await FindDllImportAttributeList(root, diag.Location.SourceSpan, context.Document, context.CancellationToken);
                    if (attributeList != null)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: CodeFixResources.ResourceManager.GetString("CodeFix_AddDllImportSearchPaths") ?? "添加DefaultDllImportSearchPaths属性",
                                createChangedDocument: c => AddDllImportSearchPathsAsync(context.Document, attributeList, c),
                                equivalenceKey: "AddDllImportSearchPaths"),
                            diag);
                    }
                }
                else if (diag.Id == LenovoQiraCodeAnalyzerAnalyzer.InvalidStackTraceUsageId)
                {
                    var memberAccess = root.FindNode(diag.Location.SourceSpan) as MemberAccessExpressionSyntax;
                    if (memberAccess != null)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: CodeFixResources.ResourceManager.GetString("CodeFix_ReplaceStackTraceWithMessage") ?? "将堆栈信息替换为错误消息",
                                createChangedDocument: c => ReplaceStackTraceWithMessageAsync(context.Document, memberAccess, c),
                                equivalenceKey: "ReplaceStackTraceWithMessage"),
                            diag);
                    }
                }
                else if (diag.Id == LenovoQiraCodeAnalyzerAnalyzer.UnsafeDllSignatureId)
                {
                    context.RegisterCodeFix(
                        CodeAction.Create(
                            title: CodeFixResources.ResourceManager.GetString("CodeFix_ShowDllSignatureHelp") ?? "查看DLL具体安全信息帮助",
                            createChangedDocument: c => ShowSignatureHelpAsync(context.Document, c),
                            equivalenceKey: "ShowDllSignatureHelp"),
                        diag);
                }
                else if (diag.Id == LenovoQiraCodeAnalyzerAnalyzer.InvalidCommentedCodeId)
                {
                    var commentTrivia = root.DescendantTrivia(diag.Location.SourceSpan)
                        .FirstOrDefault(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                                             t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                                             t.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia));
                    if (commentTrivia != default)
                    {
                        context.RegisterCodeFix(
                            CodeAction.Create(
                                title: CodeFixResources.ResourceManager.GetString("CodeFix_RemoveInvalidCommentedCode") ?? "移除包含无效代码的注释",
                                createChangedDocument: c => RemoveCommentAsync(context.Document, commentTrivia, c),
                                equivalenceKey: "RemoveInvalidCommentedCode"),
                            diag);
                    }
                }
            }
        }

        private async Task<Document> ShowSignatureHelpAsync(Document document, CancellationToken cancellationToken)
        {
            //try
            //{
            //    var helpUrl = "https://confluence.tc.lenovo.com/spaces/LS/pages/38765515/Using+Lenovo+Certificate+Validation";
            //    Process.Start(new ProcessStartInfo(helpUrl)
            //    {
            //        UseShellExecute = true
            //    });
            //}
            //catch (Exception ex)
            //{

            //}
            return await Task.FromResult(document);
        }

        private async Task<AttributeListSyntax> FindDllImportAttributeList(SyntaxNode root, TextSpan span, Document document, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var candidateNodes = root.FindNode(span).AncestorsAndSelf().OfType<AttributeListSyntax>();
            foreach (var attrList in candidateNodes)
            {
                if (attrList.Attributes.Any(attr =>
                {
                    var symbol = semanticModel.GetSymbolInfo(attr, cancellationToken).Symbol as IMethodSymbol;
                    return symbol != null && IsDllImportAttribute(symbol.ContainingType);
                }))
                {
                    return attrList;
                }
            }
            return null;
        }

        private async Task<Document> RemoveCommentAsync(Document document, SyntaxTrivia commentTrivia, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            var replacement = commentTrivia.IsKind(SyntaxKind.EndOfLineTrivia)
                ? commentTrivia
                : SyntaxFactory.Whitespace("");

            var newRoot = root.ReplaceTrivia(commentTrivia, replacement)
                .WithAdditionalAnnotations(Formatter.Annotation);

            return document.WithSyntaxRoot(Formatter.Format(newRoot, document.Project.Solution.Workspace));
        }

        private async Task<Document> AddDllImportSearchPathsAsync(Document document, AttributeListSyntax attributeList, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            var searchPathsAttr = SyntaxFactory.Attribute(
                SyntaxFactory.IdentifierName("DefaultDllImportSearchPaths"),
                SyntaxFactory.AttributeArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.AttributeArgument(
                            SyntaxFactory.IdentifierName("DllImportSearchPath.ApplicationDirectory")))));

            var newAttributeList = SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(searchPathsAttr))
                .WithAdditionalAnnotations(Formatter.Annotation);

            var newRoot = root.InsertNodesAfter(attributeList, new[] { newAttributeList });

            return document.WithSyntaxRoot(Formatter.Format(newRoot, document.Project.Solution.Workspace));
        }

        private bool IsDllImportAttribute(INamedTypeSymbol type)
        {
            return type != null
                && type.ContainingNamespace?.ToString() == "System.Runtime.InteropServices"
                && type.MetadataName == "DllImportAttribute";
        }

        private async Task<Document> ReplaceStackTraceWithMessageAsync(Document document, MemberAccessExpressionSyntax memberAccess, CancellationToken cancellationToken)
        {
            var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (root == null) return document;

            var newMemberAccess = memberAccess.WithName(
                SyntaxFactory.IdentifierName(memberAccess.Name.Identifier.Text == "StackTrace" ? "Message" : "Message"));

            var newRoot = root.ReplaceNode(memberAccess, newMemberAccess)
                .WithAdditionalAnnotations(Formatter.Annotation);

            return document.WithSyntaxRoot(Formatter.Format(newRoot, document.Project.Solution.Workspace));
        }
    }
}