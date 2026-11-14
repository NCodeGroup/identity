// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace NCode.Identity.Analyzers;

/// <summary>
/// Reports an <c>IgnoreQueryFilters()</c> call that no <c>// Cross-tenant: &lt;why&gt;</c> comment explains.
/// </summary>
// See ADR-1018.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossTenantQueryAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The identifier of the diagnostic that reports an unexplained <c>IgnoreQueryFilters()</c> call.
    /// </summary>
    public const string DiagnosticId = "NCID0004";

    private const string MethodName = "IgnoreQueryFilters";

    private const string MethodNamespace = "Microsoft.EntityFrameworkCore";

    private const string CommentPrefix = "// Cross-tenant:";

    private static string[] SkippedAssemblySuffixes { get; } = [".Tests", ".TestSupport"];

    private static DiagnosticDescriptor Rule { get; } =
        new(
            DiagnosticId,
            title: "Explain a cross-tenant query",
            messageFormat: "'IgnoreQueryFilters()' needs a '// Cross-tenant: <why>' comment on the line directly above it",
            category: "Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "An IgnoreQueryFilters() call reads across tenants. A '// Cross-tenant: <why>' comment leads the statement, or leads the '.IgnoreQueryFilters()' line of a fluent chain, and states why the query reads across tenants.",
            helpLinkUri: "https://github.com/NCodeGroup/identity/blob/main/docs/decisions/1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md"
        );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var assemblyName = context.Compilation.AssemblyName;
        if (assemblyName is not null)
        {
            foreach (var suffix in SkippedAssemblySuffixes)
            {
                if (assemblyName.EndsWith(suffix, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (
            method.Name != MethodName
            || method.ContainingNamespace?.ToDisplayString() != MethodNamespace
            || invocation.Syntax is not InvocationExpressionSyntax syntax
        )
        {
            return;
        }

        var nameToken = syntax.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier,
            SimpleNameSyntax simpleName => simpleName.Identifier,
            _ => default,
        };
        if (nameToken == default || IsExplained(nameToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, nameToken.GetLocation()));
    }

    private static bool IsExplained(SyntaxToken nameToken)
    {
        var tree = nameToken.SyntaxTree;
        if (tree is null)
        {
            return false;
        }

        var lines = tree.GetText().Lines;
        var nameLine = lines.GetLineFromPosition(nameToken.SpanStart);
        var firstPosition = FirstNonWhitespacePosition(nameLine);
        var firstToken = tree.GetRoot().FindToken(firstPosition);

        SyntaxTrivia? comment = null;
        foreach (var trivia in firstToken.LeadingTrivia)
        {
            if (
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
            )
            {
                comment = trivia;
            }
        }

        if (
            comment is not { } last
            || !last.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || !last.ToString().StartsWith(CommentPrefix, StringComparison.Ordinal)
        )
        {
            return false;
        }

        var commentLine = lines.GetLineFromPosition(last.SpanStart);
        return commentLine.LineNumber == nameLine.LineNumber - 1;
    }

    private static int FirstNonWhitespacePosition(TextLine line)
    {
        var text = line.ToString();
        var offset = 0;
        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }

        return line.Start + offset;
    }
}
