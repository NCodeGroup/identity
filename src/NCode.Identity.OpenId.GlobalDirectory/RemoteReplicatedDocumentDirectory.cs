// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="IReplicatedDocumentDirectory"/> of a cell that reaches the global directory service over
/// HTTP. Each method is one call under <c>directory/v1/</c>: <c>PUT platform/authority</c>,
/// <c>PUT teams/{teamId}/authority</c> and <c>PUT platform/management-keys</c> publish a document and answer the stored
/// revision, the same paths with <c>GET</c> read it, and <c>GET replicated-revisions</c> lists every revision. A
/// <c>404</c> answer to a lookup means the directory holds no such document. Every failure of the directory is a
/// <see cref="DirectoryUnavailableException"/>.
/// </summary>
// See ADR-1536.
internal sealed class RemoteReplicatedDocumentDirectory(DirectoryHttpClient client)
    : IReplicatedDocumentDirectory
{
    private const string PlatformAuthorityPath = "platform/authority";
    private const string ManagementKeysPath = "platform/management-keys";
    private const string TeamsPrefix = "teams/";
    private const string TeamAuthoritySuffix = "/authority";
    private const string RevisionsPath = "replicated-revisions";

    private DirectoryHttpClient Client { get; } = client;

    /// <inheritdoc />
    public async ValueTask<long> PublishAsync(
        ReplicatedDocument document,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(document);

        var path = GetPath(document.Kind, document.Key);
        var response = await Client.SendAsync(HttpMethod.Put, path, document, cancellationToken);

        response.EnsureSuccess("publish a replicated document");
        var stored = response.Read<StoredRevision>();
        return stored.Revision;
    }

    /// <inheritdoc />
    public async ValueTask<ReplicatedDocument?> GetOrDefaultAsync(
        string kind,
        string key,
        CancellationToken cancellationToken
    )
    {
        var path = GetPath(kind, key);
        var response = await Client.SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

        if (response.IsNotFound)
            return null;

        response.EnsureSuccess("get a replicated document");
        return response.Read<ReplicatedDocument>();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<ReplicatedDocument>> ListRevisionsAsync(
        CancellationToken cancellationToken
    )
    {
        var response = await Client.SendAsync(
            HttpMethod.Get,
            RevisionsPath,
            body: null,
            cancellationToken
        );

        response.EnsureSuccess("list the replicated document revisions");
        return response.Read<List<ReplicatedDocument>>();
    }

    private static string GetPath(string kind, string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);

        if (kind == ReplicatedDocumentKinds.PlatformAuthority)
            return PlatformAuthorityPath;

        if (kind == ReplicatedDocumentKinds.ManagementKeys)
            return ManagementKeysPath;

        if (kind != ReplicatedDocumentKinds.TeamAuthority)
            throw new ArgumentException(
                $"The kind '{kind}' is not a replicated document.",
                nameof(kind)
            );

        ArgumentException.ThrowIfNullOrEmpty(key);
        var segment = DirectoryHttpClient.Segment(key);
        return $"{TeamsPrefix}{segment}{TeamAuthoritySuffix}";
    }

    // The body of a successful publish.
    private sealed record StoredRevision(long Revision);
}
