// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.Secrets.GoogleCloudKms.Options;
using Xunit;

namespace NCode.Identity.Secrets.GoogleCloudKms.Tests.Options;

public class GoogleCloudKmsRemoteKeyProviderOptionsValidatorTests
{
    private static GoogleCloudKmsRemoteKeyProviderOptionsValidator CreateValidator() => new();

    #region Validate Tests

    [Theory]
    [InlineData("projects/my-project/locations/us-central1/keyRings/ncode-identity")]
    [InlineData("projects/my-project/locations/global/keyRings/Tenant_Keys-1")]
    public void Validate_KeyRingName_Succeeds(string keyRing)
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            null,
            new GoogleCloudKmsRemoteKeyProviderOptions { KeyRing = keyRing }
        );

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validate_NoKeyRing_Succeeds(string? keyRing)
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            null,
            new GoogleCloudKmsRemoteKeyProviderOptions { KeyRing = keyRing }
        );

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("ncode-identity")]
    [InlineData("projects/my-project/locations/us-central1/keyRings/")]
    [InlineData("projects/my-project/locations/us-central1/keyRings/ncode-identity\n")]
    [InlineData(
        "projects/my-project/locations/us-central1/keyRings/ncode-identity/cryptoKeys/root"
    )]
    public void Validate_InvalidKeyRing_Fails(string keyRing)
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            null,
            new GoogleCloudKmsRemoteKeyProviderOptions { KeyRing = keyRing }
        );

        Assert.True(result.Failed);
        Assert.Contains("KeyRing", result.FailureMessage);
    }

    [Fact]
    public void Validate_LocationListed_Succeeds()
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            null,
            new GoogleCloudKmsRemoteKeyProviderOptions
            {
                KeyRing = "projects/my-project/locations/us-central1/keyRings/ncode-identity",
                AllowedLocations = ["europe-west1", "us-central1"],
            }
        );

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_LocationNotListed_FailsNamingAllowedLocations()
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            null,
            new GoogleCloudKmsRemoteKeyProviderOptions
            {
                KeyRing = "projects/my-project/locations/us-east1/keyRings/ncode-identity",
                AllowedLocations = ["europe-west1", "us-central1"],
            }
        );

        Assert.True(result.Failed);
        Assert.Contains("KeyRing", result.FailureMessage);
        Assert.Contains("europe-west1, us-central1", result.FailureMessage);
    }

    [Fact]
    public void Validate_NoAllowedLocations_AllowsAnyLocation()
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            null,
            new GoogleCloudKmsRemoteKeyProviderOptions
            {
                KeyRing = "projects/my-project/locations/asia-south1/keyRings/ncode-identity",
                AllowedLocations = [],
            }
        );

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_NoKeyRingWithAllowedLocations_Succeeds()
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            null,
            new GoogleCloudKmsRemoteKeyProviderOptions
            {
                KeyRing = null,
                AllowedLocations = ["us-central1"],
            }
        );

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_DefaultOptions_HardwareProtected()
    {
        var options = new GoogleCloudKmsRemoteKeyProviderOptions();

        Assert.True(options.HardwareProtected);
    }

    [Fact]
    public void Validate_NullOptions_Throws()
    {
        var validator = CreateValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null, null!));
    }

    #endregion
}
