/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty unit definition.
/// </summary>
public sealed class OpenNettyUnitDefinition : IEquatable<OpenNettyUnitDefinition>
{
    /// <summary>
    /// Gets or sets the identifier of the associated unit, if applicable (Nitoo-only).
    /// </summary>
    public ushort? AssociatedUnitId { get; init; }

    /// <summary>
    /// Gets or sets the capabilities associated with the unit definition.
    /// </summary>
    public required ImmutableHashSet<OpenNettyCapability> Capabilities { get; init; } = [];

    /// <summary>
    /// Gets or sets the identifier of the unit.
    /// </summary>
    public required ushort Id { get; init; }

    /// <summary>
    /// Gets or sets the OpenNetty-defined settings associated with the unit definition.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } =
        ImmutableDictionary<OpenNettySetting, string>.Empty;

    /// <inheritdoc/>
    public bool Equals(OpenNettyUnitDefinition? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            AssociatedUnitId == other.AssociatedUnitId &&
            Capabilities.Count == other.Capabilities.Count && Capabilities.Except(other.Capabilities).IsEmpty &&
            Id == other.Id &&
            Settings.Count == other.Settings.Count && !Settings.Except(other.Settings).Any();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyUnitDefinition definition && Equals(definition);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(AssociatedUnitId);

        hash.Add(Capabilities.Count);
        foreach (var capability in Capabilities)
        {
            hash.Add(capability);
        }

        hash.Add(Id);

        hash.Add(Settings.Count);
        foreach (var (name, value) in Settings)
        {
            hash.Add(name);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Determines whether the unit has the specified capability.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <returns>
    /// <see langword="true"/> if the unit has the specified capability, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasCapability(OpenNettyCapability capability) => Capabilities.Contains(capability);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyUnitDefinition"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyUnitDefinition? left, OpenNettyUnitDefinition? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyUnitDefinition"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyUnitDefinition? left, OpenNettyUnitDefinition? right) => !(left == right);
}
