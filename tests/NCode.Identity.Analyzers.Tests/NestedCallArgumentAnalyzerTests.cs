// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace NCode.Identity.Analyzers;

public class NestedCallArgumentAnalyzerTests
{
    private static string Wrap(string body) =>
        $$"""
            using System;
            using System.Threading.Tasks;

            class Item
            {
                public int Value;

                public Item Next() => this;

                public void Use(int value) { }

                public static int operator +(int left, Item right) => left;
            }

            class C
            {
                static Item Make() => new Item();

                static Task<Item> MakeAsync()
                {
                    var item = new Item();
                    return Task.FromResult(item);
                }

                static Item Wrap(Item item) => item;

                static void Use(object? value) { }

                static void Run(Action action) { }

                static async Task M(Item item, int parameter, int value)
                {
                    var local = 1;
            {{body}}
                }
            }
            """;

    private static ValueTask<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string source,
        CancellationToken cancellationToken
    ) => AnalyzerHarness.AnalyzeAsync(source, new NestedCallArgumentAnalyzer(), cancellationToken);

    private static async ValueTask AssertReportsAsync(
        string body,
        string[] expectedTexts,
        CancellationToken cancellationToken
    )
    {
        var source = Wrap(body);
        await AssertSourceReportsAsync(source, expectedTexts, cancellationToken);
    }

    private static string WrapMembers(string members) =>
        $$"""
            using System;
            using System.Diagnostics;
            using System.Linq.Expressions;

            class Item { }

            class B
            {
                public B(Item item) { }
            }

            class C
            {
                static Item Make() => new Item();

                static int Make(int value) => value;

                static int Use(object? value) => 0;

                static bool Check(object? value) => true;

                [Conditional("DEBUG")]
                static void Trace(object? value) { }

            {{members}}
            }
            """;

    private static async ValueTask AssertMembersReportAsync(
        string members,
        string[] expectedTexts,
        CancellationToken cancellationToken
    )
    {
        var source = WrapMembers(members);
        await AssertSourceReportsAsync(source, expectedTexts, cancellationToken);
    }

    private static async ValueTask AssertSourceReportsAsync(
        string source,
        string[] expectedTexts,
        CancellationToken cancellationToken
    )
    {
        var diagnostics = await AnalyzeAsync(source, cancellationToken);

        var ordered = diagnostics
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();
        Assert.All(
            ordered,
            diagnostic =>
            {
                Assert.Equal("NCID0006", diagnostic.Id);
                Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
            }
        );

        var actualTexts = ordered
            .Select(diagnostic =>
                source.Substring(
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length
                )
            )
            .ToArray();
        Assert.Equal(expectedTexts, actualTexts);
    }

    #region Initialize Tests

    [Fact]
    public async Task Initialize_CallInArguments_ReportsCall()
    {
        await AssertReportsAsync("        Use(Make());", ["Make()"], CancellationToken.None);
    }

    [Fact]
    public async Task Initialize_AwaitInArguments_ReportsOnlyAwait()
    {
        await AssertReportsAsync(
            "        Use(await MakeAsync());",
            ["await MakeAsync()"],
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_ObjectCreationInArguments_ReportsCreation()
    {
        await AssertReportsAsync(
            "        Use(new Item());",
            ["new Item()"],
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_TargetTypedObjectCreationInArguments_ReportsCreation()
    {
        await AssertReportsAsync("        Use(new());", ["new()"], CancellationToken.None);
    }

    [Fact]
    public async Task Initialize_CallInBinaryExpression_ReportsCall()
    {
        await AssertReportsAsync("        Use(1 + Make());", ["Make()"], CancellationToken.None);
    }

    [Fact]
    public async Task Initialize_CallInInterpolatedString_ReportsCall()
    {
        await AssertReportsAsync("        Use($\"{Make()}\");", ["Make()"], CancellationToken.None);
    }

    [Fact]
    public async Task Initialize_ChainedCallInArguments_ReportsOnce()
    {
        await AssertReportsAsync(
            "        Use(item.Next().Next());",
            ["item.Next().Next()"],
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CallInsideCallInArguments_ReportsBoth()
    {
        await AssertReportsAsync(
            "        Use(Wrap(Make()));",
            ["Wrap(Make())", "Make()"],
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_CallInsideLambdaArgument_ReportsCall()
    {
        await AssertReportsAsync(
            "        Run(() => Use(Make()));",
            ["Make()"],
            CancellationToken.None
        );
    }

    [Theory]
    [InlineData("        Make().Use(value);")]
    [InlineData("        var x = Make();")]
    [InlineData("        Run(() => Make());")]
    [InlineData("        var x = 1; Use(nameof(x));")]
    [InlineData("        Use(typeof(int));")]
    [InlineData("        Use(default);")]
    [InlineData("        Use(sizeof(int));")]
    [InlineData("        Use(1);")]
    [InlineData("        Use(local);")]
    [InlineData("        Use(parameter);")]
    [InlineData("        Use(item.Value);")]
    public async Task Initialize_NoNestedCall_ReportsNothing(string body)
    {
        await AssertReportsAsync(body, [], CancellationToken.None);
    }

    [Theory]
    [InlineData("    int P { get; } = Use(Make());")]
    [InlineData("    static int F = Use(Make());")]
    [InlineData("    class D : B { public D() : base(Make()) { } }")]
    [InlineData("    class D() : B(Make()) { }")]
    [InlineData("    static void Test() { Expression<Func<int, int>> e = x => Use(Make(x)); }")]
    [InlineData("    static void Test() { Debug.Assert(Check(Make())); }")]
    [InlineData("    static void Test() { Trace(Make()); }")]
    [InlineData(
        "    static void Test() { try { } catch (Exception e) when (Check(e.Message.Trim())) { } }"
    )]
    [InlineData(
        "    static void Test(int value) { switch (value) { case int i when Check(Make()): break; } }"
    )]
    public async Task Initialize_PlaceThatCannotDeclareLocal_ReportsNothing(string members)
    {
        await AssertMembersReportAsync(members, [], CancellationToken.None);
    }

    [Fact]
    public async Task Initialize_ExpressionBodiedMember_ReportsCall()
    {
        await AssertMembersReportAsync(
            "    static int M() => Use(Make());",
            ["Make()"],
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_DelegateLambdaWithArguments_ReportsCall()
    {
        await AssertMembersReportAsync(
            "    static void Test() { Func<int, int> f = x => Use(Make(x)); }",
            ["Make(x)"],
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Initialize_TheAnalyzerProjectsSources_ReportsNothing()
    {
        var cancellationToken = CancellationToken.None;

        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "AnalyzerSources");
        var sourcePaths = Directory.GetFiles(sourceDirectory, "*.cs");
        Assert.NotEmpty(sourcePaths);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var strictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true
        );
        var trees = new List<SyntaxTree>();
        foreach (var sourcePath in sourcePaths)
        {
            var text = await File.ReadAllTextAsync(sourcePath, strictUtf8, cancellationToken);
            var tree = CSharpSyntaxTree.ParseText(
                text,
                parseOptions,
                sourcePath,
                cancellationToken: cancellationToken
            );
            trees.Add(tree);
        }

        const string implicitUsings = """
            global using global::System;
            global using global::System.Collections.Generic;
            global using global::System.IO;
            global using global::System.Linq;
            global using global::System.Net.Http;
            global using global::System.Threading;
            global using global::System.Threading.Tasks;
            """;
        var implicitUsingsTree = CSharpSyntaxTree.ParseText(
            implicitUsings,
            parseOptions,
            cancellationToken: cancellationToken
        );
        trees.Add(implicitUsingsTree);

        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        var paths = (trustedAssemblies ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path =>
                !string.Equals(
                    Path.GetFileName(path),
                    "NCode.Identity.Analyzers.dll",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();
        var analyzerAssemblyPath = typeof(DiagnosticAnalyzer).Assembly.Location;
        var syntaxAssemblyPath = typeof(CSharpSyntaxTree).Assembly.Location;
        foreach (var assemblyPath in new[] { analyzerAssemblyPath, syntaxAssemblyPath })
        {
            var present = paths.Contains(assemblyPath, StringComparer.OrdinalIgnoreCase);
            if (!present)
            {
                paths.Add(assemblyPath);
            }
        }

        var references = paths
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable
        );
        var compilation = CSharpCompilation.Create(
            "NCode.Identity.Analyzers",
            trees,
            references,
            compilationOptions
        );

        var compileErrors = compilation
            .GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        Assert.Empty(compileErrors);

        var analyzer = new NestedCallArgumentAnalyzer();
        var withAnalyzers = compilation.WithAnalyzers([analyzer]);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);

        var reports = diagnostics
            .OrderBy(diagnostic => diagnostic.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .Select(diagnostic => diagnostic.ToString())
            .ToArray();
        var message = string.Join(Environment.NewLine, reports);
        Assert.True(reports.Length == 0, message);
    }

    #endregion
}
