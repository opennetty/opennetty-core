/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;

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
