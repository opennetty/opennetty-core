using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using static OpenNetty.OpenNettyConstants;

namespace OpenNetty;

/// <summary>
/// Represents the command associated with an OpenNetty message.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public readonly struct OpenNettyCommand : IEquatable<OpenNettyCommand>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyCommand"/> structure.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <param name="value">The value.</param>
    public OpenNettyCommand(OpenNettyCategory category, string value)
        : this(category, value, [])
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyCommand"/> structure.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <param name="value">The value.</param>
    /// <param name="parameters">The additional parameters, if applicable.</param>
    public OpenNettyCommand(OpenNettyCategory category, string value, ImmutableArray<string> parameters)
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

        // Ensure the parameters only include ASCII digits.
        if (!Parameters.IsDefaultOrEmpty)
        {
            for (var index = 0; index < parameters.Length; index++)
            {
                foreach (var character in parameters[index])
                {
                    if (!char.IsAsciiDigit(character))
                    {
                        throw new ArgumentException(SR.GetResourceString(SR.ID0004), nameof(value));
                    }
                }
            }
        }

        Category = category;
        Value = value;
        Parameters = parameters;
    }

    /// <summary>
    /// Gets the category associated with the command.
    /// </summary>
    public OpenNettyCategory Category { get; }

    /// <summary>
    /// Gets the value associated with the command.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Gets the additional parameters associated with the command, if applicable.
    /// </summary>
    public ImmutableArray<string> Parameters { get; }

    /// <inheritdoc/>
    public bool Equals(OpenNettyCommand other)
    {
        if (Value is null)
        {
            return other.Value is null;
        }

        if (Category != other.Category)
        {
            return false;
        }

        if (!string.Equals(Value, other.Value, StringComparison.Ordinal))
        {
            return false;
        }

        if (!Parameters.IsDefaultOrEmpty && !other.Parameters.IsDefaultOrEmpty)
        {
            if (Parameters.Length != other.Parameters.Length)
            {
                return false;
            }

            for (var index = 0; index < Parameters.Length; index++)
            {
                if (!string.Equals(Parameters[index], other.Parameters[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        else if (Parameters.IsDefaultOrEmpty && !other.Parameters.IsDefaultOrEmpty)
        {
            return false;
        }

        else if (!Parameters.IsDefaultOrEmpty && other.Parameters.IsDefaultOrEmpty)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyCommand command && Equals(command);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (Value is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(Category);
        hash.Add(Value);

        if (!Parameters.IsDefaultOrEmpty)
        {
            hash.Add(Parameters.Length);

            for (var index = 0; index < Parameters.Length; index++)
            {
                hash.Add(Parameters[index]);
            }
        }

        else
        {
            hash.Add(0);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current command.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current command.</returns>
    public override string ToString()
    {
        if (Value is null)
        {
            return string.Empty;
        }

        if (Parameters.IsDefaultOrEmpty)
        {
            return Value;
        }

        var builder = new StringBuilder();
        builder.Append(Value);

        for (var index = 0; index < Parameters.Length; index++)
        {
            builder.Append((char) Separators.Hash[0]);
            builder.Append(Parameters[index]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts the command to a list of <see cref="OpenNettyParameter"/>.
    /// </summary>
    /// <returns>The list of <see cref="OpenNettyParameter"/> representing this command.</returns>
    public ImmutableArray<OpenNettyParameter> ToParameters()
    {
        if (Value is null)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<OpenNettyParameter>();
        builder.Add(new OpenNettyParameter(Value));

        if (!Parameters.IsDefaultOrEmpty)
        {
            for (var index = 0; index < Parameters.Length; index++)
            {
                builder.Add(new OpenNettyParameter(Parameters[index]));
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Creates a copy of the current instance with the specified parameters attached.
    /// </summary>
    /// <param name="parameters">The parameters.</param>
    /// <returns>A copy of the current instance with the specified parameters attached</returns>
    public OpenNettyCommand WithParameters(params ImmutableArray<string> parameters) => new(Category, Value, parameters);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyCommand"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyCommand left, OpenNettyCommand right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyCommand"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyCommand left, OpenNettyCommand right) => !(left == right);
}
