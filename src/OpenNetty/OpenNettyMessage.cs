/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenNetty;

/// <summary>
/// Represents a OpenNetty message.
/// </summary>
[DebuggerDisplay("{Frame,nq} ({Type,nq})")]
public sealed class OpenNettyMessage : IEquatable<OpenNettyMessage>
{
    /// <summary>
    /// Gets the address, if applicable.
    /// </summary>
    public OpenNettyAddress? Address { get; private set; }

    /// <summary>
    /// Gets the category of the message, if applicable.
    /// </summary>
    public OpenNettyCategory? Category { get; private set; }

    /// <summary>
    /// Gets the command (or status), if applicable.
    /// </summary>
    public OpenNettyCommand? Command { get; private set; }

    /// <summary>
    /// Gets the dimension, if applicable.
    /// </summary>
    public OpenNettyDimension? Dimension { get; private set; }

    /// <summary>
    /// Gets the raw representation of this message.
    /// </summary>
    public OpenNettyFrame Frame { get; private set; }

    /// <summary>
    /// Gets the transmission medium used to receive or send the message, if applicable.
    /// </summary>
    public OpenNettyMedium? Medium { get; private set; }

    /// <summary>
    /// Gets the transmission mode used to receive or send the message, if applicable.
    /// </summary>
    public OpenNettyMode? Mode { get; private set; }

    /// <summary>
    /// Gets the protocol.
    /// </summary>
    public OpenNettyProtocol Protocol { get; private set; }

    /// <summary>
    /// Gets the type of the message.
    /// </summary>
    public OpenNettyMessageType Type { get; private set; }

    /// <summary>
    /// Gets the values associated with the message, if applicable.
    /// </summary>
    public ImmutableArray<string> Values { get; private set; } = [];

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyMessage"/> class.
    /// </summary>
    private OpenNettyMessage()
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyMessage"/> using the specified raw frame.
    /// </summary>
    /// <param name="protocol">The OpenNetty protocol.</param>
    /// <param name="frame">The raw OpenWebNet frame.</param>
    public static OpenNettyMessage CreateFromFrame(OpenNettyProtocol protocol, string frame)
    {
        ArgumentException.ThrowIfNullOrEmpty(frame);

        return CreateFromFrame(protocol, OpenNettyFrame.Parse(frame));
    }

    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyMessage"/> using the specified raw frame.
    /// </summary>
    /// <param name="protocol">The OpenNetty protocol.</param>
    /// <param name="frame">The raw OpenWebNet frame.</param>
    public static OpenNettyMessage CreateFromFrame(OpenNettyProtocol protocol, OpenNettyFrame frame)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
        }

        var message = new OpenNettyMessage
        {
            Protocol = protocol,
            Frame = frame,
            Type = frame.Fields switch
            {
                // Acknowledgement messages MUST have exactly 2 fields:
                //
                // - the first one MUST contain exactly 2 empty parameters.
                // - the second one MUST contain a valid acknowledgement type (0, 1 or 6).
                //
                // Note: "6" is only valid for MyHome Play (Zigbee-based) interfaces.
                [
                    { Parameters: [{ IsEmpty: true }, { IsEmpty: true }] },
                    { Parameters: [{           Value: "0"             }] }
                ] => OpenNettyMessageType.NegativeAcknowledgement,

                [
                    { Parameters: [{ IsEmpty: true }, { IsEmpty: true }] },
                    { Parameters: [{           Value: "1"             }] }
                ] => OpenNettyMessageType.Acknowledgement,

                [
                    { Parameters: [{ IsEmpty: true }, { IsEmpty: true }] },
                    { Parameters: [{           Value: "6"             }] }
                ] => protocol is OpenNettyProtocol.Zigbee
                    ? OpenNettyMessageType.BusyNegativeAcknowledgement
                    : throw new InvalidOperationException(SR.GetResourceString(SR.ID0058)),

                // Status request messages MUST have exactly 2 fields:
                //
                // - WHO, that MUST contain exactly 1 empty and 1 non-empty parameters (i.e MUST be prefixed by a #).
                // - WHERE, that MUST contain 1 or more parameter(s), possibly empty.
                [
                    { Parameters: [{ IsEmpty: true }, { IsEmpty: false }] },
                    { Parameters: [        _        ,          ..       ] }
                ] => OpenNettyMessageType.StatusRequest,

                // Command/status messages MUST have exactly 3 fields:
                //
                // - WHO, that MUST contain exactly 1 non-empty parameter (i.e CANNOT be prefixed by a #).
                // - WHAT, that MUST start with 1 non-empty parameter (i.e CANNOT be prefixed by a #).
                // - WHERE, that MUST contain 1 or more parameter(s), possibly empty.
                [
                    { Parameters: [{   IsEmpty: false   }] },
                    { Parameters: [{ IsEmpty: false }, ..] },
                    { Parameters: [        _         , ..] }
                ] => OpenNettyMessageType.BusCommand,

                // Dimension request messages MUST have exactly 3 fields:
                //
                // - WHO, that MUST contain exactly 1 empty and 1 non-empty parameters (i.e MUST be prefixed by a #).
                // - WHERE, that MUST contain 1 or more parameter(s), possibly empty.
                // - DIMENSION, that MUST start with 1 non-empty parameter (i.e CANNOT be prefixed by a #).
                [
                    { Parameters: [{ IsEmpty: true  }, { IsEmpty: false }] },
                    { Parameters: [        _         ,          ..       ] },
                    { Parameters: [{ IsEmpty: false },          ..       ] }
                ] => OpenNettyMessageType.DimensionRequest,

                // Dimension read messages MUST have at least 4 fields:
                //
                // - WHO, that MUST contain exactly 1 empty and 1 non-empty parameters (i.e MUST be prefixed by a #).
                // - WHERE, that MUST contain 1 or more parameter(s), possibly empty.
                // - DIMENSION, that MUST start with 1 non-empty parameter (i.e CANNOT be prefixed by a #).
                // - 1 or more VALUE fields.
                [
                    { Parameters: [{ IsEmpty: true  }, { IsEmpty: false }] },
                    { Parameters: [        _         ,          ..       ] },
                    { Parameters: [{ IsEmpty: false },          ..       ] },
                    { Parameters: [        _         ,          ..       ] },
                                                ..
                ] => OpenNettyMessageType.DimensionRead,

                // Dimension write messages MUST have at least 4 fields:
                //
                // - WHO, that MUST contain exactly 1 empty and 1 non-empty parameters (i.e MUST be prefixed by a #).
                // - WHERE, that MUST contain 1 or more parameter(s), possibly empty.
                // - DIMENSION, that MUST start with 1 empty parameter (i.e MUST be prefixed by a #).
                // - 1 or more VALUE fields.
                [
                    { Parameters: [{ IsEmpty: true }, { IsEmpty: false }] },
                    { Parameters: [        _        ,          ..       ] },
                    { Parameters: [{ IsEmpty: true },          ..       ] },
                    { Parameters: [        _        ,          ..       ] },
                                                ..
                ] => OpenNettyMessageType.DimensionSet,

                _ => OpenNettyMessageType.Unknown
            }
        };

        // Then, infer the category from the WHO field, if applicable.
        if (message.Type is OpenNettyMessageType.BusCommand       or
                            OpenNettyMessageType.StatusRequest    or
                            OpenNettyMessageType.DimensionRequest or
                            OpenNettyMessageType.DimensionRead    or
                            OpenNettyMessageType.DimensionSet)
        {
            var parameters = new List<string>(capacity: frame.Fields[0].Parameters.Length - 1);

            // If the message is not a command, the WHO field is always prefixed by a #.
            for (
                var index = message.Type is OpenNettyMessageType.BusCommand ? 1 : 2;
                index < frame.Fields[0].Parameters.Length;
                index++)
            {
                parameters.Add(frame.Fields[0].Parameters[index].Value);
            }

            message.Category = new OpenNettyCategory(message.Type is OpenNettyMessageType.BusCommand
                ? frame.Fields[0].Parameters[0].Value
                : frame.Fields[0].Parameters[1].Value, [.. parameters]);
        }

        // Then, infer the command/status from the WHAT field if the message is a command.
        if (message.Type is OpenNettyMessageType.BusCommand)
        {
            // In most cases, the WHAT field doesn't include any extra parameter.
            var field = frame.Fields[1];
            if (field.Parameters.Length is 1)
            {
                message.Command = new OpenNettyCommand(message.Category!.Value, field.Parameters[0].Value);
            }

            else
            {
                var parameters = new List<string>(capacity: field.Parameters.Length - 1);

                for (var index = 1; index < field.Parameters.Length; index++)
                {
                    parameters.Add(field.Parameters[index].Value);
                }

                message.Command = new OpenNettyCommand(message.Category!.Value, field.Parameters[0].Value, [.. parameters]);
            }
        }

        // Then, infer the address from the WHERE field, if applicable.
        if (message.Type is OpenNettyMessageType.BusCommand       or
                            OpenNettyMessageType.StatusRequest    or
                            OpenNettyMessageType.DimensionRequest or
                            OpenNettyMessageType.DimensionRead    or
                            OpenNettyMessageType.DimensionSet)
        {
            // For commands, the WHERE field is always the 3rd field but may be empty (e.g for management frames).
            //
            // In this case, the gateway itself is assumed to be the recipient of the received frame.
            var field = frame.Fields[message.Type is OpenNettyMessageType.BusCommand ? 2 : 1];

            if (protocol is OpenNettyProtocol.Scs)
            {
                if (field.Parameters is not [{ IsEmpty: true }])
                {
                    message.Medium = OpenNettyMedium.Bus;

                    var type = GetAddressType(message.Category, field);

                    if (field.Parameters.Length is 1)
                    {
                        message.Address = new OpenNettyAddress(type, field.Parameters[0].Value);
                    }

                    else
                    {
                        var parameters = new List<string>(capacity: field.Parameters.Length - 1);

                        for (var index = 1; index < field.Parameters.Length; index++)
                        {
                            parameters.Add(field.Parameters[index].Value);
                        }

                        message.Address = new OpenNettyAddress(type, field.Parameters[0].Value, [.. parameters]);
                    }
                }

                static OpenNettyAddressType GetAddressType(OpenNettyCategory? category, OpenNettyField field)
                {
                    // Note: the WHO=2 and WHO=15 categories use the same addressing scheme as the WHO=1 category.
                    if (category == OpenNettyCategories.Lighting   ||
                        category == OpenNettyCategories.Automation ||
                        category == OpenNettyCategories.Scenarios)
                    {
                        return OpenNettyAddressType.ScsLightPoint;
                    }

                    // Note: the WHO=25 category uses different types of addresses, always
                    // prefixed by a discriminator character (2 for CEN+, 3 for DRY CONTACT).
                    if (category == OpenNettyCategories.ScenariosPlus)
                    {
                        if (field.Parameters is not [{ Value: [char discriminator, ..] }])
                        {
                            return OpenNettyAddressType.Unknown;
                        }

                        return discriminator switch
                        {
                            '2' => OpenNettyAddressType.ScsScenarioPlus,
                            '3' => OpenNettyAddressType.ScsDryContact,

                            _ => OpenNettyAddressType.Unknown
                        };
                    }

                    return OpenNettyAddressType.Unknown;
                }
            }

            else if (protocol is OpenNettyProtocol.Zigbee)
            {
                // The WHERE field of Zigbee-based frames can contain up to 3 parameters:
                //
                // - TRANSMISSION MODE: not set for unicast, empty for multicast, 0 for broadcast.
                // - ADDRESS: the address of the Zigbee device and/or the targeted unit.
                // - FAMILY TYPE: the family type of the target device (always 9 for Zigbee devices).

                (message.Mode, message.Address, message.Medium) = field.Parameters switch
                {
                    [{ IsEmpty: true }] => (null as OpenNettyMode?, null as OpenNettyAddress?, null as OpenNettyMedium?),

                    [{   Value: "0"  }, { Value: var address }, { Value: "9" }] => (OpenNettyMode.Broadcast, CreateAddress(address), OpenNettyMedium.Radio),
                    [{ IsEmpty: true }, { Value: var address }, { Value: "9" }] => (OpenNettyMode.Multicast, CreateAddress(address), OpenNettyMedium.Radio),
                    [{    Value: var address    }, {        Value: "9"       }] => (OpenNettyMode.Unicast,   CreateAddress(address), OpenNettyMedium.Radio),

                    _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0059))
                };

                static OpenNettyAddress? CreateAddress(string? address) => address switch
                {
                    null or { Length: 0 } => null,

                    { Length: 2 } value when byte.TryParse(value, CultureInfo.InvariantCulture, out byte unit)
                        => OpenNettyAddress.FromDecimalZigbeeAddress(0, unit),

                    { Length: > 2 } value when value[^2..] is "00" &&
                        uint.TryParse(value[0..^2], CultureInfo.InvariantCulture, out uint identifier)
                        => OpenNettyAddress.FromDecimalZigbeeAddress(identifier, 0),

                    { Length: > 2 } value when
                        uint.TryParse(value[0..^2], CultureInfo.InvariantCulture, out uint identifier) &&
                        byte.TryParse(value[^2..],  CultureInfo.InvariantCulture, out byte unit)
                        => OpenNettyAddress.FromDecimalZigbeeAddress(identifier, unit),

                    _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0059))
                };
            }

            else if (protocol is OpenNettyProtocol.Nitoo)
            {
                // The WHERE field of Nitoo-based frames can contain up to 3 parameters:
                //
                // - TRANSMISSION MODE: not set for unicast, empty for multicast, 0 for broadcast.
                // - ADDRESS: the address of the Nitoo device and the targeted unit.
                // - FAMILY TYPE: the family type of the target device.
                //
                // Note: powerline is always the default value when no family type is explicitly set.

                (message.Mode, message.Address, message.Medium) = field.Parameters switch
                {
                    [{ IsEmpty: true }] => (null as OpenNettyMode?, null as OpenNettyAddress?, null as OpenNettyMedium?),

                    [{        Value: "0"        }, {    Value: var address   }] => (OpenNettyMode.Broadcast, CreateAddress(address), OpenNettyMedium.Powerline),
                    [{   Value: "0"  }, { Value: var address }, { Value: "0" }] => (OpenNettyMode.Broadcast, CreateAddress(address), OpenNettyMedium.Powerline),
                    [{   Value: "0"  }, { Value: var address }, { Value: "1" }] => (OpenNettyMode.Broadcast, CreateAddress(address), OpenNettyMedium.Radio),
                    [{   Value: "0"  }, { Value: var address }, { Value: "2" }] => (OpenNettyMode.Broadcast, CreateAddress(address), OpenNettyMedium.Infrared),

                    [{       IsEmpty: true      }, {    Value: var address   }] => (OpenNettyMode.Multicast, CreateAddress(address), OpenNettyMedium.Powerline),
                    [{ IsEmpty: true }, { Value: var address }, { Value: "0" }] => (OpenNettyMode.Multicast, CreateAddress(address), OpenNettyMedium.Powerline),
                    [{ IsEmpty: true }, { Value: var address }, { Value: "1" }] => (OpenNettyMode.Multicast, CreateAddress(address), OpenNettyMedium.Radio),
                    [{ IsEmpty: true }, { Value: var address }, { Value: "2" }] => (OpenNettyMode.Multicast, CreateAddress(address), OpenNettyMedium.Infrared),

                    [{                    Value: var address                 }] => (OpenNettyMode.Unicast, CreateAddress(address), OpenNettyMedium.Powerline),
                    [{    Value: var address    }, {        Value: "0"       }] => (OpenNettyMode.Unicast, CreateAddress(address), OpenNettyMedium.Powerline),
                    [{    Value: var address    }, {        Value: "1"       }] => (OpenNettyMode.Unicast, CreateAddress(address), OpenNettyMedium.Radio),
                    [{    Value: var address    }, {        Value: "2"       }] => (OpenNettyMode.Unicast, CreateAddress(address), OpenNettyMedium.Infrared),

                    _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0059))
                };

                static OpenNettyAddress? CreateAddress(string? address) => address switch
                {
                    null or { Length: 0 } => null,

                    string value when uint.TryParse(value, CultureInfo.InvariantCulture, out uint result)
                        => OpenNettyAddress.FromNitooAddress(result / 16, (byte) (result % 16)),

                    _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0059))
                };
            }

            else
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
            }
        }

        // Then, infer the dimension from the DIMENSION field if the message is either
        // a dimension request, a dimension read or a dimension set message.
        if (message.Type is OpenNettyMessageType.DimensionRead or OpenNettyMessageType.DimensionRequest)
        {
            // In the typical case, the DIMENSION field doesn't include any extra parameter.
            var field = frame.Fields[2];
            if (field.Parameters.Length is 1)
            {
                message.Dimension = new OpenNettyDimension(message.Category!.Value, field.Parameters[0].Value);
            }

            else
            {
                var parameters = new List<string>(capacity: field.Parameters.Length - 1);

                for (var index = 1; index < field.Parameters.Length; index++)
                {
                    parameters.Add(field.Parameters[index].Value);
                }

                message.Dimension = new OpenNettyDimension(message.Category!.Value, field.Parameters[0].Value, [.. parameters]);
            }
        }

        else if (message.Type is OpenNettyMessageType.DimensionSet)
        {
            // In the typical case, the DIMENSION field doesn't include any extra parameter.
            var field = frame.Fields[2];
            if (field.Parameters.Length is 2)
            {
                // When the message is a dimension set, the DIMENSION field is always prefixed by a #.
                message.Dimension = new OpenNettyDimension(message.Category!.Value, field.Parameters[1].Value);
            }

            else
            {
                var parameters = new List<string>(capacity: field.Parameters.Length - 1);

                for (var index = 1; index < field.Parameters.Length; index++)
                {
                    parameters.Add(field.Parameters[index].Value);
                }

                // When the message is a dimension set, the DIMENSION field is always prefixed by a #.
                message.Dimension = new OpenNettyDimension(message.Category!.Value, field.Parameters[1].Value, [.. parameters]);
            }
        }

        // Finally, if the message is a dimension read or dimension set message, extract the values.
        if (message.Type is OpenNettyMessageType.DimensionRead or OpenNettyMessageType.DimensionSet)
        {
            var values = new List<string>(capacity: frame.Fields.Length - 3);

            for (var index = 3; index < frame.Fields.Length; index++)
            {
                values.Add(frame.Fields[index].Parameters[0].Value);
            }

            message.Values = [.. values];
        }

        return message;
    }

    /// <summary>
    /// Creates a new BUS COMMAND message using the specified parameters.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="command">The command.</param>
    /// <param name="address">The address.</param>
    /// <param name="medium">The medium.</param>
    /// <param name="mode">The transmission mode.</param>
    /// <returns>A new BUS COMMAND message reflecting the specified parameters.</returns>
    public static OpenNettyMessage CreateCommand(
        OpenNettyProtocol protocol, OpenNettyCommand command,
        OpenNettyAddress? address = null, OpenNettyMedium? medium = null, OpenNettyMode? mode = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
        }

        return CreateFromFrame(protocol, new OpenNettyFrame(
            /* WHO:   */ CreateWhoField(OpenNettyMessageType.BusCommand, command.Category),
            /* WHAT   */ new OpenNettyField(command.ToParameters()),
            /* WHERE: */ CreateWhereField(protocol, command.Category, address, medium, mode)));
    }

    /// <summary>
    /// Creates a new STATUS REQUEST message using the specified parameters.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="category">The category.</param>
    /// <param name="address">The address.</param>
    /// <param name="medium">The medium.</param>
    /// <param name="mode">The transmission mode.</param>
    /// <returns>A new STATUS REQUEST message reflecting the specified parameters.</returns>
    public static OpenNettyMessage CreateStatusRequest(
        OpenNettyProtocol protocol, OpenNettyCategory category, OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null, OpenNettyMode? mode = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
        }

        return CreateFromFrame(protocol, new OpenNettyFrame(
            /* #WHO:  */ CreateWhoField(OpenNettyMessageType.StatusRequest, category),
            /* WHERE: */ CreateWhereField(protocol, category, address, medium, mode)));
    }

    /// <summary>
    /// Creates a new DIMENSION REQUEST message using the specified parameters.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="dimension">The dimension.</param>
    /// <param name="address">The address.</param>
    /// <param name="medium">The medium.</param>
    /// <param name="mode">The transmission mode.</param>
    /// <returns>A new DIMENSION REQUEST message reflecting the specified parameters.</returns>
    public static OpenNettyMessage CreateDimensionRequest(
        OpenNettyProtocol protocol, OpenNettyDimension dimension, OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null, OpenNettyMode? mode = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
        }

        return CreateFromFrame(protocol, new OpenNettyFrame(
            /* #WHO:      */ CreateWhoField(OpenNettyMessageType.DimensionRequest, dimension.Category),
            /* WHERE:     */ CreateWhereField(protocol, dimension.Category, address, medium, mode),
            /* DIMENSION: */ CreateDimensionField(OpenNettyMessageType.DimensionRequest, dimension)));
    }

    /// <summary>
    /// Creates a new DIMENSION READ message using the specified parameters.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="dimension">The dimension.</param>
    /// <param name="values">The dimension values.</param>
    /// <param name="address">The address.</param>
    /// <param name="medium">The medium.</param>
    /// <param name="mode">The transmission mode.</param>
    /// <returns>A new DIMENSION READ message reflecting the specified parameters.</returns>
    public static OpenNettyMessage CreateDimensionRead(
        OpenNettyProtocol protocol, OpenNettyDimension dimension,
        ImmutableArray<string> values, OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null, OpenNettyMode? mode = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
        }

        if (values.Length is 0)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0060));
        }

        var fields = new List<OpenNettyField>(capacity: 3 + values.Length)
        {
            /* #WHO:      */ CreateWhoField(OpenNettyMessageType.DimensionRead, dimension.Category),
            /* WHERE:     */ CreateWhereField(protocol, dimension.Category, address, medium, mode),
            /* DIMENSION: */ CreateDimensionField(OpenNettyMessageType.DimensionRead, dimension)
        };

        for (var index = 0; index < values.Length; index++)
        {
            fields.Add(new OpenNettyField(new OpenNettyParameter(values[index])));
        }

        return CreateFromFrame(protocol, new OpenNettyFrame(fields.ToImmutableArray()));
    }

    /// <summary>
    /// Creates a new DIMENSION SET message using the specified parameters.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="dimension">The dimension.</param>
    /// <param name="values">The dimension values.</param>
    /// <param name="address">The address.</param>
    /// <param name="medium">The medium.</param>
    /// <param name="mode">The transmission mode.</param>
    /// <returns>A new DIMENSION SET message reflecting the specified parameters.</returns>
    public static OpenNettyMessage CreateDimensionSet(
        OpenNettyProtocol protocol, OpenNettyDimension dimension,
        ImmutableArray<string> values, OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null, OpenNettyMode? mode = null)
    {
        if (!Enum.IsDefined(protocol))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
        }

        if (values.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0061));
        }

        var fields = new List<OpenNettyField>(capacity: 3 + values.Length)
        {
            /* #WHO:       */ CreateWhoField(OpenNettyMessageType.DimensionSet, dimension.Category),
            /* WHERE:      */ CreateWhereField(protocol, dimension.Category, address, medium, mode),
            /* #DIMENSION: */ CreateDimensionField(OpenNettyMessageType.DimensionSet, dimension)
        };

        for (var index = 0; index < values.Length; index++)
        {
            fields.Add(new OpenNettyField(new OpenNettyParameter(values[index])));
        }

        return CreateFromFrame(protocol, new OpenNettyFrame(fields.ToImmutableArray()));
    }

    /// <inheritdoc/>
    public bool Equals(OpenNettyMessage? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Note: comparing the protocol and the raw frame is enough to determine whether two messages are equal.
        return other is not null && Protocol == other.Protocol && Frame == other.Frame;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettyMessage message && Equals(message);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Protocol, Frame);

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current message.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current message.</returns>
    public override string ToString() => Frame.ToString();

    /// <summary>
    /// Computes the JSON representation of the current message.
    /// </summary>
    /// <returns>The JSON representation of the current message.</returns>
    public JsonObject ToJsonObject()
    {
        var result = new JsonObject
        {
            ["protocol"] = Protocol switch
            {
                OpenNettyProtocol.Scs    => "scs",
                OpenNettyProtocol.Nitoo  => "nitoo",
                OpenNettyProtocol.Zigbee => "zigbee",

                _ => "unknown"
            },
            ["type"] = Type switch
            {
                OpenNettyMessageType.Acknowledgement             => "acknowledgement",
                OpenNettyMessageType.BusCommand                  => "bus_command",
                OpenNettyMessageType.BusyNegativeAcknowledgement => "busy_negative_acknowledgement",
                OpenNettyMessageType.DimensionRead               => "dimension_read",
                OpenNettyMessageType.DimensionRequest            => "dimension_request",
                OpenNettyMessageType.DimensionSet                => "dimension_set",
                OpenNettyMessageType.NegativeAcknowledgement     => "negative_acknowledgement",
                OpenNettyMessageType.StatusRequest               => "status_request",
                OpenNettyMessageType.Unknown or _                => "unknown"
            }
        };

        switch (Type)
        {
            case OpenNettyMessageType.BusCommand:
            case OpenNettyMessageType.DimensionRead:
            case OpenNettyMessageType.DimensionRequest:
            case OpenNettyMessageType.DimensionSet:
            case OpenNettyMessageType.StatusRequest:
                result["medium"] = Medium switch
                {
                    OpenNettyMedium.Bus       => "bus",
                    OpenNettyMedium.Infrared  => "infrared",
                    OpenNettyMedium.Powerline => "powerline",
                    OpenNettyMedium.Radio     => "radio",

                    _ => "unknown"
                };

                result["mode"] = Mode switch
                {
                    OpenNettyMode.Unicast   => "unicast",
                    OpenNettyMode.Multicast => "multicast",
                    OpenNettyMode.Broadcast => "broadcast",

                    _ => "unknown"
                };
                break;
        }

        if (Address is OpenNettyAddress address)
        {
            result["address"] = new JsonObject
            {
                ["value"] = address.Value,
                ["type"] = address.Type switch
                {
                    OpenNettyAddressType.Nitoo           => "nitoo",
                    OpenNettyAddressType.ScsDryContact   => "scs_dry_contact",
                    OpenNettyAddressType.ScsLightPoint   => "scs_light_point",
                    OpenNettyAddressType.ScsScenarioPlus => "scs_scenario_plus",
                    OpenNettyAddressType.Zigbee          => "zigbee",
                    OpenNettyAddressType.Unknown or _    => "unknown"
                }
            };

            if (address.Parameters is { IsDefaultOrEmpty: false })
            {
                result["address"]?["parameters"] = new JsonArray([.. address.Parameters]);
            }

            switch (address.Type)
            {
                case OpenNettyAddressType.Nitoo when OpenNettyAddress.ToNitooAddress(address)
                    is { Identifier: var identifier, Unit: var unit }:
                    result["address"]?["identifier"] = identifier;
                    result["address"]?["unit"] = unit;
                    break;

                case OpenNettyAddressType.ScsDryContact:
                    result["address"]?["contact"] = OpenNettyAddress.ToScsDryContactAddress(address);
                    break;

                case OpenNettyAddressType.ScsLightPoint when OpenNettyAddress.ToScsLightPointAddress(address)
                    is { Area: var area, Extension: var extension, General: var general, Group: var group, Point: var point }:
                    result["address"]?["area"] = area;
                    result["address"]?["extension"] = extension;
                    result["address"]?["general"] = general;
                    result["address"]?["group"] = group;
                    result["address"]?["point"] = point;
                    break;

                case OpenNettyAddressType.ScsScenarioPlus:
                    result["address"]?["object"] = OpenNettyAddress.ToScsScenarioPlusAddress(address);
                    break;

                case OpenNettyAddressType.Zigbee when OpenNettyAddress.ToZigbeeAddress(address)
                    is { Identifier: var identifier, Unit: var unit }:
                    result["address"]?["identifier"] = identifier;
                    result["address"]?["unit"] = unit;
                    break;
            }
        }

        if (Category is OpenNettyCategory category)
        {
            result["category"] = new JsonObject
            {
                ["value"] = category.Value
            };

            if (category.Parameters is { IsDefaultOrEmpty: false })
            {
                result["category"]?["parameters"] = new JsonArray([.. category.Parameters]);
            }
        }

        if (Command is OpenNettyCommand command)
        {
            result["command"] = new JsonObject
            {
                ["value"] = command.Value
            };

            if (command.Parameters is { IsDefaultOrEmpty: false })
            {
                result["command"]?["parameters"] = new JsonArray([.. command.Parameters]);
            }
        }

        if (Dimension is OpenNettyDimension dimension)
        {
            result["dimension"] = new JsonObject
            {
                ["value"] = dimension.Value
            };

            if (dimension.Parameters is { IsDefaultOrEmpty: false })
            {
                result["dimension"]?["parameters"] = new JsonArray([.. dimension.Parameters]);
            }
        }

        if (Type is OpenNettyMessageType.DimensionRead or OpenNettyMessageType.DimensionSet)
        {
            result["values"] = new JsonArray([.. Values]);
        }

        return result;
    }

    /// <summary>
    /// Creates a new instance of <see cref="OpenNettyMessage"/> from a
    /// JSON object containing the parameters used to create the message.
    /// </summary>
    /// <param name="parameters">The JSON object containing the parameters used to create the message.</param>
    /// <returns>A new instance of <see cref="OpenNettyMessage"/>.</returns>
    public static OpenNettyMessage CreateFromJsonObject(JsonObject parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var protocol = ParseProtocol(parameters["protocol"]?.AsValue());

        return ParseMessageType(parameters["type"]?.AsValue()) switch
        {
            OpenNettyMessageType.Acknowledgement             => CreateFromFrame(protocol, OpenNettyFrames.Acknowledgement),
            OpenNettyMessageType.NegativeAcknowledgement     => CreateFromFrame(protocol, OpenNettyFrames.NegativeAcknowledgement),
            OpenNettyMessageType.BusyNegativeAcknowledgement => CreateFromFrame(protocol, OpenNettyFrames.BusyNegativeAcknowledgement),

            OpenNettyMessageType.BusCommand
                when ParseCategory(parameters["category"]?.AsObject())         is OpenNettyCategory category &&
                     ParseCommand(parameters["command"]?.AsObject(), category) is OpenNettyCommand command &&
                     ParseAddress(parameters["address"]?.AsObject())           is var address &&
                     ParseMedium(parameters["medium"]?.AsValue())              is var medium &&
                     ParseMode(parameters["mode"]?.AsValue())                  is var mode
                => CreateCommand(protocol, command, address, medium, mode),

            OpenNettyMessageType.DimensionRequest
                when ParseCategory(parameters["category"]?.AsObject())             is OpenNettyCategory category &&
                     ParseDimension(parameters["dimension"]?.AsObject(), category) is OpenNettyDimension dimension &&
                     ParseAddress(parameters["address"]?.AsObject())               is var address &&
                     ParseMedium(parameters["medium"]?.AsValue())                  is var medium &&
                     ParseMode(parameters["mode"]?.AsValue())                      is var mode
                => CreateDimensionRequest(protocol, dimension, address, medium, mode),

            OpenNettyMessageType.DimensionRead
                when ParseCategory(parameters["category"]?.AsObject())             is OpenNettyCategory category &&
                     ParseDimension(parameters["dimension"]?.AsObject(), category) is OpenNettyDimension dimension &&
                     ParseValues(parameters["values"]?.AsArray())                  is ImmutableArray<string> values &&
                     ParseAddress(parameters["address"]?.AsObject())               is var address &&
                     ParseMedium(parameters["medium"]?.AsValue())                  is var medium &&
                     ParseMode(parameters["mode"]?.AsValue())                      is var mode
                => CreateDimensionRead(protocol, dimension, values, address, medium, mode),

            OpenNettyMessageType.DimensionSet
                when ParseCategory(parameters["category"]?.AsObject())             is OpenNettyCategory category &&
                     ParseDimension(parameters["dimension"]?.AsObject(), category) is OpenNettyDimension dimension &&
                     ParseValues(parameters["values"]?.AsArray())                  is ImmutableArray<string> values &&
                     ParseAddress(parameters["address"]?.AsObject())               is var address &&
                     ParseMedium(parameters["medium"]?.AsValue())                  is var medium &&
                     ParseMode(parameters["mode"]?.AsValue())                      is var mode
                => CreateDimensionSet(protocol, dimension, values, address, medium, mode),

            OpenNettyMessageType.StatusRequest
                when ParseCategory(parameters["category"]?.AsObject()) is OpenNettyCategory category &&
                     ParseAddress(parameters["address"]?.AsObject())   is var address &&
                     ParseMedium(parameters["medium"]?.AsValue())      is var medium &&
                     ParseMode(parameters["mode"]?.AsValue())          is var mode
                => CreateStatusRequest(protocol, category, address, medium, mode),

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0122))
        };

        static OpenNettyAddress? ParseAddress(JsonObject? node) => node is null
            ? null
            : node["type"]?.GetValue<string>() switch
            {
                "nitoo" when (uint?) node["identifier"]?.AsValue() is uint identifier &&
                             (byte?) node["unit"]?.AsValue()       is var unit
                    => OpenNettyAddress.FromNitooAddress(identifier, unit ?? 0),

                "zigbee" when (uint?) node["identifier"]?.AsValue() is uint identifier &&
                              (byte?) node["unit"]?.AsValue()       is var unit
                    => OpenNettyAddress.FromDecimalZigbeeAddress(identifier, unit ?? 0),

                "scs_dry_contact" when (byte?) node["contact"]?.AsValue() is byte identifier
                    => OpenNettyAddress.FromScsDryContactAddress(identifier),

                "scs_light_point" when (byte?) node["extension"]?.AsValue() is var extension &&
                                       (bool?) node["general"]?.AsValue()   is var general &&
                                       (byte?) node["group"]?.AsValue()     is var group &&
                                       (byte?) node["area"]?.AsValue()      is var area &&
                                       (byte?) node["point"]?.AsValue()     is var point
                    => OpenNettyAddress.FromScsLightPointAddress(extension ?? 0, general ?? false, group, area, point),

                "scs_scenario_plus" when (ushort?) node["object"]?.AsValue() is ushort identifier
                    => OpenNettyAddress.FromScsScenarioPlusAddress(identifier),

                _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0122))
            };

        static OpenNettyCategory? ParseCategory(JsonObject? node) => node is null
            ? null
            : new OpenNettyCategory(
                value     : ParseValue(node["value"]?.AsValue()),
                parameters: ParseValues(node["parameters"]?.AsArray()));

        static OpenNettyCommand? ParseCommand(JsonObject? node, OpenNettyCategory category) => node is null
            ? null
            : new OpenNettyCommand(
                category  : category,
                value     : ParseValue(node["value"]?.AsValue()),
                parameters: ParseValues(node["parameters"]?.AsArray()));

        static OpenNettyDimension? ParseDimension(JsonObject? node, OpenNettyCategory category) => node is null
            ? null
            : new OpenNettyDimension(
                category  : category,
                value     : ParseValue(node["value"]?.AsValue()),
                parameters: ParseValues(node["parameters"]?.AsArray()));

        static OpenNettyMessageType ParseMessageType(JsonValue? node) => (string?) node switch
        {
            "acknowledgement"               => OpenNettyMessageType.Acknowledgement,
            "bus_command"                   => OpenNettyMessageType.BusCommand,
            "busy_negative_acknowledgement" => OpenNettyMessageType.BusyNegativeAcknowledgement,
            "dimension_read"                => OpenNettyMessageType.DimensionRead,
            "dimension_request"             => OpenNettyMessageType.DimensionRequest,
            "dimension_set"                 => OpenNettyMessageType.DimensionSet,
            "negative_acknowledgement"      => OpenNettyMessageType.NegativeAcknowledgement,
            "status_request"                => OpenNettyMessageType.StatusRequest,

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0122))
        };

        static OpenNettyMedium? ParseMedium(JsonValue? node) => (string?) node switch
        {
            "bus"       => OpenNettyMedium.Bus,
            "infrared"  => OpenNettyMedium.Infrared,
            "powerline" => OpenNettyMedium.Powerline,
            "radio"     => OpenNettyMedium.Radio,

            null or { Length: 0 } => null,

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0122))
        };

        static OpenNettyMode? ParseMode(JsonValue? node) => (string?) node switch
        {
            "unicast"   => OpenNettyMode.Unicast,
            "multicast" => OpenNettyMode.Multicast,
            "broadcast" => OpenNettyMode.Broadcast,

            null or { Length: 0 } => null,

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0122))
        };

        static OpenNettyProtocol ParseProtocol(JsonValue? node) => (string?) node switch
        {
            "scs"    => OpenNettyProtocol.Scs,
            "nitoo"  => OpenNettyProtocol.Nitoo,
            "zigbee" => OpenNettyProtocol.Zigbee,

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0057))
        };

        static string ParseValue(JsonValue? node) => node?.GetValue<string>()
            ?? throw new InvalidOperationException(SR.GetResourceString(SR.ID0122));

        static ImmutableArray<string> ParseValues(JsonArray? node)
        {
            if (node is null)
            {
                return [];
            }

            var builder = ImmutableArray.CreateBuilder<string>(node.Count);

            for (var index = 0; index < node.Count; index++)
            {
                if (node[index]?.GetValue<string>() is not { Length: > 0 } item)
                {
                    throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
                }

                builder.Add(item);
            }

            return builder.ToImmutable();
        }
    }

    private static OpenNettyField CreateWhoField(OpenNettyMessageType type, OpenNettyCategory category)
    {
        List<OpenNettyParameter> parameters = [];

        switch (type)
        {
            case OpenNettyMessageType.StatusRequest:
            case OpenNettyMessageType.DimensionRequest:
            case OpenNettyMessageType.DimensionRead:
            case OpenNettyMessageType.DimensionSet:
                parameters.Add(OpenNettyParameter.Empty);
                break;
        }

        parameters.AddRange(category.ToParameters());

        return new OpenNettyField(parameters);
    }

    private static OpenNettyField CreateWhereField(
        OpenNettyProtocol protocol, OpenNettyCategory category,
        OpenNettyAddress? address, OpenNettyMedium? medium, OpenNettyMode? mode)
    {
        if (address is null)
        {
            return new OpenNettyField([OpenNettyParameter.Empty]);
        }

        if (protocol is OpenNettyProtocol.Scs)
        {
            return address.Value.Type switch
            {
                not OpenNettyAddressType.ScsLightPoint when category == OpenNettyCategories.Lighting ||
                                                            category == OpenNettyCategories.Automation ||
                                                            category == OpenNettyCategories.Scenarios
                    => throw new InvalidOperationException(SR.GetResourceString(SR.ID0050)),

                not (OpenNettyAddressType.ScsDryContact or OpenNettyAddressType.ScsScenarioPlus)
                    when category == OpenNettyCategories.ScenariosPlus
                    => throw new InvalidOperationException(SR.GetResourceString(SR.ID0114)),

                _ => new OpenNettyField(address.Value.ToParameters()),
            };
        }

        else if (protocol is OpenNettyProtocol.Nitoo)
        {
            if (address.Value.Type is not OpenNettyAddressType.Nitoo)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0048));
            }

            List<OpenNettyParameter> parameters = [];

            switch (mode)
            {
                case OpenNettyMode.Multicast:
                    parameters.Add(OpenNettyParameter.Empty);
                    break;

                // Note: broadcast is always the default transmission mode for WHO=25 messages (scenarios).
                case OpenNettyMode.Broadcast:
                case null when category == OpenNettyCategories.ScenariosPlus:
                    parameters.Add(new OpenNettyParameter("0"));
                    break;
            }

            parameters.AddRange(address.Value.ToParameters());

            // Note: when using the default powerline transmission medium, adding an explicit parameter is not required.
            if (medium is not null and not OpenNettyMedium.Powerline)
            {
                parameters.Add(new OpenNettyParameter(medium switch
                {
                    OpenNettyMedium.Radio    => "1",
                    OpenNettyMedium.Infrared => "2",

                    _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0062))
                }));
            }

            return new OpenNettyField(parameters);
        }

        else if (protocol is OpenNettyProtocol.Zigbee)
        {
            if (address.Value.Type is not OpenNettyAddressType.Zigbee)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0055));
            }

            List<OpenNettyParameter> parameters = [];

            switch (mode)
            {
                case OpenNettyMode.Multicast:
                    parameters.Add(OpenNettyParameter.Empty);
                    break;

                // Note: broadcast is always the default transmission mode for messages
                // sent to an address that doesn't include a device identifier part.
                case OpenNettyMode.Broadcast:
                case null when OpenNettyAddress.ToZigbeeAddress(address.Value) is { Identifier: 0 }:
                    parameters.Add(new OpenNettyParameter("0"));
                    break;
            }

            parameters.AddRange(address.Value.ToParameters());

            parameters.Add(new OpenNettyParameter(medium switch
            {
                OpenNettyMedium.Radio or null => "9",

                _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0062))
            }));

            return new OpenNettyField(parameters);
        }

        throw new InvalidOperationException(SR.GetResourceString(SR.ID0057));
    }

    private static OpenNettyField CreateDimensionField(OpenNettyMessageType type, OpenNettyDimension dimension)
    {
        Debug.Assert(type is OpenNettyMessageType.DimensionRead    or
                             OpenNettyMessageType.DimensionRequest or
                             OpenNettyMessageType.DimensionSet);

        List<OpenNettyParameter> parameters = [];

        if (type is OpenNettyMessageType.DimensionSet)
        {
            parameters.Add(OpenNettyParameter.Empty);
        }

        parameters.AddRange(dimension.ToParameters());

        return new OpenNettyField(parameters);
    }

    /// <summary>
    /// Determines whether two <see cref="OpenNettyMessage"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettyMessage? left, OpenNettyMessage? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettyMessage"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettyMessage? left, OpenNettyMessage? right) => !(left == right);
}
