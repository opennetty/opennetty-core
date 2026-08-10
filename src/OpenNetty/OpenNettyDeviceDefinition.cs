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
/// Represents an OpenNetty device definition.
/// </summary>
public sealed record class OpenNettyDeviceDefinition : IEquatable<OpenNettyDeviceDefinition>
{
    private volatile bool _writable = true;

    /// <summary>
    /// Gets the unique identifier associated with the device definition.
    /// </summary>
    public Guid Id { get; internal set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets the identities associated with the device definition.
    /// </summary>
    public ImmutableArray<OpenNettyDeviceIdentity> Identities { get; internal set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets a boolean indicating whether the current instance has been locked for user modification.
    /// </summary>
    public bool IsReadOnly => !_writable;

    /// <summary>
    /// Gets the medium associated with the device definition.
    /// </summary>
    public OpenNettyMedium Medium { get; internal set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets the protocol associated with the device definition.
    /// </summary>
    public OpenNettyProtocol Protocol { get; internal set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets the series associated with the device definition.
    /// </summary>
    public string Series { get; internal set { ArgumentException.ThrowIfNullOrEmpty(value); VerifyMutable(); field = value; } } = default!;

    /// <summary>
    /// Gets the settings associated with the device definition.
    /// </summary>
    /// <remarks>
    /// Note: the settings defined at the device level are automatically inherited by all units of the device.
    /// </remarks>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; internal set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets the unit definitions associated with the device definition.
    /// </summary>
    /// <remarks>
    /// Note: all device definitions have at least one unit definition, representing the root unit of the device.
    /// </remarks>
    public ImmutableArray<OpenNettyUnitDefinition> Units { get; internal set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyDeviceDefinition"/> class.
    /// </summary>
    internal OpenNettyDeviceDefinition()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Note: two device definitions are considered equal if they have
    /// the same unique identifier, regardless of their other properties.
    /// </remarks>
    public bool Equals([NotNullWhen(true)] OpenNettyDeviceDefinition? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && Id == other.Id;
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// Resolves the specified boolean setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

    /// <summary>
    /// Resolves the identity corresponding to the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <returns>The resolved identity.</returns>
    public OpenNettyDeviceIdentity GetIdentity(OpenNettyBrand brand, string model)
        => TryGetIdentity(brand, model, out OpenNettyDeviceIdentity? identity)
            ? identity.Value
            : throw new InvalidOperationException(SR.GetResourceString(SR.ID0127));

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
    /// Resolves the unit definition corresponding to the specified identifier.
    /// </summary>
    /// <param name="identifier">The unit identifier.</param>
    /// <returns>The definition of the resolved unit.</returns>
    public OpenNettyUnitDefinition GetUnitDefinition(byte identifier) => TryGetUnitDefinition(identifier, out OpenNettyUnitDefinition? unit)
        ? unit
        : throw new InvalidOperationException(SR.FormatID0087(identifier));

    /// <summary>
    /// Determines whether the device has has an identity matching the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <returns>
    /// <see langword="true"/> if the device has an identity matching the
    /// specified brand and model, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasIdentity(OpenNettyBrand brand, string model)
    {
        if (!Enum.IsDefined(brand))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0006), nameof(brand));
        }

        ArgumentException.ThrowIfNullOrEmpty(model);

        for (var index = 0; index < Identities.Length; index++)
        {
            if (Identities[index].Brand == brand &&
                string.Equals(Identities[index].Model, model, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Marks the current instance as read-only to prevent any further user modification.
    /// </summary>
    /// <remarks>This method is idempotent.</remarks>
    public void MakeReadOnly() => _writable = false;

    /// <summary>
    /// Tries to resolve the identity corresponding to the specified brand and model.
    /// </summary>
    /// <param name="brand">The device brand.</param>
    /// <param name="model">The device model.</param>
    /// <param name="identity">The resolved identity if found, <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> if the device has an identity matching the
    /// specified brand and model, <see langword="false"/> otherwise.
    /// </returns>
    public bool TryGetIdentity(OpenNettyBrand brand, string model, [NotNullWhen(true)] out OpenNettyDeviceIdentity? identity)
    {
        if (!Enum.IsDefined(brand))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0006), nameof(brand));
        }

        ArgumentException.ThrowIfNullOrEmpty(model);

        for (var index = 0; index < Identities.Length; index++)
        {
            if (Identities[index].Brand == brand &&
                string.Equals(Identities[index].Model, model, StringComparison.OrdinalIgnoreCase))
            {
                identity = Identities[index];
                return true;
            }
        }

        identity = null;
        return false;
    }

    /// <summary>
    /// Tries to resolve the specified setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
        => Settings.TryGetValue(setting, out value);

    /// <summary>
    /// Tries to resolve the unit definition corresponding to the specified identifier.
    /// </summary>
    /// <param name="identifier">The unit identifier.</param>
    /// <param name="definition">The definition of the resolved unit if found, <see langword="null"/> otherwise.</param>
    /// <returns>
    /// <see langword="true"/> if the device has a unit definition matching the specified identifier, <see langword="false"/> otherwise.
    /// </returns>
    public bool TryGetUnitDefinition(byte identifier, [NotNullWhen(true)] out OpenNettyUnitDefinition? definition)
    {
        if (identifier < Units.Length)
        {
            definition = Units[identifier];
            return true;
        }

        definition = null;
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
}
