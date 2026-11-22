// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NCode.Identity.AspNetCore;

/// <summary>
/// Provides extension methods that add an <see cref="IEndpointGate"/> to an endpoint or a route group.
/// </summary>
// See ADR-1520.
[PublicAPI]
public static class EndpointGateConventionBuilderExtensions
{
    /// <summary>
    /// Gets the endpoint metadata marker that records that an endpoint's request delegate is already wrapped, so a
    /// group's and a route's <c>Finally</c> conventions wrap it once.
    /// </summary>
    private static object GatesApplied { get; } = new();

    /// <summary>
    /// Wraps the final request delegate of an endpoint once, with every gate in metadata order. Minimal APIs bind an
    /// endpoint's parameters inside its request delegate, so each gate runs before the binding. A group's gates precede
    /// a route's because a group's conventions add their metadata first.
    /// </summary>
    /// <param name="endpointBuilder">The <see cref="EndpointBuilder"/> of the endpoint to wrap.</param>
    private static void ApplyGates(EndpointBuilder endpointBuilder)
    {
        if (endpointBuilder.Metadata.Contains(GatesApplied))
        {
            return;
        }

        var gates = endpointBuilder.Metadata.OfType<EndpointGateMetadata>().ToArray();
        if (gates.Length == 0)
        {
            return;
        }

        var next =
            endpointBuilder.RequestDelegate
            ?? throw new InvalidOperationException("The endpoint has no request delegate to gate.");

        endpointBuilder.Metadata.Add(GatesApplied);
        endpointBuilder.RequestDelegate = httpContext =>
            InvokeAsync(gates, next, httpContext, httpContext.RequestAborted).AsTask();
    }

    /// <summary>
    /// Runs each gate in order, each created from the request's services, and answers with the first result a gate
    /// returns. Runs the endpoint when no gate returns a result.
    /// </summary>
    /// <param name="gates">The gates to run, in order.</param>
    /// <param name="next">The request delegate of the endpoint.</param>
    /// <param name="httpContext">The <see cref="HttpContext"/> of the request.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to observe.</param>
    /// <returns>A <see cref="ValueTask"/> that completes when the request has been handled.</returns>
    private static async ValueTask InvokeAsync(
        IReadOnlyList<EndpointGateMetadata> gates,
        RequestDelegate next,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        foreach (var metadata in gates)
        {
            var gate = metadata.Create(httpContext.RequestServices);
            var result = await gate.InvokeAsync(httpContext, cancellationToken);
            if (result is null)
            {
                continue;
            }

            await result.ExecuteAsync(httpContext);
            return;
        }

        await next(httpContext);
    }

    /// <param name="builder">The <see cref="IEndpointConventionBuilder"/> instance.</param>
    /// <typeparam name="TBuilder">The type of the <see cref="IEndpointConventionBuilder"/> instance.</typeparam>
    extension<TBuilder>(TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        /// <summary>
        /// Adds an <see cref="IEndpointGate"/> to the <see cref="IEndpointConventionBuilder"/>. The gate runs before
        /// minimal APIs bind the endpoint's parameters and before its endpoint filters, a group's gates run before a
        /// route's, and each gate is created for each request from the request's services.
        /// </summary>
        /// <typeparam name="TGate">The type of the <see cref="IEndpointGate"/> to run.</typeparam>
        /// <returns>The <see cref="IEndpointConventionBuilder"/> instance for method chaining.</returns>
        public TBuilder AddEndpointGate<TGate>()
            where TGate : class, IEndpointGate
        {
            ArgumentNullException.ThrowIfNull(builder);

            var factory = ActivatorUtilities.CreateFactory<TGate>(Type.EmptyTypes);
            var metadata = new EndpointGateMetadata(serviceProvider =>
                factory(serviceProvider, null)
            );
            builder.Add(endpointBuilder => endpointBuilder.Metadata.Add(metadata));
            builder.Finally(ApplyGates);
            return builder;
        }
    }
}
