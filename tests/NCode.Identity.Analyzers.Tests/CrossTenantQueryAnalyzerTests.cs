// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCode.Identity.Analyzers;

public class CrossTenantQueryAnalyzerTests
{
    private const string Stub = """
        using System.Linq;
        using Microsoft.EntityFrameworkCore;

        namespace Microsoft.EntityFrameworkCore
        {
            public static class EntityFrameworkQueryableExtensions
            {
                public static System.Linq.IQueryable<T> IgnoreQueryFilters<T>(this System.Linq.IQueryable<T> source) => source;
            }
        }

        """;

    private static string Wrap(string body) =>
        Stub
        + $$"""
            class C
            {
                IQueryable<int> Query { get; } = null!;

                IQueryable<int> Run()
                {
            {{body}}
                }
            }
            """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string body) =>
        await AnalyzerHarness.AnalyzeAsync(
            Wrap(body),
            new CrossTenantQueryAnalyzer(),
            CancellationToken.None
        );

    private static async Task AssertReportsAsync(string body)
    {
        var diagnostics = await AnalyzeAsync(body);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NCID0004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private static async Task AssertReportsNothingAsync(string body)
    {
        var diagnostics = await AnalyzeAsync(body);

        Assert.Empty(diagnostics);
    }

    #region Initialize Tests

    [Fact]
    public async Task Initialize_CallWithoutComment_ReportsCall()
    {
        await AssertReportsAsync("        return Query.IgnoreQueryFilters();");
    }

    [Fact]
    public async Task Initialize_CommentAboveStatement_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "        // Cross-tenant: the resolver reads every tenant\n        return Query.IgnoreQueryFilters();"
        );
    }

    [Fact]
    public async Task Initialize_CommentAboveFluentChainLine_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "        return Query\n            .Where(x => x > 0)\n            // Cross-tenant: the sweep reads every tenant\n            .IgnoreQueryFilters()\n            .Where(x => x < 9);"
        );
    }

    [Fact]
    public async Task Initialize_CommentAboveStatementOfFluentChain_ReportsCall()
    {
        await AssertReportsAsync(
            "        // Cross-tenant: the sweep reads every tenant\n        return Query\n            .Where(x => x > 0)\n            .IgnoreQueryFilters();"
        );
    }

    [Fact]
    public async Task Initialize_BlankLineBetweenCommentAndCall_ReportsCall()
    {
        await AssertReportsAsync(
            "        // Cross-tenant: the resolver reads every tenant\n\n        return Query.IgnoreQueryFilters();"
        );
    }

    [Fact]
    public async Task Initialize_UnrelatedCommentAboveCall_ReportsCall()
    {
        await AssertReportsAsync(
            "        // reads every tenant\n        return Query.IgnoreQueryFilters();"
        );
    }

    [Fact]
    public async Task Initialize_CrossTenantCommentFollowedByOtherComment_ReportsCall()
    {
        await AssertReportsAsync(
            "        // Cross-tenant: the resolver reads every tenant\n        // and more\n        return Query.IgnoreQueryFilters();"
        );
    }

    [Fact]
    public async Task Initialize_MultiLineComment_ReportsCall()
    {
        await AssertReportsAsync(
            "        /* Cross-tenant: the resolver reads every tenant */\n        return Query.IgnoreQueryFilters();"
        );
    }

    [Fact]
    public async Task Initialize_SameNamedMethodOutsideEntityFramework_ReportsNothing()
    {
        var diagnostics = await AnalyzerHarness.AnalyzeAsync(
            "static class E { public static int IgnoreQueryFilters(this int value) => value; }\nclass C { int Run() => 1.IgnoreQueryFilters(); }",
            new CrossTenantQueryAnalyzer(),
            CancellationToken.None
        );

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("NCode.Identity.OpenId.Core.Tests")]
    [InlineData("NCode.Identity.OpenId.TestSupport")]
    public async Task Initialize_TestAssembly_ReportsNothing(string assemblyName)
    {
        var tree = CSharpSyntaxTree.ParseText(
            Wrap("        return Query.IgnoreQueryFilters();"),
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
            .WithAnalyzers([new CrossTenantQueryAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.Empty(diagnostics);
    }

    #endregion
}
