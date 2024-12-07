namespace OpenNetty;

/// <summary>
/// Represents a capability supported by an OpenNetty endpoint or device.
/// </summary>
public readonly struct OpenNettyCapability : IEquatable<OpenNettyCapability>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyCapability"/> structure.
    /// </summary>
    /// <param name="name">The capability name.</param>
    public OpenNettyCapability(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Name = name;
    }

    /// <summary>
    /// Gets the name associated with the capability.
    /// </summary>
    public string Name { get; }

    /// <inheritdoc/>
    public bool Equals(OpenNettyCapability other) => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyCapability capability && Equals(capability);

    /// <inheritdoc/>
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current capability.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current capability.</returns>
    public override string ToString() => Name?.ToString() ?? string.Empty;

    /// <summary>
    /// Determines whether two <see cref="OpenNettyCapability"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyCapability left, OpenNettyCapability right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyCapability"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyCapability left, OpenNettyCapability right) => !(left == right);
}
