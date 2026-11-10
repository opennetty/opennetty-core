/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.IO.Ports;
using System.Net;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyGatewayTests
{
    [Theory]
    [InlineData(OpenNettyBrand.Legrand, "88213", OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyBrand.Legrand, "88328", OpenNettyProtocol.Zigbee)]
    [InlineData(OpenNettyBrand.BTicino, "F454", OpenNettyProtocol.Scs)]
    public void Protocol_ReturnsProtocolOfTheDeviceDefinition(OpenNettyBrand brand, string model, OpenNettyProtocol expected)
    {
        var device = CreateDevice(brand, model);

        var gateway = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));

        Assert.Equal(expected, gateway.Protocol);
    }

    [Fact]
    public void Equals_TcpGatewaysWithSameDeviceAndEndpoint_ReturnsTrueRegardlessOfPasswordAndOptions()
    {
        var device = CreateDevice();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 20000);
        var left = OpenNettyGateway.Create(device, endpoint, "first");
        var right = OpenNettyGateway.Create(device, endpoint, "second");

        Assert.True(left.Equals(right));
        Assert.True(left.Equals((object) right));
        Assert.True(left == right);
        Assert.False(left != right);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var gateway = OpenNettyGateway.Create(CreateDevice(), new IPEndPoint(IPAddress.Loopback, 20000));
        var sameInstance = gateway;

        Assert.True(gateway.Equals(sameInstance));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var gateway = OpenNettyGateway.Create(CreateDevice(), new IPEndPoint(IPAddress.Loopback, 20000));

        Assert.False(gateway.Equals((OpenNettyGateway?) null));
        Assert.False(gateway.Equals((object?) null));
    }

    [Fact]
    public void Equals_TcpGatewaysWithEqualButDistinctEndpoints_ReturnsTrue()
    {
        var device = CreateDevice();
        var left = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));
        var right = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));

        Assert.True(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentEndpoints_ReturnsFalse()
    {
        var device = CreateDevice();
        var left = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));
        var right = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20001));

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_DifferentDevices_ReturnsFalse()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 20000);
        var left = OpenNettyGateway.Create(CreateDevice(name: "First"), endpoint);
        var right = OpenNettyGateway.Create(CreateDevice(name: "Second"), endpoint);

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_DevicesWithDifferentProtocols_ReturnsFalse()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 20000);
        var left = OpenNettyGateway.Create(CreateDevice(OpenNettyBrand.Legrand, "88213"), endpoint);
        var right = OpenNettyGateway.Create(CreateDevice(OpenNettyBrand.Legrand, "88328"), endpoint);

        Assert.False(left.Equals(right));
    }

    [Fact]
    public void Equals_TcpAndSerialGatewaysOfTheSameDevice_ReturnsFalse()
    {
        var device = CreateDevice();
        using var port = new SerialPort("COM1");
        var left = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));
        var right = OpenNettyGateway.Create(device, port);

        Assert.False(left.Equals(right));
    }

    [Theory]
    [InlineData("COM1", "COM1", true)]
    [InlineData("COM1", "com1", true)]
    [InlineData("COM1", "COM2", false)]
    public void Equals_SerialGateways_ComparesPortNamesIgnoringCase(string leftName, string rightName, bool expected)
    {
        var device = CreateDevice();
        using var leftPort = new SerialPort(leftName);
        using var rightPort = new SerialPort(rightName);
        var left = OpenNettyGateway.Create(device, leftPort);
        var right = OpenNettyGateway.Create(device, rightPort);

        Assert.Equal(expected, left.Equals(right));
    }

    [Fact]
    public void GetHashCode_EqualTcpGateways_ReturnsSameHashCode()
    {
        var device = CreateDevice();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 20000);

        Assert.Equal(
            OpenNettyGateway.Create(device, endpoint).GetHashCode(),
            OpenNettyGateway.Create(device, endpoint, "secret").GetHashCode());
    }

    [Fact]
    public void GetHashCode_TcpGatewaysWithEqualButDistinctEndpoints_ReturnsSameHashCode()
    {
        var device = CreateDevice();
        var left = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));
        var right = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentEndpoints_ReturnsDifferentHashCodes()
    {
        var device = CreateDevice();
        var left = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20000));
        var right = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, 20001));

        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_SerialGatewaysWithPortNamesDifferingByCase_ReturnsSameHashCode()
    {
        var device = CreateDevice();
        using var leftPort = new SerialPort("COM1");
        using var rightPort = new SerialPort("com1");

        Assert.Equal(
            OpenNettyGateway.Create(device, leftPort).GetHashCode(),
            OpenNettyGateway.Create(device, rightPort).GetHashCode());
    }

    [Fact]
    public void GetHashCode_SerialGatewaysWithDifferentPortNames_ReturnsDifferentHashCodes()
    {
        var device = CreateDevice();
        using var leftPort = new SerialPort("COM1");
        using var rightPort = new SerialPort("COM2");

        Assert.NotEqual(
            OpenNettyGateway.Create(device, leftPort).GetHashCode(),
            OpenNettyGateway.Create(device, rightPort).GetHashCode());
    }

    [Fact]
    public void GetHashCode_DnsEndpoint_DoesNotThrow()
    {
        var gateway = OpenNettyGateway.Create(CreateDevice(), new DnsEndPoint("gateway.local", 20000));

        var exception = Record.Exception(() => gateway.GetHashCode());

        Assert.Null(exception);
    }

    [Fact]
    public void ToString_ReturnsDeviceName()
    {
        var gateway = OpenNettyGateway.Create(CreateDevice(name: "Main gateway"), new IPEndPoint(IPAddress.Loopback, 20000));

        Assert.Equal("Main gateway", gateway.ToString());
    }

    [Fact]
    public void Create_TcpGateway_NullDevice_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OpenNettyGateway.Create(null!, new IPEndPoint(IPAddress.Loopback, 20000)));

        Assert.Equal("device", exception.ParamName);
    }

    [Fact]
    public void Create_TcpGateway_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => OpenNettyGateway.Create(CreateDevice(), (EndPoint) null!));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public void Create_TcpGateway_SetsConnectionTypeEndpointPasswordAndOptions()
    {
        var device = CreateDevice();
        var endpoint = new IPEndPoint(IPAddress.Loopback, 20000);
        var options = OpenNettyGatewayOptions.CreateDefaults(device);

        var gateway = OpenNettyGateway.Create(device, endpoint, "secret", options);

        Assert.Equal(OpenNettyConnectionType.Tcp, gateway.ConnectionType);
        Assert.Same(device, gateway.Device);
        Assert.Same(endpoint, gateway.Endpoint);
        Assert.Equal("secret", gateway.Password);
        Assert.Same(options, gateway.Options);
        Assert.Null(gateway.SerialPort);
    }

    [Fact]
    public void Create_TcpGateway_NoPasswordOrOptions_UsesNoPasswordAndDefaultOptions()
    {
        var gateway = OpenNettyGateway.Create(CreateDevice(), new IPEndPoint(IPAddress.Loopback, 20000));

        Assert.Null(gateway.Password);
        Assert.NotNull(gateway.Options);
        Assert.NotNull(gateway.Options.DefaultSessionOptions);
        Assert.NotNull(gateway.Options.DefaultTransmissionOptions);
        Assert.NotNull(gateway.Options.DefaultWorkerOptions);
    }

    [Fact]
    public void Create_SerialGateway_NullDevice_ThrowsArgumentNullException()
    {
        using var port = new SerialPort("COM1");

        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyGateway.Create(null!, port));

        Assert.Equal("device", exception.ParamName);
    }

    [Fact]
    public void Create_SerialGateway_NullPort_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyGateway.Create(CreateDevice(), (SerialPort) null!));

        Assert.Equal("port", exception.ParamName);
    }

    [Fact]
    public void Create_SerialGateway_SetsConnectionTypeSerialPortAndOptions()
    {
        var device = CreateDevice();
        var options = OpenNettyGatewayOptions.CreateDefaults(device);
        using var port = new SerialPort("COM1");

        var gateway = OpenNettyGateway.Create(device, port, options);

        Assert.Equal(OpenNettyConnectionType.Serial, gateway.ConnectionType);
        Assert.Same(device, gateway.Device);
        Assert.Same(port, gateway.SerialPort);
        Assert.Same(options, gateway.Options);
        Assert.Null(gateway.Endpoint);
        Assert.Null(gateway.Password);
    }

    [Fact]
    public void Create_SerialGateway_NoOptions_UsesDefaultOptions()
    {
        using var port = new SerialPort("COM1");

        var gateway = OpenNettyGateway.Create(CreateDevice(), port);

        Assert.NotNull(gateway.Options);
    }

    private static OpenNettyDevice CreateDevice(
        OpenNettyBrand brand = OpenNettyBrand.Legrand, string model = "88213", string name = "Gateway")
        => OpenNettyDevice.Create(brand, model, name, identifier: null);
}
