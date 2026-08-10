/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty identity that uniquely
/// identifies a specific Legrand/BTicino product.
/// </summary>
public readonly record struct OpenNettyDeviceIdentity : IEquatable<OpenNettyDeviceIdentity>
{
    /// <summary>
    /// Gets or sets the brand.
    /// </summary>
    public required OpenNettyBrand Brand { get; init; }

    /// <summary>
    /// Gets or sets the collection, if applicable.
    /// </summary>
    public required string? Collection { get; init; }

    /// <summary>
    /// Gets or sets the descriptions.
    /// </summary>
    public required ImmutableDictionary<CultureInfo, string> Descriptions { get; init; }

    /// <summary>
    /// Gets or sets the product code.
    /// </summary>
    public required string Model { get; init { ArgumentException.ThrowIfNullOrEmpty(value); field = value; } }

    /// <inheritdoc/>
    public bool Equals(OpenNettyDeviceIdentity other) => Brand == other.Brand &&
        string.Equals(Collection, other.Collection, StringComparison.OrdinalIgnoreCase) &&
        Descriptions.Count == other.Descriptions.Count && !Descriptions.Except(other.Descriptions).Any() &&
        string.Equals(Model, other.Model, StringComparison.OrdinalIgnoreCase);

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
        hash.Add(Brand);
        hash.Add(Collection, StringComparer.OrdinalIgnoreCase);

        hash.Add(Descriptions.Count);
        foreach (var (culture, value) in Descriptions)
        {
            hash.Add(culture);
            hash.Add(value, StringComparer.OrdinalIgnoreCase);
        }

        hash.Add(Model, StringComparer.OrdinalIgnoreCase);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current identity.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current identity.</returns>
    public override string ToString() => $"{Enum.GetName(Brand)} {Collection} ({Model})";
}
