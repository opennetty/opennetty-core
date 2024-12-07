/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Diagnostics;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty transaction that can be used to correlate multiple OpenNetty notifications.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct OpenNettyTransaction : IEquatable<OpenNettyTransaction>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyTransaction"/> structure.
    /// </summary>
    /// <param name="identifier">The GUID used to identify the transaction.</param>
    public OpenNettyTransaction(Guid identifier) => Identifier = identifier;

    /// <summary>
    /// Gets the GUID that identifies the transaction.
    /// </summary>
    public Guid Identifier { get; }

    /// <summary>
    /// Creates a new <see cref="OpenNettyTransaction"/> instance.
    /// </summary>
    /// <returns>A new <see cref="OpenNettyTransaction"/> instance.</returns>
    public static OpenNettyTransaction Create() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public bool Equals(OpenNettyTransaction transaction) => Identifier == transaction.Identifier;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyTransaction transaction && Equals(transaction);

    /// <inheritdoc/>
    public override int GetHashCode() => Identifier.GetHashCode();

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current transaction.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current transaction.</returns>
    public override string ToString() => Identifier.ToString();

    /// <summary>
    /// Determines whether two <see cref="OpenNettyTransaction"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyTransaction left, OpenNettyTransaction right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyTransaction"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyTransaction left, OpenNettyTransaction right) => !(left == right);
}
