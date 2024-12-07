using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using static OpenNetty.OpenNettyConstants;

namespace OpenNetty;

/// <summary>
/// Represents a raw OpenWebNet frame.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct OpenNettyFrame : IEquatable<OpenNettyFrame>
{
    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyFrame"/>.
    /// </summary>
    /// <param name="fields">The fields included in the frame.</param>
    public OpenNettyFrame(params IEnumerable<OpenNettyField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        Fields = [.. fields];
    }

    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyFrame"/>.
    /// </summary>
    /// <param name="fields">The fields included in the frame.</param>
    public OpenNettyFrame(params ImmutableArray<OpenNettyField> fields) => Fields = fields;

    /// <summary>
    /// Gets the raw fields included in the current frame.
    /// </summary>
    public ImmutableArray<OpenNettyField> Fields { get; }

    /// <summary>
    /// Parses an OpenNetty frame from the specified <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The raw frame.</param>
    /// <returns>The OpenNetty frame corresponding to the specified <paramref name="value"/>.</returns>
    public static OpenNettyFrame Parse(string value) => Parse(Encoding.ASCII.GetBytes(value));

    /// <summary>
    /// Parses an OpenNetty frame from the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The ASCII/UTF-8 buffer containing the raw frame.</param>
    /// <returns>The OpenNetty frame corresponding to the specified <paramref name="buffer"/>.</returns>
    public static OpenNettyFrame Parse(ReadOnlyMemory<byte> buffer) => Parse(new ReadOnlySequence<byte>(buffer));

    /// <summary>
    /// Parses an OpenNetty frame from the specified <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The ASCII/UTF-8 buffer containing the raw frame.</param>
    /// <returns>The OpenNetty frame corresponding to the specified <paramref name="buffer"/>.</returns>
    public static OpenNettyFrame Parse(in ReadOnlySequence<byte> buffer)
    {
        var reader = new SequenceReader<byte>(buffer);

        // Frames MUST always start with '*'.
        if (!reader.IsNext(Delimiters.Start, advancePast: true))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0001), nameof(buffer));
        }

        List<OpenNettyField>? fields = null;

        do
        {
            // Try to read until the next '*' (that indicates the next field in the frame).
            if (reader.TryReadTo(out ReadOnlySequence<byte> field, Separators.Asterisk, advancePastDelimiter: true))
            {
                fields ??= new(capacity: 1);
                fields.Add(OpenNettyField.Parse(field));
            }

            // If no '*' can be found, this means there's no additional field: in this case,
            // keep reading until the end of the message, indicated by two '#' characters.
            else if (reader.TryReadTo(out field, Delimiters.End, advancePastDelimiter: true))
            {
                // At this point, we should have reached the end of the message. If this is not the case,
                // throw an exception as two consecutive '#' must not appear before the end of the frame.
                if (!reader.End)
                {
                    throw new ArgumentException(SR.GetResourceString(SR.ID0002), nameof(buffer));
                }

                fields ??= new(capacity: 1);
                fields.Add(OpenNettyField.Parse(field));
            }

            // Frames MUST always end with '##'.
            else
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0003), nameof(buffer));
            }
        }

        while (!reader.End);

        return new OpenNettyFrame(fields);
    }

    /// <inheritdoc/>
    public bool Equals(OpenNettyFrame other)
    {
        if (Fields.IsDefaultOrEmpty)
        {
            return other.Fields.IsDefaultOrEmpty;
        }

        if (Fields.Length != other.Fields.Length)
        {
            return false;
        }

        for (var index = 0; index < Fields.Length; index++)
        {
            if (Fields[index] != other.Fields[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyFrame frame && Equals(frame);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (Fields.IsDefaultOrEmpty)
        {
            return 0;
        }

        var hash = new HashCode();

        for (var index = 0; index < Fields.Length; index++)
        {
            hash.Add(Fields[index]);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current frame.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current frame.</returns>
    public override string ToString()
    {
        if (Fields.IsDefaultOrEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append((char) Delimiters.Start[0]);

        for (var index = 0; index < Fields.Length; index++)
        {
            if (index is not 0)
            {
                builder.Append((char) Separators.Asterisk[0]);
            }

            builder.Append(Fields[index]);
        }

        builder.Append((char) Delimiters.End[0]);
        builder.Append((char) Delimiters.End[1]);

        return builder.ToString();
    }

    /// <summary>
    /// Determines whether two <see cref="OpenNettyFrame"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyFrame left, OpenNettyFrame right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyFrame"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyFrame left, OpenNettyFrame right) => !(left == right);
}
