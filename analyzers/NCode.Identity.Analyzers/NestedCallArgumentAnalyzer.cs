// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace NCode.Identity.Analyzers;

/// <summary>
/// Reports a call, an <see langword="await" /> or an object creation that is nested in the arguments of another call or
/// object creation.
/// </summary>
// See ADR-1521.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedCallArgumentAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The identifier of the diagnostic that reports a call, an <see langword="await" /> or an object creation nested
    /// in another call's arguments.
    /// </summary>
    public const string DiagnosticId = "NCID0006";

    /// <summary>
    /// The descriptor of the diagnostic that reports a call, an <see langword="await" /> or an object creation nested
    /// in another call's arguments.
    /// </summary>
    private static DiagnosticDescriptor Rule { get; } =
        new(
            DiagnosticId,
            title: "Hoist a nested call into a named local",
            messageFormat: "Assign this {0} to a named local before passing it as an argument",
            category: "Design",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "A call, an await or an object creation inside the arguments of a method call, a delegate call or an object creation is assigned to a named local that the call then receives. The rule reports the outermost such expression of an argument, and never the receiver of a call. It does not report a lambda or anonymous-method argument itself (the rule applies inside its body), nameof, typeof, default, sizeof, literals, locals, parameters or member accesses; a place C# cannot declare a local in (field and property initializers, constructor initializers, primary-constructor base arguments, attribute arguments, parameter defaults, catch filters, pattern 'when' clauses and query expressions); a lambda converted to an Expression<TDelegate>; or the arguments of a call to a [Conditional] method such as Debug.Assert. Generated code is not analyzed.",
            helpLinkUri: "https://github.com/NCodeGroup/identity/blob/main/docs/decisions/1521-an-analyzer-fails-the-build-on-a-call-await-or-object-creation-nested-in-another-calls-arguments.md"
        );

    /// <summary>
    /// Reports a diagnostic when the node is a call, an <see langword="await" /> or an object creation that is nested
    /// in another call's arguments.
    /// </summary>
    /// <param name="context">The context of the node to analyze.</param>
    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var node = context.Node;
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        if (!IsNestedInCallArguments(node, semanticModel, cancellationToken))
        {
            return;
        }

        var operation = semanticModel.GetOperation(node, cancellationToken);
        if (operation is INameOfOperation)
        {
            return;
        }

        var kind = Describe(node);
        var location = node.GetLocation();
        var diagnostic = Diagnostic.Create(Rule, location, kind);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Determines whether a candidate expression is, directly or anywhere inside the argument's expression, an argument
    /// of a call or an object creation.
    /// </summary>
    /// <param name="candidate">The call, <see langword="await" /> or object creation to inspect.</param>
    /// <param name="semanticModel">The semantic model of the candidate's syntax tree.</param>
    /// <param name="cancellationToken">The token that cancels the analysis.</param>
    /// <returns>
    /// <see langword="true" /> when the candidate sits in the arguments of a call or an object creation and no other
    /// candidate, lambda, statement, member or attribute lies between them; otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsNestedInCallArguments(
        SyntaxNode candidate,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        for (var current = candidate.Parent; current is not null; current = current.Parent)
        {
            if (current is ArgumentSyntax argument)
            {
                var decision = GetArgumentListDecision(argument, semanticModel, cancellationToken);
                if (decision is { } owned)
                {
                    return owned;
                }

                continue;
            }

            if (IsCandidate(current) || IsBoundary(current))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Decides what the argument list that holds an argument means for the walk that looks for a call's arguments.
    /// </summary>
    /// <param name="argument">The argument to inspect.</param>
    /// <param name="semanticModel">The semantic model of the argument's syntax tree.</param>
    /// <param name="cancellationToken">The token that cancels the analysis.</param>
    /// <returns>
    /// <see langword="true" /> when the argument belongs to a call or an object creation; <see langword="false" /> when
    /// it belongs to a constructor initializer or a primary-constructor base type, which stops the walk; otherwise,
    /// <see langword="null" />, which continues the walk through an element access, a tuple or a <c>nameof</c>.
    /// </returns>
    private static bool? GetArgumentListDecision(
        ArgumentSyntax argument,
        SemanticModel semanticModel,
        CancellationToken cancellationToken
    )
    {
        if (argument.Parent is not ArgumentListSyntax list)
        {
            return null;
        }

        switch (list.Parent)
        {
            case ConstructorInitializerSyntax:
            case PrimaryConstructorBaseTypeSyntax:
                return false;
            case BaseObjectCreationExpressionSyntax:
                return true;
            case InvocationExpressionSyntax invocation:
                var operation = semanticModel.GetOperation(invocation, cancellationToken);
                return operation is INameOfOperation ? null : true;
            default:
                return null;
        }
    }

    /// <summary>
    /// Determines whether a node is a call, an <see langword="await" /> or an object creation that the analyzer
    /// inspects.
    /// </summary>
    /// <param name="node">The node to inspect.</param>
    /// <returns>
    /// <see langword="true" /> when the node is a candidate; otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsCandidate(SyntaxNode node)
    {
        return node
            is InvocationExpressionSyntax
                or AwaitExpressionSyntax
                or BaseObjectCreationExpressionSyntax
                or AnonymousObjectCreationExpressionSyntax;
    }

    /// <summary>
    /// Determines whether a node stops the walk that looks for a call's arguments.
    /// </summary>
    /// <param name="node">The node to inspect.</param>
    /// <returns>
    /// <see langword="true" /> when the node is a lambda, an anonymous method, a local function, a statement, a member
    /// declaration or an attribute; otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsBoundary(SyntaxNode node)
    {
        return node
            is AnonymousFunctionExpressionSyntax
                or LocalFunctionStatementSyntax
                or StatementSyntax
                or MemberDeclarationSyntax
                or AttributeSyntax;
    }

    /// <summary>
    /// Names the kind of candidate for the diagnostic's message.
    /// </summary>
    /// <param name="node">The candidate to describe.</param>
    /// <returns>
    /// <c>call</c> for an invocation, <c>await</c> for an <see langword="await" /> and <c>object creation</c> for an
    /// object creation.
    /// </returns>
    private static string Describe(SyntaxNode node)
    {
        return node switch
        {
            InvocationExpressionSyntax => "call",
            AwaitExpressionSyntax => "await",
            _ => "object creation",
        };
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Rule];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(
            AnalyzeNode,
            SyntaxKind.InvocationExpression,
            SyntaxKind.AwaitExpression,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.AnonymousObjectCreationExpression
        );
    }
}
