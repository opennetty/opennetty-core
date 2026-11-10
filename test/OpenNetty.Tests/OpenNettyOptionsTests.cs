/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Net;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyOptionsTests
{
    [Fact]
    public void Devices_NewInstance_IsEmpty()
    {
        var options = new OpenNettyOptions();

        Assert.Empty(options.Devices);
    }

    [Fact]
    public void Devices_Add_IsReflectedByTheList()
    {
        var options = new OpenNettyOptions();
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null);

        options.Devices.Add(device);

        Assert.Same(device, Assert.Single(options.Devices));
    }

    [Fact]
    public void Lists_AreNotShared_BetweenInstances()
    {
        var first = new OpenNettyOptions();
        var second = new OpenNettyOptions();

        Assert.NotSame(first.Devices, second.Devices);
        Assert.NotSame(first.Gateways, second.Gateways);
        Assert.NotSame(first.Endpoints, second.Endpoints);
    }

    [Fact]
    public void Gateways_NewInstance_IsEmpty()
    {
        var options = new OpenNettyOptions();

        Assert.Empty(options.Gateways);
    }

    [Fact]
    public void Gateways_Add_IsReflectedByTheList()
    {
        var options = new OpenNettyOptions();
        var device = OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null);
        var gateway = OpenNettyGateway.Create(device, new DnsEndPoint("localhost", 20_000));

        options.Gateways.Add(gateway);

        Assert.Same(gateway, Assert.Single(options.Gateways));
    }

    [Fact]
    public void Endpoints_NewInstance_IsEmpty()
    {
        var options = new OpenNettyOptions();

        Assert.Empty(options.Endpoints);
    }

    [Fact]
    public void Endpoints_Add_IsReflectedByTheList()
    {
        var options = new OpenNettyOptions();
        var endpoint = new OpenNettyEndpoint { Name = "Endpoint", Protocol = OpenNettyProtocol.Scs };

        options.Endpoints.Add(endpoint);

        Assert.Same(endpoint, Assert.Single(options.Endpoints));
    }
}
