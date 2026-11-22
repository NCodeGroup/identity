// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.GlobalDirectory;

/// <summary>
/// Provides the <see cref="ITeamDirectory"/> of a cell that reaches the global directory service over HTTP. Each method
/// is one call under <c>directory/v1/teams/</c>: <c>POST {teamId}/reserve</c>, <c>PUT {teamId}/document</c>,
/// <c>POST {teamId}/release</c>, <c>POST {teamId}/retire</c>, <c>GET {teamId}/retired</c>, <c>GET {teamId}/document</c>
/// and <c>GET changes?after=&amp;limit=</c>. A <c>409</c> answer to a reservation is a conflict result, and a
/// <c>404</c> answer to a lookup means the team has no document or the identifier is not retired. Every failure of the
/// directory is a <see cref="DirectoryUnavailableException"/>.
/// </summary>
// See ADR-1411.
internal sealed class RemoteTeamDirectory(DirectoryHttpClient client) : ITeamDirectory
{
    private const string Prefix = "teams/";

    private DirectoryHttpClient Client { get; } = client;

    /// <inheritdoc />
    public async ValueTask<ReservationResult> ReserveAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);

        var segment = DirectoryHttpClient.Segment(teamId);
        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{segment}/reserve",
            body: null,
            cancellationToken
        );

        if (response.IsConflict)
        {
            var detail = response.ReadProblemDetail("The directory refused to reserve a team.");
            return ReservationResult.Conflict(detail);
        }

        response.EnsureSuccess("reserve a team");
        return ReservationResult.Reserved();
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(TeamDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var segment = DirectoryHttpClient.Segment(document.TeamId);
        var response = await Client.SendAsync(
            HttpMethod.Put,
            $"{Prefix}{segment}/document",
            document,
            cancellationToken
        );

        response.EnsureSuccess("publish a team document");
    }

    /// <inheritdoc />
    public async ValueTask ReleaseAsync(string teamId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);

        var segment = DirectoryHttpClient.Segment(teamId);
        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{segment}/release",
            body: null,
            cancellationToken
        );

        response.EnsureSuccess("release a team");
    }

    /// <inheritdoc />
    public async ValueTask RetireAsync(string teamId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);

        var segment = DirectoryHttpClient.Segment(teamId);
        var response = await Client.SendAsync(
            HttpMethod.Post,
            $"{Prefix}{segment}/retire",
            body: null,
            cancellationToken
        );

        response.EnsureSuccess("retire a team");
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsTeamIdRetiredAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);

        var segment = DirectoryHttpClient.Segment(teamId);
        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}{segment}/retired",
            body: null,
            cancellationToken
        );

        if (response.IsNotFound)
            return false;

        response.EnsureSuccess("look up a retired team identifier");
        return true;
    }

    /// <inheritdoc />
    public async ValueTask<TeamDocument?> GetOrDefaultAsync(
        string teamId,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);

        var segment = DirectoryHttpClient.Segment(teamId);
        var response = await Client.SendAsync(
            HttpMethod.Get,
            $"{Prefix}{segment}/document",
            body: null,
            cancellationToken
        );

        if (response.IsNotFound)
            return null;

        response.EnsureSuccess("get a team document");
        return response.Read<TeamDocument>();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<DirectoryChange>> ListChangesAsync(
        long after,
        int limit,
        CancellationToken cancellationToken
    )
    {
        var path = FormattableString.Invariant($"{Prefix}changes?after={after}&limit={limit}");
        var response = await Client.SendAsync(HttpMethod.Get, path, body: null, cancellationToken);

        response.EnsureSuccess("list the team changes");
        return response.Read<List<DirectoryChange>>();
    }
}
