/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyWorkerOptionsTests
{
    [Fact]
    public void CreateDefaults_NullDevice_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyWorkerOptions.CreateDefaults(null!));

        Assert.Equal("device", exception.ParamName);
    }

    [Fact]
    public void CreateDefaults_ScsGateway_UsesCommandSessionLimits()
    {
        var device = CreateGatewayDevice(OpenNettyProtocol.Scs);

        var options = OpenNettyWorkerOptions.CreateDefaults(device);

        Assert.Equal(TimeSpan.FromSeconds(20), options.CommandSessionMaximumLifetime);
        Assert.Equal(3, options.MaximumConcurrentCommandSessions);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void CreateDefaults_NonScsGateway_DisablesCommandSessionLimits(OpenNettyProtocol protocol)
    {
        var device = CreateGatewayDevice(protocol);

        var options = OpenNettyWorkerOptions.CreateDefaults(device);

        Assert.Equal(TimeSpan.Zero, options.CommandSessionMaximumLifetime);
        Assert.Equal(0, options.MaximumConcurrentCommandSessions);
    }

    [Fact]
    public void Equals_SameValues_ReturnsTrue()
    {
        var first = new OpenNettyWorkerOptions
        {
            CommandSessionMaximumLifetime = TimeSpan.FromSeconds(5),
            MaximumConcurrentCommandSessions = 2
        };

        var second = new OpenNettyWorkerOptions
        {
            CommandSessionMaximumLifetime = TimeSpan.FromSeconds(5),
            MaximumConcurrentCommandSessions = 2
        };

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void With_ModifiedProperty_ReturnsCopyWithNewValue()
    {
        var options = OpenNettyWorkerOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs));

        var copy = options with { MaximumConcurrentCommandSessions = 10 };

        Assert.Equal(10, copy.MaximumConcurrentCommandSessions);
        Assert.Equal(options.CommandSessionMaximumLifetime, copy.CommandSessionMaximumLifetime);
        Assert.Equal(3, options.MaximumConcurrentCommandSessions);
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
