/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettySettingsTests
{
    public static TheoryData<string> SettingNames { get; } = [.. GetSettingFields().Select(field => field.Name)];

    public static TheoryData<string> ConstantClassNames { get; } =
    [
        nameof(OpenNettySettings.FunctionTypes),
        nameof(OpenNettySettings.HomeAssistantDeviceClasses.Covers),
        nameof(OpenNettySettings.HomeAssistantDeviceClasses.Events),
        nameof(OpenNettySettings.HomeAssistantDeviceClasses.Switches),
        nameof(OpenNettySettings.HomeAssistantEntityTypes),
        nameof(OpenNettySettings.OnOffCommandTypes),
        nameof(OpenNettySettings.SwitchModes),
        nameof(OpenNettySettings.ZigbeeDeviceTypes)
    ];

    [Fact]
    public void Settings_AreExposed()
    {
        Assert.NotEmpty(GetSettingFields());
    }

    [Theory]
    [MemberData(nameof(SettingNames))]
    public void Setting_IsInitializedWithNonEmptyName(string fieldName)
    {
        var setting = GetSetting(fieldName);

        Assert.NotEqual(default, setting);
        Assert.False(string.IsNullOrWhiteSpace(setting.Name));
        Assert.Equal(setting.Name.Trim(), setting.Name);
    }

    [Fact]
    public void Settings_HaveUniqueNames()
    {
        var names = GetSettingFields().Select(field => GetSetting(field.Name).Name);

        Assert.Equal(names.Count(), names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Theory]
    [InlineData(nameof(OpenNettySettings.ActionValidation), "Action validation")]
    [InlineData(nameof(OpenNettySettings.ClockSynchronization), "Clock synchronization")]
    [InlineData(nameof(OpenNettySettings.FunctionType), "Function type")]
    [InlineData(nameof(OpenNettySettings.HomeAssistantDiscovery), "Home Assistant discovery")]
    [InlineData(nameof(OpenNettySettings.ZigbeeDeviceType), "Zigbee device type")]
    [InlineData(nameof(OpenNettySettings.ZigbeeSupervisionMode), "Zigbee supervision mode")]
    public void Setting_HasExpectedName(string fieldName, string expected)
    {
        Assert.Equal(expected, GetSetting(fieldName).Name);
    }

    [Theory]
    [MemberData(nameof(ConstantClassNames))]
    public void ConstantValues_AreNonEmptyAndUnique(string className)
    {
        var values = GetConstantClasses()
            .Single(type => string.Equals(type.Name, className, StringComparison.Ordinal))
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string) field.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(values);
        Assert.All(values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(OpenNettySettings.FunctionTypes.LightActuator, "Light actuator")]
    [InlineData(OpenNettySettings.FunctionTypes.AutomationActuator, "Automation actuator")]
    [InlineData(OpenNettySettings.HomeAssistantDeviceClasses.Covers.Shutter, "shutter")]
    [InlineData(OpenNettySettings.HomeAssistantDeviceClasses.Events.Doorbell, "doorbell")]
    [InlineData(OpenNettySettings.HomeAssistantDeviceClasses.Switches.Outlet, "outlet")]
    [InlineData(OpenNettySettings.HomeAssistantEntityTypes.Light, "light")]
    [InlineData(OpenNettySettings.OnOffCommandTypes.Toggle, "Toggle")]
    [InlineData(OpenNettySettings.SwitchModes.PushButton, "Push button")]
    [InlineData(OpenNettySettings.ZigbeeDeviceTypes.EndDevice, "End device")]
    public void ConstantValue_HasExpectedValue(string actual, string expected)
    {
        Assert.Equal(expected, actual);
    }

    private static IEnumerable<FieldInfo> GetSettingFields()
        => typeof(OpenNettySettings).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpenNettySetting));

    private static OpenNettySetting GetSetting(string fieldName)
        => (OpenNettySetting) typeof(OpenNettySettings).GetField(fieldName, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;

    private static IEnumerable<Type> GetConstantClasses()
        => typeof(OpenNettySettings).GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetNestedTypes(BindingFlags.Public).Prepend(type))
            .Where(type => type.GetFields(BindingFlags.Public | BindingFlags.Static).Length is not 0);
}
