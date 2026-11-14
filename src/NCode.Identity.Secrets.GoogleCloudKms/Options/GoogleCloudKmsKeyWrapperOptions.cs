// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.Secrets.GoogleCloudKms.Options;

/// <summary>
/// Contains the options for the Google Cloud Key Management Service key wrapper, bound from the
/// <see cref="DefaultSectionName"/> configuration section.
/// </summary>
// See ADR-1260.
[PublicAPI]
public sealed class GoogleCloudKmsKeyWrapperOptions
{
    /// <summary>
    /// Gets the name of the configuration section the options are bound from.
    /// </summary>
    public const string DefaultSectionName = "SecretProtection:RootKeyProviders:GoogleCloudKms";

    /// <summary>
    /// Gets or sets the resource name of the symmetric <c>ENCRYPT_DECRYPT</c> Cloud KMS crypto key that wraps master
    /// keys, without a version
    /// (<c>projects/&lt;project&gt;/locations/&lt;location&gt;/keyRings/&lt;key ring&gt;/cryptoKeys/&lt;key&gt;</c>).
    /// The service encrypts with the crypto key's primary version.
    /// </summary>
    public string? KeyName { get; set; }

    /// <summary>
    /// Gets or sets the Cloud KMS locations, such as <c>us-central1</c>, in which a key that a caller names may be held.
    /// A key in any other location is refused. When it is empty, a key in any location is allowed.
    /// </summary>
    public List<string> AllowedLocations { get; set; } = [];
}
