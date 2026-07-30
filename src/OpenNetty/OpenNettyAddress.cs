/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using static OpenNetty.OpenNettyConstants;

namespace OpenNetty;

/// <summary>
/// Represents the address of an OpenNetty message.
/// </summary>
[DebuggerDisplay("{ToString(),nq} ({Type,nq})")]
public readonly struct OpenNettyAddress : IEquatable<OpenNettyAddress>
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyAddress"/> structure.
    /// </summary>
    /// <param name="type">The address type.</param>
    /// <param name="value">The value.</param>
    public OpenNettyAddress(OpenNettyAddressType type, string value)
        : this(type, value, [])
    {
    }

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyAddress"/> structure.
    /// </summary>
    /// <param name="type">The address type.</param>
    /// <param name="value">The value.</param>
    /// <param name="parameters">The additional parameters, if applicable.</param>
    public OpenNettyAddress(OpenNettyAddressType type, string value, ImmutableArray<string> parameters)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!Enum.IsDefined(type))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0039));
        }

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

        Parameters = parameters;
        Type = type;
        Value = value;
    }

    /// <summary>
    /// Gets the additional parameters associated with the address, if applicable.
    /// </summary>
    public ImmutableArray<string> Parameters { get; } = [];

    /// <summary>
    /// Gets the type associated with the address.
    /// </summary>
    public OpenNettyAddressType Type { get; }

    /// <summary>
    /// Gets the value associated with the address.
    /// </summary>
    public string Value { get; }

    /// <inheritdoc/>
    public bool Equals(OpenNettyAddress other)
    {
        if (Type != other.Type)
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
    public override bool Equals(object? obj) => obj is OpenNettyAddress address && Equals(address);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (Value is null)
        {
            return 0;
        }

        var hash = new HashCode();
        hash.Add(Type);
        hash.Add(Value, StringComparer.Ordinal);

        if (!Parameters.IsDefaultOrEmpty)
        {
            hash.Add(Parameters.Length);

            for (var index = 0; index < Parameters.Length; index++)
            {
                hash.Add(Parameters[index], StringComparer.Ordinal);
            }
        }

        else
        {
            hash.Add(0);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current address.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current address.</returns>
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
    /// Converts the address to a list of <see cref="OpenNettyParameter"/>.
    /// </summary>
    /// <returns>The list of <see cref="OpenNettyParameter"/> representing this address.</returns>
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
    /// <returns>A copy of the current instance with the specified parameters attached.</returns>
    public OpenNettyAddress WithParameters(ImmutableArray<string> parameters) => new(Type, Value, parameters);

    /// <summary>
    /// Creates a copy of the current instance without any parameter attached.
    /// </summary>
    /// <returns>A copy of the current instance without any parameter attached.</returns>
    public OpenNettyAddress WithoutParameters() => new(Type, Value, []);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyAddress"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyAddress left, OpenNettyAddress right) => left.Equals(right);

    /// <summary>
    /// Determines whether two <see cref="OpenNettyAddress"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyAddress left, OpenNettyAddress right) => !(left == right);

    /// <summary>
    /// Creates a Zigbee address based on the specified decimal device identifier and unit.
    /// </summary>
    /// <param name="identifier">The decimal device identifier or <see langword="null"/> to represent a general address.</param>
    /// <param name="unit">The unit, or 0 to represent a device address that doesn't point to a specific unit.</param>
    /// <returns>A Zigbee address based on the specified device identifier and unit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The identifier or unit is not valid.</exception>
    public static OpenNettyAddress FromDecimalZigbeeAddress(uint identifier, byte unit)
    {
        // Note: Zigbee identifiers are 4-byte long and fit exactly in an
        // unsigned 32-bit integer, so a range check is not required.

        if (unit is > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), SR.GetResourceString(SR.ID0046));
        }

        if (identifier is 0)
        {
            return new OpenNettyAddress(OpenNettyAddressType.Zigbee, unit is 0 ? "00" : unit.ToString("00", CultureInfo.InvariantCulture));
        }

        return new OpenNettyAddress(OpenNettyAddressType.Zigbee, unit is 0 ?
            identifier.ToString(CultureInfo.InvariantCulture) + "00" :
            identifier.ToString(CultureInfo.InvariantCulture) + unit.ToString("00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates a Zigbee address based on the specified hexadecimal device identifier and unit.
    /// </summary>
    /// <param name="identifier">The hexadecimal device identifier or <see langword="null"/> to represent a general address.</param>
    /// <param name="unit">The unit, or 0 to represent a device address that doesn't point to a specific unit.</param>
    /// <returns>A Zigbee address based on the specified device identifier and unit.</returns>
    /// <exception cref="ArgumentException">The identifier is not a valid hexadecimal string.</exception>
    public static OpenNettyAddress FromHexadecimalZigbeeAddress(string? identifier, byte unit)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return FromDecimalZigbeeAddress(0, unit);
        }

        if (!uint.TryParse(identifier, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint result))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0047), nameof(identifier));
        }

        return FromDecimalZigbeeAddress(result, unit);
    }

    /// <summary>
    /// Creates a Nitoo address based on the specified device identifier and unit.
    /// </summary>
    /// <param name="identifier">The device identifier.</param>
    /// <param name="unit">The unit, or 0 to represent a device address that doesn't point to a specific unit.</param>
    /// <returns>A Nitoo address based on the specified device identifier and unit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The identifier or unit is not valid.</exception>
    public static OpenNettyAddress FromNitooAddress(uint identifier, byte unit)
    {
        if (identifier > Math.Pow(2, 20))
        {
            throw new ArgumentOutOfRangeException(nameof(identifier), SR.GetResourceString(SR.ID0040));
        }

        if (unit is > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), SR.GetResourceString(SR.ID0041));
        }

        return new OpenNettyAddress(OpenNettyAddressType.Nitoo, ((identifier * 16) + unit).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates a Nitoo address based on the specified device identifier and unit.
    /// </summary>
    /// <param name="identifier">The device identifier.</param>
    /// <param name="unit">The unit, or 0 to represent a device address that doesn't point to a specific unit.</param>
    /// <returns>A Nitoo address based on the specified device identifier and unit.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The identifier or unit is not valid.</exception>
    public static OpenNettyAddress FromNitooAddress(OpenNettyDeviceIdentifier identifier, byte unit)
        => FromNitooAddress(OpenNettyDeviceIdentifier.ToNitooSerialNumber(identifier), unit);

    /// <summary>
    /// Creates a SCS dry contact address based on the specified parameters.
    /// </summary>
    /// <param name="identifier">The scenario identifier.</param>
    /// <returns>A SCS dry contact address based on the specified parameters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">One of the parameters is not valid.</exception>
    public static OpenNettyAddress FromScsDryContactAddress(byte identifier)
    {
        if (identifier is > 201)
        {
            throw new ArgumentOutOfRangeException(nameof(identifier), SR.GetResourceString(SR.ID0132));
        }

        return new OpenNettyAddress(OpenNettyAddressType.ScsDryContact, $"3{identifier.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Creates a SCS light point address based on the specified parameters.
    /// </summary>
    /// <param name="extension">The bus extension (also known as interface), or 0 to represent the private riser.</param>
    /// <param name="general">A boolean indicating whether the address will be a general address or not.</param>
    /// <param name="group">The group, or <see langword="null"/> if the address is an area, general or point-to-point address.</param>
    /// <param name="area">The area, or <see langword="null"/> if the address is a general or group address.</param>
    /// <param name="point">The point of light, or <see langword="null"/> if the address is an area, general or group address.</param>
    /// <returns>A SCS light point address based on the specified parameters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">One of the parameters is not valid.</exception>
    public static OpenNettyAddress FromScsLightPointAddress(byte extension, bool general, byte? group, byte? area, byte? point)
    {
        if (area is > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(area), SR.GetResourceString(SR.ID0042));
        }

        if (group is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(group), SR.GetResourceString(SR.ID0044));
        }

        if (extension is > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(extension), SR.GetResourceString(SR.ID0043));
        }

        // SCS light point general address:
        if (general)
        {
            if (group is not null)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0052), nameof(group));
            }

            if (area is not null)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0053), nameof(area));
            }

            if (point is not null)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0054), nameof(point));
            }

            return extension is not 0
                ? new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "0", ["4", extension.ToString("00", CultureInfo.InvariantCulture)])
                : new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "0");
        }

        if (group is not null)
        {
            if (area is not null)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0053), nameof(area));
            }

            if (point is not null)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0054), nameof(point));
            }

            return extension is not 0
                ? new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, string.Empty, [group.Value.ToString(), "4", extension.ToString("00", CultureInfo.InvariantCulture)])
                : new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, string.Empty, [group.Value.ToString()]);
        }

        if (area is not null && point is null)
        {
            var builder = new StringBuilder();

            if (area is 0)
            {
                builder.Append("00");
            }

            else if (area is 10)
            {
                builder.Append("100");
            }

            else
            {
                builder.Append(area);
            }

            return extension is not 0
                ? new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, builder.ToString(), ["4", extension.ToString("00", CultureInfo.InvariantCulture)])
                : new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, builder.ToString());
        }

        // SCS light point point-to-point address:
        if (area is not null && point is not null)
        {
            var builder = new StringBuilder();

            if (area is 0)
            {
                builder.Append("00");
            }

            else if (point is >= 10)
            {
                builder.Append(area.Value.ToString("00", CultureInfo.InvariantCulture));
            }

            else
            {
                builder.Append(area);
            }

            if (area is 0 or 10)
            {
                builder.Append(point.Value.ToString("00", CultureInfo.InvariantCulture));
            }

            else
            {
                builder.Append(point);
            }

            return extension is not 0
                ? new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, builder.ToString(), ["4", extension.ToString("00", CultureInfo.InvariantCulture)])
                : new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, builder.ToString());
        }

        throw new InvalidOperationException(SR.GetResourceString(SR.ID0051));
    }

    /// <summary>
    /// Creates a SCS scenario plus address based on the specified parameters.
    /// </summary>
    /// <param name="identifier">The scenario identifier.</param>
    /// <returns>A SCS scenario plus address based on the specified parameters.</returns>
    /// <exception cref="ArgumentOutOfRangeException">One of the parameters is not valid.</exception>
    public static OpenNettyAddress FromScsScenarioPlusAddress(ushort identifier)
    {
        if (identifier is > 2047)
        {
            throw new ArgumentOutOfRangeException(nameof(identifier), SR.GetResourceString(SR.ID0116));
        }

        return new OpenNettyAddress(OpenNettyAddressType.ScsScenarioPlus, $"2{identifier.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Creates a Zigbee address based on the specified device identifier and unit.
    /// </summary>
    /// <param name="identifier">The device identifier, or <see langword="null"/> to represent a non-device-specific address.</param>
    /// <param name="unit">The unit, or 0 to represent a device address that doesn't point to a specific unit.</param>
    /// <returns>A Zigbee address based on the specified device identifier and unit.</returns>
    /// <exception cref="ArgumentException">The identifier is not a valid hexadecimal string.</exception>
    public static OpenNettyAddress FromZigbeeAddress(OpenNettyDeviceIdentifier? identifier, byte unit)
        => FromHexadecimalZigbeeAddress(identifier is not null ?
            OpenNettyDeviceIdentifier.ToZigbeeSerialNumber(identifier.Value) : null, unit);

    /// <summary>
    /// Determines whether the specified address is a SCS light point area address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address is a SCS light point area address, <see langword="false"/> otherwise.</returns>
    public static bool IsScsLightPointAreaAddress(OpenNettyAddress address)
        => address.Type is OpenNettyAddressType.ScsLightPoint && ToScsLightPointAddress(address)
            is { General: false, Group: null, Area: not null, Point: null };

    /// <summary>
    /// Determines whether the specified address is a SCS light point general address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address is a SCS light point general address, <see langword="false"/> otherwise.</returns>
    public static bool IsScsLightPointGeneralAddress(OpenNettyAddress address)
        => address.Type is OpenNettyAddressType.ScsLightPoint && ToScsLightPointAddress(address)
            is { General: true, Group: null, Area: null, Point: null };

    /// <summary>
    /// Determines whether the specified address is a SCS light point group address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address is a SCS light point group address, <see langword="false"/> otherwise.</returns>
    public static bool IsScsLightPointGroupAddress(OpenNettyAddress address)
        => address.Type is OpenNettyAddressType.ScsLightPoint && ToScsLightPointAddress(address)
            is { General: false, Group: not null, Area: null, Point: null };

    /// <summary>
    /// Determines whether the specified address is a SCS light point point-to-point address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address is a SCS light point point-to-point address, <see langword="false"/> otherwise.</returns>
    public static bool IsScsLightPointPointToPointAddress(OpenNettyAddress address)
        => address.Type is OpenNettyAddressType.ScsLightPoint && ToScsLightPointAddress(address)
            is { General: false, Group: null, Area: not null, Point: not null };

    /// <summary>
    /// Converts the specified address to a Nitoo address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A Nitoo address based on the specified address.</returns>
    /// <exception cref="InvalidOperationException">The address doesn't represent a valid Nitoo address.</exception>
    public static (uint Identifier, byte Unit) ToNitooAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.Nitoo)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0048), nameof(address));
        }

        if (!uint.TryParse(address.Value, CultureInfo.InvariantCulture, out uint value) || value > Math.Pow(2, 24))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0049), nameof(address));
        }

        return (Identifier: value / 16, Unit: (byte) (value % 16));
    }

    /// <summary>
    /// Converts the specified address to a SCS dry contact address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A SCS dry contact address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid SCS dry contact address.</exception>
    public static ushort ToScsDryContactAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.ScsDryContact)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0133), nameof(address));
        }

        if (!address.Value.StartsWith('3') ||
            !ushort.TryParse(address.Value.AsSpan()[1..], CultureInfo.InvariantCulture, out ushort identifier) ||
            identifier is > 201)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0134), nameof(address));
        }

        return identifier;
    }

    /// <summary>
    /// Converts the specified address to a SCS light point address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A SCS light point address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid SCS light point address.</exception>
    public static (byte Extension, bool General, byte? Group, byte? Area, byte? Point) ToScsLightPointAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.ScsLightPoint)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0050), nameof(address));
        }

        if (string.IsNullOrEmpty(address.Value))
        {
            return address.Parameters switch
            {
                // Group address without bus extension:
                [string value] when byte.TryParse(value, CultureInfo.InvariantCulture, out byte group) && group is >= 1 and <= 255
                    => (Extension: 0, General: false, Group: group, Area: null, Point: null),

                // Group address with bus extension:
                [string first, "4", string third] when
                    byte.TryParse(first, CultureInfo.InvariantCulture, out byte group)     && group     is >= 1 and <= 255 &&
                    byte.TryParse(third, CultureInfo.InvariantCulture, out byte extension) && extension is >= 0 and <= 15
                    => (Extension: extension, General: false, Group: group, Area: null, Point: null),

                _ => throw new ArgumentException(SR.GetResourceString(SR.ID0051), nameof(address)),
            };
        }

        else if (address.Value is "0")
        {
            return address.Parameters switch
            {
                // General address without bus extension:
                { IsDefaultOrEmpty: true } => (Extension: 0, General: true, Group: null, Area: null, Point: null),

                // General address with bus extension:
                ["4", string value] when byte.TryParse(value, CultureInfo.InvariantCulture, out byte extension) && extension is >= 0 and <= 15
                    => (Extension: extension, General: true, Group: null, Area: null, Point: null),

                _ => throw new ArgumentException(SR.GetResourceString(SR.ID0051), nameof(address))
            };
        }

        else if (address.Value is "00" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or "10" or "100")
        {
            return address.Parameters switch
            {
                // Extended A=10 area without bus extension:
                { IsDefaultOrEmpty: true } when address.Value is "100"
                    => (Extension: 0, General: false, Group: null, Area: 10, Point: null),

                // Extended A=10 area with bus extension:
                ["4", string value] when address.Value is "100" &&
                    byte.TryParse(value, CultureInfo.InvariantCulture, out byte extension) && extension is >= 0 and <= 15
                    => (Extension: extension, General: false, Group: null, Area: 10, Point: null),

                // Area address without bus extension:
                { IsDefaultOrEmpty: true } when byte.TryParse(address.Value, CultureInfo.InvariantCulture, out byte area) && area is >= 0 and <= 9
                    => (Extension: 0, General: false, Group: null, Area: area, Point: null),

                // Area address with bus extension:
                ["4", string value] when
                    byte.TryParse(address.Value, CultureInfo.InvariantCulture, out byte area)      && area      is >= 0 and <= 9 &&
                    byte.TryParse(value,         CultureInfo.InvariantCulture, out byte extension) && extension is >= 0 and <= 15
                    => (Extension: extension, General: false, Group: null, Area: area, Point: null),

                _ => throw new ArgumentException(SR.GetResourceString(SR.ID0051), nameof(address))
            };
        }

        return address.Parameters switch
        {
            // Point-to-point address without bus extension:
            { IsDefaultOrEmpty: true } when GetAreaAndLightPoint(address.Value) is { Area: byte area, Point: byte point }
                => (Extension: 0, General: false, Group: null, Area: area, Point: point),

            // Point-to-point address with bus extension:
            ["4", string value] when
                GetAreaAndLightPoint(address.Value) is { Area: byte area, Point: byte point } &&
                byte.TryParse(value, CultureInfo.InvariantCulture, out byte extension) && extension is >= 0 and <= 15
                => (Extension: extension, General: false, Group: null, Area: area, Point: point),

            _ => throw new ArgumentException(SR.GetResourceString(SR.ID0051), nameof(address))
        };

        static (byte Area, byte Point) GetAreaAndLightPoint(ReadOnlySpan<char> address) => address switch
        {
            // A = 00; PL [01 − 15]:
            ['0', '0', '0' or '1', >= '0' and <= '9'] when
                byte.TryParse(address[2..4], CultureInfo.InvariantCulture, out byte point) && point is >= 1 and <= 15
                => (0, point),

            // A [1 − 9]; PL [1 − 9]:
            [>= '1' and <= '9', >= '1' and <= '9'] when
                byte.TryParse(address[0..1], CultureInfo.InvariantCulture, out byte area) &&
                byte.TryParse(address[1..2], CultureInfo.InvariantCulture, out byte point)
                => (area, point),

            // A = 10; PL [01 − 15]:
            ['1', '0', '0' or '1', >= '0' and <= '9'] when
                byte.TryParse(address[2..4], CultureInfo.InvariantCulture, out byte point) && point is >= 1 and <= 15
                => (10, point),

            // A [01 − 09]; PL [10 − 15]:
            ['0', >= '1' and <= '9', '1', >= '0' and <= '5'] when
                byte.TryParse(address[0..2], CultureInfo.InvariantCulture, out byte area)  &&
                byte.TryParse(address[2..4], CultureInfo.InvariantCulture, out byte point) && point is >= 1 and <= 15
                => (area, point),

            _ => throw new ArgumentException(SR.GetResourceString(SR.ID0051), nameof(address))
        };
    }

    /// <summary>
    /// Converts the specified address to a SCS scenario plus address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A SCS scenario plus address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid SCS scenario plus address.</exception>
    public static ushort ToScsScenarioPlusAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.ScsScenarioPlus)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0114), nameof(address));
        }

        if (!address.Value.StartsWith('2') ||
            !ushort.TryParse(address.Value.AsSpan()[1..], CultureInfo.InvariantCulture, out ushort identifier) ||
            identifier is > 2047)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0115), nameof(address));
        }

        return identifier;
    }

    /// <summary>
    /// Converts the specified address to a Zigbee address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A Zigbee address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid Zigbee address.</exception>
    public static (uint Identifier, byte Unit) ToZigbeeAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.Zigbee)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0055), nameof(address));
        }

        if (address.Value is { Length: 2 })
        {
            if (!byte.TryParse(address.Value, CultureInfo.InvariantCulture, out byte unit))
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0056), nameof(address));
            }

            return (Identifier: 0, unit);
        }

        else if (address.Value is { Length: > 2 })
        {
            // Note: Zigbee identifiers are 4-byte long and fit exactly in an
            // unsigned 32-bit integer, so a range check is not required.

            if (!uint.TryParse(address.Value.AsSpan()[0..^2], CultureInfo.InvariantCulture, out uint identifier) ||
                !byte.TryParse(address.Value.AsSpan()[^2..],  CultureInfo.InvariantCulture, out byte unit) || unit is > 99)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0056), nameof(address));
            }

            return (identifier, unit);
        }

        throw new ArgumentException(SR.GetResourceString(SR.ID0056), nameof(address));
    }
}
