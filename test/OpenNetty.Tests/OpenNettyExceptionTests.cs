/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyExceptionTests
{
    [Theory]
    [InlineData(OpenNettyErrorCode.InvalidFrame, "The frame is invalid.")]
    [InlineData(OpenNettyErrorCode.GatewayBusy, "The gateway is busy.")]
    [InlineData(OpenNettyErrorCode.ConnectionClosed, "")]
    public void Constructor_CodeAndMessage_SetsErrorCodeAndMessage(OpenNettyErrorCode code, string message)
    {
        var exception = new OpenNettyException(code, message);

        Assert.Equal(code, exception.ErrorCode);
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Constructor_NullMessage_UsesDefaultExceptionMessage()
    {
        var exception = new OpenNettyException(OpenNettyErrorCode.GatewayBusy, null);

        Assert.False(string.IsNullOrEmpty(exception.Message));
    }

    [Fact]
    public void Constructor_CodeMessageAndInnerException_SetsAllProperties()
    {
        var inner = new InvalidOperationException("Inner.");

        var exception = new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, "Outer.", inner);

        Assert.Equal(OpenNettyErrorCode.NoAcknowledgementReceived, exception.ErrorCode);
        Assert.Equal("Outer.", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }

    [Fact]
    public void Constructor_NullInnerException_LeavesInnerExceptionUnset()
    {
        var exception = new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "Outer.", null);

        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Exception_IsAnException()
    {
        Assert.IsAssignableFrom<Exception>(new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "Message."));
    }

    [Fact]
    public void ErrorCode_DefaultValue_IsInvalidFrame()
    {
        var exception = new OpenNettyException(default, "Message.");

        Assert.Equal(OpenNettyErrorCode.InvalidFrame, exception.ErrorCode);
    }
}
