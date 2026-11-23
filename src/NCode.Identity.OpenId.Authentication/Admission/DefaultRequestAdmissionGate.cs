// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Options;

namespace NCode.Identity.OpenId.Authentication.Admission;

/// <summary>
/// Provides the default implementation of <see cref="IRequestAdmissionGate"/>: one count of the requests this replica
/// is serving, bounded per tenant by the share of <see cref="AdmissionOptions.ConcurrencyLimit"/> its tenant may fill.
/// </summary>
// See ADR-1523.
internal sealed class DefaultRequestAdmissionGate(IOptions<AdmissionOptions> optionsAccessor)
    : IRequestAdmissionGate
{
    // The number of requests this replica is serving, an Interlocked target.
    private int _inFlight;

    private IOptions<AdmissionOptions> OptionsAccessor { get; } = optionsAccessor;

    /// <inheritdoc />
    public bool TryEnter(int capacityPercent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacityPercent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacityPercent, 100);

        var limit = OptionsAccessor.Value.ConcurrencyLimit;
        if (limit is null)
        {
            Interlocked.Increment(ref _inFlight);
            return true;
        }

        var threshold = Math.Max(1, (int)((long)limit.Value * capacityPercent / 100));

        // A refused request never writes the count, so a burst that is shed never raises the count another tenant's
        // request reads. See ADR-1523.
        while (true)
        {
            var current = Volatile.Read(ref _inFlight);
            if (current >= threshold)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _inFlight, current + 1, current) == current)
            {
                return true;
            }
        }
    }

    /// <inheritdoc />
    public void Exit() => Interlocked.Decrement(ref _inFlight);
}
