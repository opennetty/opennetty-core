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
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0043));
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
    public ImmutableArray<string> Parameters { get; }

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
    public static OpenNettyAddress FromDecimalZigbeeAddress(uint? identifier, ushort unit = 0)
    {
        // Note: Zigbee identifiers are 4-byte long and fit exactly in an
        // unsigned 32-bit integer, so a range check is not required.

        if (unit is > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), SR.GetResourceString(SR.ID0050));
        }

        if (identifier is null)
        {
            return unit is 0 ?
                new OpenNettyAddress(OpenNettyAddressType.ZigbeeAllDevicesAllUnits, "00") :
                new OpenNettyAddress(OpenNettyAddressType.ZigbeeAllDevicesSpecificUnit, unit.ToString("00", CultureInfo.InvariantCulture));
        }

        return unit is 0 ?
            new OpenNettyAddress(OpenNettyAddressType.ZigbeeSpecificDeviceAllUnits,
                identifier.Value.ToString(CultureInfo.InvariantCulture) + "00") :
            new OpenNettyAddress(OpenNettyAddressType.ZigbeeSpecificDeviceSpecificUnit,
                identifier.Value.ToString(CultureInfo.InvariantCulture) + unit.ToString("00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates a Zigbee address based on the specified hexadecimal device identifier and unit.
    /// </summary>
    /// <param name="identifier">The hexadecimal device identifier or <see langword="null"/> to represent a general address.</param>
    /// <param name="unit">The unit, or 0 to represent a device address that doesn't point to a specific unit.</param>
    /// <returns>A Zigbee address based on the specified device identifier and unit.</returns>
    /// <exception cref="ArgumentException">The identifier is not a valid hexadecimal string.</exception>
    public static OpenNettyAddress FromHexadecimalZigbeeAddress(string? identifier, ushort unit = 0)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return FromDecimalZigbeeAddress(null, unit);
        }

        if (!uint.TryParse(identifier, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint result))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0051), nameof(identifier));
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
    public static OpenNettyAddress FromNitooAddress(uint identifier, ushort unit = 0)
    {
        if (identifier > Math.Pow(2, 24))
        {
            throw new ArgumentOutOfRangeException(nameof(identifier), SR.GetResourceString(SR.ID0044));
        }

        if (unit is > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(unit), SR.GetResourceString(SR.ID0045));
        }

        return unit is 0 ?
            new OpenNettyAddress(OpenNettyAddressType.NitooDevice, (identifier * 16).ToString(CultureInfo.InvariantCulture)) :
            new OpenNettyAddress(OpenNettyAddressType.NitooUnit, ((identifier * 16) + unit).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Creates a SCS light point area address based on the specified area and bus extension.
    /// </summary>
    /// <param name="area">The area.</param>
    /// <param name="extension">The bus extension (also known as interface), or 0 to represent the private riser.</param>
    /// <returns>A SCS light point area address based on the specified area and bus extension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The area or bus extension is not valid.</exception>
    public static OpenNettyAddress FromScsLightPointAreaAddress(ushort area, ushort extension = 0)
    {
        if (area is > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(area), SR.GetResourceString(SR.ID0046));
        }

        if (extension is > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(extension), SR.GetResourceString(SR.ID0047));
        }

        var builder = new StringBuilder();

        if (area is 0)
        {
            builder.Append("00");
        }

        else
        {
            builder.Append(area);
        }

        return extension is not 0 ?
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointArea, builder.ToString(), ["4", extension.ToString("00", CultureInfo.InvariantCulture)]) :
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointArea, builder.ToString());
    }

    /// <summary>
    /// Creates a SCS light point general address based on the specified bus extension.
    /// </summary>
    /// <param name="extension">The bus extension (also known as interface), or 0 to represent the private riser.</param>
    /// <returns>A SCS light point general address based on the specified bus extension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bus extension is not valid.</exception>
    public static OpenNettyAddress FromScsLightPointGeneralAddress(ushort extension = 0)
    {
        if (extension is > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(extension), SR.GetResourceString(SR.ID0047));
        }

        return extension is not 0 ?
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointGeneral, "0", ["4", extension.ToString("00", CultureInfo.InvariantCulture)]) :
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointGeneral, "0");
    }

    /// <summary>
    /// Creates a SCS light point group address based on the specified group and bus extension.
    /// </summary>
    /// <param name="group">The group.</param>
    /// <param name="extension">The bus extension (also known as interface), or 0 to represent the private riser.</param>
    /// <returns>A SCS light point general address based on the specified group and bus extension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The group or bus extension is not valid.</exception>
    public static OpenNettyAddress FromScsLightPointGroupAddress(ushort group, ushort extension = 0)
    {
        if (group is < 1 or > 255)
        {
            throw new ArgumentOutOfRangeException(nameof(group), SR.GetResourceString(SR.ID0048));
        }

        if (extension is > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(extension), SR.GetResourceString(SR.ID0047));
        }

        return extension is not 0 ?
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointGroup, string.Empty, [group.ToString(), "4", extension.ToString("00", CultureInfo.InvariantCulture)]) :
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointGroup, string.Empty, [group.ToString()]);
    }

    /// <summary>
    /// Creates a SCS light point point-to-point address based on the specified area, light point and bus extension.
    /// </summary>
    /// <param name="area">The area.</param>
    /// <param name="point">The light point.</param>
    /// <param name="extension">The bus extension (also known as interface), or 0 to represent the private riser.</param>
    /// <returns>A SCS light point point-to-point address based on the specified area, light point and bus extension.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The area, light point or bus extension is not valid.</exception>
    public static OpenNettyAddress FromScsLightPointPointToPointAddress(ushort area, ushort point, ushort extension = 0)
    {
        if (area is > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(area), SR.GetResourceString(SR.ID0046));
        }

        if (point is < 1 or > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(point), SR.GetResourceString(SR.ID0049));
        }

        if (extension is > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(extension), SR.GetResourceString(SR.ID0047));
        }

        var builder = new StringBuilder();

        if (area is 0)
        {
            builder.Append("00");
        }

        else if (point is >= 10)
        {
            builder.Append(area.ToString("00", CultureInfo.InvariantCulture));
        }

        else
        {
            builder.Append(area);
        }

        if (area is 0 or 10)
        {
            builder.Append(point.ToString("00", CultureInfo.InvariantCulture));
        }

        else
        {
            builder.Append(point);
        }

        return extension is not 0 ?
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointPointToPoint, builder.ToString(), ["4", extension.ToString("00", CultureInfo.InvariantCulture)]) :
            new OpenNettyAddress(OpenNettyAddressType.ScsLightPointPointToPoint, builder.ToString());
    }

    /// <summary>
    /// Determines whether the specified address is a Nitoo address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address is a Nitoo address, <see langword="false"/> otherwise.</returns>
    public static bool IsNitooAddress(OpenNettyAddress address)
        => address.Type is OpenNettyAddressType.NitooDevice or OpenNettyAddressType.NitooUnit;

    /// <summary>
    /// Determines whether the specified address is a SCS address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address is a SCS address, <see langword="false"/> otherwise.</returns>
    public static bool IsScsAddress(OpenNettyAddress address)
        => address.Type is OpenNettyAddressType.ScsLightPointArea  or OpenNettyAddressType.ScsLightPointGeneral or
                           OpenNettyAddressType.ScsLightPointGroup or OpenNettyAddressType.ScsLightPointPointToPoint;

    /// <summary>
    /// Determines whether the specified address is a Zigbee address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> if the address is a Zigbee address, <see langword="false"/> otherwise.</returns>
    public static bool IsZigbeeAddress(OpenNettyAddress address)
        => address.Type is OpenNettyAddressType.ZigbeeAllDevicesAllUnits     or OpenNettyAddressType.ZigbeeAllDevicesSpecificUnit or
                           OpenNettyAddressType.ZigbeeSpecificDeviceAllUnits or OpenNettyAddressType.ZigbeeSpecificDeviceSpecificUnit;

    /// <summary>
    /// Converts the specified address to a Nitoo address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A Nitoo address based on the specified address.</returns>
    /// <exception cref="InvalidOperationException">The address doesn't represent a valid Nitoo address.</exception>
    public static (uint Identifier, ushort Unit) ToNitooAddress(OpenNettyAddress address)
    {
        if (!IsNitooAddress(address))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0052), nameof(address));
        }

        if (!uint.TryParse(address.Value, CultureInfo.InvariantCulture, out uint value) || value > Math.Pow(2, 24))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0053), nameof(address));
        }

        return (Identifier: value / 16, Unit: (ushort) (value % 16));
    }

    /// <summary>
    /// Converts the specified address to a SCS light point area address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A SCS light point area address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid SCS light point area address.</exception>
    public static (ushort? Extension, ushort? Area) ToScsLightPointAreaAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.ScsLightPointArea)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0054), nameof(address));
        }

        if (address.Value is not ("00" or "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or "10"))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0055), nameof(address));
        }

        return address.Parameters switch
        {
            { IsDefaultOrEmpty: true } when ushort.TryParse(address.Value, CultureInfo.InvariantCulture, out ushort area) && area is >= 0 and <= 10
                => (Extension: null, Area: area),

            ["4", string value] when
                ushort.TryParse(address.Value, CultureInfo.InvariantCulture, out ushort area) &&
                ushort.TryParse(value, CultureInfo.InvariantCulture, out ushort extension)    && extension is >= 0 and <= 15
                => (Extension: extension, Area: area),

            _ => throw new ArgumentException(SR.GetResourceString(SR.ID0055), nameof(address)),
        };
    }

    /// <summary>
    /// Converts the specified address to a SCS light point general address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The bus extension, if applicable.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid SCS light point general address.</exception>
    public static ushort? ToScsLightPointGeneralAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.ScsLightPointGeneral)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0056), nameof(address));
        }

        if (address.Value is not "0")
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0057), nameof(address));
        }

        return address.Parameters switch
        {
            { IsDefaultOrEmpty: true } => null,

            ["4", string value] when ushort.TryParse(value, CultureInfo.InvariantCulture, out ushort extension) && extension is >= 0 and <= 15
                => extension,

            _ => throw new ArgumentException(SR.GetResourceString(SR.ID0057), nameof(address))
        };
    }

    /// <summary>
    /// Converts the specified address to a SCS light point group address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A SCS light point group address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid SCS light point group address.</exception>
    public static (ushort? Extension, ushort? Group) ToScsLightPointGroupAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.ScsLightPointGroup)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0058), nameof(address));
        }

        if (!string.IsNullOrEmpty(address.Value))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0059), nameof(address));
        }

        return address.Parameters switch
        {
            [string value] when ushort.TryParse(value, CultureInfo.InvariantCulture, out ushort group) && group is >= 1 and <= 255
                => (Extension: null, Group: group),

            [string first, "4", string third] when
                ushort.TryParse(first, CultureInfo.InvariantCulture, out ushort group)     && group     is >= 1 and <= 255 &&
                ushort.TryParse(third, CultureInfo.InvariantCulture, out ushort extension) && extension is >= 0 and <= 15
                => (Extension: extension, Group: group),

            _ => throw new ArgumentException(SR.GetResourceString(SR.ID0059), nameof(address))
        };
    }

    /// <summary>
    /// Converts the specified address to a SCS light point point-to-point address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A SCS light point point-to-point address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid SCS light point point-to-point address.</exception>
    public static (ushort? Extension, ushort? Area, ushort? Point) ToScsLightPointPointToPointAddress(OpenNettyAddress address)
    {
        if (address.Type is not OpenNettyAddressType.ScsLightPointPointToPoint)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0060), nameof(address));
        }

        return address.Parameters switch
        {
            { IsDefaultOrEmpty: true } when GetAreaAndLightPoint(address.Value) is { Area: ushort area, Point: ushort point }
                => (Extension: null, Area: area, Point: point),

            ["4", string value] when
                GetAreaAndLightPoint(address.Value) is { Area: ushort area, Point: ushort point } &&
                ushort.TryParse(value, CultureInfo.InvariantCulture, out ushort extension) && extension is >= 0 and <= 15
                => (Extension: extension, Area: area, Point: point),

            _ => throw new ArgumentException(SR.GetResourceString(SR.ID0061), nameof(address))
        };

        static (ushort Area, ushort Point) GetAreaAndLightPoint(ReadOnlySpan<char> address) => address switch
        {
            // A = 00; PL [01 − 15]:
            ['0', '0', '0' or '1', >= '0' and <= '9'] when
                ushort.TryParse(address[2..4], CultureInfo.InvariantCulture, out ushort point) && point is >= 1 and <= 15
                => (0, point),

            // A [1 − 9]; PL [1 − 9]:
            [>= '1' and <= '9', >= '1' and <= '9'] when
                ushort.TryParse(address[0..1], CultureInfo.InvariantCulture, out ushort area) &&
                ushort.TryParse(address[1..2], CultureInfo.InvariantCulture, out ushort point)
                => (area, point),

            // A = 10; PL [01 − 15]:
            ['1', '0', '0' or '1', >= '0' and <= '9'] when
                ushort.TryParse(address[2..4], CultureInfo.InvariantCulture, out ushort point) && point is >= 1 and <= 15
                => (10, point),

            // A [01 − 09]; PL [10 − 15]:
            ['0', >= '1' and <= '9', '1', >= '0' and <= '5'] when
                ushort.TryParse(address[0..2], CultureInfo.InvariantCulture, out ushort area)  &&
                ushort.TryParse(address[2..4], CultureInfo.InvariantCulture, out ushort point) && point is >= 1 and <= 15
                => (area, point),

            _ => throw new ArgumentException(SR.GetResourceString(SR.ID0061), nameof(address))
        };
    }

    /// <summary>
    /// Converts the specified address to a Zigbee address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>A Zigbee address based on the specified address.</returns>
    /// <exception cref="ArgumentException">The address doesn't represent a valid Zigbee address.</exception>
    public static (uint? Identifier, ushort Unit) ToZigbeeAddress(OpenNettyAddress address)
    {
        if (!IsZigbeeAddress(address))
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0062), nameof(address));
        }

        if (address.Value is { Length: 2 })
        {
            if (!ushort.TryParse(address.Value, CultureInfo.InvariantCulture, out ushort unit))
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0063), nameof(address));
            }

            return (Identifier: null, unit);
        }

        else if (address.Value is { Length: > 2 })
        {
            // Note: Zigbee identifiers are 4-byte long and fit exactly in an
            // unsigned 32-bit integer, so a range check is not required.

            if (!uint.TryParse(address.Value.AsSpan()[0..^2], CultureInfo.InvariantCulture, out uint identifier) ||
                !ushort.TryParse(address.Value.AsSpan()[^2..], CultureInfo.InvariantCulture, out ushort unit) || unit is > 99)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0063), nameof(address));
            }

            return (identifier, unit);
        }

        throw new ArgumentException(SR.GetResourceString(SR.ID0063), nameof(address));
    }
}
