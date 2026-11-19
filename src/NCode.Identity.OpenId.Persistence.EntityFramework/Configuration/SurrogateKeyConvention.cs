// Copyright © NCode Group
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace NCode.Identity.OpenId.Persistence.EntityFramework.Configuration;

/// <summary>
/// Provides a convention to use <see cref="SurrogateKeyValueGenerator"/> for properties marked with
/// <see cref="SurrogateKeyAttribute"/>.
/// </summary>
internal sealed class SurrogateKeyConvention : IPropertyAddedConvention
{
    /// <inheritdoc />
    public void ProcessPropertyAdded(
        IConventionPropertyBuilder propertyBuilder,
        IConventionContext<IConventionPropertyBuilder> context
    )
    {
        var metadata = propertyBuilder.Metadata;
        var memberInfo = metadata.PropertyInfo ?? (MemberInfo?)metadata.FieldInfo;
        if (memberInfo == null)
        {
            return;
        }

        if (!Attribute.IsDefined(memberInfo, typeof(SurrogateKeyAttribute), inherit: true))
        {
            return;
        }

        if (metadata.ClrType != typeof(Guid))
        {
            throw new InvalidOperationException(
                $"The surrogate key '{metadata.DeclaringType.DisplayName()}.{metadata.Name}' is not a Guid."
            );
        }

        propertyBuilder.ValueGenerated(ValueGenerated.Never, fromDataAnnotation: true);
        propertyBuilder.HasValueGenerator(
            (_, _) => new SurrogateKeyValueGenerator(),
            fromDataAnnotation: true
        );
    }
}
