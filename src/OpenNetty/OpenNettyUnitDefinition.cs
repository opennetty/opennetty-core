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
/// Represents an OpenNetty unit definition.
/// </summary>
public sealed record class OpenNettyUnitDefinition : IEquatable<OpenNettyUnitDefinition>
{
    private volatile bool _writable = true;

    /// <summary>
    /// Gets the identifier of the associated unit, if applicable (Nitoo-only).
    /// </summary>
    public byte? AssociatedUnitId { get; internal set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets the capabilities associated with the unit definition.
    /// </summary>
    public ImmutableHashSet<OpenNettyCapability> Capabilities { get; internal set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets the descriptions associated with the unit definition.
    /// </summary>
    public ImmutableDictionary<CultureInfo, string> Descriptions { get; internal set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets the device definition associated with the unit definition.
    /// </summary>
    public OpenNettyDeviceDefinition Device { get; internal set { VerifyMutable(); field = value; } } = default!;

    /// <summary>
    /// Gets the identifier of the unit.
    /// </summary>
    public byte Id { get; internal set { VerifyMutable(); field = value; } }

    /// <summary>
    /// Gets a boolean indicating whether the current instance has been locked for user modification.
    /// </summary>
    public bool IsReadOnly => !_writable;

    /// <summary>
    /// Gets the settings associated with the unit definition.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; internal set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyUnitDefinition"/> class.
    /// </summary>
    internal OpenNettyUnitDefinition()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Note: two unit definitions are considered equal if they belong to the same
    /// device and have the same identifier, regardless of their other properties.
    /// </remarks>
    public bool Equals([NotNullWhen(true)] OpenNettyUnitDefinition? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && Device == other.Device && Id == other.Id;
    }

    /// <summary>
    /// Resolves the specified boolean setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

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
    public override int GetHashCode() => HashCode.Combine(Device, Id);

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
    /// Determines whether the unit has the specified capability.
    /// </summary>
    /// <param name="capability">The capability name.</param>
    /// <returns>
    /// <see langword="true"/> if the unit has the specified capability, <see langword="false"/> otherwise.
    /// </returns>
    public bool HasCapability(OpenNettyCapability capability) => Capabilities.Contains(capability);

    /// <summary>
    /// Marks the current instance as read-only to prevent any further user modification.
    /// </summary>
    /// <remarks>This method is idempotent.</remarks>
    public void MakeReadOnly() => _writable = false;

    /// <summary>
    /// Tries to resolve the specified setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
        => Settings.TryGetValue(setting, out value);

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
