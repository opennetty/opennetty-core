/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyCommandsTests
{
    public static TheoryData<string> CommandNames { get; } = [.. GetCommandFields().Select(GetFullName)];

    [Fact]
    public void Commands_AreExposedForEveryCategoryClass()
    {
        var categories = typeof(OpenNettyCommands).GetNestedTypes(BindingFlags.Public).Select(type => type.Name);

        Assert.Equivalent(
            new[]
            {
                nameof(OpenNettyCommands.Lighting),
                nameof(OpenNettyCommands.Automation),
                nameof(OpenNettyCommands.TemperatureControl),
                nameof(OpenNettyCommands.Management),
                nameof(OpenNettyCommands.ActuatorsControl),
                nameof(OpenNettyCommands.ScenariosPlus),
                nameof(OpenNettyCommands.Diagnostics)
            },
            categories);
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void Command_IsInitializedWithoutParameters(string fullName)
    {
        var command = GetCommand(fullName);

        Assert.NotNull(command.Value);
        Assert.NotEqual(default, command.Category);
        Assert.True(command.Parameters.IsDefaultOrEmpty);
    }

    [Theory]
    [MemberData(nameof(CommandNames))]
    public void Command_BelongsToTheCategoryMatchingItsDeclaringClass(string fullName)
    {
        var command = GetCommand(fullName);
        var expected = (OpenNettyCategory) typeof(OpenNettyCategories).GetField(fullName.Split('.')[0])!.GetValue(null)!;

        Assert.Equal(expected, command.Category);
    }

    [Fact]
    public void Commands_AreUniqueWithinEachCategory()
    {
        var duplicates = GetCommandFields()
            .Select(field => (Name: GetFullName(field), Command: (OpenNettyCommand) field.GetValue(null)!))
            .GroupBy(entry => entry.Command)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(entry => entry.Name)));

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("Lighting.Off", "1", "0")]
    [InlineData("Lighting.On", "1", "1")]
    [InlineData("Lighting.On20", "1", "2")]
    [InlineData("Lighting.On100", "1", "10")]
    [InlineData("Automation.Stop", "2", "0")]
    [InlineData("Automation.Up", "2", "1")]
    [InlineData("Automation.Down", "2", "2")]
    [InlineData("TemperatureControl.WirePilotSetpointMode", "4", "50")]
    [InlineData("Management.BatteryWeak", "13", "24")]
    [InlineData("Management.CreateZigbeeNetwork", "13", "30")]
    [InlineData("ActuatorsControl.Disable", "14", "0")]
    [InlineData("ActuatorsControl.Enable", "14", "1")]
    [InlineData("ScenariosPlus.Action", "25", "11")]
    public void Command_HasExpectedCategoryAndValue(string fullName, string category, string value)
    {
        var command = GetCommand(fullName);

        Assert.Equal(category, command.Category.Value);
        Assert.Equal(value, command.Value);
    }

    private static IEnumerable<FieldInfo> GetCommandFields()
        => typeof(OpenNettyCommands).GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(OpenNettyCommand));

    private static string GetFullName(FieldInfo field) => $"{field.DeclaringType!.Name}.{field.Name}";

    private static OpenNettyCommand GetCommand(string fullName)
        => (OpenNettyCommand) GetCommandFields().Single(field => string.Equals(GetFullName(field), fullName, StringComparison.Ordinal)).GetValue(null)!;
}
