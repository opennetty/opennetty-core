using System.Collections.Immutable;

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
    /// Gets or sets the serial number associated with the device,
    /// if applicable (required for Nitoo and Zigbee devices).
    /// </summary>
    public string? SerialNumber { get; init; }

    /// <summary>
    /// Gets or sets the user-defined settings associated with the device, if applicable.
    /// </summary>
    public ImmutableDictionary<OpenNettySetting, string> Settings { get; init; } =
        ImmutableDictionary<OpenNettySetting, string>.Empty;

    /// <inheritdoc/>
    public bool Equals(OpenNettyDevice? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            Definition == other.Definition &&
            string.Equals(SerialNumber, other.SerialNumber, StringComparison.OrdinalIgnoreCase) &&
            Settings.Count == other.Settings.Count && !Settings.Except(other.Settings).Any();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyDevice device && Equals(device);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Definition);
        hash.Add(SerialNumber);

        hash.Add(Settings.Count);
        foreach (var (name, value) in Settings)
        {
            hash.Add(name);
            hash.Add(value);
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
