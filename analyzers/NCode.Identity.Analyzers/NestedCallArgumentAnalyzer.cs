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
    /// The metadata name of <see cref="System.Linq.Expressions.Expression{TDelegate}" />.
    /// </summary>
    private const string ExpressionMetadataName = "System.Linq.Expressions.Expression`1";

    /// <summary>
    /// The metadata name of <see cref="System.Diagnostics.ConditionalAttribute" />.
    /// </summary>
    private const string ConditionalMetadataName = "System.Diagnostics.ConditionalAttribute";

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
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "A call, an await or an object creation inside the arguments of a method call, a delegate call or an object creation is assigned to a named local that the call then receives. The rule reports the outermost such expression of an argument, and never the receiver of a call. It does not report a lambda or anonymous-method argument itself (the rule applies inside its body), nameof, typeof, default, sizeof, literals, locals, parameters or member accesses; a place C# cannot declare a local in (field and property initializers, constructor initializers, primary-constructor base arguments, attribute arguments, parameter defaults, catch filters, pattern 'when' clauses and query expressions); a lambda converted to an Expression<TDelegate>; or the arguments of a call to a [Conditional] method such as Debug.Assert. Generated code is not analyzed.",
            helpLinkUri: "https://github.com/NCodeGroup/identity/blob/main/docs/decisions/1521-an-analyzer-fails-the-build-on-a-call-await-or-object-creation-nested-in-another-calls-arguments.md"
        );

    /// <summary>
    /// Registers the node analysis with the symbols of <see cref="System.Linq.Expressions.Expression{TDelegate}" /> and
    /// <see cref="System.Diagnostics.ConditionalAttribute" /> that the compilation resolves.
    /// </summary>
    /// <param name="context">The context of the compilation that starts.</param>
    private static void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        var compilation = context.Compilation;
        var expressionType = compilation.GetTypeByMetadataName(ExpressionMetadataName);
        var conditionalType = compilation.GetTypeByMetadataName(ConditionalMetadataName);

        context.RegisterSyntaxNodeAction(
            nodeContext => AnalyzeNode(nodeContext, expressionType, conditionalType),
            SyntaxKind.InvocationExpression,
            SyntaxKind.AwaitExpression,
            SyntaxKind.ObjectCreationExpression,
            SyntaxKind.ImplicitObjectCreationExpression,
            SyntaxKind.AnonymousObjectCreationExpression
        );
    }

    /// <summary>
    /// Reports a diagnostic when the node is a call, an <see langword="await" /> or an object creation that is nested
    /// in another call's arguments, in a place that can declare a local.
    /// </summary>
    /// <param name="context">The context of the node to analyze.</param>
    /// <param name="expressionType">
    /// The symbol of <see cref="System.Linq.Expressions.Expression{TDelegate}" />, or <see langword="null" /> when the
    /// compilation does not reference it.
    /// </param>
    /// <param name="conditionalType">
    /// The symbol of <see cref="System.Diagnostics.ConditionalAttribute" />, or <see langword="null" /> when the
    /// compilation does not reference it.
    /// </param>
    private static void AnalyzeNode(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? expressionType,
        INamedTypeSymbol? conditionalType
    )
    {
        var node = context.Node;
        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        var owner = FindOwner(node, semanticModel, cancellationToken);
        if (owner is null)
        {
            return;
        }

        var operation = semanticModel.GetOperation(node, cancellationToken);
        if (operation is INameOfOperation)
        {
            return;
        }

        if (!CanDeclareLocal(owner, semanticModel, expressionType, cancellationToken))
        {
            return;
        }

        if (IsInConditionalCall(node, semanticModel, conditionalType, cancellationToken))
        {
            return;
        }

        var kind = Describe(node);
        var location = node.GetLocation();
        var diagnostic = Diagnostic.Create(Rule, location, kind);
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Finds the call or object creation whose arguments hold a candidate expression, directly or anywhere inside the
    /// argument's expression.
    /// </summary>
    /// <param name="candidate">The call, <see langword="await" /> or object creation to inspect.</param>
    /// <param name="semanticModel">The semantic model of the candidate's syntax tree.</param>
    /// <param name="cancellationToken">The token that cancels the analysis.</param>
    /// <returns>
    /// The call or object creation that owns the argument when the candidate sits in its arguments and no other
    /// candidate, lambda, statement, member or attribute lies between them; otherwise, <see langword="null" />.
    /// </returns>
    private static SyntaxNode? FindOwner(
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
                    return owned ? argument.Parent?.Parent : null;
                }

                continue;
            }

            if (IsCandidate(current) || IsBoundary(current))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether a statement can declare the local that would hold a nested expression, by walking up from the
    /// call or object creation that owns the argument.
    /// </summary>
    /// <param name="owner">The call or object creation whose arguments hold the nested expression.</param>
    /// <param name="semanticModel">The semantic model of the owner's syntax tree.</param>
    /// <param name="expressionType">
    /// The symbol of <see cref="System.Linq.Expressions.Expression{TDelegate}" />, or <see langword="null" />.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the analysis.</param>
    /// <returns>
    /// <see langword="true" /> when a statement, an expression-bodied member or a lambda body that is not an expression
    /// tree is reached first; <see langword="false" /> when a place that cannot declare a local is reached first.
    /// </returns>
    private static bool CanDeclareLocal(
        SyntaxNode owner,
        SemanticModel semanticModel,
        INamedTypeSymbol? expressionType,
        CancellationToken cancellationToken
    )
    {
        for (var current = owner.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case StatementSyntax:
                case ArrowExpressionClauseSyntax:
                    return true;
                case AnonymousFunctionExpressionSyntax lambda:
                    var isExpressionTree = IsExpressionTree(
                        lambda,
                        semanticModel,
                        expressionType,
                        cancellationToken
                    );
                    return !isExpressionTree;
                case EqualsValueClauseSyntax clause when IsNonLocalInitializer(clause):
                case CatchFilterClauseSyntax:
                case WhenClauseSyntax:
                case QueryExpressionSyntax:
                case AttributeArgumentSyntax:
                case ConstructorInitializerSyntax:
                case PrimaryConstructorBaseTypeSyntax:
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether an initializer belongs to a field, a property or a parameter, none of which can declare a
    /// local.
    /// </summary>
    /// <param name="clause">The initializer to inspect.</param>
    /// <returns>
    /// <see langword="true" /> when the initializer is a field's, a property's or a parameter's default; otherwise,
    /// <see langword="false" />, such as for a local declaration.
    /// </returns>
    private static bool IsNonLocalInitializer(EqualsValueClauseSyntax clause)
    {
        return clause.Parent
            is ParameterSyntax
                or PropertyDeclarationSyntax
                or VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax { Parent: BaseFieldDeclarationSyntax }
                };
    }

    /// <summary>
    /// Determines whether a lambda or an anonymous method is converted to an
    /// <see cref="System.Linq.Expressions.Expression{TDelegate}" />, whose body cannot hold a local.
    /// </summary>
    /// <param name="lambda">The lambda or anonymous method to inspect.</param>
    /// <param name="semanticModel">The semantic model of the lambda's syntax tree.</param>
    /// <param name="expressionType">
    /// The symbol of <see cref="System.Linq.Expressions.Expression{TDelegate}" />, or <see langword="null" />.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the analysis.</param>
    /// <returns>
    /// <see langword="true" /> when the lambda is converted to a construction of the expression type; otherwise,
    /// <see langword="false" />.
    /// </returns>
    private static bool IsExpressionTree(
        AnonymousFunctionExpressionSyntax lambda,
        SemanticModel semanticModel,
        INamedTypeSymbol? expressionType,
        CancellationToken cancellationToken
    )
    {
        if (expressionType is null)
        {
            return false;
        }

        var typeInfo = semanticModel.GetTypeInfo(lambda, cancellationToken);
        var convertedType = typeInfo.ConvertedType;
        return convertedType is INamedTypeSymbol { IsGenericType: true } named
            && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, expressionType);
    }

    /// <summary>
    /// Determines whether a candidate expression sits in the arguments of a call to a <c>[Conditional]</c> method, such
    /// as <see cref="System.Diagnostics.Debug.Assert(bool)" />, whose arguments are not evaluated where the call is
    /// removed.
    /// </summary>
    /// <param name="candidate">The call, <see langword="await" /> or object creation to inspect.</param>
    /// <param name="semanticModel">The semantic model of the candidate's syntax tree.</param>
    /// <param name="conditionalType">
    /// The symbol of <see cref="System.Diagnostics.ConditionalAttribute" />, or <see langword="null" />.
    /// </param>
    /// <param name="cancellationToken">The token that cancels the analysis.</param>
    /// <returns>
    /// <see langword="true" /> when a call between the candidate and its enclosing statement or lambda calls a method
    /// with the conditional attribute; otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsInConditionalCall(
        SyntaxNode candidate,
        SemanticModel semanticModel,
        INamedTypeSymbol? conditionalType,
        CancellationToken cancellationToken
    )
    {
        if (conditionalType is null)
        {
            return false;
        }

        for (var current = candidate.Parent; current is not null; current = current.Parent)
        {
            if (IsBoundary(current))
            {
                return false;
            }

            if (current is not InvocationExpressionSyntax invocation)
            {
                continue;
            }

            var symbolInfo = semanticModel.GetSymbolInfo(invocation, cancellationToken);
            if (symbolInfo.Symbol is IMethodSymbol method && HasAttribute(method, conditionalType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a method has an attribute of a given type.
    /// </summary>
    /// <param name="method">The method to inspect.</param>
    /// <param name="attributeType">The symbol of the attribute's type.</param>
    /// <returns>
    /// <see langword="true" /> when the method has the attribute; otherwise, <see langword="false" />.
    /// </returns>
    private static bool HasAttribute(IMethodSymbol method, INamedTypeSymbol attributeType)
    {
        foreach (var attribute in method.GetAttributes())
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
            {
                return true;
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
        context.RegisterCompilationStartAction(OnCompilationStart);
    }
}
