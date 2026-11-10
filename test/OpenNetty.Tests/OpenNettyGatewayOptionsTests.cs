/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyGatewayOptionsTests
{
    [Fact]
    public void CreateDefaults_NullDevice_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyGatewayOptions.CreateDefaults(null!));

        Assert.Equal("device", exception.ParamName);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void CreateDefaults_AnyProtocol_UsesDefaultWorkerOptionsOfTheDevice(OpenNettyProtocol protocol)
    {
        var device = CreateGatewayDevice(protocol);

        var options = OpenNettyGatewayOptions.CreateDefaults(device);

        Assert.Equal(OpenNettyWorkerOptions.CreateDefaults(device), options.DefaultWorkerOptions);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void CreateDefaults_AnyProtocol_UsesDefaultSessionOptionsOfTheDevice(OpenNettyProtocol protocol)
    {
        var device = CreateGatewayDevice(protocol);

        var options = OpenNettyGatewayOptions.CreateDefaults(device);

        var expected = OpenNettySessionOptions.CreateDefaults(device);
        Assert.Equal(expected with { SessionResiliencePipeline = options.DefaultSessionOptions.SessionResiliencePipeline }, options.DefaultSessionOptions);
        Assert.NotNull(options.DefaultSessionOptions.SessionResiliencePipeline);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void CreateDefaults_AnyProtocol_UsesDefaultTransmissionOptionsOfTheDevice(OpenNettyProtocol protocol)
    {
        var device = CreateGatewayDevice(protocol);

        var options = OpenNettyGatewayOptions.CreateDefaults(device);

        var expected = OpenNettyTransmissionOptions.CreateDefaults(device) with
        {
            OutgoingMessageResiliencePipeline = options.DefaultTransmissionOptions.OutgoingMessageResiliencePipeline
        };

        Assert.Equal(expected, options.DefaultTransmissionOptions);
        Assert.NotNull(options.DefaultTransmissionOptions.OutgoingMessageResiliencePipeline);
    }

    [Fact]
    public void Equals_CopyOfSameInstance_ReturnsTrue()
    {
        var options = OpenNettyGatewayOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs));

        var copy = options with { };

        Assert.Equal(options, copy);
        Assert.Equal(options.GetHashCode(), copy.GetHashCode());
    }

    [Fact]
    public void With_ModifiedWorkerOptions_ReturnsCopyWithNewValue()
    {
        var options = OpenNettyGatewayOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs));
        var workerOptions = options.DefaultWorkerOptions with { MaximumConcurrentCommandSessions = 1 };

        var copy = options with { DefaultWorkerOptions = workerOptions };

        Assert.Same(workerOptions, copy.DefaultWorkerOptions);
        Assert.Same(options.DefaultSessionOptions, copy.DefaultSessionOptions);
        Assert.Same(options.DefaultTransmissionOptions, copy.DefaultTransmissionOptions);
        Assert.NotEqual(options, copy);
    }

    private static OpenNettyDevice CreateGatewayDevice(OpenNettyProtocol protocol) => protocol switch
    {
        OpenNettyProtocol.Scs    => OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F454", "Gateway", identifier: null),
        OpenNettyProtocol.Nitoo  => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null),
        OpenNettyProtocol.Zigbee => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88328", "Gateway", identifier: null),

        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };
}
