// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCode.Identity.Analyzers;

public class SurrogateKeyExposureAnalyzerTests
{
    private static ValueTask<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        CancellationToken cancellationToken
    ) =>
        AnalyzerHarness.AnalyzeAsync(source, new SurrogateKeyExposureAnalyzer(), cancellationToken);

    private static async ValueTask AssertReportsAsync(
        string source,
        string expectedLocationText,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = await AnalyzeAsync(source, cancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NCID0005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var tree = diagnostic.Location.SourceTree;
        Assert.NotNull(tree);
        Assert.Equal(
            expectedLocationText,
            tree.GetText(cancellationToken).ToString(diagnostic.Location.SourceSpan)
        );
    }

    private static async ValueTask AssertReportsNothingAsync(
        string source,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = await AnalyzeAsync(source, cancellationToken);

        Assert.Empty(diagnostics);
    }

    #region Initialize Tests

    [Fact]
    public async Task Initialize_GuidProperty_ReportsProperty()
    {
        await AssertReportsAsync(
            "namespace Acme.Contracts.Grants { public class GrantResource { public System.Guid Id { get; set; } } }",
            "Id",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_NullableGuidProperty_ReportsProperty()
    {
        await AssertReportsAsync(
            "namespace Acme.Contracts.Grants { public class GrantResource { public System.Guid? ClientId { get; set; } } }",
            "ClientId",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidListProperty_ReportsProperty()
    {
        await AssertReportsAsync(
            "namespace Acme.Contracts.Grants { public class GrantResource { public System.Collections.Generic.IReadOnlyList<System.Guid> Ids { get; set; } = []; } }",
            "Ids",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidArrayProperty_ReportsProperty()
    {
        await AssertReportsAsync(
            "namespace Acme.Contracts.Grants { public class GrantResource { public System.Guid[] Ids { get; set; } = []; } }",
            "Ids",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidField_ReportsField()
    {
        await AssertReportsAsync(
            "namespace Acme.Contracts.Grants { public class GrantResource { public System.Guid Id; } }",
            "Id",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_PositionalRecordGuidParameter_ReportsParameter()
    {
        await AssertReportsAsync(
            "namespace Acme.Contracts.Grants { public record GrantResource(System.Guid Id, string Name); }",
            "Id",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidInDataContractsNamespace_ReportsProperty()
    {
        await AssertReportsAsync(
            "namespace Acme.DataContracts.Grants { public class PersistedGrant { public System.Guid Id { get; set; } } }",
            "Id",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_StringIdentifier_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "namespace Acme.Contracts.Grants { public class GrantResource { public string Id { get; set; } = \"\"; } }",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidInStoresNamespace_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "namespace Acme.Stores { public class GrantEntity { public System.Guid Id { get; set; } } }",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidInNamespaceWithLongerSegment_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "namespace Acme.ContractsExtra { public class GrantResource { public System.Guid Id { get; set; } } }",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidOnInternalType_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "namespace Acme.Contracts.Grants { internal class GrantResource { public System.Guid Id { get; set; } } }",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidOnPublicTypeNestedInInternalType_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "namespace Acme.Contracts.Grants { internal class Outer { public class GrantResource { public System.Guid Id { get; set; } } } }",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidOnPrivateProperty_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "namespace Acme.Contracts.Grants { public class GrantResource { private System.Guid Id { get; set; } } }",
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_GuidPropertyWithSuppressMessage_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            """
            namespace Acme.Contracts.Grants
            {
                public class GrantResource
                {
                    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "NCID0005", Justification = "A public identifier.")]
                    public System.Guid Id { get; set; }
                }
            }
            """,
            CancellationToken.None
        );
    }

    [Theory]
    [InlineData("NCode.Identity.OpenId.Core.Tests")]
    [InlineData("NCode.Identity.OpenId.TestSupport")]
    public async Task Initialize_TestAssembly_ReportsNothing(string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(
            "namespace Acme.Contracts.Grants { public class GrantResource { public System.Guid Id { get; set; } } }",
            new CSharpParseOptions(LanguageVersion.Latest),
            cancellationToken: CancellationToken.None
        );
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var references = (trustedAssemblies ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var diagnostics = await compilation
            .WithAnalyzers([new SurrogateKeyExposureAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    #endregion
}
