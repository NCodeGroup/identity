// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
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

    #endregion
}
