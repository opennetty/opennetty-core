/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyFramesTests
{
    [Fact]
    public void Acknowledgement_IsTheAckFrame()
    {
        Assert.Equal("*#*1##", OpenNettyFrames.Acknowledgement.ToString());
        Assert.Equal(OpenNettyFrame.Parse("*#*1##"), OpenNettyFrames.Acknowledgement);
    }

    [Fact]
    public void NegativeAcknowledgement_IsTheNackFrame()
    {
        Assert.Equal("*#*0##", OpenNettyFrames.NegativeAcknowledgement.ToString());
        Assert.Equal(OpenNettyFrame.Parse("*#*0##"), OpenNettyFrames.NegativeAcknowledgement);
    }

    [Fact]
    public void BusyNegativeAcknowledgement_IsTheBusyNackFrame()
    {
        Assert.Equal("*#*6##", OpenNettyFrames.BusyNegativeAcknowledgement.ToString());
        Assert.Equal(OpenNettyFrame.Parse("*#*6##"), OpenNettyFrames.BusyNegativeAcknowledgement);
    }

    [Fact]
    public void Frames_AreDistinct()
    {
        Assert.NotEqual(OpenNettyFrames.Acknowledgement, OpenNettyFrames.NegativeAcknowledgement);
        Assert.NotEqual(OpenNettyFrames.Acknowledgement, OpenNettyFrames.BusyNegativeAcknowledgement);
        Assert.NotEqual(OpenNettyFrames.NegativeAcknowledgement, OpenNettyFrames.BusyNegativeAcknowledgement);
    }
}
