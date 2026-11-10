/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyConnectionTests
{
    [Fact]
    public async Task Type_Disposed_ThrowsObjectDisposedException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var connection = await ConnectAsync(server);
        await connection.DisposeAsync();

        var exception = Assert.Throws<ObjectDisposedException>(() => connection.Type);

        Assert.Equal(SR.GetResourceString(SR.ID0007), exception.ObjectName);
    }

    [Fact]
    public async Task DisposeAsync_ClosesConnectionWithGateway()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var connection = await ConnectAsync(server);
        var peer = await server.AcceptAsync();

        await connection.DisposeAsync();

        await peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task DisposeAsync_GatewayAlreadyClosedConnection_DoesNotThrow()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var connection = await ConnectAsync(server);
        var peer = await server.AcceptAsync();
        peer.Disconnect();

        var exception = await Record.ExceptionAsync(async () => await connection.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var connection = await ConnectAsync(server);
        await connection.DisposeAsync();

        var exception = await Record.ExceptionAsync(async () => await connection.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task ReceiveAsync_FramesSentByGateway_ReturnsFramesInOrder()
    {
        await using var server = new OpenNettyLoopbackGateway();
        await using var connection = await ConnectAsync(server);
        var peer = await server.AcceptAsync();
        await peer.SendAsync("*1*1*21##");
        await peer.SendAsync("*#*0##");

        var first = await connection.ReceiveAsync(TestContext.Current.CancellationToken);
        var second = await connection.ReceiveAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OpenNettyFrame.Parse("*1*1*21##"), first);
        Assert.Equal(OpenNettyFrames.NegativeAcknowledgement, second);
    }

    [Fact]
    public async Task ReceiveAsync_GatewayClosesConnection_ReturnsNull()
    {
        await using var server = new OpenNettyLoopbackGateway();
        await using var connection = await ConnectAsync(server);
        var peer = await server.AcceptAsync();
        peer.Disconnect();

        var frame = await connection.ReceiveAsync(TestContext.Current.CancellationToken);

        Assert.Null(frame);
    }

    [Fact]
    public async Task ReceiveAsync_CanceledWhileWaiting_ThrowsOperationCanceledException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        await using var connection = await ConnectAsync(server);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = connection.ReceiveAsync(source.Token).AsTask();

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task ReceiveAsync_ConcurrentCall_ThrowsInvalidOperationException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        await using var connection = await ConnectAsync(server);
        var peer = await server.AcceptAsync();
        var pending = connection.ReceiveAsync(TestContext.Current.CancellationToken).AsTask();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await connection.ReceiveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0037), exception.Message);
        await peer.SendAsync("*#*1##");
        Assert.Equal(OpenNettyFrames.Acknowledgement, await pending);
    }

    [Fact]
    public async Task ReceiveAsync_Disposed_ThrowsObjectDisposedException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var connection = await ConnectAsync(server);
        await connection.DisposeAsync();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await connection.ReceiveAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0007), exception.ObjectName);
    }

    [Fact]
    public async Task SendAsync_Frame_IsReceivedByGateway()
    {
        await using var server = new OpenNettyLoopbackGateway();
        await using var connection = await ConnectAsync(server);
        var peer = await server.AcceptAsync();

        await connection.SendAsync(OpenNettyFrame.Parse("*1*1*21##"), TestContext.Current.CancellationToken);

        await peer.ExpectAsync("*1*1*21##");
    }

    [Fact]
    public async Task SendAsync_Disposed_ThrowsObjectDisposedException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var connection = await ConnectAsync(server);
        await connection.DisposeAsync();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await connection.SendAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0007), exception.ObjectName);
    }

    [Fact]
    public async Task InitializeAsync_ConnectedConnection_ThrowsInvalidOperationException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        await using var connection = await ConnectAsync(server);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await connection.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0008), exception.Message);
    }

    [Fact]
    public async Task InitializeAsync_Disposed_ThrowsObjectDisposedException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var connection = await ConnectAsync(server);
        await connection.DisposeAsync();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await connection.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0007), exception.ObjectName);
    }

    [Fact]
    public async Task CreateAsync_NullGateway_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await OpenNettyConnection.CreateAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("gateway", exception.ParamName);
    }

    [Fact]
    public async Task CreateAsync_SerialGatewayWithoutSerialPort_ThrowsInvalidOperationException()
    {
        var gateway = CreateGateway(OpenNettyConnectionType.Serial, endpoint: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await OpenNettyConnection.CreateAsync(gateway, TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0063), exception.Message);
    }

    [Fact]
    public async Task CreateAsync_TcpGatewayWithoutEndpoint_ThrowsInvalidOperationException()
    {
        var gateway = CreateGateway(OpenNettyConnectionType.Tcp, endpoint: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await OpenNettyConnection.CreateAsync(gateway, TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0064), exception.Message);
    }

    [Fact]
    public async Task CreateAsync_UnsupportedConnectionType_ThrowsInvalidOperationException()
    {
        var gateway = CreateGateway((OpenNettyConnectionType) 42, endpoint: new IPEndPoint(IPAddress.Loopback, 20000));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await OpenNettyConnection.CreateAsync(gateway, TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0065), exception.Message);
    }

    [Fact]
    public async Task CreateAsync_TcpGateway_ReturnsConnectedTcpConnection()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var gateway = CreateGateway(OpenNettyConnectionType.Tcp, server.EndPoint);

        await using var connection = await OpenNettyConnection.CreateAsync(gateway, TestContext.Current.CancellationToken);
        var peer = await server.AcceptAsync();
        await peer.SendAsync("*#*1##");

        Assert.Equal(OpenNettyConnectionType.Tcp, connection.Type);
        Assert.Equal(OpenNettyFrames.Acknowledgement, await connection.ReceiveAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateSerialConnectionAsync_NullPort_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await OpenNettyConnection.CreateSerialConnectionAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("port", exception.ParamName);
    }

    [Fact]
    public async Task CreateSerialConnectionAsync_UnavailablePort_ThrowsException()
    {
        using var port = new SerialPort("NOT_A_SERIAL_PORT");

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await OpenNettyConnection.CreateSerialConnectionAsync(port, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateTcpConnectionAsync_NullEndpoint_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await OpenNettyConnection.CreateTcpConnectionAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("endpoint", exception.ParamName);
    }

    [Fact]
    public async Task CreateTcpConnectionAsync_UnreachableEndpoint_ThrowsSocketException()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint) listener.LocalEndpoint;
        listener.Stop();

        await Assert.ThrowsAsync<SocketException>(
            async () => await OpenNettyConnection.CreateTcpConnectionAsync(endpoint, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateTcpConnectionAsync_CanceledToken_ThrowsOperationCanceledException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await OpenNettyConnection.CreateTcpConnectionAsync(server.EndPoint, source.Token));
    }

    private static async Task<OpenNettyConnection> ConnectAsync(OpenNettyLoopbackGateway server)
        => await OpenNettyConnection.CreateTcpConnectionAsync(server.EndPoint, TestContext.Current.CancellationToken);

    private static OpenNettyGateway CreateGateway(OpenNettyConnectionType type, EndPoint? endpoint)
    {
        var device = OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs);

        return new OpenNettyGateway
        {
            ConnectionType = type,
            Device = device,
            Endpoint = endpoint,
            Options = OpenNettyGatewayOptions.CreateDefaults(device)
        };
    }
}
