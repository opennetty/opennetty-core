/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

namespace OpenNetty;

/// <summary>
/// Represents a setting attached to an OpenNetty endpoint, device or unit.
/// </summary>
public readonly struct OpenNettySetting : IEquatable<OpenNettySetting>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettySetting"/> structure.
    /// </summary>
    /// <param name="name">The setting name.</param>
    public OpenNettySetting(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
    }

    /// <summary>
    /// Gets the name associated with the setting.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc/>
    public bool Equals(OpenNettySetting other) => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettySetting setting && Equals(setting);

    /// <inheritdoc/>
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current setting.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current setting.</returns>
    public override string ToString() => Name?.ToString() ?? string.Empty;

    /// <summary>
    /// Determines whether two <see cref="OpenNettySetting"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettySetting left, OpenNettySetting right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettySetting"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettySetting left, OpenNettySetting right) => !(left == right);
}
