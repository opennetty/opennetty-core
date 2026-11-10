/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.IO.Ports;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyBuilderTests
{
    [Fact]
    public void Constructor_NullServices_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyBuilder(null!));

        Assert.Equal("services", exception.ParamName);
    }

    [Fact]
    public void Services_ReturnsTheServiceCollectionPassedToTheConstructor()
    {
        var services = new ServiceCollection();

        var builder = new OpenNettyBuilder(services);

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void ValidateOnStart_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.ValidateOnStart());
    }

    [Fact]
    public void ValidateOnStart_InvalidOptions_ThrowsWhenStartupValidationIsExecuted()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateValidator(ValidateOptionsResult.Fail("The options are invalid.")));
        new OpenNettyBuilder(services).ValidateOnStart();
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Equal("The options are invalid.", Assert.Single(exception.Failures));
    }

    [Fact]
    public void ValidateOnStart_ValidOptions_DoesNotThrowWhenStartupValidationIsExecuted()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateValidator(ValidateOptionsResult.Success));
        new OpenNettyBuilder(services).ValidateOnStart();
        using var provider = services.BuildServiceProvider();

        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateOnStart_NotCalled_DoesNotRegisterStartupValidation()
    {
        var services = new ServiceCollection();
        _ = new OpenNettyBuilder(services).Configure(static _ => { });
        using var provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IStartupValidator>());
    }

    [Fact]
    public void Configure_NullDelegate_ThrowsArgumentNullException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.Configure(null!));

        Assert.Equal("configuration", exception.ParamName);
    }

    [Fact]
    public void Configure_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.Configure(static _ => { }));
    }

    [Fact]
    public void Configure_Delegate_IsInvokedWhenOptionsAreResolved()
    {
        var services = new ServiceCollection();
        var options = default(OpenNettyOptions);
        new OpenNettyBuilder(services).Configure(configured => options = configured);

        var resolved = GetOptions(services);

        Assert.Same(resolved, options);
    }

    [Fact]
    public void Configure_CalledMultipleTimes_InvokesAllDelegatesInOrder()
    {
        var services = new ServiceCollection();
        var gateway = CreateGateway("Gateway");
        var endpoint = new OpenNettyEndpoint { Name = "Endpoint", Protocol = OpenNettyProtocol.Scs };
        new OpenNettyBuilder(services)
            .Configure(options => options.Gateways.Add(gateway))
            .Configure(options => options.Endpoints.Add(endpoint));

        var options = GetOptions(services);

        Assert.Same(gateway, Assert.Single(options.Gateways));
        Assert.Same(endpoint, Assert.Single(options.Endpoints));
    }

    [Fact]
    public void AddDevice_NullDevice_ThrowsArgumentNullException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.AddDevice(null!));

        Assert.Equal("device", exception.ParamName);
    }

    [Fact]
    public void AddDevice_Device_IsAddedToTheOptions()
    {
        var services = new ServiceCollection();
        var device = CreateDevice("Device");

        new OpenNettyBuilder(services).AddDevice(device);

        Assert.Same(device, Assert.Single(GetOptions(services).Devices));
    }

    [Fact]
    public void AddDevice_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.AddDevice(CreateDevice("Device")));
    }

    [Fact]
    public void AddDevices_ElementsContainingNull_ThrowsArgumentException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentException>(() => builder.AddDevices(CreateDevice("Device"), null!));

        Assert.Equal("devices", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0124), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddDevices_Devices_AreAddedToTheOptionsInOrder()
    {
        var services = new ServiceCollection();
        var first = CreateDevice("First");
        var second = CreateDevice("Second");

        new OpenNettyBuilder(services).AddDevices(first, second);

        Assert.Collection(GetOptions(services).Devices,
            device => Assert.Same(first, device),
            device => Assert.Same(second, device));
    }

    [Fact]
    public void AddDevices_NoDevice_LeavesTheOptionsUnchanged()
    {
        var services = new ServiceCollection();

        new OpenNettyBuilder(services).AddDevices();

        Assert.Empty(GetOptions(services).Devices);
    }

    [Fact]
    public void AddDevices_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.AddDevices(CreateDevice("Device")));
    }

    [Fact]
    public void AddEndpoint_NullEndpoint_ThrowsArgumentNullException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.AddEndpoint(null!));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public void AddEndpoint_Endpoint_IsAddedToTheOptions()
    {
        var services = new ServiceCollection();
        var endpoint = CreateEndpoint("Endpoint");

        new OpenNettyBuilder(services).AddEndpoint(endpoint);

        Assert.Same(endpoint, Assert.Single(GetOptions(services).Endpoints));
    }

    [Fact]
    public void AddEndpoint_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.AddEndpoint(CreateEndpoint("Endpoint")));
    }

    [Fact]
    public void AddEndpoints_ElementsContainingNull_ThrowsArgumentException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentException>(() => builder.AddEndpoints(CreateEndpoint("Endpoint"), null!));

        Assert.Equal("endpoints", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0124), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddEndpoints_Endpoints_AreAddedToTheOptionsInOrder()
    {
        var services = new ServiceCollection();
        var first = CreateEndpoint("First");
        var second = CreateEndpoint("Second");

        new OpenNettyBuilder(services).AddEndpoints(first, second);

        Assert.Collection(GetOptions(services).Endpoints,
            endpoint => Assert.Same(first, endpoint),
            endpoint => Assert.Same(second, endpoint));
    }

    [Fact]
    public void AddEndpoints_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.AddEndpoints(CreateEndpoint("Endpoint")));
    }

    [Fact]
    public void AddGateway_NullGateway_ThrowsArgumentNullException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentNullException>(() => builder.AddGateway(null!));

        Assert.Equal("gateway", exception.ParamName);
    }

    [Fact]
    public void AddGateway_Gateway_IsAddedToTheOptions()
    {
        var services = new ServiceCollection();
        var gateway = CreateGateway("Gateway");

        new OpenNettyBuilder(services).AddGateway(gateway);

        Assert.Same(gateway, Assert.Single(GetOptions(services).Gateways));
    }

    [Fact]
    public void AddGateway_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.AddGateway(CreateGateway("Gateway")));
    }

    [Fact]
    public void AddGateways_ElementsContainingNull_ThrowsArgumentException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<ArgumentException>(() => builder.AddGateways(CreateGateway("Gateway"), null!));

        Assert.Equal("gateways", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0124), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddGateways_Gateways_AreAddedToTheOptionsInOrder()
    {
        var services = new ServiceCollection();
        var first = CreateGateway("First");
        var second = CreateGateway("Second");

        new OpenNettyBuilder(services).AddGateways(first, second);

        Assert.Collection(GetOptions(services).Gateways,
            gateway => Assert.Same(first, gateway),
            gateway => Assert.Same(second, gateway));
    }

    [Fact]
    public void AddGateways_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.AddGateways(CreateGateway("Gateway")));
    }

    [Fact]
    public void ImportFromXmlConfiguration_MissingFile_ThrowsFileNotFoundException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());
        var existing = CreateFile(Configuration());
        var missing = CreateFile(content: null);

        var exception = Assert.Throws<FileNotFoundException>(() => builder.ImportFromXmlConfiguration(existing, missing));

        Assert.Equal(SR.GetResourceString(SR.ID0070), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Files_ImportsTheContentOfAllFiles()
    {
        var services = new ServiceCollection();
        var first = CreateFile(Configuration(ScsTcpGateway));
        var second = CreateFile(Configuration("""<Endpoint Name="Endpoint" Type="SCS dry contact" Id="1" />"""));

        new OpenNettyBuilder(services).ImportFromXmlConfiguration(first, second);

        var options = GetOptions(services);
        Assert.Equal("Gateway", Assert.Single(options.Devices).Name);
        Assert.Equal("Endpoint", Assert.Single(options.Endpoints).Name);
    }

    [Fact]
    public void ImportFromXmlConfiguration_Files_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.ImportFromXmlConfiguration(CreateFile(Configuration())));
    }

    [Theory]
    [InlineData("<Root />")]
    [InlineData("<configuration />")]
    public void ImportFromXmlConfiguration_DocumentWithUnexpectedRoot_ThrowsInvalidOperationException(string xml)
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(() => builder.ImportFromXmlConfiguration(XDocument.Parse(xml)));

        Assert.Equal(SR.FormatID0071("Configuration"), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DocumentWithoutRoot_ThrowsInvalidOperationException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        var exception = Assert.Throws<InvalidOperationException>(() => builder.ImportFromXmlConfiguration(new XDocument()));

        Assert.Equal(SR.FormatID0071("Configuration"), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_OneInvalidDocument_ThrowsInvalidOperationException()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Throws<InvalidOperationException>(
            () => builder.ImportFromXmlConfiguration(XDocument.Parse(Configuration()), XDocument.Parse("<Root />")));
    }

    [Fact]
    public void ImportFromXmlConfiguration_Documents_ReturnsTheSameBuilder()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Same(builder, builder.ImportFromXmlConfiguration(XDocument.Parse(Configuration())));
    }

    [Fact]
    public void ImportFromXmlConfiguration_NoDocument_LeavesTheOptionsUnchanged()
    {
        var services = new ServiceCollection();

        new OpenNettyBuilder(services).ImportFromXmlConfiguration(ImmutableArray<XDocument>.Empty);

        var options = GetOptions(services);
        Assert.Empty(options.Devices);
        Assert.Empty(options.Gateways);
        Assert.Empty(options.Endpoints);
    }

    [Fact]
    public void ImportFromXmlConfiguration_EmptyConfiguration_LeavesTheOptionsUnchanged()
    {
        var options = Import(Configuration());

        Assert.Empty(options.Devices);
        Assert.Empty(options.Gateways);
        Assert.Empty(options.Endpoints);
    }

    [Fact]
    public void ImportFromXmlConfiguration_MultipleDocuments_MergesTheirContent()
    {
        var options = Import(
            Configuration(ScsTcpGateway),
            Configuration("""<Device Brand="BTicino" Model="F411U1" Name="Switch" GatewayName="Gateway" />"""));

        Assert.Equal(["Gateway", "Switch"], options.Devices.Select(static device => device.Name), StringComparer.Ordinal);
        Assert.Same(Assert.Single(options.Gateways), options.Devices[1].Gateway);
    }

    [Theory]
    [InlineData("""<Device Model="F454" Name="Gateway" />""", "Brand")]
    [InlineData("""<Device Brand="Unknown" Model="F454" Name="Gateway" />""", "Brand")]
    [InlineData("""<Device Brand="BTicino" Name="Gateway" />""", "Model")]
    [InlineData("""<Device Brand="BTicino" Model="" Name="Gateway" />""", "Model")]
    public void ImportFromXmlConfiguration_DeviceWithMissingBrandOrModel_ThrowsInvalidOperationException(string device, string attribute)
    {
        AssertInvalid(SR.FormatID0084(attribute), Configuration(device));
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithUnknownModel_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0085(OpenNettyBrand.BTicino, "Unknown"), Configuration(
            """<Device Brand="BTicino" Model="Unknown" Name="Device" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithBrandAndModel_UsesTheMatchingDefinition()
    {
        var options = Import(Configuration("""<Device Brand="Legrand" Model="03598" Name="Device" />"""));

        var device = Assert.Single(options.Devices);
        Assert.Equal(OpenNettyProtocol.Scs, device.Definition.Protocol);
        Assert.True(device.Definition.HasIdentity(OpenNettyBrand.Legrand, "03598"));
        Assert.Null(device.Identifier);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooDeviceWithSerialNumber_UsesANitooSerialNumber()
    {
        var options = Import(Configuration(NitooDevice));

        var device = Assert.Single(options.Devices);
        Assert.Equal(OpenNettyDeviceIdentifier.FromNitooSerialNumber(1), device.Identifier);
        Assert.Equal("Legrand 67201 (000001)", device.Name);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ScsDeviceWithSerialNumber_UsesAScsSerialNumber()
    {
        var options = Import(Configuration("""<Device Brand="BTicino" Model="F411U1" SerialNumber="1A2B3C4D" />"""));

        var device = Assert.Single(options.Devices);
        Assert.Equal(OpenNettyDeviceIdentifier.FromScsSerialNumber("1A2B3C4D"), device.Identifier);
        Assert.Equal("BTicino F411U1 (1A2B3C4D)", device.Name);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ZigbeeDeviceWithSerialNumber_UsesAZigbeeSerialNumber()
    {
        var options = Import(Configuration("""<Device Brand="Legrand" Model="67223" SerialNumber="1A" />"""));

        var device = Assert.Single(options.Devices);
        Assert.Equal(OpenNettyDeviceIdentifier.FromZigbeeSerialNumber("1A"), device.Identifier);
        Assert.Equal("Legrand 67223 (0000001A)", device.Name);
    }

    [Theory]
    [InlineData("Legrand", "67201")]
    [InlineData("BTicino", "F411U1")]
    [InlineData("Legrand", "67223")]
    public void ImportFromXmlConfiguration_DeviceWithMacAddressOnly_UsesAMacAddress(string brand, string model)
    {
        var options = Import(Configuration($"""<Device Brand="{brand}" Model="{model}" MacAddress="00:04:74:12:34:56" />"""));

        Assert.Equal(OpenNettyDeviceIdentifier.FromMacAddress("00:04:74:12:34:56"), Assert.Single(options.Devices).Identifier);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithSerialNumberAndMacAddress_PrefersTheSerialNumber()
    {
        var options = Import(Configuration(
            """<Device Brand="Legrand" Model="67201" SerialNumber="1" MacAddress="00:04:74:12:34:56" />"""));

        Assert.Equal(OpenNettyDeviceIdentifier.FromNitooSerialNumber(1), Assert.Single(options.Devices).Identifier);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithoutNameAndIdentifier_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.GetResourceString(SR.ID0108), Configuration("""<Device Brand="Legrand" Model="67201" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithSettings_AttachesTheSettingsToTheDevice()
    {
        var options = Import(Configuration(
            """
            <Device Brand="Legrand" Model="67201" Name="Device">
              <Setting Name="First" Value="1" />
              <Setting Name="Second" Value="2" />
            </Device>
            """));

        var settings = Assert.Single(options.Devices).Settings;
        Assert.Equal(2, settings.Count);
        Assert.Equal("1", settings[new OpenNettySetting("First")]);
        Assert.Equal("2", settings[new OpenNettySetting("Second")]);
    }

    [Fact]
    public void ImportFromXmlConfiguration_SettingWithoutName_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0086("Name"), Configuration(
            """<Device Brand="Legrand" Model="67201" Name="Device"><Setting Value="1" /></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_SettingWithoutValue_ThrowsInvalidOperationExceptionMentioningTheValueAttribute()
    {
        AssertInvalid(SR.FormatID0086("Value"), Configuration(
            """<Device Brand="Legrand" Model="67201" Name="Device"><Setting Name="First" /></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithGatewayName_AttachesTheMatchingGateway()
    {
        var options = Import(Configuration(
            ScsTcpGateway,
            """<Device Brand="BTicino" Model="F411U1" Name="Switch" GatewayName="GATEWAY" />"""));

        Assert.Same(Assert.Single(options.Gateways), options.Devices[1].Gateway);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithoutGatewayName_LeavesTheGatewayUnset()
    {
        var options = Import(Configuration(ScsTcpGateway, """<Device Brand="BTicino" Model="F411U1" Name="Switch" />"""));

        Assert.Null(options.Devices[1].Gateway);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithEmptyGatewayName_LeavesTheGatewayUnset()
    {
        var options = Import(Configuration(
            ScsTcpGateway,
            """<Device Brand="BTicino" Model="F411U1" Name="Switch" GatewayName="" />"""));

        Assert.Null(options.Devices[1].Gateway);
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceWithUnknownGatewayName_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0089("Unknown"), Configuration(
            ScsTcpGateway,
            """<Device Brand="BTicino" Model="F411U1" Name="Switch" GatewayName="Unknown" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_DeviceReferencingAGatewayDeclaredLater_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0089("Gateway"), Configuration(
            """<Device Brand="BTicino" Model="F411U1" Name="Switch" GatewayName="Gateway" />""",
            ScsTcpGateway));
    }

    [Fact]
    public void ImportFromXmlConfiguration_GatewayDeviceWithGatewayName_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0135("GatewayName"), Configuration(
            ScsTcpGateway,
            """<Device Brand="Legrand" Model="88213" Name="Other gateway" GatewayName="Gateway" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_GatewayWithIpAddress_AddsDeviceAndTcpGateway()
    {
        var options = Import(Configuration(
            """<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Tcp" Server="192.168.1.10" Password="secret" /></Device>"""));

        var device = Assert.Single(options.Devices);
        var gateway = Assert.Single(options.Gateways);
        Assert.Equal("Gateway", device.Name);
        Assert.Equal(OpenNettyProtocol.Scs, device.Definition.Protocol);
        Assert.Same(device, gateway.Device);
        Assert.Equal(OpenNettyConnectionType.Tcp, gateway.ConnectionType);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.10"), 20_000), gateway.Endpoint);
        Assert.Equal("secret", gateway.Password);
        Assert.Null(gateway.SerialPort);
    }

    [Fact]
    public void ImportFromXmlConfiguration_GatewayWithIpAddressAndPort_UsesTheSpecifiedPort()
    {
        var options = Import(Configuration(
            """<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Tcp" Server="192.168.1.10" Port="30000" /></Device>"""));

        var gateway = Assert.Single(options.Gateways);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.10"), 30_000), gateway.Endpoint);
        Assert.Null(gateway.Password);
    }

    [Fact]
    public void ImportFromXmlConfiguration_GatewayWithHostName_UsesADnsEndPoint()
    {
        var options = Import(Configuration(
            """<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Tcp" Server="gateway.local" /></Device>"""));

        Assert.Equal(new DnsEndPoint("gateway.local", 20_000), Assert.Single(options.Gateways).Endpoint);
    }

    [Fact]
    public void ImportFromXmlConfiguration_GatewayWithHostNameAndPort_UsesTheSpecifiedPort()
    {
        var options = Import(Configuration(
            """<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Tcp" Server="gateway.local" Port="30000" /></Device>"""));

        Assert.Equal(new DnsEndPoint("gateway.local", 30_000), Assert.Single(options.Gateways).Endpoint);
    }

    [Fact]
    public void ImportFromXmlConfiguration_TcpGatewayWithoutServer_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0076("Server"), Configuration(
            """<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Tcp" /></Device>"""));
    }

    [Theory]
    [InlineData("""<Gateway />""")]
    [InlineData("""<Gateway Type="" />""")]
    public void ImportFromXmlConfiguration_GatewayWithoutType_ThrowsInvalidOperationException(string gateway)
    {
        AssertInvalid(SR.FormatID0074("Type"), Configuration(
            $"""<Device Brand="BTicino" Model="F454" Name="Gateway">{gateway}</Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_GatewayWithUnknownType_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.GetResourceString(SR.ID0077), Configuration(
            """<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Bluetooth" /></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_SerialGateway_UsesTheSpecifiedSerialPortSettings()
    {
        var options = Import(Configuration(CreateScsSerialGateway(
            ("Port", "COM3"), ("BaudRate", "19200"), ("Parity", "Odd"), ("DataBits", "7"), ("StopBits", "2"))));

        var gateway = Assert.Single(options.Gateways);
        Assert.Equal(OpenNettyConnectionType.Serial, gateway.ConnectionType);
        Assert.Null(gateway.Endpoint);
        Assert.NotNull(gateway.SerialPort);
        Assert.Equal("COM3", gateway.SerialPort.PortName);
        Assert.Equal(19_200, gateway.SerialPort.BaudRate);
        Assert.Equal(Parity.Odd, gateway.SerialPort.Parity);
        Assert.Equal(7, gateway.SerialPort.DataBits);
        Assert.Equal(StopBits.Two, gateway.SerialPort.StopBits);
    }

    [Fact]
    public void ImportFromXmlConfiguration_SerialGatewayWithoutSettings_UsesTheDefaultSettingsOfTheDevice()
    {
        var options = Import(Configuration(
            """<Device Brand="Legrand" Model="88213" Name="Gateway"><Gateway Type="Serial" Port="COM1" /></Device>"""));

        var port = Assert.Single(options.Gateways).SerialPort;
        Assert.NotNull(port);
        Assert.Equal("COM1", port.PortName);
        Assert.Equal(115_200, port.BaudRate);
        Assert.Equal(Parity.None, port.Parity);
        Assert.Equal(8, port.DataBits);
        Assert.Equal(StopBits.One, port.StopBits);
    }

    [Theory]
    [InlineData("None", Parity.None)]
    [InlineData("Odd", Parity.Odd)]
    [InlineData("Even", Parity.Even)]
    [InlineData("Mark", Parity.Mark)]
    [InlineData("Space", Parity.Space)]
    public void ImportFromXmlConfiguration_SerialGatewayWithParity_UsesTheSpecifiedParity(string value, Parity expected)
    {
        var options = Import(Configuration(CreateScsSerialGateway(
            ("Port", "COM1"), ("BaudRate", "9600"), ("Parity", value), ("DataBits", "8"), ("StopBits", "1"))));

        Assert.Equal(expected, Assert.Single(options.Gateways).SerialPort!.Parity);
    }

    [Theory]
    [InlineData("1", StopBits.One)]
    [InlineData("1.5", StopBits.OnePointFive)]
    [InlineData("2", StopBits.Two)]
    public void ImportFromXmlConfiguration_SerialGatewayWithStopBits_UsesTheSpecifiedStopBits(string value, StopBits expected)
    {
        var options = Import(Configuration(CreateScsSerialGateway(
            ("Port", "COM1"), ("BaudRate", "9600"), ("Parity", "None"), ("DataBits", "8"), ("StopBits", value))));

        Assert.Equal(expected, Assert.Single(options.Gateways).SerialPort!.StopBits);
    }

    [Theory]
    [InlineData("Port")]
    [InlineData("BaudRate")]
    [InlineData("Parity")]
    [InlineData("DataBits")]
    [InlineData("StopBits")]
    public void ImportFromXmlConfiguration_SerialGatewayWithMissingAttribute_ThrowsInvalidOperationException(string missing)
    {
        (string Name, string Value)[] attributes =
        [
            ("Port", "COM1"), ("BaudRate", "9600"), ("Parity", "None"), ("DataBits", "8"), ("StopBits", "1")
        ];

        AssertInvalid(SR.FormatID0075(missing), Configuration(CreateScsSerialGateway(
            [.. attributes.Where(attribute => !string.Equals(attribute.Name, missing, StringComparison.Ordinal))])));
    }

    [Fact]
    public void ImportFromXmlConfiguration_SerialGatewayWithEmptyParity_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0075("Parity"), Configuration(CreateScsSerialGateway(
            ("Port", "COM1"), ("BaudRate", "9600"), ("Parity", ""), ("DataBits", "8"), ("StopBits", "1"))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_SerialGatewayWithInvalidParity_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0093("Invalid"), Configuration(CreateScsSerialGateway(
            ("Port", "COM1"), ("BaudRate", "9600"), ("Parity", "Invalid"), ("DataBits", "8"), ("StopBits", "1"))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_SerialGatewayWithInvalidStopBits_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0094("3"), Configuration(CreateScsSerialGateway(
            ("Port", "COM1"), ("BaudRate", "9600"), ("Parity", "None"), ("DataBits", "8"), ("StopBits", "3"))));
    }

    [Fact]
    public void ImportFromXmlConfiguration_UnitWithoutIdentifier_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0078("Id"), Configuration(
            """<Device Brand="Legrand" Model="67201" Name="Device"><Unit /></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_UnitWithUnknownIdentifier_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0087((byte) 99), Configuration(
            """<Device Brand="Legrand" Model="67201" Name="Device"><Unit Id="99" /></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_UnitWithSettingsAndScenarios_AttachesThemToTheUnit()
    {
        var options = Import(Configuration(
            """
            <Device Brand="Legrand" Model="67201" Name="Device">
              <Unit Id="1">
                <Scenario EndpointName="First" FunctionCode="5" />
                <Scenario EndpointName="Second" FunctionCode="6" />
                <Setting Name="Setting" Value="Value" />
              </Unit>
            </Device>
            """));

        var unit = Assert.Single(options.Devices).GetUnit(1);
        Assert.Equal(
        [
            new OpenNettyScenario { EndpointName = "First", FunctionCode = 5 },
            new OpenNettyScenario { EndpointName = "Second", FunctionCode = 6 }
        ], unit.Scenarios);
        Assert.Equal("Value", unit.Settings[new OpenNettySetting("Setting")]);
    }

    [Theory]
    [InlineData("""<Scenario FunctionCode="5" />""", "EndpointName")]
    [InlineData("""<Scenario EndpointName="Endpoint" />""", "FunctionCode")]
    public void ImportFromXmlConfiguration_ScenarioWithMissingAttribute_ThrowsInvalidOperationException(string scenario, string attribute)
    {
        AssertInvalid(SR.FormatID0088(attribute), Configuration(
            $"""<Device Brand="Legrand" Model="67201" Name="Device"><Unit Id="1">{scenario}</Unit></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooUnitEndpoint_InfersTheAddressFromTheDeviceIdentifier()
    {
        var options = Import(Configuration(
            """<Device Brand="Legrand" Model="67201" SerialNumber="1"><Unit Id="1"><Endpoint Name="Light" /></Unit></Device>"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal("Light", endpoint.Name);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Nitoo, "17"), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Nitoo, endpoint.Protocol);
        Assert.Equal(OpenNettyMedium.Powerline, endpoint.Medium);
        Assert.Same(Assert.Single(options.Devices).GetUnit(1), endpoint.Unit);
        Assert.Null(endpoint.Gateway);
        Assert.Empty(endpoint.Capabilities);
        Assert.Empty(endpoint.Settings);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooUnitEndpointWithoutName_UsesTheDefaultEndpointName()
    {
        var options = Import(Configuration(
            """<Device Brand="Legrand" Model="67201" SerialNumber="1"><Unit Id="1"><Endpoint /></Unit></Device>"""));

        Assert.Equal("Nitoo/000001/1", Assert.Single(options.Endpoints).Name);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooUnitEndpointWithUnit_UsesTheSpecifiedUnit()
    {
        var options = Import(Configuration(
            """<Device Brand="Legrand" Model="67201" SerialNumber="1"><Unit Id="1"><Endpoint Name="Light" Unit="2" /></Unit></Device>"""));

        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Nitoo, "18"), Assert.Single(options.Endpoints).Address);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooUnitEndpointWithIdentifier_UsesTheSpecifiedIdentifier()
    {
        var options = Import(Configuration(
            """<Device Brand="Legrand" Model="67201" SerialNumber="1"><Unit Id="1"><Endpoint Name="Light" Id="5" /></Unit></Device>"""));

        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Nitoo, "81"), Assert.Single(options.Endpoints).Address);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooUnitEndpointWithoutDeviceIdentifier_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Light", "Type"), Configuration(
            """<Device Brand="Legrand" Model="67201" Name="Device"><Unit Id="1"><Endpoint Name="Light" /></Unit></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_ZigbeeUnitEndpoint_InfersTheAddressFromTheDeviceIdentifier()
    {
        var options = Import(Configuration(
            """<Device Brand="Legrand" Model="67223" SerialNumber="1A"><Unit Id="1"><Endpoint Name="Sensor" /></Unit></Device>"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Zigbee, "2601"), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Zigbee, endpoint.Protocol);
        Assert.Equal(OpenNettyMedium.Radio, endpoint.Medium);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ZigbeeUnitEndpointWithoutDeviceIdentifier_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Sensor", "Type"), Configuration(
            """<Device Brand="Legrand" Model="67223" Name="Device"><Unit Id="1"><Endpoint Name="Sensor" /></Unit></Device>"""));
    }

    [Theory]
    [InlineData("BTicino", "F411U1")]
    [InlineData("Legrand", "67557")]
    public void ImportFromXmlConfiguration_ScsUnitEndpointWithSwitchOrShutterCapability_InfersALightPointAddress(string brand, string model)
    {
        var options = Import(Configuration(
            $"""<Device Brand="{brand}" Model="{model}" Name="Device"><Unit Id="1"><Endpoint Name="Light" Area="1" Point="2" /></Unit></Device>"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, "12"), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Scs, endpoint.Protocol);
        Assert.Equal(OpenNettyMedium.Bus, endpoint.Medium);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ScsUnitEndpointWithoutSwitchOrShutterCapability_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Scenario", "Type"), Configuration(
            """<Device Brand="BTicino" Model="F428" Name="Device"><Unit Id="1"><Endpoint Name="Scenario" /></Unit></Device>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_GatewayUnitEndpoint_DoesNotHaveAnAddress()
    {
        var options = Import(Configuration(
            """
            <Device Brand="BTicino" Model="F454" Name="Gateway">
              <Gateway Type="Tcp" Server="192.168.1.10" />
              <Unit Id="0"><Endpoint Name="Gateway endpoint" /></Unit>
            </Device>
            """));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal("Gateway endpoint", endpoint.Name);
        Assert.Null(endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Scs, endpoint.Protocol);
        Assert.Equal(OpenNettyMedium.Bus, endpoint.Medium);
        Assert.Same(Assert.Single(options.Devices).GetUnit(0), endpoint.Unit);
    }

    [Fact]
    public void ImportFromXmlConfiguration_EndpointWithCapabilitiesAndSettings_AttachesThem()
    {
        var options = Import(Configuration(
            """
            <Endpoint Name="Endpoint" Type="SCS dry contact" Id="1">
              <Capability Name="On/off switch control" />
              <Capability Name="Custom" />
              <Setting Name="Function type" Value="Light actuator" />
            </Endpoint>
            """));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(2, endpoint.Capabilities.Count);
        Assert.Contains(OpenNettyCapabilities.OnOffSwitchControl, endpoint.Capabilities);
        Assert.Contains(new OpenNettyCapability("Custom"), endpoint.Capabilities);
        Assert.Equal("Light actuator", endpoint.Settings[OpenNettySettings.FunctionType]);
    }

    [Fact]
    public void ImportFromXmlConfiguration_CapabilityWithoutName_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0083("Name"), Configuration(
            """<Endpoint Name="Endpoint" Type="SCS dry contact" Id="1"><Capability /></Endpoint>"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooEndpoint_UsesTheSpecifiedIdentifierAndUnit()
    {
        var options = Import(Configuration("""<Endpoint Name="Endpoint" Type="Nitoo" Id="1" Unit="2" />"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Nitoo, "18"), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Nitoo, endpoint.Protocol);
        Assert.Null(endpoint.Medium);
        Assert.Null(endpoint.Unit);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooEndpointWithoutUnit_UsesTheUnitZero()
    {
        var options = Import(Configuration("""<Endpoint Name="Endpoint" Type="Nitoo" Id="1" />"""));

        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Nitoo, "16"), Assert.Single(options.Endpoints).Address);
    }

    [Fact]
    public void ImportFromXmlConfiguration_NitooEndpointWithoutIdentifierAndUnit_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Endpoint", "Type"), Configuration("""<Endpoint Name="Endpoint" Type="Nitoo" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_ZigbeeEndpoint_UsesTheSpecifiedHexadecimalIdentifierAndUnit()
    {
        var options = Import(Configuration("""<Endpoint Name="Endpoint" Type="Zigbee" Id="1A" Unit="3" />"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.Zigbee, "2603"), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Zigbee, endpoint.Protocol);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ZigbeeEndpointWithoutIdentifierAndUnit_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Endpoint", "Type"), Configuration("""<Endpoint Name="Endpoint" Type="Zigbee" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_ScsDryContactEndpoint_UsesTheSpecifiedIdentifier()
    {
        var options = Import(Configuration("""<Endpoint Name="Endpoint" Type="SCS dry contact" Id="10" />"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.ScsDryContact, "310"), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Scs, endpoint.Protocol);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ScsDryContactEndpointWithoutIdentifier_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Endpoint", "Type"), Configuration("""<Endpoint Name="Endpoint" Type="SCS dry contact" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_ScsScenarioPlusEndpoint_UsesTheSpecifiedIdentifier()
    {
        var options = Import(Configuration("""<Endpoint Name="Endpoint" Type="SCS scenario plus" Id="5" />"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.ScsScenarioPlus, "25"), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Scs, endpoint.Protocol);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ScsScenarioPlusEndpointWithoutIdentifier_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Endpoint", "Type"), Configuration("""<Endpoint Name="Endpoint" Type="SCS scenario plus" />"""));
    }

    [Theory]
    [InlineData("""General="true" """, "0", new string[0])]
    [InlineData("""General="true" Extension="2" """, "0", new[] { "4", "02" })]
    [InlineData("""Group="3" """, "", new[] { "3" })]
    [InlineData("""Group="3" Extension="2" """, "", new[] { "3", "4", "02" })]
    [InlineData("""Area="0" """, "00", new string[0])]
    [InlineData("""Area="1" """, "1", new string[0])]
    [InlineData("""Area="10" Extension="2" """, "100", new[] { "4", "02" })]
    [InlineData("""Area="1" Point="2" """, "12", new string[0])]
    [InlineData("""Area="1" Point="12" Extension="15" """, "0112", new[] { "4", "15" })]
    public void ImportFromXmlConfiguration_ScsLightPointEndpoint_UsesTheSpecifiedAddressParts(string attributes, string value, string[] parameters)
    {
        var options = Import(Configuration($"""<Endpoint Name="Endpoint" Type="SCS light point" {attributes}/>"""));

        var endpoint = Assert.Single(options.Endpoints);
        Assert.Equal(new OpenNettyAddress(OpenNettyAddressType.ScsLightPoint, value, [.. parameters]), endpoint.Address);
        Assert.Equal(OpenNettyProtocol.Scs, endpoint.Protocol);
    }

    [Fact]
    public void ImportFromXmlConfiguration_ScsLightPointEndpointWithoutAddressParts_ThrowsInvalidOperationException()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => Import(Configuration("""<Endpoint Name="Endpoint" Type="SCS light point" />""")));

        Assert.Equal(SR.GetResourceString(SR.ID0051), exception.Message);
    }

    [Fact]
    public void ImportFromXmlConfiguration_EndpointWithUnknownType_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0079("Unknown"), Configuration("""<Endpoint Name="Endpoint" Type="Unknown" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_EndpointWithoutTypeAndUnit_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0080("Endpoint", "Type"), Configuration("""<Endpoint Name="Endpoint" />"""));
    }

    [Fact]
    public void ImportFromXmlConfiguration_EndpointWithGatewayName_AttachesTheMatchingGateway()
    {
        var options = Import(Configuration(
            ScsTcpGateway,
            """<Endpoint Name="Endpoint" Type="SCS dry contact" Id="1" GatewayName="gateway" />"""));

        Assert.Same(Assert.Single(options.Gateways), Assert.Single(options.Endpoints).Gateway);
    }

    [Fact]
    public void ImportFromXmlConfiguration_EndpointDeclaredBeforeItsGateway_AttachesTheMatchingGateway()
    {
        var options = Import(Configuration(
            """<Endpoint Name="Endpoint" Type="SCS dry contact" Id="1" GatewayName="Gateway" />""",
            ScsTcpGateway));

        Assert.Same(Assert.Single(options.Gateways), Assert.Single(options.Endpoints).Gateway);
    }

    [Fact]
    public void ImportFromXmlConfiguration_EndpointWithEmptyGatewayName_LeavesTheGatewayUnset()
    {
        var options = Import(Configuration(
            ScsTcpGateway,
            """<Endpoint Name="Endpoint" Type="SCS dry contact" Id="1" GatewayName="" />"""));

        Assert.Null(Assert.Single(options.Endpoints).Gateway);
    }

    [Fact]
    public void ImportFromXmlConfiguration_EndpointWithUnknownGatewayName_ThrowsInvalidOperationException()
    {
        AssertInvalid(SR.FormatID0089("Unknown"), Configuration(
            ScsTcpGateway,
            """<Endpoint Name="Endpoint" Type="SCS dry contact" Id="1" GatewayName="Unknown" />"""));
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.True(builder.Equals((object) builder));
        Assert.False(builder.Equals(new OpenNettyBuilder(new ServiceCollection())));
        Assert.False(builder.Equals(null));
    }

    [Fact]
    public void GetHashCode_SameInstance_IsStable()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Equal(builder.GetHashCode(), builder.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsTheTypeName()
    {
        var builder = new OpenNettyBuilder(new ServiceCollection());

        Assert.Equal(typeof(OpenNettyBuilder).FullName, builder.ToString());
    }

    private static IValidateOptions<OpenNettyOptions> CreateValidator(ValidateOptionsResult result)
    {
        var validator = Substitute.For<IValidateOptions<OpenNettyOptions>>();
        validator.Validate(Arg.Any<string?>(), Arg.Any<OpenNettyOptions>()).Returns(result);

        return validator;
    }

    private static OpenNettyOptions GetOptions(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IOptions<OpenNettyOptions>>().Value;
    }

    private static OpenNettyDevice CreateDevice(string name)
        => OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F411U1", name, identifier: null);

    private static OpenNettyEndpoint CreateEndpoint(string name)
        => new() { Name = name, Protocol = OpenNettyProtocol.Scs };

    private static OpenNettyGateway CreateGateway(string name)
        => OpenNettyGateway.Create(
            OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F454", name, identifier: null),
            new DnsEndPoint("localhost", 20_000));

    private const string ScsTcpGateway =
        """<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Tcp" Server="192.168.1.10" /></Device>""";

    private const string NitooDevice = """<Device Brand="Legrand" Model="67201" SerialNumber="1" />""";

    private static string Configuration(params string[] nodes) => $"<Configuration>{string.Concat(nodes)}</Configuration>";

    private static string CreateScsSerialGateway(params (string Name, string Value)[] attributes)
        => $"""<Device Brand="BTicino" Model="F454" Name="Gateway"><Gateway Type="Serial" {string.Join(' ', attributes.Select(
            static attribute => $"{attribute.Name}=\"{attribute.Value}\""))} /></Device>""";

    private static OpenNettyOptions Import(params string[] documents)
    {
        var services = new ServiceCollection();

        new OpenNettyBuilder(services).ImportFromXmlConfiguration([.. documents.Select(static document => XDocument.Parse(document))]);

        return GetOptions(services);
    }

    private static void AssertInvalid(string expectedMessage, params string[] documents)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Import(documents));

        Assert.Equal(expectedMessage, exception.Message);
    }

    private static IFileInfo CreateFile(string? content)
    {
        var file = Substitute.For<IFileInfo>();
        file.Exists.Returns(content is not null);

        if (content is not null)
        {
            file.CreateReadStream().Returns(_ => new MemoryStream(Encoding.UTF8.GetBytes(content)));
        }

        return file;
    }
}
