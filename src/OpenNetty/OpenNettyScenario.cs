/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Diagnostics.CodeAnalysis;

namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty scenario (Nitoo only).
/// </summary>
public sealed class OpenNettyScenario : IEquatable<OpenNettyScenario>
{
    /// <summary>
    /// Gets or sets the endpoint name associated with the scenario.
    /// </summary>
    public required string EndpointName { get; init; }

    /// <summary>
    /// Gets or sets the function code associated with the scenario.
    /// </summary>
    public required byte FunctionCode { get; init; }

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] OpenNettyScenario? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return other is not null &&
            string.Equals(EndpointName, other.EndpointName, StringComparison.OrdinalIgnoreCase) &&
            FunctionCode == other.FunctionCode;
    }

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is OpenNettyScenario scenario && Equals(scenario);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EndpointName, StringComparer.Ordinal);
        hash.Add(FunctionCode);

        return hash.ToHashCode();
    }

    /// <summary>
    /// Determines whether two <see cref="OpenNettyScenario"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyScenario? left, OpenNettyScenario? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyScenario"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyScenario? left, OpenNettyScenario? right) => !(left == right);
}
