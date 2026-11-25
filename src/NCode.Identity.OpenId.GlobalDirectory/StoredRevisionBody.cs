// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Describes the body of a successful publish of a replicated document, which answers the stored revision.
/// </summary>
/// <param name="Revision">The revision that the directory stored the document at.</param>
internal sealed record StoredRevisionBody(long Revision);
