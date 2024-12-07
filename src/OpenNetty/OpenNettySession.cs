using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
        OpenNettyTransmissionOptions options = default,
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

        var address = message.Address;

        if (options.HasFlag(OpenNettyTransmissionOptions.RequireActionValidation))
        {
            if (message.Protocol is not OpenNettyProtocol.Nitoo)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0011));
            }

            if (message.Type is not (OpenNettyMessageType.BusCommand or OpenNettyMessageType.DimensionSet))
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0012));
            }

            if (message.Mode is OpenNettyMode.Broadcast or OpenNettyMode.Multicast)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0013));
            }

            if (address is null)
            {
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0014));
            }
        }

        if (!await _semaphore.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0015));
        }

        try
        {
            var messages = _observable.ObserveOn(TaskPoolAsyncScheduler.Default)
                .Where(message => message switch
                {
                    { Protocol: OpenNettyProtocol.Nitoo or OpenNettyProtocol.Scs,
                      Type    : OpenNettyMessageType.Acknowledgement or OpenNettyMessageType.NegativeAcknowledgement }
                        when !options.HasFlag(OpenNettyTransmissionOptions.IgnoreAcknowledgementValidation) => true,

                    { Protocol: OpenNettyProtocol.Zigbee,
                      Type    : OpenNettyMessageType.Acknowledgement             or
                                OpenNettyMessageType.BusyNegativeAcknowledgement or
                                OpenNettyMessageType.NegativeAcknowledgement }
                        when !options.HasFlag(OpenNettyTransmissionOptions.IgnoreAcknowledgementValidation) => true,

                    { Protocol: OpenNettyProtocol.Nitoo, 
                      Type    : OpenNettyMessageType.BusCommand,
                      Command : OpenNettyCommand command,
                      Address : OpenNettyAddress }
                        when options.HasFlag(OpenNettyTransmissionOptions.RequireActionValidation) &&
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

                if (!options.HasFlag(OpenNettyTransmissionOptions.IgnoreAcknowledgementValidation))
                {
                    switch (await messages
                        .FirstOrDefault(static message => message.Type is OpenNettyMessageType.Acknowledgement             or
                                                                          OpenNettyMessageType.BusyNegativeAcknowledgement or
                                                                          OpenNettyMessageType.NegativeAcknowledgement)
                        .Timeout(_gateway.Options.FrameAcknowledgementTimeout, AsyncObservable.Return<OpenNettyMessage?>(null))
                        .RunAsync(cancellationToken))
                    {
                        case null:
                            throw new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, SR.GetResourceString(SR.ID0016));

                        case { Type: OpenNettyMessageType.BusyNegativeAcknowledgement }:
                            throw new OpenNettyException(OpenNettyErrorCode.GatewayBusy, SR.GetResourceString(SR.ID0017));

                        case { Type: OpenNettyMessageType.NegativeAcknowledgement }:
                            throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0018));
                    }
                }

                if (options.HasFlag(OpenNettyTransmissionOptions.RequireActionValidation))
                {
                    switch (await messages
                        .FirstOrDefault(static message =>
                            message.Type is OpenNettyMessageType.BusCommand &&
                           (message.Command == OpenNettyCommands.Diagnostics.ValidAction ||
                            message.Command == OpenNettyCommands.Diagnostics.InvalidAction))
                        .Timeout(_gateway.Options.ActionValidationTimeout, AsyncObservable.Return<OpenNettyMessage?>(null))
                        .RunAsync(cancellationToken))
                    {
                        case null:
                            throw new OpenNettyException(OpenNettyErrorCode.NoActionReceived, SR.GetResourceString(SR.ID0019));

                        case { Command: OpenNettyCommand command } when command == OpenNettyCommands.Diagnostics.InvalidAction:
                            throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0018));
                    }
                }
            }

            // If the gateway options indicate that a post-sending delay must be enforced, apply it immediately.
            if (_gateway.Options.PostSendingDelay != TimeSpan.Zero &&
                !options.HasFlag(OpenNettyTransmissionOptions.DisablePostSendingDelay))
            {
                await Task.Delay(_gateway.Options.PostSendingDelay, cancellationToken);
            }
        }

        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Creates and initializes a new session to the specified gateway.
    /// </summary>
    /// <param name="gateway">The gateway.</param>
    /// <param name="type">The session type.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the
    /// asynchronous operation and whose result returns the created session.
    /// </returns>
    /// <exception cref="ArgumentException">The gateway is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The session type is invalid.</exception>
    /// <exception cref="OpenNettyException">An error occurred while establishing the session.</exception>
    public static async ValueTask<OpenNettySession> CreateAsync(
        OpenNettyGateway gateway, OpenNettySessionType type, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gateway);

        switch (type)
        {
            case not (OpenNettySessionType.Command or OpenNettySessionType.Generic or OpenNettySessionType.Event):
                throw new ArgumentOutOfRangeException(nameof(type), SR.GetResourceString(SR.ID0020));

            case OpenNettySessionType.Command when !gateway.Device.Definition.Capabilities.Contains(OpenNettyCapabilities.OpenWebNetCommandSession):
            case OpenNettySessionType.Generic when !gateway.Device.Definition.Capabilities.Contains(OpenNettyCapabilities.OpenWebNetGenericSession):
            case OpenNettySessionType.Event   when !gateway.Device.Definition.Capabilities.Contains(OpenNettyCapabilities.OpenWebNetEventSession):
                throw new InvalidOperationException(SR.GetResourceString(SR.ID0021));
        }

        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (gateway.Options.ConnectionNegotiationTimeout != Timeout.InfiniteTimeSpan)
        {
            source.CancelAfter(gateway.Options.ConnectionNegotiationTimeout);
        }

        // Create a new connection that will be managed by the returned object.
        var connection = await OpenNettyConnection.CreateAsync(gateway, source.Token);

        try
        {
            if (type is OpenNettySessionType.Generic)
            {
                // If the supervision mode was enabled, enforce it when creating the session to ensure the
                // connection is working properly and receive all the state changes sent by the devices.
                if (gateway.Options.EnableSupervisionMode)
                {
                    await connection.SendAsync(new OpenNettyFrame(
                        new OpenNettyField(new OpenNettyParameter("13")),
                        new OpenNettyField(new OpenNettyParameter("66")),
                        new OpenNettyField(OpenNettyParameter.Empty)), source.Token);

                    // Ensure the server acknowledged the supervision mode request.
                    if (await connection.ReceiveAsync(source.Token) != OpenNettyFrames.Acknowledgement)
                    {
                        throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0109));
                    }
                }

                // Otherwise, ask the gateway to return its firmware version to ensure the connection is working properly.
                else
                {
                    await connection.SendAsync(new OpenNettyFrame(
                        new OpenNettyField(OpenNettyParameter.Empty, new OpenNettyParameter("13")),
                        new OpenNettyField(OpenNettyParameter.Empty),
                        new OpenNettyField(new OpenNettyParameter("16"))), source.Token);

                    // Note: Nitoo gateways don't return acknowledgement frames for firmware version requests.
                    if (gateway.Protocol is OpenNettyProtocol.Nitoo)
                    {
                        if (await connection.ReceiveAsync(source.Token) is not
                            { Fields: [{ Parameters: [{   IsEmpty: true   }, { Value: "13" }] },
                                       { Parameters: [{   IsEmpty: true   }] },
                                       { Parameters: [{    Value: "16"    }] },
                                       { Parameters: [{ Value.Length: > 0 }] },
                                       { Parameters: [{ Value.Length: > 0 }] },
                                       { Parameters: [{ Value.Length: > 0 }] }] })
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0022));
                        }
                    }

                    else
                    {
                        // Note: the acknowledgement frame may be returned before or after the firmware version.
                        var frame = await connection.ReceiveAsync(source.Token);
                        if (frame == OpenNettyFrames.Acknowledgement)
                        {
                            if (await connection.ReceiveAsync(source.Token) is not
                                { Fields: [{ Parameters: [{   IsEmpty: true   }, { Value: "13" }] },
                                           { Parameters: [{   IsEmpty: true   }] },
                                           { Parameters: [{    Value: "16"    }] },
                                           { Parameters: [{ Value.Length: > 0 }] },
                                           { Parameters: [{ Value.Length: > 0 }] },
                                           { Parameters: [{ Value.Length: > 0 }] }] })
                            {
                                throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0022));
                            }
                        }

                        else if (frame is { Fields: [{ Parameters: [{   IsEmpty: true   }, { Value: "13" }] },
                                                     { Parameters: [{   IsEmpty: true   }] },
                                                     { Parameters: [{    Value: "16"    }] },
                                                     { Parameters: [{ Value.Length: > 0 }] },
                                                     { Parameters: [{ Value.Length: > 0 }] },
                                                     { Parameters: [{ Value.Length: > 0 }] }] })
                        {
                            if (await connection.ReceiveAsync(source.Token) != OpenNettyFrames.Acknowledgement)
                            {
                                throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0022));
                            }
                        }

                        else
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0022));
                        }
                    }
                }
            }

            else
            {
                // Ensure the server acknowledged the connection request.
                if (await connection.ReceiveAsync(source.Token) != OpenNettyFrames.Acknowledgement)
                {
                    throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0022));
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
                             _  => throw new OpenNettyException(OpenNettyErrorCode.AuthenticationMethodUnsupported, SR.GetResourceString(SR.ID0023))
                        };

                        // Ensure a password was attached to the gateway instance.
                        if (string.IsNullOrEmpty(gateway.Password))
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.AuthenticationRequired, SR.GetResourceString(SR.ID0024));
                        }

                        // Acknowledge the negotiated authentication algorithm.
                        await connection.SendAsync(OpenNettyFrames.Acknowledgement, source.Token);

                        // Extract the server authentication nonce returned by the gateway.
                        if (await connection.ReceiveAsync(source.Token) is not { Fields: [{ Parameters: [{ IsEmpty: true }, { Value: { Length: > 0 } nonce }] }] })
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0025));
                        }

                        // Ensure the returned nonce has a correct size and generate a random client nonce using a CSP.
                        var parameters = (
                            ServerNonce: ConvertFromDigits(nonce) switch
                            {
                                { Length: int length } result when length * 4 == algorithm.HashSize => result,

                                _ => throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0025)),
                            },
                            ClientNonce: RandomNumberGenerator.GetBytes(algorithm.HashSize / 8));

                        // Compute the hash of the OPEN password and convert it to its lowercase hexadecimal representation.
                        var password = Convert.ToHexString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(gateway.Password))).ToLowerInvariant();

                        // Compute and send the digest used to authenticate the client.
                        await connection.SendAsync(new OpenNettyFrame(
                            new OpenNettyField(OpenNettyParameter.Empty, new OpenNettyParameter(ConvertToDigits(parameters.ClientNonce))),
                            new OpenNettyField(new OpenNettyParameter(ConvertToDigits(algorithm.ComputeHash(Encoding.UTF8.GetBytes(new StringBuilder()
                                .Append(parameters.ServerNonce)
                                .Append(Convert.ToHexString(parameters.ClientNonce).ToLowerInvariant())
                                .Append("736F70653E")
                                .Append("636F70653E")
                                .Append(password)
                                .ToString())))))), source.Token);

                        // Extract the server authentication digest returned by the gateway
                        // and validate it to ensure it matches the expected value.
                        switch (await connection.ReceiveAsync(source.Token))
                        {
                            case { Fields: [{ Parameters: [{ IsEmpty: true }, { Value: { Length: > 0 } digest }] }] }
                                when CryptographicOperations.FixedTimeEquals(
                                    left : MemoryMarshal.AsBytes<char>(digest),
                                    right: MemoryMarshal.AsBytes<char>(ConvertToDigits(algorithm.ComputeHash(Encoding.UTF8.GetBytes(new StringBuilder()
                                        .Append(parameters.ServerNonce)
                                        .Append(Convert.ToHexString(parameters.ClientNonce).ToLowerInvariant())
                                        .Append(password)
                                        .ToString()))))):
                                // Acknowledge the negotiated authentication data.
                                await connection.SendAsync(OpenNettyFrames.Acknowledgement, source.Token);
                                break;

                            case null:
                            case OpenNettyFrame frame when frame == OpenNettyFrames.NegativeAcknowledgement:
                                throw new OpenNettyException(OpenNettyErrorCode.AuthenticationInvalid, SR.GetResourceString(SR.ID0026));

                            default: throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0027));
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
                            throw new OpenNettyException(OpenNettyErrorCode.AuthenticationRequired, SR.GetResourceString(SR.ID0024));
                        }

                        // Ensure the password only includes at most 9 ASCII digit characters as non-digit
                        // characters are not supported when using the legacy authentication method.
                        if (gateway.Password.Any(static character => !char.IsAsciiDigit(character)) ||
                            gateway.Password.Length > 9 ||
                            !uint.TryParse(gateway.Password, CultureInfo.InvariantCulture, out uint password))
                        {
                            throw new OpenNettyException(OpenNettyErrorCode.AuthenticationInvalid, SR.GetResourceString(SR.ID0110));
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
                            case OpenNettyFrame frame when frame == OpenNettyFrames.Acknowledgement:
                                break;

                            case null:
                            case OpenNettyFrame frame when frame == OpenNettyFrames.NegativeAcknowledgement:
                                throw new OpenNettyException(OpenNettyErrorCode.AuthenticationInvalid, SR.GetResourceString(SR.ID0026));

                            default: throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0027));
                        }
                        break;
                    }

                    default: throw new OpenNettyException(OpenNettyErrorCode.InvalidFrame, SR.GetResourceString(SR.ID0022));
                }
            }

            return new OpenNettySession(gateway, type, connection);
        }

        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await connection.DisposeAsync();

            throw new OpenNettyException(OpenNettyErrorCode.NegotiationTimeout, SR.GetResourceString(SR.ID0028));
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
                throw new ArgumentException(SR.GetResourceString(SR.ID0029), nameof(value));
            }

            var builder = new StringBuilder();

            for (var index = 0; index < value.Length; index += 2)
            {
                if (!int.TryParse(value.Slice(index, 2), CultureInfo.InvariantCulture, out int result))
                {
                    throw new ArgumentException(SR.GetResourceString(SR.ID0029), nameof(value));
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
