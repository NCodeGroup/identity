// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Data.Common;
using Microsoft.Extensions.Configuration;
using NCode.Identity.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests;

public sealed class PostgresConnectionRulesTests
{
    private const string ConnectionStringName = "primary";
    private const string SectionName = "PrimaryDatabase";
    private const string ConnectionString =
        "Host=string-host;Database=string-db;Username=string-user;Password=string-secret";
    private const string Host = "db.example.test";
    private const string Port = "6543";
    private const string Name = "identity";
    private const string Username = "identity-user";
    private const string Password = "pa55-w0rd";

    private static IConfiguration CreateConfiguration(params (string Key, string? Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value))
            )
            .Build();

    private static (string Key, string? Value)[] CreateParts(
        string? host = Host,
        string? port = Port,
        string? name = Name,
        string? username = Username,
        string? password = Password
    ) =>
        [
            ($"{SectionName}:Host", host),
            ($"{SectionName}:Port", port),
            ($"{SectionName}:Name", name),
            ($"{SectionName}:Username", username),
            ($"{SectionName}:Password", password),
        ];

    private static DbConnectionStringBuilder Parse(string? connectionString)
    {
        Assert.NotNull(connectionString);

        return new DbConnectionStringBuilder { ConnectionString = connectionString };
    }

    #region Resolve Tests

    [Fact]
    public void Resolve_NothingConfigured_ReturnsNull()
    {
        var configuration = CreateConfiguration();

        var result = PostgresConnectionRules.Resolve(
            configuration,
            ConnectionStringName,
            SectionName
        );

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_EmptyValues_ReturnsNull()
    {
        var configuration = CreateConfiguration([
            ($"ConnectionStrings:{ConnectionStringName}", ""),
            .. CreateParts("", "", "", "", ""),
        ]);

        var result = PostgresConnectionRules.Resolve(
            configuration,
            ConnectionStringName,
            SectionName
        );

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_OtherNamesConfigured_ReturnsNull()
    {
        var configuration = CreateConfiguration([
            ("ConnectionStrings:other", ConnectionString),
            .. CreateParts().Select(pair => ($"Other{pair.Key}", pair.Value)),
        ]);

        var result = PostgresConnectionRules.Resolve(
            configuration,
            ConnectionStringName,
            SectionName
        );

        Assert.Null(result);
    }

    [Fact]
    public void Resolve_ConnectionStringOnly_ReturnsIt()
    {
        var configuration = CreateConfiguration(
            ($"ConnectionStrings:{ConnectionStringName}", ConnectionString)
        );

        var result = PostgresConnectionRules.Resolve(
            configuration,
            ConnectionStringName,
            SectionName
        );

        Assert.Equal(ConnectionString, result);
        var builder = Parse(result);
        Assert.Equal("string-host", builder["Host"]);
        Assert.Equal("string-db", builder["Database"]);
        Assert.Equal("string-user", builder["Username"]);
        Assert.Equal("string-secret", builder["Password"]);
    }

    [Fact]
    public void Resolve_PartsOnly_AssemblesThem()
    {
        var configuration = CreateConfiguration(CreateParts());

        var result = PostgresConnectionRules.Resolve(
            configuration,
            ConnectionStringName,
            SectionName
        );

        var builder = Parse(result);
        Assert.Equal(Host, builder["Host"]);
        Assert.Equal(Port, builder["Port"]);
        Assert.Equal(Name, builder["Database"]);
        Assert.Equal(Username, builder["Username"]);
        Assert.Equal(Password, builder["Password"]);
    }

    [Fact]
    public void Resolve_PartsWithoutPort_LeavesThePortOut()
    {
        var configuration = CreateConfiguration(CreateParts(port: null));

        var result = PostgresConnectionRules.Resolve(
            configuration,
            ConnectionStringName,
            SectionName
        );

        var builder = Parse(result);
        Assert.False(builder.ContainsKey("Port"));
        Assert.Equal(Host, builder["Host"]);
        Assert.Equal(Name, builder["Database"]);
        Assert.Equal(Username, builder["Username"]);
        Assert.Equal(Password, builder["Password"]);
    }

    [Fact]
    public void Resolve_PasswordWithSpecialCharacters_RoundTrips()
    {
        const string password = "p;a'ss=\"w=rd";
        var configuration = CreateConfiguration(CreateParts(password: password));

        var result = PostgresConnectionRules.Resolve(
            configuration,
            ConnectionStringName,
            SectionName
        );

        var builder = Parse(result);
        Assert.Equal(password, builder["Password"]);
        Assert.Equal(Host, builder["Host"]);
        Assert.Equal(Port, builder["Port"]);
        Assert.Equal(Name, builder["Database"]);
        Assert.Equal(Username, builder["Username"]);
    }

    [Fact]
    public void Resolve_ConnectionStringAndParts_ThrowsNamingBothPaths()
    {
        var configuration = CreateConfiguration([
            ($"ConnectionStrings:{ConnectionStringName}", ConnectionString),
            .. CreateParts(),
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionRules.Resolve(configuration, ConnectionStringName, SectionName)
        );

        Assert.Contains($"ConnectionStrings:{ConnectionStringName}", exception.Message);
        Assert.Contains(SectionName, exception.Message);
        Assert.DoesNotContain(ConnectionString, exception.Message);
        Assert.DoesNotContain(Password, exception.Message);
    }

    [Theory]
    [InlineData("Host")]
    [InlineData("Name")]
    [InlineData("Username")]
    [InlineData("Password")]
    public void Resolve_MissingRequiredPart_ThrowsNamingIt(string part)
    {
        var parts = CreateParts().Where(pair => pair.Key != $"{SectionName}:{part}").ToArray();
        var configuration = CreateConfiguration(parts);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionRules.Resolve(configuration, ConnectionStringName, SectionName)
        );

        Assert.Contains($"{SectionName}:{part}", exception.Message);
        Assert.DoesNotContain(Password, exception.Message);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("0")]
    [InlineData("65536")]
    public void Resolve_InvalidPort_ThrowsNamingThePort(string port)
    {
        var configuration = CreateConfiguration(CreateParts(port: port));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionRules.Resolve(configuration, ConnectionStringName, SectionName)
        );

        Assert.Contains($"{SectionName}:Port", exception.Message);
        Assert.DoesNotContain(port, exception.Message);
        Assert.DoesNotContain(Host, exception.Message);
        Assert.DoesNotContain(Name, exception.Message);
        Assert.DoesNotContain(Username, exception.Message);
        Assert.DoesNotContain(Password, exception.Message);
    }

    [Fact]
    public void Resolve_UnknownKeyInTheSection_ThrowsNamingTheKeyButNotTheValue()
    {
        var configuration = CreateConfiguration(
            ($"{SectionName}:Prot", Port),
            ($"{SectionName}:Pasword", Password)
        );

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgresConnectionRules.Resolve(configuration, ConnectionStringName, SectionName)
        );

        Assert.Contains("Pasword", exception.Message);
        Assert.DoesNotContain(Password, exception.Message);
        Assert.DoesNotContain(Password, exception.InnerException?.Message ?? "");
    }

    [Fact]
    public void Resolve_NullConfiguration_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PostgresConnectionRules.Resolve(null!, ConnectionStringName, SectionName)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_BlankConnectionStringName_Throws(string connectionStringName)
    {
        var configuration = CreateConfiguration();

        Assert.Throws<ArgumentException>(() =>
            PostgresConnectionRules.Resolve(configuration, connectionStringName, SectionName)
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Resolve_BlankSectionName_Throws(string sectionName)
    {
        var configuration = CreateConfiguration();

        Assert.Throws<ArgumentException>(() =>
            PostgresConnectionRules.Resolve(configuration, ConnectionStringName, sectionName)
        );
    }

    #endregion
}
