/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyFrameTests
{
    [Fact]
    public void Constructor_FieldsArray_SetsFields()
    {
        var first = new OpenNettyField("1");
        var second = new OpenNettyField("2", "3");

        var frame = new OpenNettyFrame(first, second);

        Assert.Equal([first, second], frame.Fields);
    }

    [Fact]
    public void Constructor_ImmutableArray_SetsFields()
    {
        var fields = ImmutableArray.Create(new OpenNettyField("1"));

        var frame = new OpenNettyFrame(fields);

        Assert.Equal(fields, frame.Fields);
    }

    [Fact]
    public void Default_HasNoFields()
    {
        Assert.True(default(OpenNettyFrame).Fields.IsDefaultOrEmpty);
    }

    [Theory]
    [InlineData("*1##", new[] { "1" })]
    [InlineData("*1*2*3##", new[] { "1", "2", "3" })]
    [InlineData("*1*2#3*4##", new[] { "1", "2#3", "4" })]
    [InlineData("*#*1##", new[] { "#", "1" })]
    [InlineData("*#*0##", new[] { "#", "0" })]
    [InlineData("*1**3##", new[] { "1", "", "3" })]
    [InlineData("*1*2#*3##", new[] { "1", "2#", "3" })]
    [InlineData("*##", new[] { "" })]
    [InlineData("*#1*2##", new[] { "#1", "2" })]
    public void Parse_String_ReturnsExpectedFields(string value, string[] expected)
    {
        var frame = OpenNettyFrame.Parse(value);

        Assert.Equal(expected, frame.Fields.Select(field => field.ToString()), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("*1*2*3##")]
    [InlineData("*#*1##")]
    [InlineData("*1**3##")]
    [InlineData("*##")]
    public void Parse_ReadOnlyMemory_ReturnsSameResultAsString(string value)
    {
        var frame = OpenNettyFrame.Parse(new ReadOnlyMemory<byte>(Encoding.ASCII.GetBytes(value)));

        Assert.Equal(OpenNettyFrame.Parse(value), frame);
    }

    [Theory]
    [InlineData("*1*2*3##")]
    [InlineData("*#*1##")]
    [InlineData("*1**3##")]
    [InlineData("*##")]
    public void Parse_ReadOnlySequence_ReturnsSameResultAsString(string value)
    {
        var frame = OpenNettyFrame.Parse(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(value)));

        Assert.Equal(OpenNettyFrame.Parse(value), frame);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1*2##")]
    [InlineData("#*1##")]
    [InlineData(" *1##")]
    public void Parse_FrameNotStartingWithAsterisk_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("buffer", () => OpenNettyFrame.Parse(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0001), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*1##2")]
    [InlineData("*1*2##3")]
    [InlineData("*1*2###")]
    public void Parse_FrameWithDelimiterBeforeTheEnd_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("buffer", () => OpenNettyFrame.Parse(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0002), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("*1")]
    [InlineData("*1*2")]
    [InlineData("*1*2#")]
    [InlineData("*1#")]
    public void Parse_FrameNotEndingWithTwoHashes_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("buffer", () => OpenNettyFrame.Parse(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0003), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*1*")]
    [InlineData("*1*2*")]
    public void Parse_FrameEndingWithAsterisk_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("buffer", () => OpenNettyFrame.Parse(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0003), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FieldWithInvalidParameter_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>("value", () => OpenNettyFrame.Parse("*1*a##"));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*1##*2##")]
    [InlineData("*1*2##3*4##")]
    public void Parse_FieldWithConsecutiveHashes_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("buffer", () => OpenNettyFrame.Parse(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0005), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NullString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OpenNettyFrame.Parse((string) null!));
    }

    [Fact]
    public void Parse_MultiSegmentSequence_ReturnsFields()
    {
        var buffer = CreateSequence("*1*2", "#3*4", "##");

        var frame = OpenNettyFrame.Parse(in buffer);

        Assert.Equal(["1", "2#3", "4"], frame.Fields.Select(field => field.ToString()), StringComparer.Ordinal);
    }

    [Fact]
    public void Parse_MultiSegmentSequenceWithEndDelimiterSplitAcrossSegments_ReturnsFields()
    {
        var buffer = CreateSequence("*1*2#", "#");

        var frame = OpenNettyFrame.Parse(in buffer);

        Assert.Equal(["1", "2"], frame.Fields.Select(field => field.ToString()), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("*1##", "*1##", true)]
    [InlineData("*1##", "*2##", false)]
    [InlineData("*1*2##", "*1*2##", true)]
    [InlineData("*1*2##", "*1*3##", false)]
    [InlineData("*1*2##", "*1*2*3##", false)]
    [InlineData("*1*2*3##", "*1*2##", false)]
    [InlineData("*#*1##", "*#*1##", true)]
    [InlineData("*#*1##", "*#*0##", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "*1##", false)]
    public void Equals_ReturnsExpectedResult(string? left, string? right, bool expected)
    {
        var first = Create(left);
        var second = Create(right);

        Assert.Equal(expected, first.Equals(second));
        Assert.Equal(expected, first.Equals((object) second));
        Assert.Equal(expected, first == second);
        Assert.Equal(!expected, first != second);
    }

    [Fact]
    public void Equals_NonEmptyFrameAndDefaultFrame_ReturnsFalse()
    {
        var frame = OpenNettyFrame.Parse("*1##");

        Assert.False(frame.Equals(default(OpenNettyFrame)));
        Assert.True(frame != default);
    }

    [Fact]
    public void Equals_EmptyAndDefaultFields_ReturnsTrue()
    {
        var empty = new OpenNettyFrame(ImmutableArray<OpenNettyField>.Empty);

        Assert.Equal(default(OpenNettyFrame), empty);
        Assert.Equal(empty, default(OpenNettyFrame));
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        Assert.False(OpenNettyFrame.Parse("*1##").Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        Assert.False(OpenNettyFrame.Parse("*1##").Equals("*1##"));
    }

    [Theory]
    [InlineData("*1##", "*1##")]
    [InlineData("*1*2##", "*1*2##")]
    [InlineData("*#*1##", "*#*1##")]
    [InlineData(null, null)]
    public void GetHashCode_EqualFrames_ReturnsSameHashCode(string? left, string? right)
    {
        Assert.Equal(Create(left).GetHashCode(), Create(right).GetHashCode());
    }

    [Theory]
    [InlineData("*1##", "*2##")]
    [InlineData("*1##", "*1*2##")]
    [InlineData("*#*1##", "*#*0##")]
    public void GetHashCode_DifferentFrames_ReturnsDifferentHashCodes(string left, string right)
    {
        Assert.NotEqual(Create(left).GetHashCode(), Create(right).GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultFrame_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyFrame).GetHashCode());
    }

    [Fact]
    public void ToString_DefaultFrame_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettyFrame).ToString());
    }

    [Theory]
    [InlineData("*1##")]
    [InlineData("*1*2*3##")]
    [InlineData("*1*2#3*4##")]
    [InlineData("*#*1##")]
    [InlineData("*1**3##")]
    [InlineData("*##")]
    public void ToString_ReturnsRawFrame(string value)
    {
        Assert.Equal(value, OpenNettyFrame.Parse(value).ToString());
    }

    [Fact]
    public void ToString_FrameCreatedFromFields_ReturnsRawFrame()
    {
        var frame = new OpenNettyFrame(new OpenNettyField("1"), new OpenNettyField("2", "3"));

        Assert.Equal("*1*2#3##", frame.ToString());
    }

    private static OpenNettyFrame Create(string? value) => value is null ? default : OpenNettyFrame.Parse(value);

    private static ReadOnlySequence<byte> CreateSequence(params string[] segments)
    {
        MemorySegment? first = null;
        MemorySegment? last = null;

        foreach (var segment in segments)
        {
            last = last is null ? first = new(Encoding.ASCII.GetBytes(segment)) : last.Append(Encoding.ASCII.GetBytes(segment));
        }

        return new ReadOnlySequence<byte>(first!, 0, last!, last!.Memory.Length);
    }

    private sealed class MemorySegment : ReadOnlySequenceSegment<byte>
    {
        public MemorySegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public MemorySegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new MemorySegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;

            return next;
        }
    }
}
