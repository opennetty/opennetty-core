/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using static OpenNetty.OpenNettyConstants;

namespace OpenNetty;

/// <summary>
/// Represents a duplex pipe from which OpenWebNet frames can be read from and written to.
/// </summary>
public class OpenNettyPipe : IDisposable
{
    private readonly PipeReader _reader;
    private SemaphoreSlim? _readLock = new(initialCount: 1, maxCount: 1);
    private readonly PipeWriter _writer;
    private SemaphoreSlim? _writeLock = new(initialCount: 1, maxCount: 1);

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyPipe"/> class.
    /// </summary>
    /// <param name="reader">The pipe reader.</param>
    /// <param name="writer">The pipe writer.</param>
    /// <exception cref="ArgumentNullException">The pipe reader or writer is null.</exception>
    public OpenNettyPipe(PipeReader reader, PipeWriter writer)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <summary>
    /// Creates a new OpenNetty pipe wrapping the specified duplex pipe.
    /// </summary>
    /// <param name="pipe">The duplex pipe.</param>
    /// <returns>The OpenNetty pipe.</returns>
    public static OpenNettyPipe Create(IDuplexPipe pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);

        return new OpenNettyPipe(pipe.Input, pipe.Output);
    }

    /// <summary>
    /// Creates a new OpenNetty pipe wrapping the specified stream.
    /// </summary>
    /// <remarks>Note: the stream is not closed when this instance is disposed.</remarks>
    /// <param name="stream">The stream.</param>
    /// <returns>The OpenNetty pipe.</returns>
    public static OpenNettyPipe Create(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0038), nameof(stream));
        }

        if (!stream.CanWrite)
        {
            throw new ArgumentException(SR.GetResourceString(SR.ID0039), nameof(stream));
        }

        return new OpenNettyPipe(
            reader: PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true)),
            writer: PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true)));
    }

    /// <summary>
    /// Disposes the current instance.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _readLock, null) is SemaphoreSlim readLock)
        {
            readLock.Dispose();
        }

        if (Interlocked.Exchange(ref _writeLock, null) is SemaphoreSlim writeLock)
        {
            writeLock.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reads the next frame available.
    /// </summary>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> that can be used to monitor the asynchronous operation and whose result
    /// returns the next frame available, or <see langword="null"/> if the end of the data stream has been reached.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The current instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">A concurrent read operation is already being processed.</exception>
    public async ValueTask<OpenNettyFrame?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_readLock is not SemaphoreSlim semaphore)
        {
            throw new ObjectDisposedException(SR.GetResourceString(SR.ID0040));
        }

        if (!await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0041));
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _reader.ReadAsync(cancellationToken);
                if (result.IsCanceled)
                {
                    break;
                }

                var buffer = result.Buffer;
                var consumed = buffer.Start;
                var examined = buffer.End;

                try
                {
                    if (TryReadFrame(ref buffer, out OpenNettyFrame? frame))
                    {
                        consumed = buffer.Start;
                        examined = consumed;

                        return frame.Value;
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }

                finally
                {
                    _reader.AdvanceTo(consumed, examined);
                }
            }

            return null;
        }

        finally
        {
            semaphore.Release();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static bool TryReadFrame(ref ReadOnlySequence<byte> buffer, [NotNullWhen(true)] out OpenNettyFrame? frame)
        {
            var reader = new SequenceReader<byte>(buffer);

            if (!reader.TryAdvanceTo(Separators.Asterisk[0], advancePastDelimiter: false))
            {
                frame = null;
                return false;
            }

            var start = reader.Position;

            // Note: the ReadOnlySequence<byte> returned by TryReadTo() doesn't include the end delimiter (##)
            // that is part of an OpenWebNet frame and is required by OpenNettyFrame.Parse(). To work around
            // this limitation, the returned sequence is ignored and a slice is done on the original sequence.
            if (!reader.TryReadTo(out ReadOnlySequence<byte> _, Delimiters.End, advancePastDelimiter: true))
            {
                frame = null;
                return false;
            }

            frame = OpenNettyFrame.Parse(buffer.Slice(start, reader.Position));
            buffer = buffer.Slice(reader.Position);
            return true;
        }
    }

    /// <summary>
    /// Writes the specified frame.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> that can be used to abort the operation.</param>
    /// <returns>
    /// A <see cref="ValueTask"/> that can be used to monitor the asynchronous operation.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The current instance has been disposed.</exception>
    /// <exception cref="InvalidOperationException">A concurrent write operation is already being processed.</exception>
    public async ValueTask WriteAsync(OpenNettyFrame frame, CancellationToken cancellationToken = default)
    {
        if (_writeLock is not SemaphoreSlim semaphore)
        {
            throw new ObjectDisposedException(SR.GetResourceString(SR.ID0040));
        }

        if (!await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException(SR.GetResourceString(SR.ID0042));
        }

        try
        {
            await _writer.WriteAsync(Encoding.ASCII.GetBytes(frame.ToString()), cancellationToken);
        }

        finally
        {
            semaphore.Release();
        }
    }
}
