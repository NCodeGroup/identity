// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests;

public class PostgresIdentifiersTests
{
    #region Quote Tests

    [Fact]
    public void Quote_WhenPlain_WrapsInDoubleQuotes()
    {
        var result = PostgresIdentifiers.Quote("Tenants");

        Assert.Equal("\"Tenants\"", result);
    }

    [Fact]
    public void Quote_WhenItHoldsADoubleQuote_DoublesIt()
    {
        var result = PostgresIdentifiers.Quote("tenant\"column");

        Assert.Equal("\"tenant\"\"column\"", result);
    }

    [Fact]
    public void Quote_WhenNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PostgresIdentifiers.Quote(null!));
    }

    [Fact]
    public void Quote_WhenEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => PostgresIdentifiers.Quote(string.Empty));
    }

    #endregion

    #region QualifiedName Tests

    [Fact]
    public void QualifiedName_WhenSchemaIsNull_QuotesTheNameAlone()
    {
        var result = PostgresIdentifiers.QualifiedName(null, "Rows");

        Assert.Equal("\"Rows\"", result);
    }

    [Fact]
    public void QualifiedName_WhenSchemaIsSet_JoinsTheQuotedSchemaAndName()
    {
        var result = PostgresIdentifiers.QualifiedName("app", "Rows");

        Assert.Equal("\"app\".\"Rows\"", result);
    }

    [Fact]
    public void QualifiedName_WhenSchemaIsEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            PostgresIdentifiers.QualifiedName(string.Empty, "Rows")
        );
    }

    #endregion
}
