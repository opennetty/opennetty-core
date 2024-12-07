namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty error code.
/// </summary>
public enum OpenNettyErrorCode
{
    /// <summary>
    /// The gateway that received the frame rejected it.
    /// </summary>
    InvalidFrame = 0,

    /// <summary>
    /// The gateway that received the frame was too busy to process the frame.
    /// </summary>
    GatewayBusy = 1,

    /// <summary>
    /// No worker was active to process the frame.
    /// </summary>
    NoWorkerAvailable = 2,

    /// <summary>
    /// No acknowledgement frame was received for this trame.
    /// </summary>
    NoAcknowledgementReceived = 3,

    /// <summary>
    /// The protocol implemented by the gateway doesn't support the requested action.
    /// </summary>
    IncompatibleAction = 4,

    /// <summary>
    /// An invalid dimension value was specified.
    /// </summary>
    InvalidDimensionValue = 5,

    /// <summary>
    /// An invalid action was rejected by the remote device.
    /// </summary>
    InvalidAction = 6,

    /// <summary>
    /// No valid/invalid action frame was received for this trame.
    /// </summary>
    NoActionReceived = 7,

    /// <summary>
    /// No status reply was received for this status request.
    /// </summary>
    NoStatusReceived = 8,

    /// <summary>
    /// No dimension frame was received for this dimension request.
    /// </summary>
    NoDimensionReceived = 9,

    /// <summary>
    /// Authentication was required by the gateway but no password was provided.
    /// </summary>
    AuthenticationRequired = 10,

    /// <summary>
    /// The authentication method returned by the gateway is not supported.
    /// </summary>
    AuthenticationMethodUnsupported = 11,

    /// <summary>
    /// The authentication data was rejected by the gateway.
    /// </summary>
    AuthenticationInvalid = 12,

    /// <summary>
    /// The connection negotiation couldn't be completed in the allowed time frame.
    /// </summary>
    NegotiationTimeout = 13
}
