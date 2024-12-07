namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty identity that uniquely
/// identifies a specific Legrand/BTicino product.
/// </summary>
public readonly struct OpenNettyIdentity : IEquatable<OpenNettyIdentity>
{
    /// <summary>
    /// Gets or sets the brand.
    /// </summary>
    public required OpenNettyBrand Brand { get; init; }

    /// <summary>
    /// Gets or sets the model.
    /// </summary>
    public required string Model { get; init; }

    /// <inheritdoc/>
    public bool Equals(OpenNettyIdentity other) => Brand == other.Brand &&
        string.Equals(Model, other.Model, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyIdentity identity && Equals(identity);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Brand, Model);

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current identity.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current identity.</returns>
    public override string ToString() => $"{Enum.GetName(Brand)} {Model}";

    /// <summary>
    /// Determines whether two <see cref="OpenNettyIdentity"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyIdentity left, OpenNettyIdentity right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyIdentity"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyIdentity left, OpenNettyIdentity right) => !(left == right);
}
