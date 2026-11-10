/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyCapabilitiesTests
{
    public static TheoryData<string> CapabilityNames { get; } = [.. GetCapabilityFields().Select(field => field.Name)];

    [Fact]
    public void Capabilities_AreExposed()
    {
        Assert.NotEmpty(GetCapabilityFields());
    }

    [Theory]
    [MemberData(nameof(CapabilityNames))]
    public void Capability_IsInitializedWithNonEmptyName(string fieldName)
    {
        var capability = GetCapability(fieldName);

        Assert.NotEqual(default, capability);
        Assert.False(string.IsNullOrWhiteSpace(capability.Name));
        Assert.Equal(capability.Name.Trim(), capability.Name);
    }

    [Fact]
    public void Capabilities_HaveUniqueNames()
    {
        var names = GetCapabilityFields().Select(field => GetCapability(field.Name).Name);

        Assert.Equal(names.Count(), names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(nameof(OpenNettyCapabilities.ActionScenarioActivation), "Action scenario activation")]
    [InlineData(nameof(OpenNettyCapabilities.BasicDimmingControl), "Basic dimming control")]
    [InlineData(nameof(OpenNettyCapabilities.OnOffSwitchControl), "On/off switch control")]
    [InlineData(nameof(OpenNettyCapabilities.OpenWebNetGateway), "OpenWebNet gateway")]
    public void Capability_HasExpectedName(string fieldName, string expected)
    {
        Assert.Equal(expected, GetCapability(fieldName).Name);
    }

    private static IEnumerable<FieldInfo> GetCapabilityFields()
        => typeof(OpenNettyCapabilities).GetFields(BindingFlags.Public | BindingFlags.Static);

    private static OpenNettyCapability GetCapability(string fieldName)
        => (OpenNettyCapability) typeof(OpenNettyCapabilities).GetField(fieldName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
}
