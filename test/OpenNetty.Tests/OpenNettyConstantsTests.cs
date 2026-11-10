/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Text;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyConstantsTests
{
    [Fact]
    public void Delimiters_Start_IsAnAsterisk()
    {
        Assert.Equal("*", Encoding.ASCII.GetString(OpenNettyConstants.Delimiters.Start));
    }

    [Fact]
    public void Delimiters_End_IsTwoHashes()
    {
        Assert.Equal("##", Encoding.ASCII.GetString(OpenNettyConstants.Delimiters.End));
    }

    [Fact]
    public void Separators_Asterisk_IsAnAsterisk()
    {
        Assert.Equal("*", Encoding.ASCII.GetString(OpenNettyConstants.Separators.Asterisk));
    }

    [Fact]
    public void Separators_Hash_IsAHash()
    {
        Assert.Equal("#", Encoding.ASCII.GetString(OpenNettyConstants.Separators.Hash));
    }
}
