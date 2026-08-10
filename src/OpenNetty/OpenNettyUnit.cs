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
/// Represents an OpenNetty unit.
/// </summary>
public sealed record class OpenNettyUnit : IEquatable<OpenNettyUnit>
{
    private volatile bool _writable = true;

    /// <summary>
    /// Gets the unit definition associated with the unit.
    /// </summary>
    public OpenNettyUnitDefinition Definition { get; internal set { VerifyMutable(); field = value; } } = default!;

    /// <summary>
    /// Gets the device associated with the unit.
    /// </summary>
    public OpenNettyDevice Device { get; internal set { VerifyMutable(); field = value; } } = default!;

    /// <summary>
    /// Gets a boolean indicating whether the current instance has been locked for user modification.
    /// </summary>
    public bool IsReadOnly => !_writable;

    /// <summary>
    /// Gets or sets the scenarios associated with the unit, if applicable (Nitoo-only).
    /// </summary>
    public ImmutableArray<OpenNettyScenario> Scenarios { get; set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Gets or sets the user-defined settings associated with the unit, if applicable.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; set { VerifyMutable(); field = value; } } = [];

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyUnit"/> class.
    /// </summary>
    internal OpenNettyUnit()
    {
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Note: two units are considered equal if they belong to the same device
    /// and have the same definition, regardless of their other properties.
    /// </remarks>
    public bool Equals([NotNullWhen(true)] OpenNettyUnit? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null && Definition == other.Definition && Device == other.Device;
    }

    /// <summary>
    /// Resolves the specified boolean setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <returns>The boolean setting if it could be found, <see langword="null"/> otherwise.</returns>
    public bool? GetBooleanSetting(OpenNettySetting setting)
        => TryGetSetting(setting, out string? value) && bool.TryParse(value, out bool result) ? result : null;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Definition, Device);

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
    public bool HasCapability(OpenNettyCapability capability) => Definition.HasCapability(capability);

    /// <summary>
    /// Tries to resolve the specified setting from the settings.
    /// </summary>
    /// <param name="setting">The setting name.</param>
    /// <param name="value">The setting value, or <see langword="null"/> if it was not found.</param>
    /// <returns><see langword="true"/> if the setting was found, <see langword="false"/> otherwise.</returns>
    public bool TryGetSetting(OpenNettySetting setting, [NotNullWhen(true)] out string? value)
        => Settings.TryGetValue(setting, out value) || Definition.Settings.TryGetValue(setting, out value);

    /// <summary>
    /// Marks the current instance as read-only to prevent any further user modification.
    /// </summary>
    /// <remarks>This method is idempotent.</remarks>
    public void MakeReadOnly() => _writable = false;

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
