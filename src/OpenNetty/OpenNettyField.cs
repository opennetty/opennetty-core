/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using static OpenNetty.OpenNettyConstants;

namespace OpenNetty;

/// <summary>
/// Represents a raw OpenNetty field.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct OpenNettyField : IEquatable<OpenNettyField>
{
    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyField"/>.
    /// </summary>
    /// <param name="parameters">The parameters included in the field.</param>
    public OpenNettyField(params IEnumerable<OpenNettyParameter> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        Parameters = [.. parameters];
    }

    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyField"/>.
    /// </summary>
    /// <param name="parameters">The parameters included in the field.</param>
    public OpenNettyField(params ImmutableArray<OpenNettyParameter> parameters) => Parameters = parameters;

    /// <summary>
    /// Gets the raw parameters included in the field.
    /// </summary>
    public ImmutableArray<OpenNettyParameter> Parameters { get; }

    /// <summary>
    /// Represents an empty field.
    /// </summary>
    public static readonly OpenNettyField Empty = new([]);

    /// <summary>
    /// Parses an OpenNetty field from the specified <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The UTF-16 string containing the raw field.</param>
    /// <returns>The OpenNetty field corresponding to the specified <paramref name="value"/>.</returns>
    public static OpenNettyField Parse(string value) => Parse(Encoding.ASCII.GetBytes(value));

    /// <summary>
    /// Parses an OpenNetty field from the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The ASCII/UTF-8 buffer containing the raw field.</param>
    /// <returns>The OpenNetty field corresponding to the specified <paramref name="buffer"/>.</returns>
    public static OpenNettyField Parse(ReadOnlyMemory<byte> buffer) => Parse(new ReadOnlySequence<byte>(buffer));

    /// <summary>
    /// Parses an OpenNetty field from the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The ASCII/UTF-8 buffer containing the raw field.</param>
    /// <returns>The OpenNetty field corresponding to the specified <paramref name="buffer"/>.</returns>
    public static OpenNettyField Parse(in ReadOnlySequence<byte> buffer)
    {
        // Note: fields can be omitted. In this case, they are represented as empty values.

        var reader = new SequenceReader<byte>(buffer);
        List<OpenNettyParameter>? parameters = null;

        do
        {
            // Try to read until the next '#' (that indicates the next parameter in the frame).
            if (reader.TryReadTo(out ReadOnlySpan<byte> parameter, Separators.Hash, advancePastDelimiter: true))
            {
                // Ensure the next character is not a second '#', which is not valid in a parameter.
                if (reader.IsNext(Separators.Hash, advancePast: false))
                {
                    throw new ArgumentException(SR.GetResourceString(SR.ID0005), nameof(buffer));
                }

                parameters ??= new(capacity: 1);
                parameters.Add(OpenNettyParameter.Parse(parameter));

                // If '#' is not followed by any character, add an empty parameter.
                if (reader.End)
                {
                    parameters.Add(OpenNettyParameter.Empty);
                }
            }

            // Try to read until the next '*' (that indicates the next field in the frame).
            else if (reader.TryReadTo(out parameter, Separators.Asterisk, advancePastDelimiter: true))
            {
                parameters ??= new(capacity: 1);
                parameters.Add(OpenNettyParameter.Parse(parameter));
            }

            // If no '#' or '*' can be found, this means there's no additional parameter:
            // in this case, return the rest of the frame as a unique parameter.
            else
            {
                parameters ??= new(capacity: 1);
                parameters.Add(OpenNettyParameter.Parse(reader.UnreadSpan));
                reader.AdvanceToEnd();
            }
        }

        while (!reader.End);

        return new OpenNettyField(parameters);
    }

    /// <inheritdoc/>
    public bool Equals(OpenNettyField other)
    {
        if (Parameters.IsDefaultOrEmpty)
        {
            return other.Parameters.IsDefaultOrEmpty;
        }

        if (Parameters.Length != other.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < Parameters.Length; index++)
        {
            if (Parameters[index] != other.Parameters[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyField field && Equals(field);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (Parameters.IsDefaultOrEmpty)
        {
            return 0;
        }

        var hash = new HashCode();

        for (var index = 0; index < Parameters.Length; index++)
        {
            hash.Add(Parameters[index]);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current field.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current field.</returns>
    public override string ToString()
    {
        if (Parameters.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        for (var index = 0; index < Parameters.Length; index++)
        {
            if (index is not 0)
            {
                builder.Append((char) Separators.Hash[0]);
            }

            builder.Append(Parameters[index]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether two <see cref="OpenNettyField"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyField left, OpenNettyField right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyField"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyField left, OpenNettyField right) => !(left == right);
}