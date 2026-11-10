/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyScenarioTests
{
    [Fact]
    public void Initializer_ValidValues_SetsProperties()
    {
        var scenario = CreateScenario("Living room", 12);

        Assert.Equal("Living room", scenario.EndpointName);
        Assert.Equal(12, scenario.FunctionCode);
    }

    [Theory]
    [InlineData("Living room", 12, "Living room", 12, true)]
    [InlineData("Living room", 12, "LIVING ROOM", 12, true)]
    [InlineData("Living room", 12, "Living room", 13, false)]
    [InlineData("Living room", 12, "Kitchen", 12, false)]
    public void Equals_Scenario_ComparesEndpointNameIgnoringCaseAndFunctionCode(
        string leftName, byte leftCode, string rightName, byte rightCode, bool expected)
    {
        var left = CreateScenario(leftName, leftCode);
        var right = CreateScenario(rightName, rightCode);

        Assert.Equal(expected, left.Equals(right));
        Assert.Equal(expected, left.Equals((object) right));
        Assert.Equal(expected, left == right);
        Assert.Equal(!expected, left != right);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var scenario = CreateScenario("Living room", 12);

        var sameInstance = scenario;

        Assert.True(scenario.Equals(sameInstance));
        Assert.True(scenario == sameInstance);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var scenario = CreateScenario("Living room", 12);

        Assert.False(scenario.Equals((OpenNettyScenario?) null));
        Assert.False(scenario.Equals((object?) null));
    }

    [Fact]
    public void Equals_UnrelatedObject_ReturnsFalse()
    {
        var scenario = CreateScenario("Living room", 12);

        Assert.False(scenario.Equals("Living room"));
    }

    [Fact]
    public void EqualityOperators_NullOperands_AreHandled()
    {
        var scenario = CreateScenario("Living room", 12);

        Assert.True((OpenNettyScenario?) null == null);
        Assert.False((OpenNettyScenario?) null != null);
        Assert.False(scenario == null);
        Assert.False(null == scenario);
        Assert.True(scenario != null);
        Assert.True(null != scenario);
    }

    [Fact]
    public void GetHashCode_IdenticalScenarios_ReturnsSameHashCode()
    {
        var left = CreateScenario("Living room", 12);
        var right = CreateScenario("Living room", 12);

        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_DifferentFunctionCodes_ReturnsDifferentHashCodes()
    {
        var left = CreateScenario("Living room", 12);
        var right = CreateScenario("Living room", 13);

        Assert.NotEqual(left.GetHashCode(), right.GetHashCode());
    }

    [Fact]
    public void GetHashCode_EqualScenariosDifferingByCase_ReturnsSameHashCode()
    {
        var left = CreateScenario("Living room", 12);
        var right = CreateScenario("LIVING ROOM", 12);

        Assert.True(left.Equals(right));
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    private static OpenNettyScenario CreateScenario(string endpointName, byte functionCode)
        => new() { EndpointName = endpointName, FunctionCode = functionCode };
}
