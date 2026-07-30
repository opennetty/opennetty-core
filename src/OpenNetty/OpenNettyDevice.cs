/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

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
    /// Gets or sets the gateway that will process messages pointing to this device.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>
    /// Note: incoming frames that point to this device but are not
    /// received by the specified gateway will be automatically ignored.
    /// </item>
    /// <item>
    /// This property must be set to <see langword="null"/> for gateway devices.
    /// </item>
    /// </list>
    /// </remarks>
    public OpenNettyGateway? Gateway { get; init; }

    /// <summary>
    /// Gets or sets the unique identifier associated with the device,
    /// if applicable (typically, a serial number or a MAC address).
    /// </summary>
    /// <remarks>
    /// Note: some devices may not have a unique identifier (e.g, legacy SCS devices).
    /// </remarks>
    public OpenNettyDeviceIdentifier? Identifier { get; init; }

    /// <summary>
    /// Gets or sets the identity associated with the device.
    /// </summary>
    public required OpenNettyDeviceIdentity Identity { get; init; }

    /// <summary>
    /// Gets or sets the name associated with the device.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the user-defined settings associated with the device, if applicable.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } = [];

    /// <summary>
    /// Gets or sets the units associated with the device.
    /// </summary>
    public ImmutableArray<OpenNettyUnit> Units { get; init; } = [];

    /// <summary>
    /// Resolves the specified boolean setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

    /// <summary>
    /// Resolves the specified integer setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The integer setting if it could be found, <see langword="null"/> otherwise.</returns>
    public long? GetIntegerSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && long.TryParse(value, CultureInfo.InvariantCulture, out long result) ? result : null;

    /// <summary>
    /// Resolves the specified string setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The string setting if it could be found, <see langword="null"/> otherwise.</returns>
    public string? GetStringSetting(OpenNettySetting setting) => TryGetSetting(setting, out string? value) ? value : null;

    /// <summary>
    /// Determines whether the device has the specified capability.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <returns>
    /// <see langword="true"/> if the device has the specified capability, <see langword="false"/> otherwise.
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
    public bool Equals(OpenNettyDevice? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Definition == other.Definition &&
            Gateway == other.Gateway &&
            Identifier == other.Identifier &&
            Identity == other.Identity &&
            string.Equals(Name, other.Name, StringComparison.Ordinal) &&
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
        hash.Add(Gateway);
        hash.Add(Identifier);
        hash.Add(Identity);
        hash.Add(Name, StringComparer.Ordinal);

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
    /// Computes the <see cref="string"/> representation of the current device.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current device.</returns>
    public override string ToString() => Name ?? string.Empty;

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
