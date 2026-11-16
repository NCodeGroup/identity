// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;

namespace NCode.Identity.OpenId.GlobalDirectory.Host;

/// <summary>
/// A public marker type that identifies this assembly as the application under test. Tests use it as the
/// <c>WebApplicationFactory&lt;TEntryPoint&gt;</c> entry point so the real host is discovered and booted through the
/// full HTTP pipeline.
/// </summary>
[PublicAPI]
public sealed class DirectoryHostMarker;
