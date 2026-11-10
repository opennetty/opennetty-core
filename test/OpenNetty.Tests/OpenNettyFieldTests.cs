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

public sealed class OpenNettyFieldTests
{
    [Fact]
    public void Constructor_ParametersArray_SetsParameters()
    {
        var field = new OpenNettyField("1", "02");

        Assert.Equal(["1", "02"], field.Parameters.Select(parameter => parameter.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void Constructor_ImmutableArray_SetsParameters()
    {
        var parameters = ImmutableArray.Create<OpenNettyParameter>("3", "4");

        var field = new OpenNettyField(parameters);

        Assert.Equal(parameters, field.Parameters);
    }

    [Fact]
    public void Default_HasNoParameters()
    {
        Assert.True(default(OpenNettyField).Parameters.IsDefaultOrEmpty);
    }

    [Theory]
    [InlineData("", new[] { "" })]
    [InlineData("1", new[] { "1" })]
    [InlineData("12", new[] { "12" })]
    [InlineData("1#2", new[] { "1", "2" })]
    [InlineData("1#2#3", new[] { "1", "2", "3" })]
    [InlineData("1#", new[] { "1", "" })]
    [InlineData("#", new[] { "", "" })]
    [InlineData("#1", new[] { "", "1" })]
    [InlineData("1#2#", new[] { "1", "2", "" })]
    [InlineData("1*2", new[] { "1", "2" })]
    public void Parse_String_ReturnsExpectedParameters(string value, string[] expected)
    {
        var field = OpenNettyField.Parse(value);

        Assert.Equal(expected, field.Parameters.Select(parameter => parameter.Value), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("1#2#3")]
    [InlineData("1#")]
    [InlineData("#1")]
    public void Parse_ReadOnlyMemory_ReturnsSameResultAsString(string value)
    {
        var field = OpenNettyField.Parse(new ReadOnlyMemory<byte>(Encoding.ASCII.GetBytes(value)));

        Assert.Equal(OpenNettyField.Parse(value), field);
    }

    [Theory]
    [InlineData("1#2#3")]
    [InlineData("1#")]
    [InlineData("#1")]
    public void Parse_ReadOnlySequence_ReturnsSameResultAsString(string value)
    {
        var field = OpenNettyField.Parse(new ReadOnlySequence<byte>(Encoding.ASCII.GetBytes(value)));

        Assert.Equal(OpenNettyField.Parse(value), field);
    }

    [Theory]
    [InlineData("1##2")]
    [InlineData("##")]
    [InlineData("1##")]
    public void Parse_ConsecutiveHashes_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("buffer", () => OpenNettyField.Parse(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0005), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("1#a")]
    [InlineData("a#1")]
    public void Parse_NonDigitParameter_ThrowsArgumentException(string value)
    {
        var exception = Assert.Throws<ArgumentException>("value", () => OpenNettyField.Parse(value));

        Assert.StartsWith(SR.GetResourceString(SR.ID0004), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_NullString_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => OpenNettyField.Parse((string) null!));
    }

    [Fact]
    public void Parse_MultiSegmentSequenceWithoutSeparator_ReturnsWholeParameter()
    {
        var buffer = CreateSequence("1", "2");

        var field = OpenNettyField.Parse(in buffer);

        Assert.Equal(["12"], field.Parameters.Select(parameter => parameter.Value), StringComparer.Ordinal);
    }

    [Fact]
    public void Parse_MultiSegmentSequence_ReturnsParametersSplitOnSeparators()
    {
        var buffer = CreateSequence("1#", "2#", "3");

        var field = OpenNettyField.Parse(in buffer);

        Assert.Equal(["1", "2", "3"], field.Parameters.Select(parameter => parameter.Value), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("1", "1", true)]
    [InlineData("1", "2", false)]
    [InlineData("1#2", "1#2", true)]
    [InlineData("1#2", "1#3", false)]
    [InlineData("1#2", "1#2#3", false)]
    [InlineData("1#2#3", "1#2", false)]
    [InlineData("1", "1#2", false)]
    [InlineData("1#", "1#", true)]
    [InlineData("1#", "1", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "1", false)]
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
    public void Equals_NonEmptyFieldAndDefaultField_ReturnsFalse()
    {
        var field = new OpenNettyField("1");

        Assert.False(field.Equals(default(OpenNettyField)));
        Assert.True(field != default);
    }

    [Fact]
    public void Equals_EmptyAndDefaultParameters_ReturnsTrue()
    {
        var empty = new OpenNettyField(ImmutableArray<OpenNettyParameter>.Empty);

        Assert.Equal(default, empty);
        Assert.Equal(empty, default);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        Assert.False(new OpenNettyField("1").Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        Assert.False(new OpenNettyField("1").Equals("1"));
    }

    [Theory]
    [InlineData("1", "1")]
    [InlineData("1#2", "1#2")]
    [InlineData("1#2#3", "1#2#3")]
    [InlineData(null, null)]
    public void GetHashCode_EqualFields_ReturnsSameHashCode(string? left, string? right)
    {
        Assert.Equal(Create(left).GetHashCode(), Create(right).GetHashCode());
    }

    [Theory]
    [InlineData("1", "2")]
    [InlineData("1", "1#2")]
    [InlineData("1#2", "2#1")]
    public void GetHashCode_DifferentFields_ReturnsDifferentHashCodes(string left, string right)
    {
        Assert.NotEqual(Create(left).GetHashCode(), Create(right).GetHashCode());
    }

    [Fact]
    public void GetHashCode_DefaultField_ReturnsZero()
    {
        Assert.Equal(0, default(OpenNettyField).GetHashCode());
    }

    [Fact]
    public void ToString_DefaultField_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, default(OpenNettyField).ToString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1#2")]
    [InlineData("1#2#3")]
    [InlineData("1#")]
    [InlineData("#")]
    [InlineData("#1")]
    public void ToString_ReturnsHashSeparatedParameters(string value)
    {
        Assert.Equal(value, Create(value).ToString());
        Assert.Equal(value, OpenNettyField.Parse(value).ToString());
    }

    private static OpenNettyField Create(string? text)
    {
        if (text is null)
        {
            return default;
        }

        return new OpenNettyField([.. text.Split('#').Select(value => new OpenNettyParameter(value))]);
    }

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
