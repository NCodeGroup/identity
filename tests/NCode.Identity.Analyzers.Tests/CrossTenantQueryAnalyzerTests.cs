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

    private static string WrapInDeclaringType(string body) =>
        $$"""
            using System.Linq;

            namespace Microsoft.EntityFrameworkCore
            {
                public static class EntityFrameworkQueryableExtensions
                {
                    public static IQueryable<T> IgnoreQueryFilters<T>(this IQueryable<T> source) => source;

                    public static IQueryable<T> ReadEveryTenant<T>(IQueryable<T> source)
                    {
            {{body}}
                    }
                }
            }
            """;

    private static ValueTask<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        CancellationToken cancellationToken
    ) => AnalyzerHarness.AnalyzeAsync(source, new CrossTenantQueryAnalyzer(), cancellationToken);

    private static async ValueTask AssertReportsAsync(
        string source,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = await AnalyzeAsync(source, cancellationToken);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("NCID0004", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        var tree = diagnostic.Location.SourceTree;
        Assert.NotNull(tree);
        Assert.Equal(
            "IgnoreQueryFilters",
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
    public async Task Initialize_CallWithoutComment_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap("        return Query.IgnoreQueryFilters();"),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CommentAboveStatement_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            Wrap(
                "        // Cross-tenant: the resolver reads every tenant\n        return Query.IgnoreQueryFilters();"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CommentAboveFluentChainLine_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            Wrap(
                "        return Query\n            .Where(x => x > 0)\n            // Cross-tenant: the sweep reads every tenant\n            .IgnoreQueryFilters()\n            .Where(x => x < 9);"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CommentAboveStatementOfFluentChain_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap(
                "        // Cross-tenant: the sweep reads every tenant\n        return Query\n            .Where(x => x > 0)\n            .IgnoreQueryFilters();"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_BlankLineBetweenCommentAndCall_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap(
                "        // Cross-tenant: the resolver reads every tenant\n\n        return Query.IgnoreQueryFilters();"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_UnrelatedCommentAboveCall_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap("        // reads every tenant\n        return Query.IgnoreQueryFilters();"),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CrossTenantCommentFollowedByOtherComment_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap(
                "        // Cross-tenant: the resolver reads every tenant\n        // and more\n        return Query.IgnoreQueryFilters();"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_MultiLineComment_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap(
                "        /* Cross-tenant: the resolver reads every tenant */\n        return Query.IgnoreQueryFilters();"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_ConditionalAccessCallWithoutComment_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap("        return Query?.IgnoreQueryFilters();"),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CommentAboveConditionalAccessCall_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            Wrap(
                "        // Cross-tenant: the resolver reads every tenant\n        return Query?.IgnoreQueryFilters();"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_ParenthesizedCallWithoutComment_ReportsCall()
    {
        await AssertReportsAsync(
            Wrap("        return (this.Query.IgnoreQueryFilters)();"),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CommentAboveParenthesizedFluentChainLine_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            Wrap(
                "        return (this.Query\n            .Where(x => x > 0)\n            // Cross-tenant: the sweep reads every tenant\n            .IgnoreQueryFilters)();"
            ),
            CancellationToken.None
        );
    }

    [Theory]
    [InlineData("            return IgnoreQueryFilters(source);")]
    [InlineData("            return IgnoreQueryFilters<T>(source);")]
    public async Task Initialize_UnqualifiedCallWithoutComment_ReportsCall(string body)
    {
        await AssertReportsAsync(WrapInDeclaringType(body), CancellationToken.None);
    }

    [Fact]
    public async Task Initialize_CommentAboveUnqualifiedCall_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            WrapInDeclaringType(
                "            // Cross-tenant: the helper reads every tenant\n            return IgnoreQueryFilters(source);"
            ),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_SameNamedMethodOutsideEntityFramework_ReportsNothing()
    {
        await AssertReportsNothingAsync(
            "static class E { public static int IgnoreQueryFilters(this int value) => value; }\nclass C { int Run() => 1.IgnoreQueryFilters(); }",
            CancellationToken.None
        );
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
