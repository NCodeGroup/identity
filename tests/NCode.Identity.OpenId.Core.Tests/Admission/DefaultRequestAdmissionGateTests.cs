// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;
using NCode.Identity.OpenId.Authentication.Admission;
using Xunit;

namespace NCode.Identity.OpenId.Core.Tests.Admission;

public class DefaultRequestAdmissionGateTests
{
    private static DefaultRequestAdmissionGate CreateGate(int? concurrencyLimit = 10) =>
        new(Options.Create(new AdmissionOptions { ConcurrencyLimit = concurrencyLimit }));

    private static void AdmitAll(DefaultRequestAdmissionGate gate, int count, int capacityPercent)
    {
        for (var i = 0; i < count; i++)
        {
            Assert.True(gate.TryEnter(capacityPercent));
        }
    }

    #region TryEnter Tests

    [Fact]
    public void TryEnter_WithoutAConcurrencyLimit_AdmitsEveryRequest()
    {
        var gate = CreateGate(concurrencyLimit: null);

        AdmitAll(gate, 100, capacityPercent: 1);
    }

    [Fact]
    public void TryEnter_WhenTheTenantsShareIsFull_ShedsTheRequest()
    {
        var gate = CreateGate();

        AdmitAll(gate, 6, capacityPercent: 60);

        Assert.False(gate.TryEnter(60));
    }

    [Fact]
    public void TryEnter_WhenASmallerShareIsFull_StillAdmitsALargerShare()
    {
        var gate = CreateGate();

        AdmitAll(gate, 6, capacityPercent: 60);
        AdmitAll(gate, 4, capacityPercent: 100);

        Assert.False(gate.TryEnter(100));
    }

    [Fact]
    public void TryEnter_WhenTheShareRoundsBelowOne_AdmitsOneRequest()
    {
        var gate = CreateGate();

        Assert.True(gate.TryEnter(1));
        Assert.False(gate.TryEnter(1));
    }

    [Fact]
    public void TryEnter_WhenAShedRequestHoldsNoPlace_AdmitsTheNextAfterExit()
    {
        var gate = CreateGate();
        AdmitAll(gate, 6, capacityPercent: 60);
        Assert.False(gate.TryEnter(60));

        gate.Exit();

        Assert.True(gate.TryEnter(60));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void TryEnter_WhenCapacityPercentIsOutOfRange_Throws(int capacityPercent)
    {
        var gate = CreateGate();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            gate.TryEnter(capacityPercent)
        );

        Assert.Equal("capacityPercent", exception.ParamName);
    }

    #endregion

    #region Exit Tests

    [Fact]
    public void Exit_WhenAnAdmittedRequestCompletes_FreesItsPlace()
    {
        var gate = CreateGate();
        AdmitAll(gate, 6, capacityPercent: 60);

        gate.Exit();

        Assert.True(gate.TryEnter(60));
    }

    #endregion
}
