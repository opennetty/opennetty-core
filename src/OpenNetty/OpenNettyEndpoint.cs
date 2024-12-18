/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty endpoint.
/// </summary>
public sealed class OpenNettyEndpoint : IEquatable<OpenNettyEndpoint>
{
    /// <summary>
    /// Gets or sets the address associated with the endpoint, if applicable.
    /// </summary>
    public OpenNettyAddress? Address { get; init; }

    /// <summary>
    /// Gets or sets the capabilities associated with the endpoint.
    /// </summary>
    public ImmutableHashSet<OpenNettyCapability> Capabilities { get; init; } = [];

    /// <summary>
    /// Gets or sets the device associated with the endpoint, if applicable.
    /// </summary>
    public OpenNettyDevice? Device { get; init; }

    /// <summary>
    /// Gets or sets the gateway that will process messages pointing to this endpoint.
    /// </summary>
    /// <remarks>
    /// Note: incoming frames that point to this endpoint but are not
    /// received by the specified gateway will be automatically ignored.
    /// </remarks>
    public OpenNettyGateway? Gateway { get; init; }

    /// <summary>
    /// Gets or sets the medium associated with the endpoint.
    /// </summary>
    public OpenNettyMedium? Medium { get; init; }

    /// <summary>
    /// Gets or sets the optional name associated with the endpoint.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets or sets the protocol associated with the endpoint.
    /// </summary>
    public required OpenNettyProtocol Protocol { get; init; }

    /// <summary>
    /// Gets or sets the settings associated with the endpoint.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } =
        ImmutableDictionary<OpenNettySetting, string>.Empty;

    /// <summary>
    /// Gets or sets the unit associated with the endpoint, if applicable.
    /// </summary>
    /// <remarks>Note: units are only valid for Nitoo or Zigbee endpoints.</remarks>
    public OpenNettyUnit? Unit { get; init; }

    /// <summary>
    /// Resolves the specified boolean setting from the settings attached
    /// to the endpoint (if set) or from the device or unit device objects.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

    /// <summary>
    /// Resolves the specified string setting from the settings attached
    /// to the endpoint (if set) or from the device or unit device objects.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The string setting if it could be found, <see langword="null"/> otherwise.</returns>
    public string? GetStringSetting(OpenNettySetting setting) => TryGetSetting(setting, out string? value) ? value : null;

    /// <summary>
    /// Determines whether the endpoint or the attached unit/device have the specified capability.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <returns>
    /// <see langword="true"/> if the endpoint or the attached unit/device
    /// have the specified capability, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasCapability(OpenNettyCapability capability)
    {
        if (Protocol is OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee &&
            Unit is OpenNettyUnit unit)
        {
            return unit.Definition.HasCapability(capability);
        }

        if (Device is OpenNettyDevice device)
        {
            return device.Definition.HasCapability(capability);
        }

        return Capabilities.Contains(capability);
    }

    /// <summary>
    /// Tries to resolve the specified setting from the settings attached
    /// to the endpoint (if set) or from the device or unit device objects.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
    {
        if (Protocol is OpenNettyProtocol.Nitoo or OpenNettyProtocol.Zigbee &&
            Unit is OpenNettyUnit unit)
        {
            return unit.Settings.TryGetValue(setting, out value) ||
                unit.Definition.Settings.TryGetValue(setting, out value) ||
                Settings.TryGetValue(setting, out value);
        }

        else if (Device is OpenNettyDevice device)
        {
            return device.Settings.TryGetValue(setting, out value) ||
                device.Definition.Settings.TryGetValue(setting, out value) ||
                Settings.TryGetValue(setting, out value);
        }

        return Settings.TryGetValue(setting, out value);
    }

    /// <inheritdoc/>
    public bool Equals(OpenNettyEndpoint? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        if (Address != other.Address)
        {
            return false;
        }

        if (Capabilities.Count != other.Capabilities.Count || !Capabilities.Except(other.Capabilities).IsEmpty)
        {
            return false;
        }

        if (Device != other.Device)
        {
            return false;
        }

        if (Medium != other.Medium)
        {
            return false;
        }

        if (!string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Protocol != other.Protocol)
        {
            return false;
        }

        if (Settings.Count != other.Settings.Count || Settings.Except(other.Settings).Any())
        {
            return false;
        }

        if (Unit != other.Unit)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyEndpoint endpoint && Equals(endpoint);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Address);

        hash.Add(Capabilities.Count);
        foreach (var capability in Capabilities)
        {
            hash.Add(capability);
        }

        hash.Add(Device);
        hash.Add(Medium);
        hash.Add(Name);
        hash.Add(Protocol);

        hash.Add(Settings.Count);
        foreach (var (name, value) in Settings)
        {
            hash.Add(name);
            hash.Add(value);
        }

        hash.Add(Unit);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current endpoint.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current endpoint.</returns>
    public override string ToString() => Name ?? string.Empty;

    /// <summary>
    /// Determines whether two <see cref="OpenNettyEndpoint"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyEndpoint? left, OpenNettyEndpoint? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyEndpoint"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyEndpoint? left, OpenNettyEndpoint? right) => !(left == right);
}
