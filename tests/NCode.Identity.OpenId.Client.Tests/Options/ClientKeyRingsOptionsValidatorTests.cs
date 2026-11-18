// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Hosting;
using NCode.Identity.OpenId.Client.Options;
using Xunit;

namespace NCode.Identity.OpenId.Client.Tests.Options;

public sealed class ClientKeyRingsOptionsValidatorTests : BaseTests
{
    private const string SectionPath = "Test:KeyRings";

    private DirectoryInfo KeysDirectory { get; } = Directory.CreateTempSubdirectory("client-keys-");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            KeysDirectory.Delete(true);

        base.Dispose(disposing);
    }

    private ClientKeyRingsOptionsValidator CreateValidator(string? contentRootPath = null)
    {
        var mockEnvironment = CreateStrictMock<IHostEnvironment>();
        if (contentRootPath is not null)
            mockEnvironment.Setup(x => x.ContentRootPath).Returns(contentRootPath).Verifiable();

        return new ClientKeyRingsOptionsValidator(mockEnvironment.Object);
    }

    private static ClientKeyRingsOptions CreateOptions() => new() { SectionPath = SectionPath };

    private static ClientRemoteKeyOptions CreateValidRemoteKey() =>
        new()
        {
            Provider = "aws-kms",
            KeyId = "arn:aws:kms:us-east-1:111122223333:key/1234abcd",
            Algorithm = "ES256",
            SecretType = "ecc",
            KeySizeBits = 256,
            Curve = "P-256",
        };

    #region Validate Tests

    [Fact]
    public void Validate_WhenNoKeyRingIsConfigured_Succeeds()
    {
        var result = CreateValidator().Validate(null, CreateOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenKeyRingsNull_Fails()
    {
        var options = CreateOptions();
        options.KeyRings = null!;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"{SectionPath} must be set.", result.Failures ?? []);
    }

    [Fact]
    public void Validate_WhenKeyRingHasNoName_Fails()
    {
        var options = CreateOptions();
        options.KeyRings[" "] = new ClientKeyRingOptions { PemFiles = ["a.pem"] };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains($"{SectionPath} has a key ring without a name.", result.Failures ?? []);
    }

    [Fact]
    public void Validate_WhenKeyRingNull_FailsWithNoKey()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = null!;

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            $"{SectionPath}:client must list at least one PEM file or remote key.",
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenPemFilesEmpty_FailsWithNoKey()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions { PemFiles = [] };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            $"{SectionPath}:client must list at least one PEM file or remote key.",
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenPemFilesNull_FailsWithNoKey()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions { PemFiles = null! };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            $"{SectionPath}:client must list at least one PEM file or remote key.",
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenKeyRingHasOnlyRemoteKeys_Succeeds()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            PemFiles = null!,
            RemoteKeys = { ["kms-sig"] = CreateValidRemoteKey() },
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenAKeyRingIsRegisteredInCode_FailsNamingIt()
    {
        var options = CreateOptions();
        options.KeyRings["alpha"] = new ClientKeyRingOptions
        {
            RemoteKeys = { ["kms-sig"] = CreateValidRemoteKey() },
        };
        options.RegisteredInCode.Add("alpha");

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [
                $"{SectionPath}:alpha names a key ring that a keyed ISecretKeyCollectionProvider already provides; a key ring is registered once, in code or in configuration.",
            ],
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenRemoteKeyHasNoId_Fails()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            RemoteKeys = { [" "] = CreateValidRemoteKey() },
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [$"{SectionPath}:client:RemoteKeys has a key without an id."],
            result.Failures ?? []
        );
    }

    [Theory]
    [InlineData(null, "it is not set")]
    [InlineData("", "it is ''")]
    [InlineData("AWS-KMS", "it is 'AWS-KMS'")]
    public void Validate_WhenRemoteKeyProviderIsInvalid_FailsNamingTheValue(
        string? provider,
        string statement
    )
    {
        var remoteKey = CreateValidRemoteKey();
        remoteKey.Provider = provider;
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            RemoteKeys = { ["kms-sig"] = remoteKey },
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [
                $"{SectionPath}:client:RemoteKeys:kms-sig:Provider must name a remote key provider: lowercase letters and digits in groups separated by single hyphens; {statement}.",
            ],
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenRemoteKeyFieldsAreMissing_FailsForEach()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            RemoteKeys =
            {
                ["kms-sig"] = new ClientRemoteKeyOptions { Provider = "aws-kms", KeySizeBits = 0 },
            },
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [
                $"{SectionPath}:client:RemoteKeys:kms-sig:KeyId must not be blank.",
                $"{SectionPath}:client:RemoteKeys:kms-sig:Algorithm must not be blank.",
                $"{SectionPath}:client:RemoteKeys:kms-sig:SecretType must not be blank.",
                $"{SectionPath}:client:RemoteKeys:kms-sig:KeySizeBits must be positive; it is 0.",
            ],
            result.Failures ?? []
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-256)]
    public void Validate_WhenRemoteKeySizeIsNotPositive_FailsNamingTheValue(int keySizeBits)
    {
        var remoteKey = CreateValidRemoteKey();
        remoteKey.KeySizeBits = keySizeBits;
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            RemoteKeys = { ["kms-sig"] = remoteKey },
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [
                $"{SectionPath}:client:RemoteKeys:kms-sig:KeySizeBits must be positive; it is {keySizeBits}.",
            ],
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenRemoteKeySectionIsEmpty_FailsForEachField()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            RemoteKeys = { ["kms-sig"] = null! },
        };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [
                $"{SectionPath}:client:RemoteKeys:kms-sig:Provider must name a remote key provider: lowercase letters and digits in groups separated by single hyphens; it is not set.",
                $"{SectionPath}:client:RemoteKeys:kms-sig:KeyId must not be blank.",
                $"{SectionPath}:client:RemoteKeys:kms-sig:Algorithm must not be blank.",
                $"{SectionPath}:client:RemoteKeys:kms-sig:SecretType must not be blank.",
                $"{SectionPath}:client:RemoteKeys:kms-sig:KeySizeBits must be positive; it is not set.",
            ],
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenAPemFileAndARemoteKeyShareAnId_Fails()
    {
        File.WriteAllBytes(Path.Combine(KeysDirectory.FullName, "shared.pem"), []);
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            PemFiles = ["shared.pem"],
            RemoteKeys = { ["shared"] = CreateValidRemoteKey() },
        };

        var result = CreateValidator(KeysDirectory.FullName).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [$"{SectionPath}:client holds more than one key with the id 'shared'."],
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenTwoPemFilesShareAnId_Fails()
    {
        Directory.CreateDirectory(Path.Combine(KeysDirectory.FullName, "a"));
        Directory.CreateDirectory(Path.Combine(KeysDirectory.FullName, "b"));
        File.WriteAllBytes(Path.Combine(KeysDirectory.FullName, "a", "shared.pem"), []);
        File.WriteAllBytes(Path.Combine(KeysDirectory.FullName, "b", "shared.pem"), []);
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            PemFiles = [Path.Combine("a", "shared.pem"), Path.Combine("b", "shared.pem")],
        };

        var result = CreateValidator(KeysDirectory.FullName).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [$"{SectionPath}:client holds more than one key with the id 'shared'."],
            result.Failures ?? []
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Validate_WhenPemFileBlank_Fails(string? path)
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions { PemFiles = [path!] };

        var result = CreateValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            $"{SectionPath}:client:PemFiles:0 must not be blank.",
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenPemFileMissing_Fails()
    {
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions { PemFiles = ["missing.pem"] };

        var result = CreateValidator(KeysDirectory.FullName).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            $"{SectionPath}:client:PemFiles:0 names 'missing.pem', which does not exist.",
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WhenOnlyTheSecondPemFileIsMissing_FailsWithItsIndex()
    {
        var existing = Path.Combine(KeysDirectory.FullName, "client.pem");
        File.WriteAllBytes(existing, []);
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            PemFiles = [existing, "missing.pem"],
        };

        var result = CreateValidator(KeysDirectory.FullName).Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(
            [$"{SectionPath}:client:PemFiles:1 names 'missing.pem', which does not exist."],
            result.Failures ?? []
        );
    }

    [Fact]
    public void Validate_WithExistingPemFiles_Succeeds()
    {
        File.WriteAllBytes(Path.Combine(KeysDirectory.FullName, "relative.pem"), []);
        var absolute = Path.Combine(KeysDirectory.FullName, "absolute.pem");
        File.WriteAllBytes(absolute, []);
        var options = CreateOptions();
        options.KeyRings["client"] = new ClientKeyRingOptions
        {
            PemFiles = ["relative.pem", absolute],
        };

        var result = CreateValidator(KeysDirectory.FullName).Validate(null, options);

        Assert.True(result.Succeeded);
    }

    #endregion
}
