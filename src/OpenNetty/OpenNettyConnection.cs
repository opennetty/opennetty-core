/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

namespace OpenNetty;

/// <summary>
/// Represents a raw connection to an OpenWebNet gateway.
/// </summary>
public abstract class OpenNettyConnection : IAsyncDisposable
{
    /// <summary>
    /// Gets the type of connection.
    /// </summary>
    public abstract OpenNettyConnectionType Type { get; }

    /// <summary>
    /// Releases the connection.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Waits until a new frame is received from the gateway.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <remarks>Note: concurrent calls to this API are not allowed.</remarks>
    /// <returns>
    /// A <see cref="ValueTask"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the frame received from the gateway.
    /// </returns>
    public abstract ValueTask<OpenNettyFrame?> ReceiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Sends the specified frame to the gateway.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <remarks>Note: concurrent calls to this API are not allowed.</remarks>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public abstract ValueTask SendAsync(OpenNettyFrame frame, CancellationToken cancellationToken);

    /// <summary>
    /// Initializes the connection.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    protected internal abstract ValueTask InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new connection to the specified gateway.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the created connection.
    /// </returns>
    public static ValueTask<OpenNettyConnection> CreateAsync(OpenNettyGateway gateway, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        return gateway.ConnectionType switch
        {
            OpenNettyConnectionType.Serial => CreateSerialConnectionAsync(gateway.SerialPort ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0070)), cancellationToken),

            OpenNettyConnectionType.Tcp => CreateTcpConnectionAsync(gateway.IPEndpoint ??
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0071)), cancellationToken),

            _ => throw new InvalidOperationException(SR.GetResourceString(SR.ID0072))
        };
    }

    /// <summary>
    /// Creates a new serial connection to the specified serial port.
    /// </summary>
    /// <param name="port">The serial port.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the created serial connection.
    /// </returns>
    public static async ValueTask<OpenNettyConnection> CreateSerialConnectionAsync(SerialPort port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(port);

        if (port.IsOpen)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0105));
        }

        var connection = new SerialConnection(new SerialPort(port.PortName, port.BaudRate, port.Parity, port.DataBits, port.StopBits));
        await connection.InitializeAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Creates a new TCP connection to the specified Internet Protocol endpoint.
    /// </summary>
    /// <param name="endpoint">The Internet Protocol endpoint.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that can be used to monitor the asynchronous
    /// operation and whose result returns the created TCP connection.
    /// </returns>
    public static async ValueTask<OpenNettyConnection> CreateTcpConnectionAsync(IPEndPoint endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 2);
        socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 2);

        var connection = new SocketConnection(endpoint, socket);
        await connection.InitializeAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Represents a serial connection.
    /// </summary>
    private sealed class SerialConnection : OpenNettyConnection
    {
        private OpenNettyPipe? _pipe;
        private SerialPort? _port;

        /// <summary>
        /// Creates a new instance of the <see cref="SerialConnection"/> class.
        /// </summary>
        /// <param name="port">The serial port.</param>
        public SerialConnection(SerialPort port)
        {
            ArgumentNullException.ThrowIfNull(port);

            _port = port;
        }

        /// <inheritdoc/>
        public override OpenNettyConnectionType Type => OpenNettyConnectionType.Serial;

        /// <inheritdoc/>
        public override ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _pipe, null) is OpenNettyPipe pipe)
            {
                pipe.Dispose();
            }

            if (Interlocked.Exchange(ref _port, null) is SerialPort port)
            {
                port.Close();
                port.Dispose();
            }

            return ValueTask.CompletedTask;
        }

        /// <inheritdoc/>
        public override async ValueTask<OpenNettyFrame?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_pipe is not OpenNettyPipe pipe)
            {
                throw new ObjectDisposedException(SR.GetResourceString(SR.ID0007));
            }

            return await pipe.ReadAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public override async ValueTask SendAsync(OpenNettyFrame frame, CancellationToken cancellationToken)
        {
            if (_pipe is not OpenNettyPipe pipe)
            {
                throw new ObjectDisposedException(SR.GetResourceString(SR.ID0007));
            }

            await pipe.WriteAsync(frame, cancellationToken);
        }

        /// <inheritdoc/>
        protected internal override async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            if (_port is not SerialPort port)
            {
                throw new ObjectDisposedException(SR.GetResourceString(SR.ID0007));
            }

            if (port.IsOpen)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0008));
            }

            await Task.Yield();

            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();

            _pipe = OpenNettyPipe.Create(port.BaseStream);
        }
    }

    /// <summary>
    /// Represents a socket connection.
    /// </summary>
    private sealed class SocketConnection : OpenNettyConnection
    {
        private readonly EndPoint _endpoint;
        private OpenNettyPipe? _pipe;
        private Socket? _socket;

        /// <summary>
        /// Creates a new instance of the <see cref="SocketConnection"/> class.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="socket">The socket.</param>
        public SocketConnection(EndPoint endpoint, Socket socket)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(socket);

            _endpoint = endpoint;
            _socket = socket;
        }

        /// <inheritdoc/>
        public override OpenNettyConnectionType Type
        {
            get
            {
                if (_socket is not Socket socket)
                {
                    throw new ObjectDisposedException(SR.GetResourceString(SR.ID0007));
                }

                return socket.ProtocolType switch
                {
                    ProtocolType.Tcp  => OpenNettyConnectionType.Tcp,
                    ProtocolType type => throw new InvalidOperationException(SR.FormatID0108(Enum.GetName(type)))
                };
            }
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _pipe, null) is OpenNettyPipe pipe)
            {
                pipe.Dispose();
            }

            if (Interlocked.Exchange(ref _socket, null) is Socket socket)
            {
                await socket.DisconnectAsync(reuseSocket: false);
                socket.Dispose();
            }
        }

        /// <inheritdoc/>
        public override async ValueTask<OpenNettyFrame?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_pipe is not OpenNettyPipe pipe)
            {
                throw new ObjectDisposedException(SR.GetResourceString(SR.ID0007));
            }

            return await pipe.ReadAsync(cancellationToken);
        }

        /// <inheritdoc/>
        public override async ValueTask SendAsync(OpenNettyFrame frame, CancellationToken cancellationToken)
        {
            if (_pipe is not OpenNettyPipe pipe)
            {
                throw new ObjectDisposedException(SR.GetResourceString(SR.ID0007));
            }

            await pipe.WriteAsync(frame, cancellationToken);
        }

        /// <inheritdoc/>
        protected internal override async ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            if (_socket is not Socket socket)
            {
                throw new ObjectDisposedException(SR.GetResourceString(SR.ID0007));
            }

            if (socket.Connected)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0008));
            }

            await _socket.ConnectAsync(_endpoint, cancellationToken);

            _pipe = OpenNettyPipe.Create(new NetworkStream(_socket, ownsSocket: false));
        }
    }
}
