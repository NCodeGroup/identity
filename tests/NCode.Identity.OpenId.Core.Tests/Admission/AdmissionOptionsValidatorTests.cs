// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using NCode.Identity.OpenId.Authentication.Admission;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Admission;

public class AdmissionOptionsValidatorTests
{
    #region Validate Tests

    [Fact]
    public void Validate_WhenTheDefaultsAreKept_Succeeds()
    {
        var result = new AdmissionOptionsValidator().Validate(null, new AdmissionOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenConcurrencyLimitIsBelowOne_Fails()
    {
        var options = new AdmissionOptions { ConcurrencyLimit = 0 };

        var result = new AdmissionOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "AdmissionOptions.ConcurrencyLimit",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenConcurrencyLimitIsOne_Succeeds()
    {
        var options = new AdmissionOptions { ConcurrencyLimit = 1 };

        var result = new AdmissionOptionsValidator().Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenRetryAfterIsBelowOneSecond_Fails()
    {
        var options = new AdmissionOptions { RetryAfter = TimeSpan.FromMilliseconds(500) };

        var result = new AdmissionOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "AdmissionOptions.RetryAfter",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void Validate_WhenRetryAfterIsAboveOneHour_Fails()
    {
        var options = new AdmissionOptions { RetryAfter = TimeSpan.FromMinutes(61) };

        var result = new AdmissionOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(
            "AdmissionOptions.RetryAfter",
            result.FailureMessage,
            StringComparison.Ordinal
        );
    }

    #endregion
}
