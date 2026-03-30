/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty unit definition.
/// </summary>
public sealed class OpenNettyUnitDefinition : IEquatable<OpenNettyUnitDefinition>
{
    /// <summary>
    /// Gets or sets the identifier of the associated unit, if applicable (Nitoo-only).
    /// </summary>
    public byte? AssociatedUnitId { get; init; }

    /// <summary>
    /// Gets or sets the descriptions associated with the unit definition.
    /// </summary>
    public required ImmutableDictionary<CultureInfo, string> Descriptions { get; init; }

    /// <summary>
    /// Gets or sets the capabilities associated with the unit definition.
    /// </summary>
    public required ImmutableHashSet<OpenNettyCapability> Capabilities { get; init; } = [];

    /// <summary>
    /// Gets or sets the identifier of the unit.
    /// </summary>
    public required byte Id { get; init; }

    /// <summary>
    /// Gets or sets the OpenNetty-defined settings associated with the unit definition.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } = [];

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
            Descriptions.Count == other.Descriptions.Count && !Descriptions.Except(other.Descriptions).Any() &&
            Id == other.Id &&
            Settings.Count == other.Settings.Count && !Settings.Except(other.Settings).Any();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyUnitDefinition definition && Equals(definition);

    /// <summary>
    /// Gets the localized description corresponding to the specified culture (or one of its parents).
    /// If the description is not available in the specified culture, the English version is returned if available.
    /// </summary>
    /// <param name="culture">The culture.</param>
    /// <returns>
    /// The localized description corresponding to the specified culture,
    /// or <see langword="null"/> if it's not available.
    /// </returns>
    public string? GetDescription(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        string? description;

        do
        {
            if (Descriptions.TryGetValue(culture, out description))
            {
                return description;
            }

            culture = culture.Parent;
        }

        while (culture != CultureInfo.InvariantCulture);

        return Descriptions.TryGetValue(CultureInfo.GetCultureInfo("en"), out description) ? description : null;
    }

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

        hash.Add(Descriptions.Count);
        foreach (var (culture, value) in Descriptions)
        {
            hash.Add(culture);
            hash.Add(value);
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
