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

    /// <summary>
    /// The name of the method whose invocation the analyzer inspects.
    /// </summary>
    private const string MethodName = "IgnoreQueryFilters";

    /// <summary>
    /// The namespace that declares the method whose invocation the analyzer inspects.
    /// </summary>
    private const string MethodNamespace = "Microsoft.EntityFrameworkCore";

    /// <summary>
    /// The text that an explaining comment starts with.
    /// </summary>
    private const string CommentPrefix = "// Cross-tenant:";

    /// <summary>
    /// The suffixes of the assembly names that the analyzer skips.
    /// </summary>
    private static string[] SkippedAssemblySuffixes { get; } = [".Tests", ".TestSupport"];

    /// <summary>
    /// The descriptor of the diagnostic that reports an unexplained <c>IgnoreQueryFilters()</c> call.
    /// </summary>
    private static DiagnosticDescriptor Rule { get; } =
        new(
            DiagnosticId,
            title: "Explain a cross-tenant query",
            messageFormat: "'IgnoreQueryFilters()' needs a '// Cross-tenant: <why>' comment on the line directly above it",
            category: "Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "An IgnoreQueryFilters() call reads across tenants. A '// Cross-tenant: <why>' comment sits on the line directly above the line that holds the call: above its statement when the call is on the statement's first line, or above its '.IgnoreQueryFilters()' line in a fluent chain, and states why the query reads across tenants.",
            helpLinkUri: "https://github.com/NCodeGroup/identity/blob/main/docs/decisions/1018-tenant-selection-is-shared-and-tenant-bound-rows-are-scoped-at-the-persistence-layer.md"
        );

    /// <summary>
    /// Registers the invocation analysis unless the compilation's assembly is one of the skipped assemblies.
    /// </summary>
    /// <param name="context">The context of the compilation that starts.</param>
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

    /// <summary>
    /// Reports a diagnostic when the invocation calls <c>IgnoreQueryFilters()</c> without an explaining comment.
    /// </summary>
    /// <param name="context">The context of the invocation to analyze.</param>
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

        var nameToken = GetNameToken(syntax.Expression);
        if (IsExplained(syntax.SyntaxTree, nameToken))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, nameToken.GetLocation()));
    }

    /// <summary>
    /// Finds the token that names the called method in the expression that an invocation calls.
    /// </summary>
    /// <param name="expression">The expression that the invocation calls.</param>
    /// <returns>
    /// The name of the member access or member binding that the expression holds inside any parentheses; otherwise,
    /// the first token of the expression, which is the method's name when the call names the method alone.
    /// </returns>
    private static SyntaxToken GetNameToken(ExpressionSyntax expression)
    {
        return expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => GetNameToken(parenthesized.Expression),
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier,
            MemberBindingExpressionSyntax binding => binding.Name.Identifier,
            _ => expression.GetFirstToken(),
        };
    }

    /// <summary>
    /// Determines whether a <c>// Cross-tenant: &lt;why&gt;</c> comment leads the line of the method name, on the line
    /// directly above the first token of that line.
    /// </summary>
    /// <param name="tree">The syntax tree that holds the call.</param>
    /// <param name="nameToken">The token that names the called method.</param>
    /// <returns>
    /// <see langword="true" /> when the call carries an explaining comment; otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsExplained(SyntaxTree tree, SyntaxToken nameToken)
    {
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

    /// <summary>
    /// Finds the position of the first non-whitespace character of a line.
    /// </summary>
    /// <param name="line">The line of source text to inspect.</param>
    /// <returns>The position of the first non-whitespace character of the line.</returns>
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

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(OnCompilationStart);
    }
}
