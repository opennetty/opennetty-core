/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty device.
/// </summary>
public sealed class OpenNettyDevice : IEquatable<OpenNettyDevice>
{
    /// <summary>
    /// Gets or sets the device definition associated with the device.
    /// </summary>
    public required OpenNettyDeviceDefinition Definition { get; init; }

    /// <summary>
    /// Gets or sets the identity associated with the device.
    /// </summary>
    public required OpenNettyIdentity Identity { get; init; }

    /// <summary>
    /// Gets or sets the serial number associated with the device,
    /// if applicable (required for Nitoo and Zigbee devices).
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Gets or sets the user-defined settings associated with the device, if applicable.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } =
        ImmutableDictionary<OpenNettySetting, string>.Empty;

    /// <summary>
    /// Gets or sets the units associated with the device, if applicable.
    /// </summary>
    public ImmutableArray<OpenNettyUnit> Units { get; init; }

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
    /// Tries to resolve the specified setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
        => Settings.TryGetValue(setting, out value);

    /// <inheritdoc/>
    public bool Equals(OpenNettyDevice? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Definition == other.Definition &&
            Identity == other.Identity &&
            string.Equals(SerialNumber, other.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
            Settings.Count == other.Settings.Count && !Settings.Except(other.Settings).Any() &&
            Units.Length == other.Units.Length && !Units.Except(other.Units).Any();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyDevice device && Equals(device);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Definition);
        hash.Add(Identity);
        hash.Add(SerialNumber);

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
    /// Computes the <see cref="string"/> representation of the current device.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current device.</returns>
    public override string ToString() => SerialNumber ?? string.Empty;

    /// <summary>
    /// Determines whether two <see cref="OpenNettyDevice"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyDevice? left, OpenNettyDevice? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyDevice"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyDevice? left, OpenNettyDevice? right) => !(left == right);
}
