/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Diagnostics;
using System.Text;

namespace OpenNetty;

/// <summary>
/// Represents a raw OpenNetty parameter.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct OpenNettyParameter : IEquatable<OpenNettyParameter>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyParameter"/> structure.
    /// </summary>
    /// <param name="value">The value.</param>
    public OpenNettyParameter(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        // Ensure the value only includes ASCII digits.
        foreach (var character in value)
        {
            if (!char.IsAsciiDigit(character))
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0004), nameof(value));
            }
        }

        Value = value;
    }

    /// <summary>
    /// Gets a boolean indicating whether the parameter represents an empty value.
    /// </summary>
    public bool IsEmpty => string.IsNullOrEmpty(Value);

    /// <summary>
    /// Gets the value associated with the parameter.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Represents an empty parameter.
    /// </summary>
    public static readonly OpenNettyParameter Empty = new(string.Empty);

    /// <summary>
    /// Parses an OpenNetty parameter from the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The ASCII/UTF-8 buffer containing the raw parameter.</param>
    /// <returns>The OpenNetty parameter corresponding to the specified <paramref name="buffer"/>.</returns>
    public static OpenNettyParameter Parse(ReadOnlySpan<byte> buffer)
    {
        // Note: parameters can be omitted. In this case, they are represented as empty values.
        if (buffer.IsEmpty)
        {
            return new(string.Empty);
        }

        return new(Encoding.ASCII.GetString(buffer));
    }

    /// <inheritdoc/>
    public bool Equals(OpenNettyParameter other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyParameter parameter && Equals(parameter);

    /// <inheritdoc/>
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current parameter.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current parameter.</returns>
    public override string ToString() => Value ?? string.Empty;

    /// <summary>
    /// Determines whether two <see cref="OpenNettyParameter"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyParameter left, OpenNettyParameter right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyParameter"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyParameter left, OpenNettyParameter right) => !(left == right);
}
