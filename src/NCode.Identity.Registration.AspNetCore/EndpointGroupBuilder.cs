// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NCode.Identity.Registration.AspNetCore;

/// <summary>
/// Provides the default implementation of <see cref="IEndpointGroupBuilder"/> that registers a group's endpoints as
/// keyed <see cref="IEndpointProvider"/> services and its child groups as keyed <see cref="IEndpointGroup"/> services,
/// both keyed by the owning group's <see cref="IEndpointGroup.Name"/>, and recurses into each child group's
/// <see cref="IEndpointGroup.ConfigureServices"/>.
/// </summary>
internal sealed class EndpointGroupBuilder(IServiceBuilder serviceBuilder, string groupName)
    : IEndpointGroupBuilder
{
    /// <inheritdoc />
    public IServiceCollection ServiceCollection => serviceBuilder.ServiceCollection;

    /// <inheritdoc />
    public IServiceBuilder<TNewMarker> NewBuilder<TNewMarker>()
        where TNewMarker : IRegistrationMarker<TNewMarker>, new() =>
        serviceBuilder.NewBuilder<TNewMarker>();

    /// <inheritdoc />
    public void AddEndpoint<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T
    >()
        where T : class, IEndpointProvider
    {
        var descriptor = ServiceDescriptor.KeyedSingleton<IEndpointProvider, T>(groupName);
        ServiceCollection.TryAddEnumerable(descriptor);
    }

    /// <inheritdoc />
    public void AddGroup<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T
    >()
        where T : class, IEndpointGroup, new()
    {
        var child = new T();
        var descriptor = ServiceDescriptor.KeyedSingleton<IEndpointGroup>(groupName, child);
        ServiceCollection.TryAddEnumerable(descriptor);
        var childBuilder = new EndpointGroupBuilder(serviceBuilder, child.Name);
        child.ConfigureServices(childBuilder);
    }
}
