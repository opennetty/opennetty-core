/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyTransactionTests
{
    [Fact]
    public void Constructor_SetsIdentifier()
    {
        var identifier = Guid.NewGuid();

        var transaction = new OpenNettyTransaction(identifier);

        Assert.Equal(identifier, transaction.Identifier);
    }

    [Fact]
    public void Default_HasEmptyIdentifier()
    {
        Assert.Equal(Guid.Empty, default(OpenNettyTransaction).Identifier);
    }

    [Fact]
    public void Create_ReturnsTransactionWithNonEmptyIdentifier()
    {
        var transaction = OpenNettyTransaction.Create();

        Assert.NotEqual(Guid.Empty, transaction.Identifier);
    }

    [Fact]
    public void Create_ReturnsDistinctTransactionsOnEachCall()
    {
        var first = OpenNettyTransaction.Create();
        var second = OpenNettyTransaction.Create();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_SameIdentifier_ReturnsTrue()
    {
        var identifier = Guid.NewGuid();
        var first = new OpenNettyTransaction(identifier);
        var second = new OpenNettyTransaction(identifier);

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object) second));
        Assert.True(first == second);
        Assert.False(first != second);
    }

    [Fact]
    public void Equals_DifferentIdentifiers_ReturnsFalse()
    {
        var first = new OpenNettyTransaction(Guid.NewGuid());
        var second = new OpenNettyTransaction(Guid.NewGuid());

        Assert.False(first.Equals(second));
        Assert.False(first.Equals((object) second));
        Assert.False(first == second);
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        Assert.False(OpenNettyTransaction.Create().Equals(null));
    }

    [Fact]
    public void Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        var identifier = Guid.NewGuid();

        Assert.False(new OpenNettyTransaction(identifier).Equals(identifier));
    }

    [Fact]
    public void GetHashCode_SameIdentifier_ReturnsSameHashCode()
    {
        var identifier = Guid.NewGuid();

        Assert.Equal(new OpenNettyTransaction(identifier).GetHashCode(), new OpenNettyTransaction(identifier).GetHashCode());
    }

    [Fact]
    public void GetHashCode_ReturnsIdentifierHashCode()
    {
        var identifier = Guid.NewGuid();

        Assert.Equal(identifier.GetHashCode(), new OpenNettyTransaction(identifier).GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsIdentifierRepresentation()
    {
        var identifier = Guid.NewGuid();

        Assert.Equal(identifier.ToString(), new OpenNettyTransaction(identifier).ToString());
    }
}
