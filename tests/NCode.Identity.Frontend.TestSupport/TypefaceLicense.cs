// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.Frontend.TestSupport;

/// <summary>
/// A typeface's license that every built bundle ships beside the fonts.
/// </summary>
/// <param name="Name">The license's file name before the build's content hash, such as <c>OFL-Martian-Mono</c>.</param>
/// <param name="Copyright">The copyright line that the license's text starts with.</param>
public sealed record TypefaceLicense(string Name, string Copyright);
