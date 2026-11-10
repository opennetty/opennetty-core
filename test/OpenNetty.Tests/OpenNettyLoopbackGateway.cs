/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Xunit;

namespace OpenNetty.Tests;

/// <summary>
/// Represents an OpenWebNet gateway simulator listening on the loopback interface.
/// </summary>
internal sealed class OpenNettyLoopbackGateway : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentBag<Peer> _accepted = [];
    private readonly Channel<Peer> _peers = Channel.CreateUnbounded<Peer>();
    private readonly CancellationTokenSource _source = new();
    private readonly Task _accepting;

    public OpenNettyLoopbackGateway()
    {
        _listener.Start();
        _accepting = Task.Run(AcceptPeersAsync);
    }

    public IPEndPoint EndPoint => (IPEndPoint) _listener.LocalEndpoint;

    public static OpenNettyDevice CreateDevice(OpenNettyProtocol protocol, params OpenNettyCapability[] capabilities)
    {
        var definition = new OpenNettyDeviceDefinition
        {
            Id = Guid.NewGuid(),
            Medium = OpenNettyMedium.Bus,
            Protocol = protocol,
            Series = "Test"
        };

        var unitDefinition = new OpenNettyUnitDefinition
        {
            Capabilities = [.. capabilities],
            Device = definition,
            Id = 0
        };

        definition.Units = [unitDefinition];

        var unit = new OpenNettyUnit { Definition = unitDefinition };
        var device = new OpenNettyDevice
        {
            Definition = definition,
            Identity = new OpenNettyDeviceIdentity { Brand = OpenNettyBrand.BTicino, Collection = null, Descriptions = [], Model = "Test" },
            Name = $"{protocol} gateway",
            Units = [unit]
        };

        unit.Device = device;
        return device;
    }

    public OpenNettyGateway CreateGateway(
        OpenNettyDevice device, string? password = null,
        Func<OpenNettyGatewayOptions, OpenNettyGatewayOptions>? configure = null)
    {
        var options = OpenNettyGatewayOptions.CreateDefaults(device);

        return OpenNettyGateway.Create(device, EndPoint, password, configure?.Invoke(options) ?? options);
    }

    public async Task<Peer> AcceptAsync()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        source.CancelAfter(Timeout);

        return await _peers.Reader.ReadAsync(source.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _source.CancelAsync();
        _listener.Stop();
        await _accepting;

        foreach (var peer in _accepted)
        {
            peer.Dispose();
        }

        _source.Dispose();
    }

    private async Task AcceptPeersAsync()
    {
        try
        {
            while (!_source.IsCancellationRequested)
            {
                var peer = new Peer(await _listener.AcceptTcpClientAsync(_source.Token));
                _accepted.Add(peer);
                await _peers.Writer.WriteAsync(peer, _source.Token);
            }
        }

        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or SocketException)
        {
        }
    }

    /// <summary>
    /// Represents a client connection accepted by the simulated gateway.
    /// </summary>
    internal sealed class Peer : IDisposable
    {
        private readonly TcpClient _client;
        private readonly OpenNettyPipe _pipe;

        public Peer(TcpClient client)
        {
            _client = client;
            _pipe = OpenNettyPipe.Create(client.GetStream());
        }

        public async Task<OpenNettyFrame?> ReceiveAsync()
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            source.CancelAfter(Timeout);

            return await _pipe.ReadAsync(source.Token);
        }

        public async Task ExpectAsync(string expected)
            => Assert.Equal(OpenNettyFrame.Parse(expected), await ReceiveAsync());

        public async Task ExpectClosedAsync() => Assert.Null(await ReceiveAsync());

        public async Task SendAsync(string frame)
            => await _pipe.WriteAsync(OpenNettyFrame.Parse(frame), TestContext.Current.CancellationToken);

        public async Task AcceptSessionAsync(string request)
        {
            await SendAsync("*#*1##");
            await ExpectAsync(request);
            await SendAsync("*#*1##");
        }

        public async Task NegotiateSessionAsync(OpenNettyProtocol protocol, OpenNettySessionType type)
        {
            if (type is not OpenNettySessionType.Generic)
            {
                await AcceptSessionAsync(type is OpenNettySessionType.Command ? "*99*9##" : "*99*1##");
            }

            else if (protocol is OpenNettyProtocol.Zigbee)
            {
                await ExpectAsync("*13*66*##");
                await SendAsync("*#*1##");
            }

            else
            {
                await ExpectAsync("*#13**16##");
                await SendAsync("*#13**16*1*2*3##");
            }
        }

        public void Disconnect() => _client.Close();

        public void Dispose()
        {
            _pipe.Dispose();
            _client.Dispose();
        }
    }
}
