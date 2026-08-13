/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty device.
/// </summary>
public sealed record class OpenNettyDevice : IEquatable<OpenNettyDevice>
{
    private volatile bool _writable = true;

    /// <summary>
    /// Gets the device definition associated with the device.
    /// </summary>
    public OpenNettyDeviceDefinition Definition { get; internal set { VerifyMutable(); field = value; } } = default!;

    /// <summary>
    /// Gets or sets the gateway that will process messages pointing to this device.
    /// </summary>
    /// <remarks>
    /// Note: incoming frames that point to this device but are not
    /// received by the specified gateway will be automatically ignored.
    /// </remarks>
    public OpenNettyGateway? Gateway { get; set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets the unique identifier associated with the device,
    /// if applicable (typically, a serial number or a MAC address).
    /// </summary>
    /// <remarks>
    /// Note: some devices may not have a unique identifier (e.g, legacy SCS devices).
    /// </remarks>
    public OpenNettyDeviceIdentifier? Identifier { get; internal set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets the identity associated with the device.
    /// </summary>
    public OpenNettyDeviceIdentity Identity { get; internal set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets a boolean indicating whether the current instance has been locked for user modification.
    /// </summary>
    public bool IsReadOnly => !_writable;

    /// <summary>
    /// Gets the name associated with the device.
    /// </summary>
    public string Name { get; internal set { ArgumentException.ThrowIfNullOrEmpty(value); VerifyMutable(); field = value; } } = default!;

    /// <summary>
    /// Gets or sets the user-defined settings associated with the device, if applicable.
    /// </summary>
    /// <remarks>
    /// Note: the settings defined at the device level are automatically inherited by all units of the device.
    /// </remarks>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets or sets the units associated with the device.
    /// </summary>
    /// <remarks>
    /// Note: devices always have at least one unit, representing the device itself.
    /// </remarks>
    public ImmutableArray<OpenNettyUnit> Units { get; set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyDevice"/> class.
    /// </summary>
    internal OpenNettyDevice()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Note: two units are considered equal if they belong to the same device and have the
    /// same definition, name, identifier and identity, regardless of their other properties.
    /// </remarks>
    public bool Equals([NotNullWhen(true)] OpenNettyDevice? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Definition == other.Definition &&
            Identifier == other.Identifier &&
            Identity == other.Identity &&
            string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// Resolves the specified boolean setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Definition);
        hash.Add(Identifier);
        hash.Add(Identity);
        hash.Add(Name, StringComparer.Ordinal);

        return hash.ToHashCode();
    }

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
    /// Resolves the unit corresponding to the specified identifier.
    /// </summary>
    /// <param name="identifier">The unit identifier.</param>
    /// <returns>The resolved unit.</returns>
    public OpenNettyUnit GetUnit(byte identifier) => TryGetUnit(identifier, out OpenNettyUnit? unit)
        ? unit
        : throw new InvalidOperationException(SR.FormatID0087(identifier));

    /// <summary>
    /// Marks the current instance as read-only to prevent any further user modification.
    /// </summary>
    /// <remarks>This method is idempotent.</remarks>
    public void MakeReadOnly() => _writable = false;

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current device.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current device.</returns>
    public override string ToString() => Name ?? string.Empty;

    /// <summary>
    /// Tries to resolve the specified setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
        => Settings.TryGetValue(setting, out value) || Definition.Settings.TryGetValue(setting, out value);

    /// <summary>
    /// Tries to resolve the unit corresponding to the specified identifier.
    /// </summary>
    /// <param name="identifier">The unit identifier.</param>
    /// <param name="unit">The resolved unit if found, <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> if the device has a unit matching the specified identifier, <see langword="false"/> otherwise.
    /// </returns>
    public bool TryGetUnit(byte identifier, [NotNullWhen(true)] out OpenNettyUnit? unit)
    {
        if (identifier < Units.Length)
        {
            unit = Units[identifier];
            return true;
        }

        unit = null;
        return false;
    }

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

    /// <summary>
    /// Creates a new device based on the specified brand and model.
    /// </summary>
    /// <remarks>
    /// Note: the properties of the returned instance can be freely mutated until the
    /// <see cref="MakeReadOnly"/> method is called (explicitly or implicitly once initialization
    /// is complete), after which any further modification will throw an exception.
    /// </remarks>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <param name="name">The device name.</param>
    /// <param name="identifier">The device identifier.</param>
    /// <returns>A new device based on the specified brand and model.</returns>
    public static OpenNettyDevice Create(OpenNettyBrand brand, string model,
        string? name, OpenNettyDeviceIdentifier? identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(model);

        if (!Enum.IsDefined(brand))
        {
            throw new InvalidEnumArgumentException(nameof(brand), (int) brand, typeof(OpenNettyBrand));
        }

        var definition = OpenNettyDevices.GetDeviceDefinitionByModel(brand, model);

        if (string.IsNullOrEmpty(name))
        {
            if (identifier is null)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0108));
            }

            name = $"{Enum.GetName(brand)} {model} ({identifier})";
        }

        var device = new OpenNettyDevice
        {
            Definition = definition,
            Identifier = identifier,
            Identity = definition.GetIdentity(brand, model),
            Name = name,
            Units = [.. definition.Units.Select(static unit => new OpenNettyUnit { Definition = unit })]
        };

        foreach (var unit in device.Units)
        {
            unit.Device = device;
        }

        return device;
    }
}
