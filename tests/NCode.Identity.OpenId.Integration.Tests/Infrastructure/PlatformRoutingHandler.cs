// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

namespace NCode.Identity.OpenId.Integration.Tests.Infrastructure;

/// <summary>
/// Dispatches each request to the in-process host that serves the request's host name, so the hosts of one platform
/// call each other the way separate deployments do. A host that is stopped refuses every request with an
/// <see cref="HttpRequestException"/>, as a host that refuses connections does. Every client of the platform uses one
/// instance as its primary handler, so disposing it, as a client factory does when it rotates a handler, ends nothing.
/// </summary>
public sealed class PlatformRoutingHandler : HttpMessageHandler
{
    private sealed class Route(string name, string host, Func<HttpMessageHandler> createHandler)
    {
        public string Name { get; } = name;

        public string Host { get; } = host;

        public Lazy<HttpMessageInvoker> Invoker { get; } =
            new(() => new HttpMessageInvoker(createHandler(), disposeHandler: false));

        public bool Matches(string requestHost) =>
            string.Equals(requestHost, Host, StringComparison.OrdinalIgnoreCase)
            || requestHost.EndsWith($".{Host}", StringComparison.OrdinalIgnoreCase);
    }

    private object Gate { get; } = new();

    private List<Route> Routes { get; } = [];

    private HashSet<string> StoppedNames { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Adds a host that serves the given host name and every host name beneath it.
    /// </summary>
    /// <param name="name">The name that <see cref="Stop"/> and <see cref="Start"/> use for the host.</param>
    /// <param name="host">The host name the host serves, such as <c>cell.platform.test</c>.</param>
    /// <param name="createHandler">Creates the handler that serves the host, when its first request arrives.</param>
    public void Add(string name, string host, Func<HttpMessageHandler> createHandler)
    {
        lock (Gate)
            Routes.Add(new Route(name, host, createHandler));
    }

    /// <summary>
    /// Makes the named host refuse every request until <see cref="Start"/> is called.
    /// </summary>
    /// <param name="name">The name the host was added with.</param>
    public void Stop(string name)
    {
        lock (Gate)
            StoppedNames.Add(name);
    }

    /// <summary>
    /// Makes the named host serve requests again.
    /// </summary>
    /// <param name="name">The name the host was added with.</param>
    public void Start(string name)
    {
        lock (Gate)
            StoppedNames.Remove(name);
    }

    /// <summary>
    /// Determines whether the named host is stopped.
    /// </summary>
    /// <param name="name">The name the host was added with.</param>
    /// <returns><see langword="true"/> when the host refuses requests; otherwise, <see langword="false"/>.</returns>
    public bool IsStopped(string name)
    {
        lock (Gate)
            return StoppedNames.Contains(name);
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var requestUri =
            request.RequestUri
            ?? throw new InvalidOperationException("The request has no absolute URI.");
        var requestHost = requestUri.Host;

        Route route;
        lock (Gate)
        {
            route =
                Routes
                    .OrderByDescending(candidate => candidate.Host.Length)
                    .FirstOrDefault(candidate => candidate.Matches(requestHost))
                ?? throw new HttpRequestException(
                    $"No host of the platform serves '{requestHost}'."
                );

            if (StoppedNames.Contains(route.Name))
            {
                throw new HttpRequestException(
                    $"The host '{route.Name}' refused the connection to '{requestHost}'."
                );
            }
        }

        return await route.Invoker.Value.SendAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        // Every client of the platform shares this handler, so no client ends it; the hosts' own factories end the hosts.
        base.Dispose(false);
    }
}
