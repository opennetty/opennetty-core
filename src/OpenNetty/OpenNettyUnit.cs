/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty unit.
/// </summary>
public sealed class OpenNettyUnit : IEquatable<OpenNettyUnit>
{
    /// <summary>
    /// Gets or sets the unit definition associated with the unit.
    /// </summary>
    public required OpenNettyUnitDefinition Definition { get; init; }

    /// <summary>
    /// Gets or sets the scenarios associated with the unit, if applicable.
    /// </summary>
    public ImmutableArray<OpenNettyScenario> Scenarios { get; init; } = [];

    /// <summary>
    /// Gets or sets the user-defined settings associated with the unit, if applicable.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } =
        ImmutableDictionary<OpenNettySetting, string>.Empty;

    /// <summary>
    /// Resolves the specified boolean setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

    /// <summary>
    /// Resolves the specified string setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The string setting if it could be found, <see langword="null"/> otherwise.</returns>
    public string? GetStringSetting(OpenNettySetting setting) => TryGetSetting(setting, out string? value) ? value : null;

    /// <summary>
    /// Determines whether the unit has the specified capability.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <returns>
    /// <see langword="true"/> if the unit has the specified capability, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasCapability(OpenNettyCapability capability) => Definition.HasCapability(capability);

    /// <summary>
    /// Tries to resolve the specified setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
        => Settings.TryGetValue(setting, out value) || Definition.Settings.TryGetValue(setting, out value);

    /// <inheritdoc/>
    public bool Equals(OpenNettyUnit? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Definition == other.Definition &&
            Scenarios.Length == other.Scenarios.Length && !Scenarios.Except(other.Scenarios).Any() &&
            Settings.Count == other.Settings.Count && !Settings.Except(other.Settings).Any();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyUnit unit && Equals(unit);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Definition);

        hash.Add(Scenarios.Length);
        foreach (var scenario in Scenarios)
        {
            hash.Add(scenario);
        }

        hash.Add(Settings.Count);
        foreach (var (name, value) in Settings)
        {
            hash.Add(name);
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Determines whether two <see cref="OpenNettyUnit"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyUnit? left, OpenNettyUnit? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyUnit"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyUnit? left, OpenNettyUnit? right) => !(left == right);
}
