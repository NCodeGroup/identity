// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Accounts.Management;

/// <summary>
/// The paging inputs of a list request.
/// </summary>
/// <param name="Cursor">The cursor of the page, or <c>null</c> for the first page.</param>
/// <param name="Limit">The requested page size, or <c>null</c> for the default.</param>
internal readonly record struct PageWindow(string? Cursor, int? Limit);
