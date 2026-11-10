/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Net;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyManagerTests
{
    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyManager(null!));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public async Task EnumerateDevicesAsync_ReturnsRegisteredDevicesInOrder()
    {
        var first = CreateDevice("First");
        var second = CreateDevice("Second");
        var manager = CreateManager(options => options.Devices.AddRange([first, second]));

        var devices = await ToListAsync(manager.EnumerateDevicesAsync(TestContext.Current.CancellationToken));

        Assert.Equal([first, second], devices);
    }

    [Fact]
    public async Task EnumerateDevicesAsync_NoDevice_ReturnsEmptySequence()
    {
        var manager = CreateManager();

        var devices = await ToListAsync(manager.EnumerateDevicesAsync(TestContext.Current.CancellationToken));

        Assert.Empty(devices);
    }

    [Fact]
    public async Task EnumerateDevicesAsync_OptionsReloaded_ReturnsCurrentValue()
    {
        var monitor = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();
        var manager = new OpenNettyManager(monitor);
        var before = new OpenNettyOptions();
        var after = new OpenNettyOptions();
        after.Devices.Add(CreateDevice("Reloaded"));
        monitor.CurrentValue.Returns(before, after);

        var first = await ToListAsync(manager.EnumerateDevicesAsync(TestContext.Current.CancellationToken));
        var second = await ToListAsync(manager.EnumerateDevicesAsync(TestContext.Current.CancellationToken));

        Assert.Empty(first);
        Assert.Equal("Reloaded", Assert.Single(second).Name);
    }

    [Fact]
    public async Task EnumerateEndpointsAsync_ReturnsRegisteredEndpointsInOrder()
    {
        var first = CreateEndpoint("First");
        var second = CreateEndpoint("Second");
        var manager = CreateManager(options => options.Endpoints.AddRange([first, second]));

        var endpoints = await ToListAsync(manager.EnumerateEndpointsAsync(TestContext.Current.CancellationToken));

        Assert.Equal([first, second], endpoints);
    }

    [Fact]
    public async Task EnumerateGatewaysAsync_ReturnsRegisteredGatewaysInOrder()
    {
        var first = CreateGateway("First");
        var second = CreateGateway("Second");
        var manager = CreateManager(options => options.Gateways.AddRange([first, second]));

        var gateways = await ToListAsync(manager.EnumerateGatewaysAsync(TestContext.Current.CancellationToken));

        Assert.Equal([first, second], gateways);
    }

    [Fact]
    public async Task FindDeviceByNameAsync_MatchingName_ReturnsDevice()
    {
        var device = CreateDevice("Kitchen");
        var manager = CreateManager(options => options.Devices.AddRange([CreateDevice("Hall"), device]));

        var result = await manager.FindDeviceByNameAsync("Kitchen", TestContext.Current.CancellationToken);

        Assert.Same(device, result);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("kitchen")]
    public async Task FindDeviceByNameAsync_NoMatchingName_ReturnsNull(string name)
    {
        var manager = CreateManager(options => options.Devices.Add(CreateDevice("Kitchen")));

        var result = await manager.FindDeviceByNameAsync(name, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindDeviceByNameAsync_MultipleMatches_ThrowsInvalidOperationException()
    {
        var manager = CreateManager(options => options.Devices.AddRange([CreateDevice("Kitchen"), CreateDevice("Kitchen")]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.FindDeviceByNameAsync("Kitchen", TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0126), exception.Message);
    }

    [Fact]
    public async Task FindDeviceByNameAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var manager = CreateManager(options => options.Devices.Add(CreateDevice("Kitchen")));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await manager.FindDeviceByNameAsync("Kitchen", source.Token));
    }

    [Fact]
    public async Task FindDeviceByNameAsync_NullName_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await manager.FindDeviceByNameAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task FindDeviceByNameAsync_EmptyName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => await manager.FindDeviceByNameAsync(string.Empty, TestContext.Current.CancellationToken));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task FindEndpointByNameAsync_MatchingName_ReturnsEndpoint()
    {
        var endpoint = CreateEndpoint("Kitchen light");
        var manager = CreateManager(options => options.Endpoints.AddRange([CreateEndpoint("Hall light"), endpoint]));

        var result = await manager.FindEndpointByNameAsync("Kitchen light", TestContext.Current.CancellationToken);

        Assert.Same(endpoint, result);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("kitchen light")]
    public async Task FindEndpointByNameAsync_NoMatchingName_ReturnsNull(string name)
    {
        var manager = CreateManager(options => options.Endpoints.Add(CreateEndpoint("Kitchen light")));

        var result = await manager.FindEndpointByNameAsync(name, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindEndpointByNameAsync_MultipleMatches_ThrowsInvalidOperationException()
    {
        var manager = CreateManager(options => options.Endpoints.AddRange([CreateEndpoint("Kitchen light"), CreateEndpoint("Kitchen light")]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.FindEndpointByNameAsync("Kitchen light", TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0102), exception.Message);
    }

    [Fact]
    public async Task FindEndpointByNameAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var manager = CreateManager(options => options.Endpoints.Add(CreateEndpoint("Kitchen light")));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await manager.FindEndpointByNameAsync("Kitchen light", source.Token));
    }

    [Fact]
    public async Task FindEndpointByNameAsync_NullName_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await manager.FindEndpointByNameAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task FindEndpointByNameAsync_EmptyName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => await manager.FindEndpointByNameAsync(string.Empty, TestContext.Current.CancellationToken));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void FindEndpointsByAddressAsync_NullGateway_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var exception = Assert.Throws<ArgumentNullException>(() => manager.FindEndpointsByAddressAsync(
            null!, OpenNettyAddress.FromNitooAddress(1, 1), TestContext.Current.CancellationToken));

        Assert.Equal("gateway", exception.ParamName);
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_NitooAddress_ReturnsEndpointsWithSameAddressOnSameGateway()
    {
        var gateway = CreateGateway("Nitoo gateway");
        var address = OpenNettyAddress.FromNitooAddress(1, 1);
        var expected = CreateEndpoint("Expected", OpenNettyProtocol.Nitoo, address, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            expected,
            CreateEndpoint("Other address", OpenNettyProtocol.Nitoo, OpenNettyAddress.FromNitooAddress(1, 2), gateway),
            CreateEndpoint("Other gateway", OpenNettyProtocol.Nitoo, address, CreateGateway("Another gateway")),
            CreateEndpoint("Other protocol", OpenNettyProtocol.Zigbee, address, gateway),
            CreateEndpoint("No address", OpenNettyProtocol.Nitoo, null, gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ScsDryContactAddress_ReturnsEndpointsWithSameAddressOnSameGateway()
    {
        var gateway = CreateGateway("Scs gateway");
        var address = OpenNettyAddress.FromScsDryContactAddress(12);
        var expected = CreateEndpoint("Expected", OpenNettyProtocol.Scs, address, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            expected,
            CreateEndpoint("Other address", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsDryContactAddress(13), gateway),
            CreateEndpoint("Other gateway", OpenNettyProtocol.Scs, address, CreateGateway("Another gateway")),
            CreateEndpoint("Other protocol", OpenNettyProtocol.Nitoo, address, gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ScsScenarioPlusAddress_ReturnsEndpointsWithSameAddressOnSameGateway()
    {
        var gateway = CreateGateway("Scs gateway");
        var address = OpenNettyAddress.FromScsScenarioPlusAddress(42);
        var expected = CreateEndpoint("Expected", OpenNettyProtocol.Scs, address, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            expected,
            CreateEndpoint("Other address", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsScenarioPlusAddress(43), gateway),
            CreateEndpoint("No address", OpenNettyProtocol.Scs, null, gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ScsPointToPointAddress_ReturnsOnlyEndpointWithSameAddress()
    {
        var gateway = CreateGateway("Scs gateway");
        var address = OpenNettyAddress.FromScsLightPointAddress(0, general: false, group: null, area: 2, point: 3);
        var expected = CreateEndpoint("Expected", OpenNettyProtocol.Scs, address, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            expected,
            CreateEndpoint("Same area", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, null, 2, 4), gateway),
            CreateEndpoint("Area address", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, null, 2, null), gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ScsAreaAddress_ReturnsEndpointsOfSameAreaAndExtension()
    {
        var gateway = CreateGateway("Scs gateway");
        var area = OpenNettyAddress.FromScsLightPointAddress(0, general: false, group: null, area: 2, point: null);
        var areaEndpoint = CreateEndpoint("Area", OpenNettyProtocol.Scs, area, gateway);
        var pointEndpoint = CreateEndpoint("Point", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, null, 2, 3), gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            areaEndpoint,
            pointEndpoint,
            CreateEndpoint("Other area", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, null, 3, null), gateway),
            CreateEndpoint("Other area point", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, null, 3, 3), gateway),
            CreateEndpoint("Other extension", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(1, false, null, 2, 3), gateway),
            CreateEndpoint("General", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, true, null, null, null), gateway),
            CreateEndpoint("Group", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, 2, null, null), gateway),
            CreateEndpoint("Other gateway", OpenNettyProtocol.Scs, area, CreateGateway("Another gateway"))]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, area, TestContext.Current.CancellationToken));

        Assert.Equal([areaEndpoint, pointEndpoint], endpoints);
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ScsGeneralAddress_ReturnsAreaGeneralAndPointEndpointsOfSameExtension()
    {
        var gateway = CreateGateway("Scs gateway");
        var general = OpenNettyAddress.FromScsLightPointAddress(1, general: true, group: null, area: null, point: null);
        var generalEndpoint = CreateEndpoint("General", OpenNettyProtocol.Scs, general, gateway);
        var areaEndpoint = CreateEndpoint("Area", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(1, false, null, 4, null), gateway);
        var pointEndpoint = CreateEndpoint("Point", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(1, false, null, 5, 6), gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            generalEndpoint,
            areaEndpoint,
            pointEndpoint,
            CreateEndpoint("Group", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(1, false, 4, null, null), gateway),
            CreateEndpoint("Other extension area", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, null, 4, null), gateway),
            CreateEndpoint("Other extension point", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(2, false, null, 4, 4), gateway),
            CreateEndpoint("Other protocol", OpenNettyProtocol.Nitoo, OpenNettyAddress.FromNitooAddress(1, 1), gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, general, TestContext.Current.CancellationToken));

        Assert.Equal([generalEndpoint, areaEndpoint, pointEndpoint], endpoints);
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ScsGroupAddress_ReturnsOnlyEndpointWithSameAddress()
    {
        var gateway = CreateGateway("Scs gateway");
        var group = OpenNettyAddress.FromScsLightPointAddress(0, general: false, group: 3, area: null, point: null);
        var expected = CreateEndpoint("Group", OpenNettyProtocol.Scs, group, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            expected,
            CreateEndpoint("General", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, true, null, null, null), gateway),
            CreateEndpoint("Other group", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, 4, null, null), gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, group, TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ScsAreaAddressWithNonLightPointEndpoints_IgnoresThem()
    {
        var gateway = CreateGateway("Scs gateway");
        var area = OpenNettyAddress.FromScsLightPointAddress(0, general: false, group: null, area: 2, point: null);
        var areaEndpoint = CreateEndpoint("Area", OpenNettyProtocol.Scs, area, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            CreateEndpoint("Dry contact", OpenNettyProtocol.Scs, OpenNettyAddress.FromScsDryContactAddress(1), gateway),
            areaEndpoint]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, area, TestContext.Current.CancellationToken));

        Assert.Same(areaEndpoint, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ZigbeeUnicastAddress_ReturnsOnlyEndpointWithSameAddress()
    {
        var gateway = CreateGateway("Zigbee gateway");
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(1234, 2);
        var expected = CreateEndpoint("Expected", OpenNettyProtocol.Zigbee, address, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            expected,
            CreateEndpoint("Other unit", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(1234, 3), gateway),
            CreateEndpoint("Other device", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(4321, 2), gateway),
            CreateEndpoint("Other gateway", OpenNettyProtocol.Zigbee, address, CreateGateway("Another gateway")),
            CreateEndpoint("No address", OpenNettyProtocol.Zigbee, null, gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ZigbeeUnitOnlyAddress_ReturnsEndpointsWithSameUnit()
    {
        var gateway = CreateGateway("Zigbee gateway");
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(0, 2);
        var first = CreateEndpoint("First", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(1234, 2), gateway);
        var second = CreateEndpoint("Second", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(4321, 2), gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            first,
            CreateEndpoint("Other unit", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(1234, 3), gateway),
            second]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Equal([first, second], endpoints);
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ZigbeeGeneralAddress_ReturnsAllZigbeeEndpointsOfGateway()
    {
        var gateway = CreateGateway("Zigbee gateway");
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(0, 0);
        var first = CreateEndpoint("First", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(1234, 2), gateway);
        var second = CreateEndpoint("Second", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(4321, 0), gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            first,
            second,
            CreateEndpoint("Other gateway", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(1234, 2), CreateGateway("Another gateway")),
            CreateEndpoint("Other protocol", OpenNettyProtocol.Nitoo, OpenNettyAddress.FromNitooAddress(1, 1), gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Equal([first, second], endpoints);
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_ZigbeeDeviceOnlyAddress_ReturnsOnlyEndpointWithSameAddress()
    {
        var gateway = CreateGateway("Zigbee gateway");
        var address = OpenNettyAddress.FromDecimalZigbeeAddress(1234, 0);
        var expected = CreateEndpoint("Expected", OpenNettyProtocol.Zigbee, address, gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            expected,
            CreateEndpoint("Unit", OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(1234, 2), gateway)]));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public async Task FindEndpointsByAddressAsync_UnknownAddressType_ReturnsNoEndpoint()
    {
        var gateway = CreateGateway("Scs gateway");
        var address = new OpenNettyAddress(OpenNettyAddressType.Unknown, "1");
        var manager = CreateManager(options => options.Endpoints.Add(CreateEndpoint("Endpoint", OpenNettyProtocol.Scs, address, gateway)));

        var endpoints = await ToListAsync(manager.FindEndpointsByAddressAsync(gateway, address, TestContext.Current.CancellationToken));

        Assert.Empty(endpoints);
    }

    [Fact]
    public async Task FindEndpointsByDeviceAsync_ReturnsEndpointsOfAnyUnitOfDevice()
    {
        var device = CreateDevice("Switch", "F523");
        var first = CreateEndpoint("First", unit: device.GetUnit(1));
        var second = CreateEndpoint("Second", unit: device.GetUnit(2));
        var manager = CreateManager(options => options.Endpoints.AddRange([
            first,
            CreateEndpoint("Other device", unit: CreateDevice("Other switch", "F523").GetUnit(1)),
            CreateEndpoint("No unit"),
            second]));

        var endpoints = await ToListAsync(manager.FindEndpointsByDeviceAsync(device, TestContext.Current.CancellationToken));

        Assert.Equal([first, second], endpoints);
    }

    [Fact]
    public void FindEndpointsByDeviceAsync_NullDevice_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var exception = Assert.Throws<ArgumentNullException>(() => manager.FindEndpointsByDeviceAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("device", exception.ParamName);
    }

    [Fact]
    public async Task FindEndpointsByUnitAsync_ReturnsEndpointsOfUnit()
    {
        var device = CreateDevice("Switch", "F523");
        var expected = CreateEndpoint("Expected", unit: device.GetUnit(2));
        var manager = CreateManager(options => options.Endpoints.AddRange([
            CreateEndpoint("Other unit", unit: device.GetUnit(1)),
            CreateEndpoint("No unit"),
            expected]));

        var endpoints = await ToListAsync(manager.FindEndpointsByUnitAsync(device.GetUnit(2), TestContext.Current.CancellationToken));

        Assert.Same(expected, Assert.Single(endpoints));
    }

    [Fact]
    public void FindEndpointsByUnitAsync_NullUnit_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var exception = Assert.Throws<ArgumentNullException>(() => manager.FindEndpointsByUnitAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("unit", exception.ParamName);
    }

    [Fact]
    public async Task FindEndpointsByGatewayAsync_ReturnsEndpointsOfGateway()
    {
        var gateway = CreateGateway("Scs gateway");
        var first = CreateEndpoint("First", gateway: gateway);
        var second = CreateEndpoint("Second", gateway: gateway);
        var manager = CreateManager(options => options.Endpoints.AddRange([
            first,
            CreateEndpoint("Other gateway", gateway: CreateGateway("Another gateway")),
            CreateEndpoint("No gateway"),
            second]));

        var endpoints = await ToListAsync(manager.FindEndpointsByGatewayAsync(gateway, TestContext.Current.CancellationToken));

        Assert.Equal([first, second], endpoints);
    }

    [Fact]
    public void FindEndpointsByGatewayAsync_NullGateway_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var exception = Assert.Throws<ArgumentNullException>(() => manager.FindEndpointsByGatewayAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("gateway", exception.ParamName);
    }

    [Theory]
    [InlineData("Living room")]
    [InlineData("LIVING ROOM")]
    [InlineData("living room")]
    public async Task FindGatewayByNameAsync_MatchingNameIgnoringCase_ReturnsGateway(string name)
    {
        var gateway = CreateGateway("Living room");
        var manager = CreateManager(options => options.Gateways.AddRange([CreateGateway("Garage"), gateway]));

        var result = await manager.FindGatewayByNameAsync(name, TestContext.Current.CancellationToken);

        Assert.Same(gateway, result);
    }

    [Fact]
    public async Task FindGatewayByNameAsync_NoMatchingName_ReturnsNull()
    {
        var manager = CreateManager(options => options.Gateways.Add(CreateGateway("Living room")));

        var result = await manager.FindGatewayByNameAsync("Unknown", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindGatewayByNameAsync_MultipleMatches_ThrowsInvalidOperationException()
    {
        var manager = CreateManager(options => options.Gateways.AddRange([CreateGateway("Living room"), CreateGateway("LIVING ROOM")]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.FindGatewayByNameAsync("Living room", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindGatewayByNameAsync_MultipleMatches_ReportsMultipleGateways()
    {
        var manager = CreateManager(options => options.Gateways.AddRange([CreateGateway("Living room"), CreateGateway("LIVING ROOM")]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await manager.FindGatewayByNameAsync("Living room", TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0103), exception.Message);
    }

    [Fact]
    public async Task FindGatewayByNameAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        var manager = CreateManager(options => options.Gateways.Add(CreateGateway("Living room")));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await manager.FindGatewayByNameAsync("Living room", source.Token));
    }

    [Fact]
    public async Task FindGatewayByNameAsync_NullName_ThrowsArgumentNullException()
    {
        var manager = CreateManager();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await manager.FindGatewayByNameAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public async Task FindGatewayByNameAsync_EmptyName_ThrowsArgumentException()
    {
        var manager = CreateManager();

        var exception = await Assert.ThrowsAsync<ArgumentException>(async () => await manager.FindGatewayByNameAsync(string.Empty, TestContext.Current.CancellationToken));

        Assert.Equal("name", exception.ParamName);
    }

    private static OpenNettyManager CreateManager(Action<OpenNettyOptions>? configure = null)
    {
        var options = new OpenNettyOptions();
        configure?.Invoke(options);

        var monitor = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();
        monitor.CurrentValue.Returns(options);

        return new OpenNettyManager(monitor);
    }

    private static OpenNettyDevice CreateDevice(string name, string model = "F454")
        => OpenNettyDevice.Create(OpenNettyBrand.BTicino, model, name, identifier: null);

    private static OpenNettyGateway CreateGateway(string name)
        => OpenNettyGateway.Create(CreateDevice(name), new IPEndPoint(IPAddress.Loopback, 20000));

    private static OpenNettyEndpoint CreateEndpoint(
        string name,
        OpenNettyProtocol protocol = OpenNettyProtocol.Scs,
        OpenNettyAddress? address = null,
        OpenNettyGateway? gateway = null,
        OpenNettyUnit? unit = null)
        => new()
        {
            Address = address,
            Gateway = gateway,
            Name = name,
            Protocol = protocol,
            Unit = unit
        };

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> items = [];

        await foreach (var item in source.WithCancellation(TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }
}
