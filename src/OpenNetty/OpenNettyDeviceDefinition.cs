/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty device definition.
/// </summary>
public sealed class OpenNettyDeviceDefinition : IEquatable<OpenNettyDeviceDefinition>
{
    /// <summary>
    /// Gets or sets the capabilities associated with the device definition.
    /// </summary>
    public required ImmutableHashSet<OpenNettyCapability> Capabilities { get; init; } = [];

    /// <summary>
    /// Gets or sets the identities associated with the device definition.
    /// </summary>
    public required ImmutableArray<OpenNettyDeviceIdentity> Identities { get; init; }

    /// <summary>
    /// Gets or sets the medium associated with the device definition.
    /// </summary>
    public required OpenNettyMedium Medium { get; init; }

    /// <summary>
    /// Gets or sets the protocol associated with the device definition.
    /// </summary>
    public required OpenNettyProtocol Protocol { get; init; }

    /// <summary>
    /// Gets or sets the series associated with the device definition.
    /// </summary>
    public required string Series { get; init; }

    /// <summary>
    /// Gets or sets the OpenNetty-defined settings associated with the device definition.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } = [];

    /// <summary>
    /// Gets or sets the unit definitions associated with the device definition.
    /// </summary>
    public required ImmutableArray<OpenNettyUnitDefinition> Units { get; init; }

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] OpenNettyDeviceDefinition? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Capabilities.Count == other.Capabilities.Count && Capabilities.Except(other.Capabilities).IsEmpty &&
            Identities.Length == other.Identities.Length && !Identities.Except(other.Identities).Any() &&
            Medium == other.Medium &&
            Protocol == other.Protocol &&
            string.Equals(Series, other.Series, StringComparison.OrdinalIgnoreCase) &&
            Settings.Count == other.Settings.Count && !Settings.Except(other.Settings).Any() &&
            Units.Length == other.Units.Length && !Units.Except(other.Units).Any();
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is OpenNettyDeviceDefinition definition && Equals(definition);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Capabilities.Count);
        foreach (var capability in Capabilities)
        {
            hash.Add(capability);
        }

        hash.Add(Identities.Length);
        foreach (var identity in Identities)
        {
            hash.Add(identity);
        }

        hash.Add(Medium);
        hash.Add(Protocol);
        hash.Add(Series, StringComparer.OrdinalIgnoreCase);

        hash.Add(Settings.Count);
        foreach (var (name, value) in Settings)
        {
            hash.Add(name);
            hash.Add(value, StringComparer.Ordinal);
        }

        hash.Add(Units.Length);
        foreach (var unit in Units)
        {
            hash.Add(unit);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Resolves the identity corresponding to the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <returns>The resolved identity.</returns>
    public OpenNettyDeviceIdentity GetIdentity(OpenNettyBrand brand, string model)
        => TryGetIdentity(brand, model, out OpenNettyDeviceIdentity? identity)
            ? identity.Value
            : throw new InvalidOperationException(SR.GetResourceString(SR.ID0127));

    /// <summary>
    /// Resolves the unit corresponding to the specified identifier.
    /// </summary>
    /// <param name="identifier">The unit identifier.</param>
    /// <returns>The definition of the resolved unit.</returns>
    public OpenNettyUnitDefinition GetUnitDefinition(byte identifier)
        => TryGetUnitDefinition(identifier, out OpenNettyUnitDefinition? unit)
            ? unit
            : throw new InvalidOperationException(SR.FormatID0087(identifier));

    /// <summary>
    /// Determines whether the device has the specified capability.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <returns>
    /// <see langword="true"/> if the device has the specified capability, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasCapability(OpenNettyCapability capability) => Capabilities.Contains(capability);

    /// <summary>
    /// Determines whether the device has has an identity matching the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <returns>
    /// <see langword="true"/> if the device has an identity matching the
    /// specified brand and model, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasIdentity(OpenNettyBrand brand, string model)
    {
        if (!Enum.IsDefined(brand))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0006), nameof(brand));
        }

        ArgumentException.ThrowIfNullOrEmpty(model);

        for (var index = 0; index < Identities.Length; index++)
        {
            if (Identities[index].Brand == brand &&
                string.Equals(Identities[index].Model, model, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tries to resolve the identity corresponding to the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <param name="identity">The resolved identity if found, <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> if the device has an identity matching the
    /// specified brand and model, <see langword="false"/> otherwise.
    /// </returns>
    public bool TryGetIdentity(OpenNettyBrand brand, string model, [NotNullWhen(true)] out OpenNettyDeviceIdentity? identity)
    {
        if (!Enum.IsDefined(brand))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0006), nameof(brand));
        }

        ArgumentException.ThrowIfNullOrEmpty(model);

        for (var index = 0; index < Identities.Length; index++)
        {
            if (Identities[index].Brand == brand &&
                string.Equals(Identities[index].Model, model, StringComparison.OrdinalIgnoreCase))
            {
                identity = Identities[index];
                return true;
            }
        }

        identity = null;
        return false;
    }

    /// <summary>
    /// Tries to resolve the unit corresponding to the specified identifier.
    /// </summary>
    /// <param name="identifier">The unit identifier.</param>
    /// <param name="definition">The definition of the resolved unit if found, <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> if the device has a unit matching the specified identifier, <see langword="false"/> otherwise.
    /// </returns>
    public bool TryGetUnitDefinition(byte identifier, [NotNullWhen(true)] out OpenNettyUnitDefinition? definition)
    {
        for (var index = 0; index < Units.Length; index++)
        {
            if (Units[index].Id == identifier)
            {
                definition = Units[index];
                return true;
            }
        }

        definition = null;
        return false;
    }

    /// <summary>
    /// Determines whether two <see cref="OpenNettyDeviceDefinition"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyDeviceDefinition? left, OpenNettyDeviceDefinition? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyDeviceDefinition"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyDeviceDefinition? left, OpenNettyDeviceDefinition? right) => !(left == right);
}
