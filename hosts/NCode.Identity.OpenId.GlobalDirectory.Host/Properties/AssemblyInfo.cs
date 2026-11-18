// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.CompilerServices;

// Allows the host's two test projects, the unit tests and the end-to-end suite, to reference its internal types.
[assembly: InternalsVisibleTo("NCode.Identity.OpenId.GlobalDirectory.Host.Tests")]
[assembly: InternalsVisibleTo("NCode.Identity.OpenId.GlobalDirectory.Host.EndToEnd.Tests")]
