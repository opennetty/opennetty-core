/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace OpenNetty;

/// <summary>
/// Represents an observable session to an OpenWebNet gateway.
/// </summary>
public sealed class OpenNettySession : IConnectableAsyncObservable<OpenNettyMessage>, IEquatable<OpenNettySession>, IAsyncDisposable
{
    private OpenNettyConnection? _connection;
    private readonly OpenNettyGateway _gateway;
    private readonly IConnectableAsyncObservable<OpenNettyMessage> _observable;
    private readonly SemaphoreSlim _semaphore = new(initialCount: 1, maxCount: 1);
    private readonly CancellationTokenSource _source = new();
    private readonly OpenNettySessionType _type;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettySession"/> class.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="type">The session type.</param>
    /// <param name="connection">The connection.</param>
    private OpenNettySession(
        OpenNettyGateway gateway,
        OpenNettySessionType type,
        OpenNettyConnection connection)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(connection);

        _connection = connection;
        _gateway = gateway;
        _observable = AsyncObservable.Create<OpenNettyMessage>(observer =>
        {
            return TaskPoolAsyncScheduler.Default.ScheduleAsync(async cancellationToken =>
            {
                using var source = CancellationTokenSource.CreateLinkedTokenSource(_source.Token, cancellationToken);

                while (!source.Token.IsCancellationRequested)
                {
                    OpenNettyFrame? frame;

                    try
                    {
                        frame = await _connection.ReceiveAsync(source.Token);
                    }

                    catch (OperationCanceledException) when (source.Token.IsCancellationRequested)
                    {
                        await observer.OnCompletedAsync();
                        return;
                    }

                    catch (Exception exception)
                    {
                        await observer.OnErrorAsync(exception);
                        continue;
                    }

                    if (frame is null)
                    {
                        await observer.OnCompletedAsync();
                        return;
                    }

                    OpenNettyMessage message;

                    try
                    {
                        message = OpenNettyMessage.CreateFromFrame(gateway.Protocol, frame.GetValueOrDefault());
                    }

                    catch (Exception exception)
                    {
                        await observer.OnErrorAsync(exception);
                        continue;
                    }

                    await observer.OnNextAsync(message);
                }
            });
        })
        .Retry()
        .Multicast(new ConcurrentSimpleAsyncSubject<OpenNettyMessage>());

        _type = type;
    }

    /// <summary>
    /// Gets the gateway used by this session.
    /// </summary>
    public OpenNettyGateway Gateway => _gateway;

    /// <summary>
    /// Gets the protocol used by this session.
    /// </summary>
    public OpenNettyProtocol Protocol => _gateway.Protocol;

    /// <summary>
    /// Gets the type of session negotiated with the gateway.
    /// </summary>
    public OpenNettySessionType Type => _type;

    /// <summary>
    /// Gets the unique identifier associated to the current session.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Sends the specified message to the gateway.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="options">The transmission options.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <remarks>Note: concurrent calls to this API are not allowed.</remarks>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    /// <exception cref="InvalidOperationException">Invalid transmission options are specified.</exception>
    /// <exception cref="ObjectDisposedException">The session is disposed.</exception>
    /// <exception cref="OpenNettyException">An error occurred while sending the message.</exception>
    public async ValueTask SendAsync(
        OpenNettyMessage message,
        OpenNettyTransmissionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message, nameof(message));

        if (_connection is not OpenNettyConnection connection)
        {
            throw new ObjectDisposedException(SR.GetResourceString(SR.ID0009));
        }

        if (message.Protocol != _gateway.Protocol)
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0010));
        }

        // If no explicit transmissions options were specified, resolve them from the gateway options.
        options ??= _gateway.Options.DefaultTransmissionOptions;

        if (!await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0011));
        }

        try
        {
            var address = message.Address;

            var messages = _observable.ObserveOn(TaskPoolAsyncScheduler.Default)
                .Where(message => message switch
                {
                    { Protocol: OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs,
                      Type    : OpenNettyMessageType.Acknowledgement or OpenNettyMessageType.NegativeAcknowledgement }
                        when !options.IgnoreAcknowledgementValidation => true,

                    { Protocol: OpenNettyProtocol.Zigbee,
                      Type    : OpenNettyMessageType.Acknowledgement             or
                                OpenNettyMessageType.BusyNegativeAcknowledgement or
                                OpenNettyMessageType.NegativeAcknowledgement }
                        when !options.IgnoreAcknowledgementValidation => true,

                    { Protocol: OpenNettyProtocol.Nitoo, 
                      Type    : OpenNettyMessageType.BusCommand,
                      Command : OpenNettyCommand command,
                      Address : OpenNettyAddress }
                        when !options.IgnoreActionValidation && IsActionValidationSupported(message) &&
                            (command == OpenNettyCommands.Diagnostics.ValidAction ||
                             command == OpenNettyCommands.Diagnostics.InvalidAction) &&
                             message.Address == address => true,

                    _ => false
                })
                .Replay();

            // Connect the observable just before sending the frame to ensure the acknowledgement
            // and validation replies, if applicable, are not missed due to a race condition.
            await using (await messages.ConnectAsync())
            {
                await connection.SendAsync(message.Frame, cancellationToken);

                if (!options.IgnoreAcknowledgementValidation)
                {
                    switch (await messages
                        .FirstOrDefault(static message => message.Type is OpenNettyMessageType.Acknowledgement             or
                                                                          OpenNettyMessageType.BusyNegativeAcknowledgement or
                                                                          OpenNettyMessageType.NegativeAcknowledgement)
                        .Timeout(options.AcknowledgementTimeout, AsyncObservable.Return<OpenNettyMessage?>(null))
                        .RunAsync(cancellationToken))
                    {
                        case null:
                            throw new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, SR.GetResourceString(SR.ID0012));

                        case { Type: OpenNettyMessageType.BusyNegativeAcknowledgement }:
                            // Wait for the NACK frame to be returned before throwing an exception.
                            throw await messages
                                .FirstOrDefault(static message => message.Type is OpenNettyMessageType.NegativeAcknowledgement)
                                .Timeout(options.AcknowledgementTimeout, AsyncObservable.Return<OpenNettyMessage?>(null))
                                .RunAsync(cancellationToken) switch
                                {
                                    null => new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, SR.GetResourceString(SR.ID0012)),

                                    _ => new OpenNettyException(OpenNettyErrorCode.GatewayBusy, SR.GetResourceString(SR.ID0013)),
                                };

                        case { Type: OpenNettyMessageType.NegativeAcknowledgement }:
                            throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0014));
                    }
                }

                if (!options.IgnoreActionValidation && IsActionValidationSupported(message))
                {
                    switch (await messages
                        .FirstOrDefault(static message =>
                            message.Type is OpenNettyMessageType.BusCommand &&
                           (message.Command == OpenNettyCommands.Diagnostics.ValidAction ||
                            message.Command == OpenNettyCommands.Diagnostics.InvalidAction))
                        .Timeout(options.ActionValidationTimeout, AsyncObservable.Return<OpenNettyMessage?>(null))
                        .RunAsync(cancellationToken))
                    {
                        case null:
                            throw new OpenNettyException(OpenNettyErrorCode.NoActionReceived, SR.GetResourceString(SR.ID0015));

                        case { Command: OpenNettyCommand command } when command == OpenNettyCommands.Diagnostics.InvalidAction:
                            throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0014));
                    }
                }
            }

            // If the gateway options indicate that a post-sending delay must be enforced, apply it immediately.
            if (options.PostSendingDelay != TimeSpan.Zero)
            {
                await Task.Delay(options.PostSendingDelay, cancellationToken);
            }
        }

        finally
        {
            _semaphore.Release();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static bool IsActionValidationSupported(OpenNettyMessage message) => message is {
            Protocol: OpenNettyProtocol.Nitoo,
            Address : not null,
            Medium  : OpenNettyMedium.Powerline,
            Mode    : OpenNettyMode.Unicast,
            Type    : OpenNettyMessageType.BusCommand or OpenNettyMessageType.DimensionSet };
    }

    /// <summary>
    /// Creates and initializes a new session to the specified gateway.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="type">The session type.</param>
    /// <param name="options">The session options.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the
    /// asynchronous operation and whose result returns the created session.
    /// </returns>
    /// <exception cref="ArgumentException">The gateway is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The session type is invalid.</exception>
    /// <exception cref="OpenNettyException">An error occurred while establishing the session.</exception>
    public static async ValueTask<OpenNettySession> CreateAsync(
        OpenNettyGateway gateway, OpenNettySessionType type,
        OpenNettySessionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        switch (type)
        {
            case not (OpenNettySessionType.Command or OpenNettySessionType.Generic or OpenNettySessionType.Event):
                throw new ArgumentOutOfRangeException(nameof(type), SR.GetResourceString(SR.ID0016));

            case OpenNettySessionType.Command when !gateway.Device.HasCapability(OpenNettyCapabilities.OpenWebNetCommandSession):
            case OpenNettySessionType.Generic when !gateway.Device.HasCapability(OpenNettyCapabilities.OpenWebNetGenericSession):
            case OpenNettySessionType.Event   when !gateway.Device.HasCapability(OpenNettyCapabilities.OpenWebNetEventSession):
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0017));
        }

        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        options ??= gateway.Options.DefaultSessionOptions;

        if (options.ConnectionNegotiationTimeout != Timeout.InfiniteTimeSpan)
        {
            source.CancelAfter(options.ConnectionNegotiationTimeout);
        }

        // Create a new connection that will be managed by the returned object.
        var connection = await OpenNettyConnection.CreateAsync(gateway, source.Token);

        try
        {
            // Ask the gateway to return its firmware version to ensure the connection is working properly.
            if (type is OpenNettySessionType.Generic)
            {
                await connection.SendAsync(new OpenNettyFrame(
                    new OpenNettyField(OpenNettyParameter.Empty, new OpenNettyParameter("13")),
                    new OpenNettyField(OpenNettyParameter.Empty),
                    new OpenNettyField(new OpenNettyParameter("16"))), source.Token);

                try
                {
                    if (gateway.Protocol is OpenNettyProtocol.Nitoo)
                    {
                        // Note: Nitoo gateways do not return acknowledgement frames for firmware version requests.
                        if (await WaitFrameAsync(connection, IsFirmwareVersion, source.Token) is null)
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100));
                        }
                    }

                    else
                    {
                        // Note: unlike Nitoo gateways, Zigbee gateways always acknowledge firmware version requests.
                        switch (await WaitFrameAsync(connection, static frame =>
                            frame == OpenNettyFrames.Acknowledgement ||
                            frame == OpenNettyFrames.BusyNegativeAcknowledgement ||
                            frame == OpenNettyFrames.NegativeAcknowledgement || IsFirmwareVersion(frame), source.Token))
                        {
                            case null:
                                throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100));

                            case OpenNettyFrame frame when frame == OpenNettyFrames.BusyNegativeAcknowledgement ||
                                                           frame == OpenNettyFrames.NegativeAcknowledgement:
                                throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0101));

                            case OpenNettyFrame frame when frame == OpenNettyFrames.Acknowledgement:
                                if (await WaitFrameAsync(connection, IsFirmwareVersion, source.Token) is null)
                                {
                                    throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100));
                                }
                                break;

                            case OpenNettyFrame frame when IsFirmwareVersion(frame):
                                if (await WaitFrameAsync(connection, static frame =>
                                    frame == OpenNettyFrames.Acknowledgement ||
                                    frame == OpenNettyFrames.BusyNegativeAcknowledgement ||
                                    frame == OpenNettyFrames.NegativeAcknowledgement, source.Token) != OpenNettyFrames.Acknowledgement)
                                {
                                    throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0101));
                                }
                                break;
                        }
                    }
                }

                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0101));
                }
            }

            else
            {
                // Ensure the server acknowledged the connection request.
                switch (await connection.ReceiveAsync(source.Token))
                {
                    case null:
                        throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100));

                    case OpenNettyFrame frame when frame != OpenNettyFrames.Acknowledgement:
                        throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0018));
                }

                // Negotiate the requested session type.
                await connection.SendAsync(new OpenNettyFrame(
                    new OpenNettyField(new OpenNettyParameter("99")),
                    new OpenNettyField(new OpenNettyParameter(type switch
                    {
                        OpenNettySessionType.Command => "9",
                        OpenNettySessionType.Event   => "1",
                                     _               => "0"
                    }))), source.Token);

                switch (await connection.ReceiveAsync(source.Token))
                {
                    case null:
                        throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100));

                    // If the client IP address was whitelisted, authentication is not required and
                    // an ACK frame is directly returned by the OpenWebNet gateway to reflect that.
                    case OpenNettyFrame frame when frame == OpenNettyFrames.Acknowledgement:
                        break;

                    // If the client IP address wasn't whitelisted and the server requires using "HMAC authentication" (that
                    // isn't based on the standard HMAC-SHA1 or HMAC-SHA256 algorithms but is actually a variant of digest
                    // authentication), extract the returned method algorithm and authenticate using SHA1 or SHA256 digests.
                    case { Fields: [{ Parameters: [{ Value: "98" }] }, { Parameters: [{ Value: { Length: > 0 } method }] }] }:
                    {
                        using HashAlgorithm algorithm = method switch
                        {
                            "1" => SHA1.Create(),
                            "2" => SHA256.Create(),
                             _  => throw new OpenNettyException(OpenNettyErrorCode.AuthenticationMethodUnsupported, SR.GetResourceString(SR.ID0019))
                        };

                        // Ensure a password was attached to the gateway instance.
                        if (string.IsNullOrEmpty(gateway.Password))
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.AuthenticationRequired, SR.GetResourceString(SR.ID0020));
                        }

                        // Acknowledge the negotiated authentication algorithm.
                        await connection.SendAsync(OpenNettyFrames.Acknowledgement, source.Token);

                        // Extract the server authentication nonce returned by the gateway.
                        var nonce = await connection.ReceiveAsync(source.Token) switch
                        {
                            null => throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100)),

                            { Fields: [{ Parameters: [{ IsEmpty: true }, { Value: { Length: > 0 } value }] }] } => value,

                            _ => throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0021)),
                        };

                        // Ensure the returned nonce has a correct size and generate a random client nonce using a CSP.
                        var parameters = (
                            ServerNonce: ConvertFromDigits(nonce) switch
                            {
                                { Length: int length } result when length * 4 == algorithm.HashSize => result,

                                _ => throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0021)),
                            },
                            ClientNonce: RandomNumberGenerator.GetBytes(algorithm.HashSize / 8));

                        // Compute the hash of the OPEN password and convert it to its lowercase hexadecimal representation.
                        var password = Convert.ToHexStringLower(algorithm.ComputeHash(Encoding.UTF8.GetBytes(gateway.Password)));

                        // Compute and send the digest used to authenticate the client.
                        await connection.SendAsync(new OpenNettyFrame(
                            new OpenNettyField(OpenNettyParameter.Empty, new OpenNettyParameter(ConvertToDigits(parameters.ClientNonce))),
                            new OpenNettyField(new OpenNettyParameter(ConvertToDigits(algorithm.ComputeHash(Encoding.UTF8.GetBytes(new StringBuilder()
                                .Append(parameters.ServerNonce)
                                .Append(Convert.ToHexStringLower(parameters.ClientNonce))
                                .Append("736F70653E")
                                .Append("636F70653E")
                                .Append(password)
                                .ToString())))))), source.Token);

                        // Extract the server authentication digest returned by the gateway
                        // and validate it to ensure it matches the expected value.
                        switch (await connection.ReceiveAsync(source.Token))
                        {
                            case null:
                                throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100));

                            case { Fields: [{ Parameters: [{ IsEmpty: true }, { Value: { Length: > 0 } digest }] }] }
                                when CryptographicOperations.FixedTimeEquals(
                                    left : MemoryMarshal.AsBytes<char>(digest),
                                    right: MemoryMarshal.AsBytes<char>(ConvertToDigits(algorithm.ComputeHash(Encoding.UTF8.GetBytes(new StringBuilder()
                                        .Append(parameters.ServerNonce)
                                        .Append(Convert.ToHexStringLower(parameters.ClientNonce))
                                        .Append(password)
                                        .ToString()))))):
                                // Acknowledge the negotiated authentication data.
                                await connection.SendAsync(OpenNettyFrames.Acknowledgement, source.Token);
                                break;

                            case OpenNettyFrame frame when frame == OpenNettyFrames.NegativeAcknowledgement:
                                throw new OpenNettyException(OpenNettyErrorCode.AuthenticationInvalid, SR.GetResourceString(SR.ID0022));

                            default: throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0023));
                        }
                        break;
                    }

                    // If the client IP address wasn't whitelisted and the gateway requires using the legacy
                    // "OPEN authentication" method, extract the nonce and authenticate using the password.
                    case { Fields: [{ Parameters: [{ IsEmpty: true }, { Value: { Length: > 0 } nonce }] }] }:
                    {
                        // Ensure a password was attached to the gateway instance.
                        if (string.IsNullOrEmpty(gateway.Password))
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.AuthenticationRequired, SR.GetResourceString(SR.ID0020));
                        }

                        // Ensure the password only includes at most 9 ASCII digit characters as non-digit
                        // characters are not supported when using the legacy authentication method.
                        if (gateway.Password.Any(static character => !char.IsAsciiDigit(character)) ||
                            gateway.Password.Length is > 9 ||
                            !uint.TryParse(gateway.Password, CultureInfo.InvariantCulture, out uint password))
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.AuthenticationInvalid, SR.GetResourceString(SR.ID0097));
                        }

                        // Compute and send the obfuscated password used to authenticate the client.
                        await connection.SendAsync(new OpenNettyFrame(
                            new OpenNettyField(
                                OpenNettyParameter.Empty,
                                new OpenNettyParameter(nonce.Aggregate(password, static (password, character) => character switch
                                {
                                    '1' => (password >> 7) | (password << 25),
                                    '2' => (password >> 4) | (password << 28),
                                    '3' => (password >> 3) | (password << 29),
                                    '4' => (password << 1) | (password >> 31),
                                    '5' => (password << 5) | (password >> 27),
                                    '6' => (password << 12) | (password >> 20),
                                    '7' => (password & 0x0000FF00) | (password << 24) | (password & 0x00FF0000) >> 16 | (password & 0xFF000000) >> 8,
                                    '8' => (password << 16) | (password >> 24) | ((password & 0x00FF0000) >> 8),
                                    '9' => ~password,
                                     _  => password
                                }).ToString(CultureInfo.InvariantCulture)))), source.Token);

                        // Ensure the server acknowledged the authentication demand.
                        switch (await connection.ReceiveAsync(source.Token))
                        {
                            case null:
                                throw new OpenNettyException(OpenNettyErrorCode.ConnectionClosed, SR.GetResourceString(SR.ID0100));

                            case OpenNettyFrame frame when frame == OpenNettyFrames.Acknowledgement:
                                break;

                            case OpenNettyFrame frame when frame == OpenNettyFrames.NegativeAcknowledgement:
                                throw new OpenNettyException(OpenNettyErrorCode.AuthenticationInvalid, SR.GetResourceString(SR.ID0022));

                            default: throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0023));
                        }
                        break;
                    }

                    default: throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0018));
                }
            }

            return new OpenNettySession(gateway, type, connection);
        }

        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync();

            throw new OpenNettyException(OpenNettyErrorCode.NegotiationTimeout, SR.GetResourceString(SR.ID0024));
        }

        catch (Exception)
        {
            await connection.DisposeAsync();

            throw;
        }

        static string ConvertFromDigits(ReadOnlySpan<char> value)
        {
            if (value.Length % 4 is not 0)
            {
                throw new ArgumentException(SR.GetResourceString(SR.ID0025), nameof(value));
            }

            var builder = new StringBuilder();

            for (var index = 0; index < value.Length; index += 2)
            {
                if (!int.TryParse(value.Slice(index, 2), CultureInfo.InvariantCulture, out int result))
                {
                    throw new ArgumentException(SR.GetResourceString(SR.ID0025), nameof(value));
                }

                builder.Append(result.ToString("x", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        static string ConvertToDigits(ReadOnlySpan<byte> value)
        {
            var builder = new StringBuilder();

            var span = Convert.ToHexString(value).AsSpan();

            for (var index = 0; index < span.Length; index++)
            {
                var digit = int.Parse(span.Slice(index, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                builder.Append(digit.ToString("00", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        static async ValueTask<OpenNettyFrame?> WaitFrameAsync(
            OpenNettyConnection connection, Func<OpenNettyFrame, bool> filter, CancellationToken cancellationToken)
        {
            // Note: the expected frame may not be the next frame in the buffer as generic sessions
            // are not synchronized (e.g state changes may be returned immediately before or after
            // sending a command). In this case, unwanted frames are automatically discarded.

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (await connection.ReceiveAsync(cancellationToken))
                {
                    case null:                                    return null;
                    case OpenNettyFrame frame when filter(frame): return frame;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static bool IsFirmwareVersion(OpenNettyFrame frame) => frame is
            { Fields: [{ Parameters: [{   IsEmpty: true   }, { Value: "13" }] },
                       { Parameters: [{            IsEmpty: true           }] },
                       { Parameters: [{             Value: "16"            }] },
                       { Parameters: [{          Value.Length: > 0         }] },
                       { Parameters: [{          Value.Length: > 0         }] },
                       { Parameters: [{          Value.Length: > 0         }] }] };
    }

    /// <summary>
    /// Connects the <see cref="IAsyncObservable{T}"/> so that incoming
    /// frames can start being processed by the registered observers.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the
    /// asynchronous operation and whose result is used as a signal by the caller
    /// to inform the session that no additional frame will be processed.
    /// </returns>
    public ValueTask<IAsyncDisposable> ConnectAsync() => _observable.ConnectAsync();

    /// <summary>
    /// Subscribes to incoming frames.
    /// </summary>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the
    /// asynchronous operation and whose result is used as a signal by the caller
    /// to inform the session that no additional frame will be processed.
    /// </returns>
    public ValueTask<IAsyncDisposable> SubscribeAsync(IAsyncObserver<OpenNettyMessage> observer)
        => _observable.ObserveOn(TaskPoolAsyncScheduler.Default).SubscribeAsync(observer);

    /// <summary>
    /// Releases the session.
    /// </summary>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _connection, null) is OpenNettyConnection connection)
        {
            await connection.DisposeAsync();

            _semaphore.Dispose();
            _source.Cancel();
            _source.Dispose();
        }
    }

    /// <inheritdoc/>
    public bool Equals(OpenNettySession? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return Id == other.Id;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is OpenNettySession session && Equals(session);

    /// <inheritdoc/>
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// Computes the <see cref="string"/> representation of the current session.
    /// </summary>
    /// <returns>The <see cref="string"/> representation of the current session.</returns>
    public override string ToString() => Id.ToString();

    /// <summary>
    /// Determines whether two <see cref="OpenNettySession"/> instances are equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are equal, <see langword="false"/> otherwise.</returns>
    public static bool operator ==(OpenNettySession? left, OpenNettySession? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null && left.Equals(right));

    /// <summary>
    /// Determines whether two <see cref="OpenNettySession"/> instances are not equal.
    /// </summary>
    /// <param name="left">The first instance.</param>
    /// <param name="right">The second instance.</param>
    /// <returns><see langword="true"/> if the two instances are not equal, <see langword="false"/> otherwise.</returns>
    public static bool operator !=(OpenNettySession? left, OpenNettySession? right) => !(left == right);
}
