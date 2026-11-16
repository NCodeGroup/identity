// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using NCode.Identity.Secrets.Options;

namespace NCode.Identity.Secrets.GoogleCloudKms.Options;

/// <summary>
/// Contains the options for the Google Cloud Key Management Service remote key provider, bound from the
/// <see cref="DefaultSectionName"/> configuration section.
/// </summary>
// See ADR-1471.
[PublicAPI]
public sealed class GoogleCloudKmsRemoteKeyProviderOptions
{
    /// <summary>
    /// Gets the name of the configuration section the options are bound from.
    /// </summary>
    public const string DefaultSectionName = "SecretProtection:RemoteKeyProviders:GoogleCloudKms";

    /// <summary>
    /// Gets or sets the resource name of the Cloud KMS key ring in which the provider creates and lists crypto keys
    /// (<c>projects/&lt;project&gt;/locations/&lt;location&gt;/keyRings/&lt;key ring&gt;</c>). When it is not set, the
    /// provider creates and lists no keys and still reads, signs with, decrypts with and destroys the key versions each
    /// name names.
    /// </summary>
    public string? KeyRing { get; set; }

    /// <summary>
    /// Gets or sets the Cloud KMS locations, such as <c>us-central1</c>, in which the provider may create keys. A
    /// <see cref="KeyRing"/> in a location that is not listed is refused. When it is empty, any location is allowed.
    /// When <see cref="KeyResidencyOptions.CloudRegions"/> names cloud regions, they replace this list.
    /// </summary>
    public List<string> AllowedLocations { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the provider creates keys with the <c>HSM</c> protection level, so the
    /// private half of a key is held in a hardware security module. When <see langword="false"/> the keys have the
    /// <c>SOFTWARE</c> protection level. The default is <see langword="true"/>.
    /// </summary>
    public bool HardwareProtected { get; set; } = true;
}
