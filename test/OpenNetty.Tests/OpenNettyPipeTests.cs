/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using NSubstitute;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyPipeTests
{
    [Fact]
    public void Constructor_NullReader_ThrowsArgumentNullException()
    {
        var pipe = new Pipe();

        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyPipe(null!, pipe.Writer));

        Assert.Equal("reader", exception.ParamName);
    }

    [Fact]
    public void Constructor_NullWriter_ThrowsArgumentNullException()
    {
        var pipe = new Pipe();

        var exception = Assert.Throws<ArgumentNullException>(() => new OpenNettyPipe(pipe.Reader, null!));

        Assert.Equal("writer", exception.ParamName);
    }

    [Fact]
    public void Create_NullDuplexPipe_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyPipe.Create((IDuplexPipe) null!));

        Assert.Equal("pipe", exception.ParamName);
    }

    [Fact]
    public async Task Create_DuplexPipe_ReadsFromInputAndWritesToOutput()
    {
        var input = new Pipe();
        var output = new Pipe();
        using var pipe = OpenNettyPipe.Create(new TestDuplexPipe(input.Reader, output.Writer));
        await input.Writer.WriteAsync("*1*1*21##"u8.ToArray(), TestContext.Current.CancellationToken);

        var frame = await pipe.ReadAsync(TestContext.Current.CancellationToken);
        await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken);

        Assert.Equal(OpenNettyFrame.Parse("*1*1*21##"), frame);
        var written = await output.Reader.ReadAsync(TestContext.Current.CancellationToken);
        Assert.Equal("*#*1##", Encoding.ASCII.GetString(written.Buffer));
    }

    [Fact]
    public void Create_NullStream_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyPipe.Create((Stream) null!));

        Assert.Equal("stream", exception.ParamName);
    }

    [Fact]
    public void Create_UnreadableStream_ThrowsArgumentException()
    {
        var stream = Substitute.For<Stream>();
        stream.CanRead.Returns(false);
        stream.CanWrite.Returns(true);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyPipe.Create(stream));

        Assert.Equal("stream", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0034), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_UnwritableStream_ThrowsArgumentException()
    {
        var stream = Substitute.For<Stream>();
        stream.CanRead.Returns(true);
        stream.CanWrite.Returns(false);

        var exception = Assert.Throws<ArgumentException>(() => OpenNettyPipe.Create(stream));

        Assert.Equal("stream", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0035), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_Stream_DoesNotCloseStreamWhenDisposed()
    {
        using var stream = new MemoryStream();
        var pipe = OpenNettyPipe.Create(stream);

        await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken);
        pipe.Dispose();

        Assert.True(stream.CanWrite);
        Assert.Equal("*#*1##", Encoding.ASCII.GetString(stream.ToArray()));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var pipe = CreateStreamPipe(string.Empty);

        pipe.Dispose();
        var exception = Record.Exception(pipe.Dispose);

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("*#*1##")]
    [InlineData("*1*1*21##")]
    [InlineData("*#13**16*1*2*3##")]
    public async Task ReadAsync_SingleFrame_ReturnsFrame(string value)
    {
        using var pipe = CreateStreamPipe(value);

        var frame = await pipe.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OpenNettyFrame.Parse(value), frame);
    }

    [Fact]
    public async Task ReadAsync_MultipleFrames_ReturnsFramesInOrderThenNull()
    {
        using var pipe = CreateStreamPipe("*#*1##*1*1*21##*#*0##");

        var first = await pipe.ReadAsync(TestContext.Current.CancellationToken);
        var second = await pipe.ReadAsync(TestContext.Current.CancellationToken);
        var third = await pipe.ReadAsync(TestContext.Current.CancellationToken);
        var fourth = await pipe.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OpenNettyFrames.Acknowledgement, first);
        Assert.Equal(OpenNettyFrame.Parse("*1*1*21##"), second);
        Assert.Equal(OpenNettyFrames.NegativeAcknowledgement, third);
        Assert.Null(fourth);
    }

    [Fact]
    public async Task ReadAsync_DataBeforeFirstAsterisk_IsDiscarded()
    {
        using var pipe = CreateStreamPipe("garbage\r\n*#*1##");

        var frame = await pipe.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OpenNettyFrames.Acknowledgement, frame);
    }

    [Fact]
    public async Task ReadAsync_FrameSplitAcrossWrites_ReturnsFrameOnceComplete()
    {
        var duplex = new Pipe();
        using var pipe = new OpenNettyPipe(duplex.Reader, new Pipe().Writer);

        var pending = pipe.ReadAsync(TestContext.Current.CancellationToken);
        await duplex.Writer.WriteAsync("*1*1"u8.ToArray(), TestContext.Current.CancellationToken);
        await duplex.Writer.WriteAsync("*21#"u8.ToArray(), TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        await duplex.Writer.WriteAsync("#"u8.ToArray(), TestContext.Current.CancellationToken);

        var frame = await pending;

        Assert.Equal(OpenNettyFrame.Parse("*1*1*21##"), frame);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*1*1*21#")]
    [InlineData("no frame at all")]
    public async Task ReadAsync_StreamEndsWithoutCompleteFrame_ReturnsNull(string value)
    {
        using var pipe = CreateStreamPipe(value);

        var frame = await pipe.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Null(frame);
    }

    [Fact]
    public async Task ReadAsync_PendingReadCanceledByReader_ReturnsNull()
    {
        var duplex = new Pipe();
        using var pipe = new OpenNettyPipe(duplex.Reader, new Pipe().Writer);
        duplex.Reader.CancelPendingRead();

        var frame = await pipe.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Null(frame);
    }

    [Fact]
    public async Task ReadAsync_TokenAlreadyCanceled_ThrowsOperationCanceledException()
    {
        using var pipe = CreateStreamPipe("*#*1##");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pipe.ReadAsync(source.Token));
    }

    [Fact]
    public async Task ReadAsync_TokenCanceledWhileWaiting_ThrowsOperationCanceledException()
    {
        var duplex = new Pipe();
        using var pipe = new OpenNettyPipe(duplex.Reader, new Pipe().Writer);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = pipe.ReadAsync(source.Token);

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
    }

    [Fact]
    public async Task ReadAsync_ConcurrentRead_ThrowsInvalidOperationException()
    {
        var duplex = new Pipe();
        using var pipe = new OpenNettyPipe(duplex.Reader, new Pipe().Writer);
        var pending = pipe.ReadAsync(TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipe.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0037), exception.Message);
        await duplex.Writer.CompleteAsync();
        Assert.Null(await pending);
    }

    [Fact]
    public async Task ReadAsync_AfterCompletedRead_AllowsNewRead()
    {
        using var pipe = CreateStreamPipe("*#*1##*#*0##");

        await pipe.ReadAsync(TestContext.Current.CancellationToken);
        var frame = await pipe.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OpenNettyFrames.NegativeAcknowledgement, frame);
    }

    [Fact]
    public async Task ReadAsync_ReaderThrows_ReleasesReadLock()
    {
        var reader = Substitute.For<PipeReader>();
#pragma warning disable CA2012 // NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
        reader.ReadAsync(Arg.Any<CancellationToken>()).Returns(
            _ => throw new IOException(),
            _ => new ValueTask<ReadResult>(new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true)));
#pragma warning restore CA2012
        using var pipe = new OpenNettyPipe(reader, new Pipe().Writer);

        await Assert.ThrowsAsync<IOException>(async () => await pipe.ReadAsync(TestContext.Current.CancellationToken));
        var frame = await pipe.ReadAsync(TestContext.Current.CancellationToken);

        Assert.Null(frame);
    }

    [Fact]
    public async Task ReadAsync_Disposed_ThrowsObjectDisposedException()
    {
        var pipe = CreateStreamPipe("*#*1##");
        pipe.Dispose();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await pipe.ReadAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0036), exception.ObjectName);
    }

    [Fact]
    public async Task WriteAsync_Frame_WritesAsciiRepresentation()
    {
        using var stream = new MemoryStream();
        using var pipe = OpenNettyPipe.Create(stream);

        await pipe.WriteAsync(OpenNettyFrame.Parse("*1*1*21##"), TestContext.Current.CancellationToken);

        Assert.Equal("*1*1*21##", Encoding.ASCII.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task WriteAsync_MultipleFrames_WritesFramesInOrder()
    {
        using var stream = new MemoryStream();
        using var pipe = OpenNettyPipe.Create(stream);

        await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken);
        await pipe.WriteAsync(OpenNettyFrames.NegativeAcknowledgement, TestContext.Current.CancellationToken);

        Assert.Equal("*#*1##*#*0##", Encoding.ASCII.GetString(stream.ToArray()));
    }

    [Fact]
    public async Task WriteAsync_TokenAlreadyCanceled_ThrowsOperationCanceledException()
    {
        using var stream = new MemoryStream();
        using var pipe = OpenNettyPipe.Create(stream);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, source.Token));
        Assert.Empty(stream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_ConcurrentWrite_ThrowsInvalidOperationException()
    {
        var duplex = new Pipe(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1));
        using var pipe = new OpenNettyPipe(new Pipe().Reader, duplex.Writer);
        var pending = pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0038), exception.Message);
        var result = await duplex.Reader.ReadAsync(TestContext.Current.CancellationToken);
        duplex.Reader.AdvanceTo(result.Buffer.End);
        await pending;
    }

    [Fact]
    public async Task WriteAsync_WriterThrows_ReleasesWriteLock()
    {
        var writer = Substitute.For<PipeWriter>();
#pragma warning disable CA2012 // NSubstitute requires the ValueTask-returning call to be used as the receiver of Returns().
        writer.WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(
            _ => throw new IOException(),
            _ => new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: false)));
#pragma warning restore CA2012
        using var pipe = new OpenNettyPipe(new Pipe().Reader, writer);

        await Assert.ThrowsAsync<IOException>(
            async () => await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken));
        await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken);

        await writer.Received(2).WriteAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WriteAsync_Disposed_ThrowsObjectDisposedException()
    {
        using var stream = new MemoryStream();
        var pipe = OpenNettyPipe.Create(stream);
        pipe.Dispose();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await pipe.WriteAsync(OpenNettyFrames.Acknowledgement, TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0036), exception.ObjectName);
    }

    private static OpenNettyPipe CreateStreamPipe(string content)
        => OpenNettyPipe.Create(new MemoryStream(Encoding.ASCII.GetBytes(content)));

    private sealed class TestDuplexPipe(PipeReader input, PipeWriter output) : IDuplexPipe
    {
        public PipeReader Input { get; } = input;

        public PipeWriter Output { get; } = output;
    }
}
