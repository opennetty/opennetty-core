/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyMessageTests
{
    private static readonly OpenNettyAddress NitooAddress = new(OpenNettyAddressType.Nitoo, "17");
    private static readonly OpenNettyAddress ZigbeeAddress = new(OpenNettyAddressType.Zigbee, "2601");
    private static readonly OpenNettyAddress LightPointAddress = new(OpenNettyAddressType.ScsLightPoint, "11");

    private static readonly OpenNettyMessageType[] CreatableTypes =
    [
        OpenNettyMessageType.BusCommand,
        OpenNettyMessageType.StatusRequest,
        OpenNettyMessageType.DimensionRequest,
        OpenNettyMessageType.DimensionRead,
        OpenNettyMessageType.DimensionSet
    ];

    [Fact]
    public void CreateFromFrame_NullString_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, (string) null!));

        Assert.Equal("frame", exception.ParamName);
    }

    [Fact]
    public void CreateFromFrame_EmptyString_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, string.Empty));

        Assert.Equal("frame", exception.ParamName);
    }

    [Fact]
    public void CreateFromFrame_MalformedString_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "1*1*11##"));

        Assert.Equal("buffer", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0001), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateFromFrame_UndefinedProtocolWithString_ThrowsInvalidEnumArgumentException()
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => OpenNettyMessage.CreateFromFrame((OpenNettyProtocol) 42, "*#*1##"));

        Assert.Equal("protocol", exception.ParamName);
    }

    [Fact]
    public void CreateFromFrame_UndefinedProtocolWithFrame_ThrowsInvalidEnumArgumentException()
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(
            () => OpenNettyMessage.CreateFromFrame((OpenNettyProtocol) 42, OpenNettyFrames.Acknowledgement));

        Assert.Equal("protocol", exception.ParamName);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void CreateFromFrame_StringAndFrame_ReturnEqualMessages(OpenNettyProtocol protocol)
    {
        var frame = OpenNettyFrame.Parse("*#*1##");

        var fromString = OpenNettyMessage.CreateFromFrame(protocol, "*#*1##");
        var fromFrame = OpenNettyMessage.CreateFromFrame(protocol, frame);

        Assert.Equal(fromFrame, fromString);
        Assert.Equal(frame, fromString.Frame);
        Assert.Equal(protocol, fromString.Protocol);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, "*#*1##", OpenNettyMessageType.Acknowledgement)]
    [InlineData(OpenNettyProtocol.Nitoo, "*#*1##", OpenNettyMessageType.Acknowledgement)]
    [InlineData(OpenNettyProtocol.Zigbee, "*#*1##", OpenNettyMessageType.Acknowledgement)]
    [InlineData(OpenNettyProtocol.Scs, "*#*0##", OpenNettyMessageType.NegativeAcknowledgement)]
    [InlineData(OpenNettyProtocol.Nitoo, "*#*0##", OpenNettyMessageType.NegativeAcknowledgement)]
    [InlineData(OpenNettyProtocol.Zigbee, "*#*0##", OpenNettyMessageType.NegativeAcknowledgement)]
    [InlineData(OpenNettyProtocol.Zigbee, "*#*6##", OpenNettyMessageType.BusyNegativeAcknowledgement)]
    public void CreateFromFrame_AcknowledgementFrame_ReturnsTheMatchingMessageType(
        OpenNettyProtocol protocol, string frame, OpenNettyMessageType expected)
    {
        var message = OpenNettyMessage.CreateFromFrame(protocol, frame);

        Assert.Equal(expected, message.Type);
        AssertHasNoDetails(message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    public void CreateFromFrame_BusyNegativeAcknowledgementOutsideZigbee_ThrowsInvalidOperationException(OpenNettyProtocol protocol)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyMessage.CreateFromFrame(protocol, "*#*6##"));

        Assert.Equal(SR.GetResourceString(SR.ID0058), exception.Message);
    }

    [Theory]
    [InlineData("*#1*11##", OpenNettyMessageType.StatusRequest)]
    [InlineData("*1*1*11##", OpenNettyMessageType.BusCommand)]
    [InlineData("*#1*11*1##", OpenNettyMessageType.DimensionRequest)]
    [InlineData("*#1*11*1*10##", OpenNettyMessageType.DimensionRead)]
    [InlineData("*#1*11*1*10*20##", OpenNettyMessageType.DimensionRead)]
    [InlineData("*#1*11*#1*10##", OpenNettyMessageType.DimensionSet)]
    [InlineData("*#1*11*#1*10*20##", OpenNettyMessageType.DimensionSet)]
    public void CreateFromFrame_FrameWithKnownStructure_ReturnsTheMatchingMessageType(string frame, OpenNettyMessageType expected)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, frame);

        Assert.Equal(expected, message.Type);
    }

    [Theory]
    [InlineData("*1##")]
    [InlineData("*1*1##")]
    [InlineData("*1*1*1*1##")]
    [InlineData("*#*5##")]
    [InlineData("*#*1*1##")]
    [InlineData("*#*#1##")]
    [InlineData("*#1*11*#1##")]
    [InlineData("*#1#2*11##")]
    [InlineData("*1#2*1*11##")]
    public void CreateFromFrame_FrameWithUnknownStructure_ReturnsAnUnknownMessage(string frame)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, frame);

        Assert.Equal(OpenNettyMessageType.Unknown, message.Type);
        AssertHasNoDetails(message);
        Assert.Equal(OpenNettyFrame.Parse(frame), message.Frame);
    }

    [Fact]
    public void CreateFromFrame_StatusRequest_ExposesCategoryAndAddress()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11##");

                Assert.Equal(OpenNettyCategories.Lighting, message.Category!.Value);
        Assert.Equal(LightPointAddress, message.Address);
        Assert.Equal(OpenNettyMedium.Bus, message.Medium);
        Assert.Null(message.Mode);
        Assert.Null(message.Command);
        Assert.Null(message.Dimension);
        Assert.Empty(message.Values);
    }

    [Fact]
    public void CreateFromFrame_BusCommand_ExposesCategoryCommandAndAddress()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");

                Assert.Equal(OpenNettyCategories.Lighting, message.Category!.Value);
        Assert.Equal(new OpenNettyCommand(OpenNettyCategories.Lighting, "1"), message.Command);
        Assert.Equal(LightPointAddress, message.Address);
        Assert.Equal(OpenNettyMedium.Bus, message.Medium);
        Assert.Null(message.Mode);
        Assert.Null(message.Dimension);
        Assert.Empty(message.Values);
    }

    [Fact]
    public void CreateFromFrame_BusCommandWithParameters_ExposesTheCommandParameters()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1#5#6*11##");

        Assert.Equal(new OpenNettyCommand(OpenNettyCategories.Lighting, "1", ["5", "6"]), message.Command);
    }

    [Fact]
    public void CreateFromFrame_DimensionRequest_ExposesCategoryDimensionAndAddress()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*1##");

                Assert.Equal(OpenNettyCategories.Lighting, message.Category!.Value);
        Assert.Equal(new OpenNettyDimension(OpenNettyCategories.Lighting, "1"), message.Dimension);
        Assert.Equal(LightPointAddress, message.Address);
        Assert.Null(message.Command);
        Assert.Empty(message.Values);
    }

    [Fact]
    public void CreateFromFrame_DimensionRequestWithParameters_ExposesTheDimensionParameters()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*1#7#8##");

        Assert.Equal(new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["7", "8"]), message.Dimension);
    }

    [Fact]
    public void CreateFromFrame_DimensionRead_ExposesDimensionAndValues()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*1*10*20##");

                Assert.Equal(OpenNettyCategories.Lighting, message.Category!.Value);
        Assert.Equal(new OpenNettyDimension(OpenNettyCategories.Lighting, "1"), message.Dimension);
        Assert.Equal(LightPointAddress, message.Address);
        Assert.Equal(["10", "20"], message.Values, StringComparer.Ordinal);
    }

    [Fact]
    public void CreateFromFrame_DimensionReadWithParameters_ExposesTheDimensionParameters()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*1#7*10##");

        Assert.Equal(new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["7"]), message.Dimension);
    }

    [Fact]
    public void CreateFromFrame_DimensionSet_ExposesDimensionAndValues()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*#1*10*20##");

                Assert.Equal(OpenNettyCategories.Lighting, message.Category!.Value);
        Assert.Equal(new OpenNettyDimension(OpenNettyCategories.Lighting, "1"), message.Dimension);
        Assert.Equal(LightPointAddress, message.Address);
        Assert.Equal(["10", "20"], message.Values, StringComparer.Ordinal);
    }

    [Fact]
    public void CreateFromFrame_NonDimensionMessages_HaveNoValues()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");

        Assert.True(message.Values.IsEmpty);
    }

    [Theory]
    [InlineData("*1*1*11##", OpenNettyAddressType.ScsLightPoint, "11")]
    [InlineData("*2*1*11##", OpenNettyAddressType.ScsLightPoint, "11")]
    [InlineData("*15*1*11##", OpenNettyAddressType.ScsLightPoint, "11")]
    [InlineData("*25*1*2105##", OpenNettyAddressType.ScsScenarioPlus, "2105")]
    [InlineData("*25*1*310##", OpenNettyAddressType.ScsDryContact, "310")]
    [InlineData("*25*1*110##", OpenNettyAddressType.Unknown, "110")]
    [InlineData("*4*1*5##", OpenNettyAddressType.Unknown, "5")]
    [InlineData("*13*0*1##", OpenNettyAddressType.Unknown, "1")]
    public void CreateFromFrame_ScsWhereField_InfersTheAddressTypeFromTheCategory(string frame, OpenNettyAddressType type, string value)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, frame);

        Assert.Equal(new OpenNettyAddress(type, value), message.Address);
        Assert.Equal(OpenNettyMedium.Bus, message.Medium);
        Assert.Null(message.Mode);
    }

    [Theory]
    [InlineData("*1*1*0#4#02##", OpenNettyAddressType.ScsLightPoint, "0", new[] { "4", "02" })]
    [InlineData("*1*1*#5##", OpenNettyAddressType.ScsLightPoint, "", new[] { "5" })]
    [InlineData("*25*1*21#4#02##", OpenNettyAddressType.Unknown, "21", new[] { "4", "02" })]
    public void CreateFromFrame_ScsWhereFieldWithParameters_ExposesTheAddressParameters(
        string frame, OpenNettyAddressType type, string value, string[] parameters)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, frame);

        Assert.Equal(new OpenNettyAddress(type, value, [.. parameters]), message.Address);
    }

    [Fact]
    public void CreateFromFrame_ScsEmptyWhereField_DoesNotExposeAnAddress()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*13*0*##");

        Assert.Equal(OpenNettyMessageType.BusCommand, message.Type);
        Assert.Null(message.Address);
        Assert.Null(message.Medium);
        Assert.Null(message.Mode);
    }

    [Theory]
    [InlineData("17", OpenNettyMode.Unicast, OpenNettyMedium.Powerline)]
    [InlineData("17#0", OpenNettyMode.Unicast, OpenNettyMedium.Powerline)]
    [InlineData("17#1", OpenNettyMode.Unicast, OpenNettyMedium.Radio)]
    [InlineData("17#2", OpenNettyMode.Unicast, OpenNettyMedium.Infrared)]
    [InlineData("#17", OpenNettyMode.Multicast, OpenNettyMedium.Powerline)]
    [InlineData("#17#0", OpenNettyMode.Multicast, OpenNettyMedium.Powerline)]
    [InlineData("#17#1", OpenNettyMode.Multicast, OpenNettyMedium.Radio)]
    [InlineData("#17#2", OpenNettyMode.Multicast, OpenNettyMedium.Infrared)]
    [InlineData("0#17", OpenNettyMode.Broadcast, OpenNettyMedium.Powerline)]
    [InlineData("0#17#0", OpenNettyMode.Broadcast, OpenNettyMedium.Powerline)]
    [InlineData("0#17#1", OpenNettyMode.Broadcast, OpenNettyMedium.Radio)]
    [InlineData("0#17#2", OpenNettyMode.Broadcast, OpenNettyMedium.Infrared)]
    public void CreateFromFrame_NitooWhereField_ExposesAddressModeAndMedium(string where, OpenNettyMode mode, OpenNettyMedium medium)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, $"*1*1*{where}##");

        Assert.Equal(NitooAddress, message.Address);
        Assert.Equal(mode, message.Mode);
        Assert.Equal(medium, message.Medium);
    }

    [Fact]
    public void CreateFromFrame_NitooEmptyWhereField_DoesNotExposeAnAddress()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, "*13*0*##");

        Assert.Null(message.Address);
        Assert.Null(message.Medium);
        Assert.Null(message.Mode);
    }

    [Fact]
    public void CreateFromFrame_NitooWhereFieldWithEmptyAddress_ExposesTheModeWithoutAddress()
    {
        var frame = new OpenNettyFrame(new OpenNettyField("1"), new OpenNettyField("1"), new OpenNettyField("0", ""));

        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, frame);

        Assert.Null(message.Address);
        Assert.Equal(OpenNettyMode.Broadcast, message.Mode);
        Assert.Equal(OpenNettyMedium.Powerline, message.Medium);
    }

    [Theory]
    [InlineData("17#3")]
    [InlineData("0#17#3")]
    [InlineData("#17#3")]
    [InlineData("17#1#1")]
    [InlineData("99999999999")]
    public void CreateFromFrame_MalformedNitooWhereField_ThrowsInvalidOperationException(string where)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, $"*1*1*{where}##"));

        Assert.Equal(SR.GetResourceString(SR.ID0059), exception.Message);
    }

    [Theory]
    [InlineData("2601#9", OpenNettyMode.Unicast, "2601")]
    [InlineData("#2601#9", OpenNettyMode.Multicast, "2601")]
    [InlineData("0#2601#9", OpenNettyMode.Broadcast, "2601")]
    [InlineData("01#9", OpenNettyMode.Unicast, "01")]
    [InlineData("00#9", OpenNettyMode.Unicast, "00")]
    [InlineData("2600#9", OpenNettyMode.Unicast, "2600")]
    [InlineData("#01#9", OpenNettyMode.Multicast, "01")]
    [InlineData("0#01#9", OpenNettyMode.Broadcast, "01")]
    public void CreateFromFrame_ZigbeeWhereField_ExposesAddressModeAndMedium(string where, OpenNettyMode mode, string address)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, $"*1*1*{where}##");

        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Zigbee, address), message.Address);
        Assert.Equal(mode, message.Mode);
        Assert.Equal(OpenNettyMedium.Radio, message.Medium);
    }

    [Fact]
    public void CreateFromFrame_ZigbeeEmptyWhereField_DoesNotExposeAnAddress()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, "*13*0*##");

        Assert.Null(message.Address);
        Assert.Null(message.Medium);
        Assert.Null(message.Mode);
    }

    [Fact]
    public void CreateFromFrame_ZigbeeWhereFieldWithEmptyAddress_ExposesTheModeWithoutAddress()
    {
        var frame = new OpenNettyFrame(new OpenNettyField("1"), new OpenNettyField("1"), new OpenNettyField("0", "", "9"));

        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, frame);

        Assert.Null(message.Address);
        Assert.Equal(OpenNettyMode.Broadcast, message.Mode);
        Assert.Equal(OpenNettyMedium.Radio, message.Medium);
    }

    [Theory]
    [InlineData("2601")]
    [InlineData("2601#8")]
    [InlineData("0#2601#8")]
    [InlineData("0#2601")]
    [InlineData("5#9")]
    [InlineData("99999999999999#9")]
    [InlineData("0#2601#9#9")]
    public void CreateFromFrame_MalformedZigbeeWhereField_ThrowsInvalidOperationException(string where)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, $"*1*1*{where}##"));

        Assert.Equal(SR.GetResourceString(SR.ID0059), exception.Message);
    }

    public static TheoryData<OpenNettyMessageType> MessageTypes => new(CreatableTypes);

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_UndefinedProtocol_ThrowsInvalidEnumArgumentException(OpenNettyMessageType type)
    {
        var exception = Assert.Throws<InvalidEnumArgumentException>(() => Create(type, (OpenNettyProtocol) 42));

        Assert.Equal("protocol", exception.ParamName);
    }

    public static TheoryData<OpenNettyMessageType, OpenNettyProtocol> MessageTypesAndProtocols
    {
        get
        {
            var data = new TheoryData<OpenNettyMessageType, OpenNettyProtocol>();

            foreach (var type in CreatableTypes)
            {
                foreach (var protocol in Enum.GetValues<OpenNettyProtocol>())
                {
                    data.Add(type, protocol);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MessageTypesAndProtocols))]
    public void Create_WithoutAddress_ProducesAnEmptyWhereField(OpenNettyMessageType type, OpenNettyProtocol protocol)
    {
        var message = Create(type, protocol);

        Assert.Equal(Frame(type, "1", string.Empty), message.ToString());
        Assert.Null(message.Address);
        Assert.Null(message.Medium);
        Assert.Null(message.Mode);
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ScsLightPointAddress_ProducesTheMatchingFrame(OpenNettyMessageType type)
    {
        var message = Create(type, OpenNettyProtocol.Scs, LightPointAddress);

        Assert.Equal(Frame(type, "1", "11"), message.ToString());
        Assert.Equal(LightPointAddress, message.Address);
        Assert.Equal(OpenNettyMedium.Bus, message.Medium);
        Assert.Null(message.Mode);
        Assert.Equal(type, message.Type);
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ScsLightPointAddressWithExtension_AppendsTheAddressParameters(OpenNettyMessageType type)
    {
        var address = OpenNettyAddress.FromScsLightPointAddress(extension: 2, general: true, group: null, area: null, point: null);

        var message = Create(type, OpenNettyProtocol.Scs, address);

        Assert.Equal(Frame(type, "1", "0#4#02"), message.ToString());
        Assert.Equal(address, message.Address);
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ScsWithMediumAndMode_IgnoresThem(OpenNettyMessageType type)
    {
        var message = Create(type, OpenNettyProtocol.Scs, LightPointAddress, OpenNettyMedium.Radio, OpenNettyMode.Multicast);

        Assert.Equal(Frame(type, "1", "11"), message.ToString());
        Assert.Equal(OpenNettyMedium.Bus, message.Medium);
        Assert.Null(message.Mode);
    }

    public static TheoryData<OpenNettyMessageType, string> MessageTypesAndCategories
    {
        get
        {
            var data = new TheoryData<OpenNettyMessageType, string>();

            foreach (var type in CreatableTypes)
            {
                foreach (var category in new[] { "1", "2", "15" })
                {
                    data.Add(type, category);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MessageTypesAndCategories))]
    public void Create_ScsLightingAutomationOrScenariosCategoryWithAnotherAddressType_ThrowsInvalidOperationException(OpenNettyMessageType type, string category)
    {
        foreach (var address in new[] { NitooAddress, ZigbeeAddress, OpenNettyAddress.FromScsScenarioPlusAddress(5) })
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => Create(type, OpenNettyProtocol.Scs, address, category: category));

            Assert.Equal(SR.GetResourceString(SR.ID0050), exception.Message);
        }
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ScsScenariosPlusCategoryWithAnotherAddressType_ThrowsInvalidOperationException(OpenNettyMessageType type)
    {
        foreach (var address in new[] { LightPointAddress, NitooAddress, ZigbeeAddress })
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => Create(type, OpenNettyProtocol.Scs, address, category: "25"));

            Assert.Equal(SR.GetResourceString(SR.ID0114), exception.Message);
        }
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ScsScenariosPlusCategoryWithDryContactAddress_ProducesTheMatchingFrame(OpenNettyMessageType type)
    {
        var address = OpenNettyAddress.FromScsDryContactAddress(10);

        var message = Create(type, OpenNettyProtocol.Scs, address, category: "25");

        Assert.Equal(Frame(type, "25", "310"), message.ToString());
        Assert.Equal(address, message.Address);
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ScsScenariosPlusCategoryWithScenarioPlusAddress_ProducesTheMatchingFrame(OpenNettyMessageType type)
    {
        var address = OpenNettyAddress.FromScsScenarioPlusAddress(5);

        var message = Create(type, OpenNettyProtocol.Scs, address, category: "25");

        Assert.Equal(Frame(type, "25", "25"), message.ToString());
        Assert.Equal(address, message.Address);
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ScsOtherCategoryWithAnyAddressType_ProducesTheMatchingFrame(OpenNettyMessageType type)
    {
        var message = Create(type, OpenNettyProtocol.Scs, NitooAddress, category: "4");

        Assert.Equal(Frame(type, "4", "17"), message.ToString());
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Unknown, "17"), message.Address);
    }

    public static TheoryData<OpenNettyMessageType, OpenNettyMode?, OpenNettyMedium?, string> NitooWhereFields
    {
        get
        {
            var data = new TheoryData<OpenNettyMessageType, OpenNettyMode?, OpenNettyMedium?, string>();

            foreach (var type in CreatableTypes)
            {
                data.Add(type, null, null, "17");
                data.Add(type, null, OpenNettyMedium.Powerline, "17");
                data.Add(type, null, OpenNettyMedium.Radio, "17#1");
                data.Add(type, null, OpenNettyMedium.Infrared, "17#2");
                data.Add(type, OpenNettyMode.Unicast, null, "17");
                data.Add(type, OpenNettyMode.Unicast, OpenNettyMedium.Radio, "17#1");
                data.Add(type, OpenNettyMode.Multicast, null, "#17");
                data.Add(type, OpenNettyMode.Multicast, OpenNettyMedium.Radio, "#17#1");
                data.Add(type, OpenNettyMode.Broadcast, null, "0#17");
                data.Add(type, OpenNettyMode.Broadcast, OpenNettyMedium.Infrared, "0#17#2");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(NitooWhereFields))]
    public void Create_NitooAddress_AppliesTheModeAndMedium(OpenNettyMessageType type, OpenNettyMode? mode, OpenNettyMedium? medium, string where)
    {
        var message = Create(type, OpenNettyProtocol.Nitoo, NitooAddress, medium, mode);

        Assert.Equal(Frame(type, "1", where), message.ToString());
        Assert.Equal(NitooAddress, message.Address);
        Assert.Equal(mode ?? OpenNettyMode.Unicast, message.Mode);
        Assert.Equal(medium ?? OpenNettyMedium.Powerline, message.Medium);
    }

    [Theory]
    [InlineData(null, "0#17", OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Broadcast, "0#17", OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Multicast, "#17", OpenNettyMode.Multicast)]
    [InlineData(OpenNettyMode.Unicast, "17", OpenNettyMode.Unicast)]
    public void Create_NitooScenariosPlusCategory_UsesTheBroadcastModeByDefault(OpenNettyMode? mode, string where, OpenNettyMode expected)
    {
        var message = Create(OpenNettyMessageType.BusCommand, OpenNettyProtocol.Nitoo, NitooAddress, mode: mode, category: "25");

        Assert.Equal(Frame(OpenNettyMessageType.BusCommand, "25", where), message.ToString());
        Assert.Equal(expected, message.Mode);
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_NitooWithNonNitooAddress_ThrowsInvalidOperationException(OpenNettyMessageType type)
    {
        foreach (var address in new[] { LightPointAddress, ZigbeeAddress })
        {
            var exception = Assert.Throws<InvalidOperationException>(() => Create(type, OpenNettyProtocol.Nitoo, address));

            Assert.Equal(SR.GetResourceString(SR.ID0048), exception.Message);
        }
    }

    [Theory]
    [InlineData(OpenNettyMedium.Bus)]
    [InlineData((OpenNettyMedium) 42)]
    public void Create_NitooWithUnsupportedMedium_ThrowsInvalidOperationException(OpenNettyMedium medium)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Create(OpenNettyMessageType.BusCommand, OpenNettyProtocol.Nitoo, NitooAddress, medium));

        Assert.Equal(SR.GetResourceString(SR.ID0062), exception.Message);
    }

    public static TheoryData<OpenNettyMessageType, OpenNettyMode?, OpenNettyMedium?, string> ZigbeeWhereFields
    {
        get
        {
            var data = new TheoryData<OpenNettyMessageType, OpenNettyMode?, OpenNettyMedium?, string>();

            foreach (var type in CreatableTypes)
            {
                data.Add(type, null, null, "2601#9");
                data.Add(type, null, OpenNettyMedium.Radio, "2601#9");
                data.Add(type, OpenNettyMode.Unicast, null, "2601#9");
                data.Add(type, OpenNettyMode.Multicast, null, "#2601#9");
                data.Add(type, OpenNettyMode.Broadcast, OpenNettyMedium.Radio, "0#2601#9");
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ZigbeeWhereFields))]
    public void Create_ZigbeeAddress_AppliesTheMode(OpenNettyMessageType type, OpenNettyMode? mode, OpenNettyMedium? medium, string where)
    {
        var message = Create(type, OpenNettyProtocol.Zigbee, ZigbeeAddress, medium, mode);

        Assert.Equal(Frame(type, "1", where), message.ToString());
        Assert.Equal(ZigbeeAddress, message.Address);
        Assert.Equal(mode ?? OpenNettyMode.Unicast, message.Mode);
        Assert.Equal(OpenNettyMedium.Radio, message.Medium);
    }

    [Theory]
    [InlineData(null, "0#01#9", OpenNettyMode.Broadcast)]
    [InlineData(OpenNettyMode.Unicast, "01#9", OpenNettyMode.Unicast)]
    [InlineData(OpenNettyMode.Multicast, "#01#9", OpenNettyMode.Multicast)]
    [InlineData(OpenNettyMode.Broadcast, "0#01#9", OpenNettyMode.Broadcast)]
    public void Create_ZigbeeAddressWithoutDeviceIdentifier_UsesTheBroadcastModeByDefault(OpenNettyMode? mode, string where, OpenNettyMode expected)
    {
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(0, 1);

        var message = Create(OpenNettyMessageType.BusCommand, OpenNettyProtocol.Zigbee, address, mode: mode);

        Assert.Equal(Frame(OpenNettyMessageType.BusCommand, "1", where), message.ToString());
        Assert.Equal(address, message.Address);
        Assert.Equal(expected, message.Mode);
    }

    [Theory]
    [MemberData(nameof(MessageTypes))]
    public void Create_ZigbeeWithNonZigbeeAddress_ThrowsInvalidOperationException(OpenNettyMessageType type)
    {
        foreach (var address in new[] { LightPointAddress, NitooAddress })
        {
            var exception = Assert.Throws<InvalidOperationException>(() => Create(type, OpenNettyProtocol.Zigbee, address));

            Assert.Equal(SR.GetResourceString(SR.ID0055), exception.Message);
        }
    }

    [Theory]
    [InlineData(OpenNettyMedium.Bus)]
    [InlineData(OpenNettyMedium.Infrared)]
    [InlineData(OpenNettyMedium.Powerline)]
    public void Create_ZigbeeWithUnsupportedMedium_ThrowsInvalidOperationException(OpenNettyMedium medium)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Create(OpenNettyMessageType.BusCommand, OpenNettyProtocol.Zigbee, ZigbeeAddress, medium));

        Assert.Equal(SR.GetResourceString(SR.ID0062), exception.Message);
    }

    [Fact]
    public void CreateCommand_CommandWithParameters_AppendsTheParametersToTheWhatField()
    {
        var command = new OpenNettyCommand(OpenNettyCategories.Lighting, "1", ["5", "6"]);

        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, command, LightPointAddress);

        Assert.Equal("*1*1#5#6*11##", message.ToString());
        Assert.Equal(OpenNettyMessageType.BusCommand, message.Type);
        Assert.Equal(command, message.Command);
    }

    [Fact]
    public void CreateCommand_ReturnsAMessageEqualToTheParsedFrame()
    {
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs,
            new OpenNettyCommand(OpenNettyCategories.Lighting, "1"), LightPointAddress);

        Assert.Equal(OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##"), message);
    }

    [Fact]
    public void CreateStatusRequest_ProducesAStatusRequestMessage()
    {
        var message = OpenNettyMessage.CreateStatusRequest(OpenNettyProtocol.Scs, OpenNettyCategories.Lighting, LightPointAddress);

        Assert.Equal("*#1*11##", message.ToString());
        Assert.Equal(OpenNettyMessageType.StatusRequest, message.Type);
        Assert.Equal(OpenNettyCategories.Lighting, message.Category!.Value);
        Assert.Null(message.Command);
        Assert.Null(message.Dimension);
    }

    [Fact]
    public void CreateDimensionRequest_ProducesADimensionRequestMessage()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        var message = OpenNettyMessage.CreateDimensionRequest(OpenNettyProtocol.Scs, dimension, LightPointAddress);

        Assert.Equal("*#1*11*1##", message.ToString());
        Assert.Equal(OpenNettyMessageType.DimensionRequest, message.Type);
        Assert.Equal(dimension, message.Dimension);
        Assert.Empty(message.Values);
    }

    [Fact]
    public void CreateDimensionRequest_DimensionWithParameters_AppendsTheParametersToTheDimensionField()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["7", "8"]);

        var message = OpenNettyMessage.CreateDimensionRequest(OpenNettyProtocol.Scs, dimension, LightPointAddress);

        Assert.Equal("*#1*11*1#7#8##", message.ToString());
        Assert.Equal(dimension, message.Dimension);
    }

    [Fact]
    public void CreateDimensionRead_ProducesADimensionReadMessageWithOneFieldPerValue()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        var message = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, dimension, ["10", "20", "30"], LightPointAddress);

        Assert.Equal("*#1*11*1*10*20*30##", message.ToString());
        Assert.Equal(OpenNettyMessageType.DimensionRead, message.Type);
        Assert.Equal(dimension, message.Dimension);
        Assert.Equal(["10", "20", "30"], message.Values, StringComparer.Ordinal);
    }

    [Fact]
    public void CreateDimensionRead_DimensionWithParameters_AppendsTheParametersToTheDimensionField()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["7"]);

        var message = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, dimension, ["10"], LightPointAddress);

        Assert.Equal("*#1*11*1#7*10##", message.ToString());
        Assert.Equal(dimension, message.Dimension);
    }

    [Fact]
    public void CreateDimensionRead_NoValue_ThrowsInvalidOperationException()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, dimension, [], LightPointAddress));

        Assert.Equal(SR.GetResourceString(SR.ID0060), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettyMessageType.DimensionRead)]
    [InlineData(OpenNettyMessageType.DimensionSet)]
    public void Create_NonNumericValue_ThrowsArgumentException(OpenNettyMessageType type)
    {
        var exception = Assert.Throws<ArgumentException>(() => Create(type, OpenNettyProtocol.Scs, LightPointAddress, values: ImmutableArray.Create("abc")));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDimensionSet_ProducesADimensionSetMessageWithOneFieldPerValue()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        var message = OpenNettyMessage.CreateDimensionSet(OpenNettyProtocol.Scs, dimension, ["10", "20", "30"], LightPointAddress);

        Assert.Equal("*#1*11*#1*10*20*30##", message.ToString());
        Assert.Equal(OpenNettyMessageType.DimensionSet, message.Type);
        Assert.Equal(dimension, message.Dimension);
        Assert.Equal(["10", "20", "30"], message.Values, StringComparer.Ordinal);
    }

    [Fact]
    public void CreateDimensionSet_DimensionWithParameters_AppendsTheParametersToTheDimensionField()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["7"]);

        var message = OpenNettyMessage.CreateDimensionSet(OpenNettyProtocol.Scs, dimension, ["10"], LightPointAddress);

        Assert.Equal("*#1*11*#1#7*10##", message.ToString());
    }

    [Fact]
    public void CreateDimensionSet_DimensionWithParameters_ExposesTheOriginalDimension()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1", ["7"]);

        var message = OpenNettyMessage.CreateDimensionSet(OpenNettyProtocol.Scs, dimension, ["10"], LightPointAddress);

        Assert.Equal(dimension, message.Dimension);
    }

    [Fact]
    public void CreateDimensionSet_NoValue_ThrowsInvalidOperationException()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyMessage.CreateDimensionSet(OpenNettyProtocol.Scs, dimension, [], LightPointAddress));

        Assert.Equal(SR.GetResourceString(SR.ID0061), exception.Message);
    }

    [Fact]
    public void CreateDimensionSet_DefaultValues_ThrowsInvalidOperationException()
    {
        var dimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "1");

        var exception = Assert.Throws<InvalidOperationException>(
            () => OpenNettyMessage.CreateDimensionSet(OpenNettyProtocol.Scs, dimension, default, LightPointAddress));

        Assert.Equal(SR.GetResourceString(SR.ID0061), exception.Message);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");

        var sameReference = message;

        Assert.True(message.Equals(message));
        Assert.True(message == sameReference);
    }

    [Fact]
    public void Equals_SameProtocolAndFrame_ReturnsTrue()
    {
        var first = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");
        var second = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object) second));
        Assert.True(first == second);
        Assert.False(first != second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentFrame_ReturnsFalse()
    {
        var first = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");
        var second = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*0*11##");

        Assert.False(first.Equals(second));
        Assert.False(first.Equals((object) second));
        Assert.False(first == second);
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_DifferentProtocol_ReturnsFalse()
    {
        var first = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#*1##");
        var second = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, "*#*1##");

        Assert.False(first.Equals(second));
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");

        Assert.False(message.Equals(null));
        Assert.False(message.Equals((object?) null));
        Assert.False(message == null);
        Assert.False(null == message);
        Assert.True(message != null);
    }

    [Fact]
    public void Equals_OtherObjectType_ReturnsFalse()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*11##");

        Assert.False(message.Equals("*1*1*11##"));
    }

    [Theory]
    [InlineData("*#*1##")]
    [InlineData("*1*1*11##")]
    [InlineData("*#1*11*1*10*20##")]
    public void ToString_ReturnsTheRawFrame(string frame)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, frame);

        Assert.Equal(frame, message.ToString());
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, "*#*1##", """{"protocol":"scs","type":"acknowledgement"}""")]
    [InlineData(OpenNettyProtocol.Nitoo, "*#*0##", """{"protocol":"nitoo","type":"negative_acknowledgement"}""")]
    [InlineData(OpenNettyProtocol.Zigbee, "*#*6##", """{"protocol":"zigbee","type":"busy_negative_acknowledgement"}""")]
    [InlineData(OpenNettyProtocol.Scs, "*1##", """{"protocol":"scs","type":"unknown"}""")]
    public void ToJsonObject_MessageWithoutDetails_OnlyContainsTheProtocolAndType(OpenNettyProtocol protocol, string frame, string expected)
    {
        var message = OpenNettyMessage.CreateFromFrame(protocol, frame);

        AssertJson(expected, message);
    }

    [Fact]
    public void ToJsonObject_StatusRequest_ContainsTheCategoryAndAddress()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11##");

        AssertJson(
            """
            {"protocol":"scs","type":"status_request","medium":"bus",
             "address":{"value":"11","type":"scs_light_point","area":1,"extension":0,"general":false,"group":null,"point":1},
             "category":{"value":"1"}}
            """, message);
    }

    [Fact]
    public void ToJsonObject_BusCommand_ContainsTheCommandWithItsParameters()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1#5#6*0##");

        AssertJson(
            """
            {"protocol":"scs","type":"bus_command","medium":"bus",
             "address":{"value":"0","type":"scs_light_point","area":null,"extension":0,"general":true,"group":null,"point":null},
             "category":{"value":"1"},
             "command":{"value":"1","parameters":["5","6"]}}
            """, message);
    }

    [Fact]
    public void ToJsonObject_AddressWithParameters_ContainsTheAddressParameters()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*0#4#02##");

        AssertJson(
            """
            {"protocol":"scs","type":"bus_command","medium":"bus",
             "address":{"value":"0","type":"scs_light_point","parameters":["4","02"],"area":null,"extension":2,"general":true,"group":null,"point":null},
             "category":{"value":"1"},
             "command":{"value":"1"}}
            """, message);
    }

    [Fact]
    public void ToJsonObject_ScsDryContactAddress_ContainsTheContact()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*25*21*310##");

        AssertJson(
            """
            {"protocol":"scs","type":"bus_command","medium":"bus",
             "address":{"value":"310","type":"scs_dry_contact","contact":10},
             "category":{"value":"25"},
             "command":{"value":"21"}}
            """, message);
    }

    [Fact]
    public void ToJsonObject_ScsScenarioPlusAddress_ContainsTheObject()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*25*21*210##");

        AssertJson(
            """
            {"protocol":"scs","type":"bus_command","medium":"bus",
             "address":{"value":"210","type":"scs_scenario_plus","object":10},
             "category":{"value":"25"},
             "command":{"value":"21"}}
            """, message);
    }

    [Fact]
    public void ToJsonObject_UnknownAddressType_OnlyContainsTheValue()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*4*1*5##");

        AssertJson(
            """
            {"protocol":"scs","type":"bus_command","medium":"bus",
             "address":{"value":"5","type":"unknown"},
             "category":{"value":"4"},
             "command":{"value":"1"}}
            """, message);
    }

    [Theory]
    [InlineData("17", "powerline", "unicast")]
    [InlineData("17#1", "radio", "unicast")]
    [InlineData("#17#1", "radio", "multicast")]
    [InlineData("0#17#2", "infrared", "broadcast")]
    public void ToJsonObject_NitooMessage_ContainsTheIdentifierUnitMediumAndMode(string where, string medium, string mode)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, $"*1*1*{where}##");

        AssertJson(
            $$"""
            {"protocol":"nitoo","type":"bus_command","medium":"{{medium}}","mode":"{{mode}}",
             "address":{"value":"17","type":"nitoo","identifier":1,"unit":1},
             "category":{"value":"1"},
             "command":{"value":"1"} }
            """, message);
    }

    [Fact]
    public void ToJsonObject_ZigbeeMessage_ContainsTheIdentifierUnitMediumAndMode()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, "*1*1*2601#9##");

        AssertJson(
            """
            {"protocol":"zigbee","type":"bus_command","medium":"radio","mode":"unicast",
             "address":{"value":"2601","type":"zigbee","identifier":26,"unit":1},
             "category":{"value":"1"},
             "command":{"value":"1"}}
            """, message);
    }

    [Fact]
    public void ToJsonObject_MessageWithoutAddress_OmitsMediumAndMode()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*13*0*##");

        AssertJson(
            """
            {"protocol":"scs","type":"bus_command",
             "category":{"value":"13"},
             "command":{"value":"0"}}
            """, message);
    }

    [Fact]
    public void ToJsonObject_DimensionRequest_ContainsTheDimensionWithItsParameters()
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*1#7##");

        AssertJson(
            """
            {"protocol":"scs","type":"dimension_request","medium":"bus",
             "address":{"value":"11","type":"scs_light_point","area":1,"extension":0,"general":false,"group":null,"point":1},
             "category":{"value":"1"},
             "dimension":{"value":"1","parameters":["7"]}}
            """, message);
    }

    [Theory]
    [InlineData("*#1*11*1*10*20##", "dimension_read")]
    [InlineData("*#1*11*#1*10*20##", "dimension_set")]
    public void ToJsonObject_DimensionReadOrSet_ContainsTheDimensionAndValues(string frame, string type)
    {
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, frame);

        AssertJson(
            $$"""
            {"protocol":"scs","type":"{{type}}","medium":"bus",
             "address":{"value":"11","type":"scs_light_point","area":1,"extension":0,"general":false,"group":null,"point":1},
             "category":{"value":"1"},
             "dimension":{"value":"1"},
             "values":["10","20"]}
            """, message);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, "*#*1##")]
    [InlineData(OpenNettyProtocol.Nitoo, "*#*0##")]
    [InlineData(OpenNettyProtocol.Zigbee, "*#*6##")]
    [InlineData(OpenNettyProtocol.Scs, "*#1*11##")]
    [InlineData(OpenNettyProtocol.Scs, "*1*1#5*11##")]
    [InlineData(OpenNettyProtocol.Scs, "*1*1*0##")]
    [InlineData(OpenNettyProtocol.Scs, "*1*1*#5##")]
    [InlineData(OpenNettyProtocol.Scs, "*1*1*#5#4#02##")]
    [InlineData(OpenNettyProtocol.Scs, "*1*1*00##")]
    [InlineData(OpenNettyProtocol.Scs, "*1*1*100#4#02##")]
    [InlineData(OpenNettyProtocol.Scs, "*1*1*1012##")]
    [InlineData(OpenNettyProtocol.Scs, "*25*21*310##")]
    [InlineData(OpenNettyProtocol.Scs, "*25*21*210##")]
    [InlineData(OpenNettyProtocol.Scs, "*#1*11*1*10*20##")]
    [InlineData(OpenNettyProtocol.Scs, "*#1*11*1#7*10##")]
    [InlineData(OpenNettyProtocol.Scs, "*#1*11*1#7##")]
    [InlineData(OpenNettyProtocol.Scs, "*#1*11*#1*10##")]
    [InlineData(OpenNettyProtocol.Nitoo, "*1*1*17##")]
    [InlineData(OpenNettyProtocol.Nitoo, "*1*1*#17#1##")]
    [InlineData(OpenNettyProtocol.Nitoo, "*1*1*0#17#2##")]
    [InlineData(OpenNettyProtocol.Zigbee, "*1*1*2601#9##")]
    [InlineData(OpenNettyProtocol.Zigbee, "*1*1*#2601#9##")]
    [InlineData(OpenNettyProtocol.Zigbee, "*1*1*0#2601#9##")]
    public void CreateFromJsonObject_JsonProducedByToJsonObject_ReturnsAnEquivalentMessage(OpenNettyProtocol protocol, string frame)
    {
        var message = OpenNettyMessage.CreateFromFrame(protocol, frame);
        var json = message.ToJsonObject();

        var result = OpenNettyMessage.CreateFromJsonObject(json);

        Assert.Equal(message, result);
    }

    [Fact]
    public void CreateFromJsonObject_NullParameters_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyMessage.CreateFromJsonObject(null!));

        Assert.Equal("parameters", exception.ParamName);
    }

    [Theory]
    [InlineData("""{"type":"acknowledgement"}""")]
    [InlineData("""{"protocol":"unknown","type":"acknowledgement"}""")]
    [InlineData("""{"protocol":"SCS","type":"acknowledgement"}""")]
    [InlineData("""{"protocol":"scs"}""")]
    [InlineData("""{"protocol":"scs","type":"unknown"}""")]
    [InlineData("""{"protocol":"scs","type":"ACKNOWLEDGEMENT"}""")]
    public void CreateFromJsonObject_MissingOrUnsupportedProtocolOrType_ThrowsInvalidOperationException(string json)
    {
        AssertInvalidJson(json);
    }

    [Theory]
    [InlineData("scs", OpenNettyMessageType.Acknowledgement, "acknowledgement")]
    [InlineData("nitoo", OpenNettyMessageType.Acknowledgement, "acknowledgement")]
    [InlineData("zigbee", OpenNettyMessageType.Acknowledgement, "acknowledgement")]
    [InlineData("scs", OpenNettyMessageType.NegativeAcknowledgement, "negative_acknowledgement")]
    [InlineData("nitoo", OpenNettyMessageType.NegativeAcknowledgement, "negative_acknowledgement")]
    [InlineData("zigbee", OpenNettyMessageType.NegativeAcknowledgement, "negative_acknowledgement")]
    [InlineData("zigbee", OpenNettyMessageType.BusyNegativeAcknowledgement, "busy_negative_acknowledgement")]
    public void CreateFromJsonObject_AcknowledgementTypes_ReturnsTheMatchingMessage(string protocol, OpenNettyMessageType expected, string type)
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse($$"""{"protocol":"{{protocol}}","type":"{{type}}"}"""));

        Assert.Equal(expected, message.Type);
        Assert.Equal(Enum.Parse<OpenNettyProtocol>(protocol, ignoreCase: true), message.Protocol);
    }

    [Theory]
    [InlineData("scs")]
    [InlineData("nitoo")]
    public void CreateFromJsonObject_BusyNegativeAcknowledgementOutsideZigbee_ThrowsInvalidOperationException(string protocol)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyMessage.CreateFromJsonObject(
            Parse($$"""{"protocol":"{{protocol}}","type":"busy_negative_acknowledgement"}""")));

        Assert.Equal(SR.GetResourceString(SR.ID0058), exception.Message);
    }

    [Fact]
    public void CreateFromJsonObject_StatusRequest_ReturnsTheMatchingMessage()
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            """
            {"protocol":"nitoo","type":"status_request","medium":"radio","mode":"multicast",
             "address":{"type":"nitoo","identifier":1,"unit":1},
             "category":{"value":"1"}}
            """));

        Assert.Equal(OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, "*#1*#17#1##"), message);
    }

    [Fact]
    public void CreateFromJsonObject_BusCommand_ReturnsTheMatchingMessage()
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            """
            {"protocol":"zigbee","type":"bus_command","medium":"radio","mode":"broadcast",
             "address":{"type":"zigbee","identifier":26,"unit":1},
             "category":{"value":"1"},
             "command":{"value":"1","parameters":["5","6"]}}
            """));

        Assert.Equal(OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, "*1*1#5#6*0#2601#9##"), message);
    }

    [Fact]
    public void CreateFromJsonObject_DimensionRequest_ReturnsTheMatchingMessage()
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            """
            {"protocol":"scs","type":"dimension_request",
             "address":{"type":"scs_light_point","area":1,"point":1},
             "category":{"value":"1"},
             "dimension":{"value":"1","parameters":["7"]}}
            """));

        Assert.Equal(OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*1#7##"), message);
    }

    [Fact]
    public void CreateFromJsonObject_DimensionRead_ReturnsTheMatchingMessage()
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            """
            {"protocol":"scs","type":"dimension_read",
             "address":{"type":"scs_light_point","area":1,"point":1},
             "category":{"value":"1"},
             "dimension":{"value":"1"},
             "values":["10","20"]}
            """));

        Assert.Equal(OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*1*10*20##"), message);
    }

    [Fact]
    public void CreateFromJsonObject_DimensionSet_ReturnsTheMatchingMessage()
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            """
            {"protocol":"scs","type":"dimension_set",
             "address":{"type":"scs_light_point","area":1,"point":1},
             "category":{"value":"1"},
             "dimension":{"value":"1"},
             "values":["10","20"]}
            """));

        Assert.Equal(OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*#1*11*#1*10*20##"), message);
    }

    [Theory]
    [InlineData("""{"protocol":"scs","type":"bus_command","command":{"value":"1"}}""")]
    [InlineData("""{"protocol":"scs","type":"bus_command","category":{"value":"1"}}""")]
    [InlineData("""{"protocol":"scs","type":"status_request"}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_request","dimension":{"value":"1"}}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_request","category":{"value":"1"}}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_read","dimension":{"value":"1"},"values":["10"]}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_read","category":{"value":"1"},"values":["10"]}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_set","dimension":{"value":"1"},"values":["10"]}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_set","category":{"value":"1"},"values":["10"]}""")]
    public void CreateFromJsonObject_MissingCategoryCommandOrDimension_ThrowsInvalidOperationException(string json)
    {
        AssertInvalidJson(json);
    }

    [Fact]
    public void CreateFromJsonObject_DimensionReadWithoutValues_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyMessage.CreateFromJsonObject(Parse(
            """{"protocol":"scs","type":"dimension_read","category":{"value":"1"},"dimension":{"value":"1"}}""")));

        Assert.Equal(SR.GetResourceString(SR.ID0060), exception.Message);
    }

    [Fact]
    public void CreateFromJsonObject_DimensionSetWithoutValues_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyMessage.CreateFromJsonObject(Parse(
            """{"protocol":"scs","type":"dimension_set","category":{"value":"1"},"dimension":{"value":"1"}}""")));

        Assert.Equal(SR.GetResourceString(SR.ID0061), exception.Message);
    }

    [Theory]
    [InlineData("""{"protocol":"scs","type":"bus_command","category":{},"command":{"value":"1"}}""")]
    [InlineData("""{"protocol":"scs","type":"bus_command","category":{"value":"1"},"command":{}}""")]
    [InlineData("""{"protocol":"scs","type":"bus_command","category":{"value":"1","parameters":[""]},"command":{"value":"1"}}""")]
    [InlineData("""{"protocol":"scs","type":"bus_command","category":{"value":"1","parameters":[null]},"command":{"value":"1"}}""")]
    [InlineData("""{"protocol":"scs","type":"bus_command","category":{"value":"1"},"command":{"value":"1","parameters":[""]}}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_request","category":{"value":"1"},"dimension":{}}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_read","category":{"value":"1"},"dimension":{"value":"1"},"values":[""]}""")]
    [InlineData("""{"protocol":"scs","type":"dimension_set","category":{"value":"1"},"dimension":{"value":"1"},"values":[null]}""")]
    public void CreateFromJsonObject_InvalidValueOrParameters_ThrowsInvalidOperationException(string json)
    {
        AssertInvalidJson(json);
    }

    [Theory]
    [InlineData("""{"type":"nitoo","identifier":1,"unit":1}""", OpenNettyProtocol.Nitoo, "1", "17")]
    [InlineData("""{"type":"nitoo","identifier":1}""", OpenNettyProtocol.Nitoo, "1", "16")]
    [InlineData("""{"type":"zigbee","identifier":26,"unit":1}""", OpenNettyProtocol.Zigbee, "1", "2601#9")]
    [InlineData("""{"type":"zigbee","identifier":26}""", OpenNettyProtocol.Zigbee, "1", "2600#9")]
    [InlineData("""{"type":"scs_dry_contact","contact":10}""", OpenNettyProtocol.Scs, "25", "310")]
    [InlineData("""{"type":"scs_scenario_plus","object":5}""", OpenNettyProtocol.Scs, "25", "25")]
    [InlineData("""{"type":"scs_light_point","general":true}""", OpenNettyProtocol.Scs, "1", "0")]
    [InlineData("""{"type":"scs_light_point","general":true,"extension":2}""", OpenNettyProtocol.Scs, "1", "0#4#02")]
    [InlineData("""{"type":"scs_light_point","group":3}""", OpenNettyProtocol.Scs, "1", "#3")]
    [InlineData("""{"type":"scs_light_point","group":3,"extension":2}""", OpenNettyProtocol.Scs, "1", "#3#4#02")]
    [InlineData("""{"type":"scs_light_point","area":4}""", OpenNettyProtocol.Scs, "1", "4")]
    [InlineData("""{"type":"scs_light_point","area":1,"point":2}""", OpenNettyProtocol.Scs, "1", "12")]
    [InlineData("""{"type":"scs_light_point","area":1,"point":2,"extension":3,"group":null,"general":false}""", OpenNettyProtocol.Scs, "1", "12#4#03")]
    public void CreateFromJsonObject_AddressObject_ReturnsAMessageTargetingTheMatchingAddress(
        string address, OpenNettyProtocol protocol, string category, string where)
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            $$"""
            {"protocol":"{{protocol.ToString().ToLowerInvariant()}}","type":"status_request","address":{{address}},"category":{"value":"{{category}}"} }
            """));

        Assert.Equal(OpenNettyMessage.CreateFromFrame(protocol, Frame(OpenNettyMessageType.StatusRequest, category, where)), message);
    }

    [Theory]
    [InlineData("""{"identifier":1}""")]
    [InlineData("""{"type":"unknown","identifier":1}""")]
    [InlineData("""{"type":"NITOO","identifier":1}""")]
    [InlineData("""{"type":"nitoo","unit":1}""")]
    [InlineData("""{"type":"zigbee","unit":1}""")]
    [InlineData("""{"type":"scs_dry_contact"}""")]
    [InlineData("""{"type":"scs_scenario_plus"}""")]
    public void CreateFromJsonObject_InvalidAddressObject_ThrowsInvalidOperationException(string address)
    {
        AssertInvalidJson($$"""{"protocol":"scs","type":"status_request","address":{{address}},"category":{"value":"1"} }""");
    }

    [Theory]
    [InlineData("bus")]
    [InlineData("radio")]
    [InlineData("powerline")]
    [InlineData("infrared")]
    [InlineData("")]
    public void CreateFromJsonObject_ScsMedium_IsIgnored(string medium)
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            $$"""{"protocol":"scs","type":"status_request","address":{"type":"scs_light_point","area":1,"point":1},"category":{"value":"1"},"medium":"{{medium}}"}"""));

        Assert.Equal(OpenNettyMedium.Bus, message.Medium);
    }

    [Theory]
    [InlineData("powerline", OpenNettyMedium.Powerline)]
    [InlineData("radio", OpenNettyMedium.Radio)]
    [InlineData("infrared", OpenNettyMedium.Infrared)]
    public void CreateFromJsonObject_NitooMedium_IsAppliedToTheMessage(string medium, OpenNettyMedium expected)
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            $$"""{"protocol":"nitoo","type":"status_request","address":{"type":"nitoo","identifier":1,"unit":1},"category":{"value":"1"},"medium":"{{medium}}"}"""));

        Assert.Equal(expected, message.Medium);
    }

    [Theory]
    [InlineData("unicast", OpenNettyMode.Unicast)]
    [InlineData("multicast", OpenNettyMode.Multicast)]
    [InlineData("broadcast", OpenNettyMode.Broadcast)]
    public void CreateFromJsonObject_Mode_IsAppliedToTheMessage(string mode, OpenNettyMode expected)
    {
        var message = OpenNettyMessage.CreateFromJsonObject(Parse(
            $$"""{"protocol":"zigbee","type":"status_request","address":{"type":"zigbee","identifier":26,"unit":1},"category":{"value":"1"},"mode":"{{mode}}"}"""));

        Assert.Equal(expected, message.Mode);
    }

    [Theory]
    [InlineData("""{"medium":"satellite"}""")]
    [InlineData("""{"medium":"unknown"}""")]
    [InlineData("""{"medium":"BUS"}""")]
    [InlineData("""{"mode":"anycast"}""")]
    [InlineData("""{"mode":"unknown"}""")]
    [InlineData("""{"mode":"UNICAST"}""")]
    public void CreateFromJsonObject_UnsupportedMediumOrMode_ThrowsInvalidOperationException(string property)
    {
        AssertInvalidJson(
            $$"""{"protocol":"scs","type":"status_request","address":{"type":"scs_light_point","area":1,"point":1},"category":{"value":"1"},{{property[1..^1]}} }""");
    }

    [Fact]
    public void EqualityOperator_TwoNullReferences_ReturnsTrue()
    {
        OpenNettyMessage? left = null;
        OpenNettyMessage? right = null;

        Assert.True(left == right);
        Assert.False(left != right);
    }

    private static void AssertHasNoDetails(OpenNettyMessage message)
    {
        Assert.Null(message.Address);
        Assert.Null(message.Category);
        Assert.Null(message.Command);
        Assert.Null(message.Dimension);
        Assert.Null(message.Medium);
        Assert.Null(message.Mode);
        Assert.True(message.Values.IsEmpty);
    }

    private static string Frame(OpenNettyMessageType type, string who, string where) => type switch
    {
        OpenNettyMessageType.BusCommand       => $"*{who}*1*{where}##",
        OpenNettyMessageType.StatusRequest    => $"*#{who}*{where}##",
        OpenNettyMessageType.DimensionRequest => $"*#{who}*{where}*1##",
        OpenNettyMessageType.DimensionRead    => $"*#{who}*{where}*1*10##",
        OpenNettyMessageType.DimensionSet     => $"*#{who}*{where}*#1*10##",

        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static OpenNettyMessage Create(
        OpenNettyMessageType type, OpenNettyProtocol protocol, OpenNettyAddress? address = null,
        OpenNettyMedium? medium = null, OpenNettyMode? mode = null,
        string category = "1", ImmutableArray<string>? values = null)
    {
        var who = new OpenNettyCategory(category);

        return type switch
        {
            OpenNettyMessageType.BusCommand => OpenNettyMessage.CreateCommand(
                protocol, new OpenNettyCommand(who, "1"), address, medium, mode),

            OpenNettyMessageType.StatusRequest => OpenNettyMessage.CreateStatusRequest(
                protocol, who, address, medium, mode),

            OpenNettyMessageType.DimensionRequest => OpenNettyMessage.CreateDimensionRequest(
                protocol, new OpenNettyDimension(who, "1"), address, medium, mode),

            OpenNettyMessageType.DimensionRead => OpenNettyMessage.CreateDimensionRead(
                protocol, new OpenNettyDimension(who, "1"), values ?? ImmutableArray.Create("10"), address, medium, mode),

            OpenNettyMessageType.DimensionSet => OpenNettyMessage.CreateDimensionSet(
                protocol, new OpenNettyDimension(who, "1"), values ?? ImmutableArray.Create("10"), address, medium, mode),

            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    private static void AssertJson(string expected, OpenNettyMessage message)
        => Assert.Equal(JsonNode.Parse(expected)!.ToJsonString(), message.ToJsonObject().ToJsonString());

    private static void AssertInvalidJson(string json)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => OpenNettyMessage.CreateFromJsonObject(Parse(json)));

        Assert.Equal(SR.GetResourceString(SR.ID0122), exception.Message);
    }
}
