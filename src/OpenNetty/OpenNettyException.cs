﻿namespace OpenNetty;

/// <summary>
/// Represents an OpenNetty exception.
/// </summary>
public sealed class OpenNettyException : Exception
{
    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyException"/> class.
    /// </summary>
    /// <param name="code">The error associated to the exception.</param>
    /// <param name="message">The message associated to the exception.</param>
    public OpenNettyException(OpenNettyErrorCode code, string? message)
        : base(message)
        => ErrorCode = code;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyException"/> class.
    /// </summary>
    /// <param name="code">The error associated to the exception.</param>
    /// <param name="message">The message associated to the exception.</param>
    /// <param name="innerException">The inner exception, if available.</param>
    public OpenNettyException(OpenNettyErrorCode code, string? message, Exception? innerException)
        : base(message, innerException)
        => ErrorCode = code;

    /// <summary>
    /// Gets the error code associated to the exception.
    /// </summary>
    public OpenNettyErrorCode ErrorCode { get; }
}