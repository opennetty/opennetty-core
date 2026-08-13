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
/// Represents an OpenNetty endpoint.
/// </summary>
public sealed record class OpenNettyEndpoint : IEquatable<OpenNettyEndpoint>
{
    private volatile bool _writable = true;

    /// <summary>
    /// Gets or sets the address associated with the endpoint, if applicable.
    /// </summary>
    public OpenNettyAddress? Address { get; set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets or sets the capabilities associated with the endpoint.
    /// </summary>
    public ImmutableHashSet<OpenNettyCapability> Capabilities { get; set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets or sets the gateway that will process messages pointing to this endpoint.
    /// </summary>
    /// <remarks>
    /// Note: incoming frames that point to this endpoint but are not
    /// received by the specified gateway will be automatically ignored.
    /// </remarks>
    public OpenNettyGateway? Gateway { get; set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets a boolean indicating whether the current instance has been locked for user modification.
    /// </summary>
    public bool IsReadOnly => !_writable;

    /// <summary>
    /// Gets or sets the medium associated with the endpoint.
    /// </summary>
    public OpenNettyMedium? Medium { get; set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets or sets the name associated with the endpoint.
    /// </summary>
    public required string Name { get; set { ArgumentException.ThrowIfNullOrEmpty(value); VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets or sets the protocol associated with the endpoint.
    /// </summary>
    public required OpenNettyProtocol Protocol { get; set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets or sets the settings associated with the endpoint.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets or sets the unit associated with the endpoint, if applicable.
    /// </summary>
    public OpenNettyUnit? Unit { get; set { VerifyMutable(); field = value; } }

    /// <inheritdoc/>
    /// <remarks>
    /// Note: two endpoints are considered equal if they have the same
    /// address and the same unit, regardless of their other properties.
    /// </remarks>
    public bool Equals([NotNullWhen(true)] OpenNettyEndpoint? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return Address == other.Address && Unit == other.Unit;
    }

    /// <summary>
    /// Resolves the specified boolean setting from the settings attached
    /// to the endpoint (if set) or from the device or unit device objects.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Address, Unit);

    /// <summary>
    /// Resolves the specified integer setting from the settings attached
    /// to the endpoint (if set) or from the device or unit device objects.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The integer setting if it could be found, <see langword="null"/> otherwise.</returns>
    public long? GetIntegerSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && long.TryParse(value, CultureInfo.InvariantCulture, out long result) ? result : null;

    /// <summary>
    /// Resolves the specified string setting from the settings attached
    /// to the endpoint (if set) or from the device or unit device objects.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The string setting if it could be found, <see langword="null"/> otherwise.</returns>
    public string? GetStringSetting(OpenNettySetting setting) => TryGetSetting(setting, out string? value) ? value : null;

    /// <summary>
    /// Determines whether the endpoint - or the attached device or unit - has the specified capability.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <returns>
    /// <see langword="true"/> if the endpoint has the specified capability, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasCapability(OpenNettyCapability capability)
    {
        if (Capabilities.Contains(capability))
        {
            return true;
        }

        if (Unit is OpenNettyUnit unit)
        {
            return unit.HasCapability(capability);
        }

        return false;
    }

    /// <summary>
    /// Marks the current instance as read-only to prevent any further user modification.
    /// </summary>
    /// <remarks>This method is idempotent.</remarks>
    public void MakeReadOnly() => _writable = false;

    /// <summary>
    /// Tries to resolve the specified setting from the settings attached
    /// to the endpoint (if set) or from the device or unit device objects.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
    {
        if (Settings.TryGetValue(setting, out value))
        {
            return true;
        }

        if (Unit is OpenNettyUnit unit && (unit.TryGetSetting(setting, out value) || unit.Device.TryGetSetting(setting, out value)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current endpoint.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current endpoint.</returns>
    public override string ToString() => Name ?? string.Empty;

    /// <summary>
    /// Verifies that the current instance is mutable and throws an exception if it is not.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current instance is read-only.</exception>
    private void VerifyMutable()
    {
        if (!_writable)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID2021));
        }
    }
}
