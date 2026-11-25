// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Moq;
using NCode.Identity.OpenId.GlobalDirectory.Server.Options;
using NCode.Identity.OpenId.Tenants;
using NCode.Identity.Secrets.Keys;
using NCode.Identity.Secrets.Persistence;
using Xunit;

namespace NCode.Identity.OpenId.GlobalDirectory.Server;

public class DirectoryServerOptionsValidatorTests
{
    private const string IssuerKey = "DirectoryServer:Issuer";
    private const string AnchorIssuerKey = "DirectoryServer:ControlPlane:Issuer";

    private const string DisallowedIssuerRule =
        "must be an absolute https URL (or http on a loopback host) of at most 1000 characters with no user information, query or fragment.";

    public static TheoryData<string> NonLabels =>
        [
            "Control-Plane",
            "1cell",
            "cell-",
            "control--plane",
            new string('a', CellLabelRules.MaxLabelLength + 1),
        ];

    public static TheoryData<string> NonTenantIds =>
        [
            "Root",
            "-root",
            "root-",
            "root tenant",
            new string('a', TenantIdentifierRules.MaxTenantIdLength + 1),
        ];

    private static DirectoryServerOptions CreateOptions() =>
        new()
        {
            Issuer = "https://directory.example",
            KeyRing = "directory",
            ControlPlane = new DirectoryControlPlaneOptions
            {
                Cell = "control",
                Region = "region-1",
                TenantId = "root",
                Issuer = "https://auth.example/tenants/root",
                ClientJwks = """{"keys":[]}""",
            },
        };

    private static DirectoryServerOptionsValidator CreateValidator(string? error = null)
    {
        var readerMock = new Mock<IPublicJwksReader>(MockBehavior.Strict);
        IReadOnlyList<SecretKey>? outKeys = error is null ? [] : null;
        var outError = error;
        readerMock
            .Setup(reader => reader.TryRead(It.IsAny<JsonElement>(), out outKeys, out outError))
            .Returns(error is null);
        return new DirectoryServerOptionsValidator(readerMock.Object);
    }

    private static string CreateIssuerOfLength(int length)
    {
        const string prefix = "https://auth.example/";
        return prefix + new string('a', length - prefix.Length);
    }

    private static string SingleFailure(DirectoryServerOptions options)
    {
        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        return Assert.Single(result.Failures);
    }

    #region Validate Tests

    [Fact]
    public void Validate_CompleteOptions_Succeeds()
    {
        var result = CreateValidator().Validate(null, CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("https://auth.example")]
    [InlineData("https://auth.example/tenants/root")]
    [InlineData("http://localhost:5000")]
    [InlineData("http://127.0.0.1:5000")]
    public void Validate_AllowedIssuers_Succeed(string issuer)
    {
        var options = CreateOptions();
        options.Issuer = issuer;
        options.ControlPlane.Issuer = issuer;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyIssuers_FailAsRequired(string? issuer)
    {
        var options = CreateOptions();
        options.Issuer = issuer!;
        options.ControlPlane.Issuer = issuer!;

        var result = CreateValidator().Validate(null, options);

        Assert.Equal(
            [$"{IssuerKey} is required.", $"{AnchorIssuerKey} is required."],
            result.Failures
        );
    }

    [Theory]
    [InlineData("auth.example")]
    [InlineData("/relative")]
    [InlineData("http://example.com")]
    [InlineData("https://auth.example/?x=1")]
    [InlineData("https://auth.example/#section")]
    [InlineData("https://@auth.example")]
    [InlineData("ftp://auth.example")]
    public void Validate_DisallowedIssuers_FailNamingTheKeyAndRule(string issuer)
    {
        var options = CreateOptions();
        options.Issuer = issuer;
        options.ControlPlane.Issuer = issuer;

        var result = CreateValidator().Validate(null, options);

        Assert.Equal(
            [$"{IssuerKey} {DisallowedIssuerRule}", $"{AnchorIssuerKey} {DisallowedIssuerRule}"],
            result.Failures
        );
    }

    [Fact]
    public void Validate_IssuerWithUserInformation_FailsWithoutEchoingIt()
    {
        var options = CreateOptions();
        options.ControlPlane.Issuer = "https://user:secret@auth.example";

        var failure = SingleFailure(options);

        Assert.Equal($"{AnchorIssuerKey} {DisallowedIssuerRule}", failure);
        Assert.DoesNotContain("secret", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_IssuerOverMaxLength_Fails()
    {
        var options = CreateOptions();
        options.Issuer = CreateIssuerOfLength(DirectoryLimits.MaxIssuerLength + 1);

        Assert.Equal($"{IssuerKey} {DisallowedIssuerRule}", SingleFailure(options));
    }

    [Fact]
    public void Validate_IssuerAtMaxLength_Succeeds()
    {
        var options = CreateOptions();
        options.Issuer = CreateIssuerOfLength(DirectoryLimits.MaxIssuerLength);

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_EmptyKeyRing_FailsAsRequired(string? keyRing)
    {
        var options = CreateOptions();
        options.KeyRing = keyRing!;

        Assert.Equal("DirectoryServer:KeyRing is required.", SingleFailure(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(59)]
    [InlineData(3601)]
    public void Validate_TokenLifetimeOutsideTheRange_Fails(int seconds)
    {
        var options = CreateOptions();
        options.TokenLifetime = TimeSpan.FromSeconds(seconds);

        Assert.StartsWith("DirectoryServer:TokenLifetime must be between ", SingleFailure(options));
    }

    [Fact]
    public void Validate_TokenLifetimeOutsideTheRange_NamesTheConfiguredValue()
    {
        var options = CreateOptions();
        options.TokenLifetime = TimeSpan.FromSeconds(59);

        Assert.EndsWith($"; it is {options.TokenLifetime}.", SingleFailure(options));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(3600)]
    public void Validate_TokenLifetimeAtTheLimits_Succeeds(int seconds)
    {
        var options = CreateOptions();
        options.TokenLifetime = TimeSpan.FromSeconds(seconds);

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(nameof(DirectoryControlPlaneOptions.Cell))]
    [InlineData(nameof(DirectoryControlPlaneOptions.Region))]
    [InlineData(nameof(DirectoryControlPlaneOptions.Kind))]
    [InlineData(nameof(DirectoryControlPlaneOptions.TenantId))]
    [InlineData(nameof(DirectoryControlPlaneOptions.ClientJwks))]
    public void Validate_EmptyControlPlaneValue_FailsAsRequired(string property)
    {
        var options = CreateOptions();
        typeof(DirectoryControlPlaneOptions)
            .GetProperty(property)!
            .SetValue(options.ControlPlane, " ");

        Assert.Equal(
            $"DirectoryServer:ControlPlane:{property} is required.",
            SingleFailure(options)
        );
    }

    [Fact]
    public void Validate_WhenTheKindIsNotALabel_Succeeds()
    {
        var options = CreateOptions();
        options.ControlPlane.Kind = "Dedicated_Eu";

        Assert.True(CreateValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Validate_WhenTheKindIsTooLong_FailsNamingTheKeyRuleAndValue()
    {
        var options = CreateOptions();
        var kind = new string('k', CellKindRules.MaxKindLength + 1);
        options.ControlPlane.Kind = kind;

        Assert.Equal(
            $"DirectoryServer:ControlPlane:Kind must be a cell kind: {CellKindRules.Description}; it is '{kind}'.",
            SingleFailure(options)
        );
    }

    [Theory]
    [MemberData(nameof(NonLabels))]
    public void Validate_WhenCellIsNotACellLabel_FailsNamingTheKeyRuleAndValue(string cell)
    {
        var options = CreateOptions();
        options.ControlPlane.Cell = cell;

        Assert.Equal(
            $"DirectoryServer:ControlPlane:Cell must be a cell label: {CellLabelRules.Description}; it is '{cell}'.",
            SingleFailure(options)
        );
    }

    [Theory]
    [MemberData(nameof(NonLabels))]
    public void Validate_WhenRegionIsNotACellLabel_Fails(string region)
    {
        var options = CreateOptions();
        options.ControlPlane.Region = region;

        Assert.StartsWith(
            "DirectoryServer:ControlPlane:Region must be a cell label",
            SingleFailure(options)
        );
    }

    [Theory]
    [MemberData(nameof(NonTenantIds))]
    public void Validate_NonTenantIdentifierTenantId_FailsNamingTheKeyRuleAndValue(string tenantId)
    {
        var options = CreateOptions();
        options.ControlPlane.TenantId = tenantId;

        Assert.Equal(
            $"DirectoryServer:ControlPlane:TenantId must be a tenant identifier: 1 to {TenantIdentifierRules.MaxTenantIdLength} lowercase letters, digits or hyphens that start and end with a letter or digit; it is '{tenantId}'.",
            SingleFailure(options)
        );
    }

    [Fact]
    public void Validate_ClientJwksThatIsNotJson_Fails()
    {
        var options = CreateOptions();
        options.ControlPlane.ClientJwks = "{not json";

        Assert.Equal(
            "DirectoryServer:ControlPlane:ClientJwks must be JSON.",
            SingleFailure(options)
        );
    }

    [Fact]
    public void Validate_ClientJwksTheReaderRefuses_FailsWithTheReason()
    {
        var result = CreateValidator("a key is private").Validate(null, CreateOptions());

        Assert.Equal(
            [
                "DirectoryServer:ControlPlane:ClientJwks must be a public JSON Web Key Set: a key is private",
            ],
            result.Failures
        );
    }

    [Fact]
    public void Validate_SeveralFaults_ReportsEveryFailure()
    {
        var options = CreateOptions();
        options.Issuer = "ftp://auth.example";
        options.ControlPlane.Cell = string.Empty;

        var result = CreateValidator().Validate(null, options);

        Assert.Equal(
            [
                $"{IssuerKey} {DisallowedIssuerRule}",
                "DirectoryServer:ControlPlane:Cell is required.",
            ],
            result.Failures
        );
    }

    #endregion
}
