/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Reactive;

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
    public required ImmutableArray<OpenNettyIdentity> Identities { get; init; }

    /// <summary>
    /// Gets or sets the media associated with the device definition.
    /// </summary>
    public required OpenNettyMedia Media { get; init; }

    /// <summary>
    /// Gets or sets the protocol associated with the device definition.
    /// </summary>
    public required OpenNettyProtocol Protocol { get; init; }

    /// <summary>
    /// Gets or sets the OpenNetty-defined settings associated with the device definition.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } =
        ImmutableDictionary<OpenNettySetting, string>.Empty;

    /// <summary>
    /// Gets or sets the unit definitions associated with the device definition.
    /// </summary>
    public required ImmutableArray<OpenNettyUnitDefinition> Units { get; init; }

    /// <inheritdoc/>
    public bool Equals(OpenNettyDeviceDefinition? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Capabilities.Count == other.Capabilities.Count && Capabilities.Except(other.Capabilities).IsEmpty &&
            Identities.Length == other.Identities.Length && !Identities.Except(other.Identities).Any() &&
            Media == other.Media &&
            Protocol == other.Protocol &&
            Settings.Count == other.Settings.Count && !Settings.Except(other.Settings).Any() &&
            Units.Length == other.Units.Length && !Units.Except(other.Units).Any();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyDeviceDefinition definition && Equals(definition);

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

        hash.Add(Media);
        hash.Add(Protocol);

        hash.Add(Settings.Count);
        foreach (var (name, value) in Settings)
        {
            hash.Add(name);
            hash.Add(value);
        }

        hash.Add(Units.Length);
        foreach (var unit in Units)
        {
            hash.Add(unit);
        }

        return hash.ToHashCode();
    }

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

        foreach (var identity in Identities)
        {
            if (identity.Brand == brand && string.Equals(identity.Model, model, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

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
