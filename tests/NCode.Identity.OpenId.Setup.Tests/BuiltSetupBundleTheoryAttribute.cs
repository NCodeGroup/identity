// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Setup;

/// <summary>
/// A theory that runs against the built <c>apps/setup</c> bundle, which the .NET gate never builds. The theory is
/// skipped unless the <see cref="BuiltSetupBundleFactAttribute.IndexPathVariable"/> environment variable names the
/// built bundle's <c>index.html</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class BuiltSetupBundleTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuiltSetupBundleTheoryAttribute"/> class, which skips the theory
    /// when the environment names no built bundle.
    /// </summary>
    public BuiltSetupBundleTheoryAttribute()
    {
        Skip = BuiltSetupBundleFactAttribute.SkipReason;
    }
}
