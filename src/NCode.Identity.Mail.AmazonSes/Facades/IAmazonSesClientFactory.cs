// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using Amazon.SimpleEmailV2;

namespace NCode.Identity.Mail.AmazonSes.Facades;

/// <summary>
/// Provides the Amazon SES client every delivery uses, so the code that calls Amazon SES can be tested without it.
/// </summary>
// See ADR-1528.
internal interface IAmazonSesClientFactory
{
    /// <summary>
    /// Gets the Amazon SES client, which the factory creates on first use, shares between deliveries and disposes.
    /// </summary>
    /// <returns>The shared <see cref="IAmazonSimpleEmailServiceV2"/>, which the caller does not dispose.</returns>
    IAmazonSimpleEmailServiceV2 GetClient();
}
