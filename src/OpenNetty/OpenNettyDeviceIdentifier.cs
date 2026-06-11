/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Diagnostics;
using System.Globalization;

namespace OpenNetty;

/// <summary>
/// Represents the identifier assigned to an OpenNetty device.
/// </summary>
[DebuggerDisplay("{ToString(),nq} ({Type,nq})")]
public readonly struct OpenNettyDeviceIdentifier : IEquatable<OpenNettyDeviceIdentifier>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyDeviceIdentifier"/> structure.
    /// </summary>
    /// <param name="type">The identifier type.</param>
    /// <param name="value">The value.</param>
    public OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType type, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Enum.IsDefined(type))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0039));
        }

        Type = type;
        Value = value;
    }

    /// <summary>
    /// Gets the type associated with the identifier.
    /// </summary>
    public OpenNettyDeviceIdentifierType Type { get; }

    /// <summary>
    /// Gets the value associated with the identifier.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc/>
    public bool Equals(OpenNettyDeviceIdentifier other) => other.Type == Type && other.Value == Value;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyDeviceIdentifier identifier && Equals(identifier);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Type, Value);

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current identifier.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current identifier.</returns>
    public override string ToString() => Value;

    /// <summary>
    /// Determines whether two <see cref="OpenNettyDeviceIdentifier"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyDeviceIdentifier left, OpenNettyDeviceIdentifier right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyDeviceIdentifier"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyDeviceIdentifier left, OpenNettyDeviceIdentifier right) => !(left == right);

    /// <summary>
    /// Creates a new <see cref="OpenNettyDeviceIdentifier"/> instance representing a MAC address.
    /// </summary>
    /// <param name="address">The MAC address.</param>
    /// <returns>A new <see cref="OpenNettyDeviceIdentifier"/> instance representing a MAC address.</returns>
    public static OpenNettyDeviceIdentifier FromMacAddress(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);

        address = address.Trim();

        if (address.Contains(':') && address.Contains('-'))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0109), nameof(address));
        }

        if (address.Contains(':') || address.Contains('-'))
        {
            var parts = address.Split(address.Contains(':') ? ':' : '-');

            address = (parts.Length is 6 or 8 && parts.All(part => part.Length is 2 && part.All(char.IsAsciiHexDigit))) ?
                string.Join(":", parts.Select(part => part.ToUpperInvariant())) :
                throw new ArgumentException(SR.GetResourceString(SR.ID0109), nameof(address));
        }

        else
        {
            address = (address.Length is 12 or 16 && address.All(char.IsAsciiHexDigit)) ?
                string.Join(":", Enumerable.Range(0, address.Length / 2)
                    .Select(index => address.Substring(index * 2, 2).ToUpperInvariant())) :
                throw new ArgumentException(SR.GetResourceString(SR.ID0109), nameof(address));
        }

        return new OpenNettyDeviceIdentifier(OpenNettyDeviceIdentifierType.MacAddress, address);
    }

    /// <summary>
    /// Creates a new <see cref="OpenNettyDeviceIdentifier"/> instance representing a Nitoo serial number.
    /// </summary>
    /// <param name="identifier">The serial number.</param>
    /// <returns>A new <see cref="OpenNettyDeviceIdentifier"/> instance representing a Nitoo serial number.</returns>
    public static OpenNettyDeviceIdentifier FromNitooSerialNumber(uint identifier)
    {
        if (identifier > Math.Pow(2, 20))
        {
            throw new ArgumentOutOfRangeException(nameof(identifier), SR.GetResourceString(SR.ID0040));
        }

        return new(OpenNettyDeviceIdentifierType.NitooSerialNumber, identifier.ToString("D6", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates a new <see cref="OpenNettyDeviceIdentifier"/> instance representing a SCS serial number.
    /// </summary>
    /// <param name="identifier">The serial number.</param>
    /// <returns>A new <see cref="OpenNettyDeviceIdentifier"/> instance representing a SCS serial number.</returns>
    public static OpenNettyDeviceIdentifier FromScsSerialNumber(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        if (!uint.TryParse(identifier, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0047), nameof(identifier));
        }

        return new(OpenNettyDeviceIdentifierType.ScsSerialNumber, value.ToString("X8", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates a new <see cref="OpenNettyDeviceIdentifier"/> instance representing a Zigbee serial number.
    /// </summary>
    /// <param name="identifier">The serial number.</param>
    /// <returns>A new <see cref="OpenNettyDeviceIdentifier"/> instance representing a Zigbee serial number.</returns>
    public static OpenNettyDeviceIdentifier FromZigbeeSerialNumber(uint identifier)
        => new(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber, identifier.ToString("X8", CultureInfo.InvariantCulture));

    /// <summary>
    /// Creates a new <see cref="OpenNettyDeviceIdentifier"/> instance representing a Zigbee serial number.
    /// </summary>
    /// <param name="identifier">The serial number.</param>
    /// <returns>A new <see cref="OpenNettyDeviceIdentifier"/> instance representing a Zigbee serial number.</returns>
    public static OpenNettyDeviceIdentifier FromZigbeeSerialNumber(string identifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        if (!uint.TryParse(identifier, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0047), nameof(identifier));
        }

        return new(OpenNettyDeviceIdentifierType.ZigbeeSerialNumber, value.ToString("X8", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Converts the specified <see cref="OpenNettyDeviceIdentifier"/> to a MAC address.
    /// </summary>
    /// <param name="identifier">The identifier to convert.</param>
    /// <returns>The MAC address corresponding to the identifier.</returns>
    public static string ToMacAddress(OpenNettyDeviceIdentifier identifier)
    {
        if (identifier.Type is OpenNettyDeviceIdentifierType.MacAddress)
        {
            return identifier.Value;
        }

        else if (identifier.Type is OpenNettyDeviceIdentifierType.ScsSerialNumber or
                                    OpenNettyDeviceIdentifierType.ZigbeeSerialNumber)
        {
            return $"00:04:74:00:{string.Join(":", Enumerable.Range(0, 4).Select(index => identifier.Value.Substring(index * 2, 2)))}";
        }

        throw new ArgumentException(SR.GetResourceString(SR.ID0110), nameof(identifier));
    }

    /// <summary>
    /// Converts the specified <see cref="OpenNettyDeviceIdentifier"/> to a Nitoo serial number.
    /// </summary>
    /// <param name="identifier">The identifier to convert.</param>
    /// <returns>The Nitoo serial number corresponding to the identifier.</returns>
    public static uint ToNitooSerialNumber(OpenNettyDeviceIdentifier identifier)
    {
        if (identifier.Type is not OpenNettyDeviceIdentifierType.NitooSerialNumber)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0111), nameof(identifier));
        }

        return uint.Parse(identifier.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Converts the specified <see cref="OpenNettyDeviceIdentifier"/> to a SCS serial number.
    /// </summary>
    /// <param name="identifier">The identifier to convert.</param>
    /// <returns>The SCS serial number corresponding to the identifier.</returns>
    public static string ToScsSerialNumber(OpenNettyDeviceIdentifier identifier)
    {
        if (identifier.Type is OpenNettyDeviceIdentifierType.ScsSerialNumber)
        {
            return identifier.Value;
        }

        else if (identifier.Type is OpenNettyDeviceIdentifierType.MacAddress)
        {
            if (!identifier.Value.StartsWith("00:04:74:00:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0112), nameof(identifier));
            }

            return identifier.Value["00:04:74:00:".Length..].Replace(":", "");
        }

        throw new ArgumentException(SR.GetResourceString(SR.ID0112), nameof(identifier));
    }

    /// <summary>
    /// Converts the specified <see cref="OpenNettyDeviceIdentifier"/> to a Zigbee serial number.
    /// </summary>
    /// <param name="identifier">The identifier to convert.</param>
    /// <returns>The Zigbee serial number corresponding to the identifier.</returns>
    public static string ToZigbeeSerialNumber(OpenNettyDeviceIdentifier identifier)
    {
        if (identifier.Type is OpenNettyDeviceIdentifierType.ZigbeeSerialNumber)
        {
            return identifier.Value;
        }

        else if (identifier.Type is OpenNettyDeviceIdentifierType.MacAddress)
        {
            if (!identifier.Value.StartsWith("00:04:74:00:", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0113), nameof(identifier));
            }

            return identifier.Value["00:04:74:00:".Length..].Replace(":", "");
        }

        throw new ArgumentException(SR.GetResourceString(SR.ID0113), nameof(identifier));
    }
}
