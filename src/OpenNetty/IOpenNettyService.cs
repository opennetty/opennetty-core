using System.Collections.Immutable;

namespace OpenNetty;

/// <summary>
/// Represents a low-level service that can be used to send and receive common OpenWebNet messages.
/// </summary>
public interface IOpenNettyService
{
    /// <summary>
    /// Sends a dimension request and iterates the dimension values
    /// returned by all the devices matching the specified address.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="dimension">The dimension.</param>
    /// <param name="address">The address, if applicable.</param>
    /// <param name="media">The media to use or <see langword="null"/> to use the default media.</param>
    /// <param name="mode">The mode to use or <see langword="null"/> to use the default mode.</param>
    /// <param name="filter">
    /// The delegate called by the service to filter the returned dimensions.
    /// If set to <see langword="null"/>, only the requested dimension is returned.
    /// </param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the
    /// dimension values returned by all the devices matching the specified address.
    /// </returns>
    IAsyncEnumerable<(OpenNettyAddress Address, ImmutableArray<string> Values)> EnumerateDimensionsAsync(
        OpenNettyProtocol protocol,
        OpenNettyDimension dimension,
        OpenNettyAddress? address = null,
        OpenNettyMedia? media = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyDimension, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a status request and iterates the status replies
    /// returned by all the devices matching the specified address.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="category">The category.</param>
    /// <param name="address">The address, if applicable.</param>
    /// <param name="media">The media to use or <see langword="null"/> to use the default media.</param>
    /// <param name="mode">The mode to use or <see langword="null"/> to use the default mode.</param>
    /// <param name="filter">
    /// The delegate called by the service to filter the returned status replies. If set to
    /// <see langword="null"/>, any bus command matching the specified <paramref name="category"/> is returned.
    /// </param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> that can be used to iterate the
    /// status replies returned by all the devices matching the specified address.
    /// </returns>
    IAsyncEnumerable<(OpenNettyAddress Address, OpenNettyCommand Command)> EnumerateStatusesAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyAddress? address = null,
        OpenNettyMedia? media = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyCommand, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the specified command.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="command">The command.</param>
    /// <param name="address">The address, if applicable.</param>
    /// <param name="media">The media to use or <see langword="null"/> to use the default media.</param>
    /// <param name="mode">The mode to use or <see langword="null"/> to use the default mode.</param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    ValueTask ExecuteCommandAsync(
        OpenNettyProtocol protocol,
        OpenNettyCommand command,
        OpenNettyAddress? address = null,
        OpenNettyMedia? media = null,
        OpenNettyMode? mode = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a dimension request and returns the dimension values
    /// transmitted by the first device matching the specified address.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="dimension">The dimension.</param>
    /// <param name="address">The address, if applicable.</param>
    /// <param name="media">The media to use or <see langword="null"/> to use the default media.</param>
    /// <param name="mode">The mode to use or <see langword="null"/> to use the default mode.</param>
    /// <param name="filter">
    /// The delegate called by the service to filter the returned dimensions.
    /// If set to <see langword="null"/>, only the requested dimension is returned.
    /// </param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose
    /// result contains the dimension values returned by the first device matching the specified address.
    /// </returns>
    ValueTask<ImmutableArray<string>> GetDimensionAsync(OpenNettyProtocol protocol,
        OpenNettyDimension dimension,
        OpenNettyAddress? address = null,
        OpenNettyMedia? media = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyDimension, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a status request and returns the status reply
    /// transmitted by the first device matching the specified address.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="category">The category.</param>
    /// <param name="address">The address, if applicable.</param>
    /// <param name="media">The media to use or <see langword="null"/> to use the default media.</param>
    /// <param name="mode">The mode to use or <see langword="null"/> to use the default mode.</param>
    /// <param name="filter">
    /// The delegate called by the service to filter the returned dimensions.
    /// If set to <see langword="null"/>, only the requested dimension is returned.
    /// </param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose
    /// result contains the dimension values returned by the first device matching the specified address.
    /// </returns>
    ValueTask<OpenNettyCommand> GetStatusAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyAddress? address = null,
        OpenNettyMedia? media = null,
        OpenNettyMode? mode = null,
        Func<OpenNettyCommand, ValueTask<bool>>? filter = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Observes all the status replies matching the specified protocol and category.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="category">The category.</param>
    /// <param name="gateway">The gateway from which status replies should be observed.</param>
    /// <returns>
    /// An <see cref="IAsyncObservable{T}"/> that can be used to iterate the status
    /// replies returned by all the devices matching the specified protocol and category.
    /// </returns>
    IAsyncObservable<(OpenNettyAddress? Address, OpenNettyCommand Command)> ObserveStatusesAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyGateway? gateway = null);

    /// <summary>
    /// Observes all the dimensions matching the specified protocol and category.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="category">The category.</param>
    /// <param name="gateway">The gateway from which dimensions should be observed.</param>
    /// <returns>
    /// An <see cref="IAsyncObservable{T}"/> that can be used to iterate the dimensions
    /// returned by all the devices matching the specified protocol and category.
    /// </returns>
    IAsyncObservable<(OpenNettyAddress? Address, OpenNettyDimension Dimension, ImmutableArray<string> Values)> ObserveDimensionsAsync(
        OpenNettyProtocol protocol,
        OpenNettyCategory category,
        OpenNettyGateway? gateway = null);

    /// <summary>
    /// Observes all the event messages matching the specified protocol.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="gateway">The gateway from which events should be observed.</param>
    /// <returns>
    /// An <see cref="IAsyncObservable{T}"/> that can be used to iterate the event
    /// messages returned by all the devices matching the specified protocol.
    /// </returns>
    IAsyncObservable<OpenNettyMessage> ObserveEventsAsync(
        OpenNettyProtocol protocol,
        OpenNettyGateway? gateway = null);

    /// <summary>
    /// Sends a raw message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    ValueTask SendMessageAsync(
        OpenNettyMessage message,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the specified dimension.
    /// </summary>
    /// <param name="protocol">The protocol.</param>
    /// <param name="dimension">The dimension.</param>
    /// <param name="values">The dimension values.</param>
    /// <param name="address">The address, if applicable.</param>
    /// <param name="media">The media to use or <see langword="null"/> to use the default media.</param>
    /// <param name="mode">The mode to use or <see langword="null"/> to use the default mode.</param>
    /// <param name="gateway">The gateway used to send the message.</param>
    /// <param name="options">The transmission options to use.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.</returns>
    ValueTask SetDimensionAsync(
        OpenNettyProtocol protocol,
        OpenNettyDimension dimension,
        ImmutableArray<string> values,
        OpenNettyAddress? address = null,
        OpenNettyMedia? media = null,
        OpenNettyMode? mode = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions options = OpenNettyTransmissionOptions.None,
        CancellationToken cancellationToken = default);
}