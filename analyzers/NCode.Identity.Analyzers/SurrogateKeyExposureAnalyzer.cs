// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace NCode.Identity.Analyzers;

/// <summary>
/// Reports a public property or field of a public contract type whose type is or contains <see cref="Guid" />.
/// </summary>
// See ADR-1517.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SurrogateKeyExposureAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The identifier of the diagnostic that reports a contract member that holds a <see cref="Guid" />.
    /// </summary>
    public const string DiagnosticId = "NCID0005";

    /// <summary>
    /// The metadata name of the type that the analyzer looks for in the type of a contract member.
    /// </summary>
    private const string GuidMetadataName = "System.Guid";

    /// <summary>
    /// The suffixes of the assembly names that the analyzer skips.
    /// </summary>
    private static string[] SkippedAssemblySuffixes { get; } = [".Tests", ".TestSupport"];

    /// <summary>
    /// The namespace segments that mark a namespace as holding contract types.
    /// </summary>
    private static string[] ContractSegments { get; } = ["Contracts", "DataContracts"];

    /// <summary>
    /// The descriptor of the diagnostic that reports a contract member that holds a <see cref="Guid" />.
    /// </summary>
    private static DiagnosticDescriptor Rule { get; } =
        new(
            DiagnosticId,
            title: "Keep surrogate keys off contract types",
            messageFormat: "'{0}' on the contract type '{1}' holds a Guid; a contract carries public identifiers, never a surrogate key",
            category: "Design",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: "Every surrogate key is a Guid and every resource identifier is an opaque string, so a public property or field whose type is or contains a Guid, on a public type in a namespace with a 'Contracts' or 'DataContracts' segment, exposes a surrogate key unless it is a public identifier that carries a [SuppressMessage] with a justification.",
            helpLinkUri: "https://github.com/NCodeGroup/identity/blob/main/docs/decisions/1517-internal-surrogate-keys-are-uuidv7-minted-in-process-behind-one-seam-and-never-reach-a-caller.md"
        );

    /// <summary>
    /// Registers the member analysis unless the compilation's assembly is one of the skipped assemblies or does not
    /// reference <see cref="Guid" />.
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

        var guidType = context.Compilation.GetTypeByMetadataName(GuidMetadataName);
        if (guidType is null)
        {
            return;
        }

        context.RegisterSymbolAction(
            symbolContext => AnalyzeMember(symbolContext, guidType),
            SymbolKind.Property,
            SymbolKind.Field
        );
    }

    /// <summary>
    /// Reports a diagnostic when the member is a public member of a public contract type and holds a
    /// <see cref="Guid" />.
    /// </summary>
    /// <param name="context">The context of the member to analyze.</param>
    /// <param name="guidType">The symbol of <see cref="Guid" />.</param>
    private static void AnalyzeMember(SymbolAnalysisContext context, INamedTypeSymbol guidType)
    {
        var member = context.Symbol;
        var memberType = member switch
        {
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            _ => null,
        };

        if (
            memberType is null
            || member.DeclaredAccessibility != Accessibility.Public
            || member.ContainingType is not { } containingType
            || !IsPublicContract(containingType)
            || !ContainsGuid(memberType, guidType)
            || member.Locations.IsEmpty
        )
        {
            return;
        }

        var diagnostic = Diagnostic.Create(
            Rule,
            member.Locations[0],
            member.Name,
            containingType.Name
        );
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Determines whether a type and every type that contains it are public, and whether its namespace has a segment
    /// that marks it as holding contract types.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>
    /// <see langword="true" /> when the type is a public contract type; otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsPublicContract(INamedTypeSymbol type)
    {
        for (
            INamedTypeSymbol? current = type;
            current is not null;
            current = current.ContainingType
        )
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        var segments = type.ContainingNamespace.ToDisplayString().Split('.');
        foreach (var segment in segments)
        {
            foreach (var contractSegment in ContractSegments)
            {
                if (segment == contractSegment)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a type is <see cref="Guid" />, is an array whose element type contains it, or is a named type
    /// with a type argument that contains it.
    /// </summary>
    /// <param name="type">The type to inspect.</param>
    /// <param name="guidType">The symbol of <see cref="Guid" />.</param>
    /// <returns>
    /// <see langword="true" /> when the type is or contains <see cref="Guid" />; otherwise, <see langword="false" />.
    /// </returns>
    private static bool ContainsGuid(ITypeSymbol type, INamedTypeSymbol guidType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, guidType))
        {
            return true;
        }

        if (type is IArrayTypeSymbol array)
        {
            return ContainsGuid(array.ElementType, guidType);
        }

        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments)
            {
                if (ContainsGuid(argument, guidType))
                {
                    return true;
                }
            }
        }

        return false;
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
