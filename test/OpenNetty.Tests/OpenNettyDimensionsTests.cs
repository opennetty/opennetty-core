/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyDimensionsTests
{
    public static TheoryData<string> DimensionNames { get; } = [.. GetDimensionFields().Select(GetFullName)];

    [Fact]
    public void Dimensions_AreExposedForEveryCategoryClass()
    {
        var categories = typeof(OpenNettyDimensions).GetNestedTypes(BindingFlags.Public).Select(type => type.Name);

        Assert.Equivalent(
            new[]
            {
                nameof(OpenNettyDimensions.Lighting),
                nameof(OpenNettyDimensions.Automation),
                nameof(OpenNettyDimensions.TemperatureControl),
                nameof(OpenNettyDimensions.Management),
                nameof(OpenNettyDimensions.Diagnostics)
            },
            categories);
    }

    [Theory]
    [MemberData(nameof(DimensionNames))]
    public void Dimension_IsInitializedWithoutParameters(string fullName)
    {
        var dimension = GetDimension(fullName);

        Assert.NotNull(dimension.Value);
        Assert.NotEqual(default, dimension.Category);
        Assert.True(dimension.Parameters.IsDefaultOrEmpty);
    }

    [Theory]
    [MemberData(nameof(DimensionNames))]
    public void Dimension_BelongsToTheCategoryMatchingItsDeclaringClass(string fullName)
    {
        var dimension = GetDimension(fullName);
        var expected = (OpenNettyCategory) typeof(OpenNettyCategories).GetField(fullName.Split('.')[0])!.GetValue(null)!;

        Assert.Equal(expected, dimension.Category);
    }

    [Fact]
    public void Dimensions_AreUniqueWithinEachCategory()
    {
        var duplicates = GetDimensionFields()
            .Select(field => (Name: GetFullName(field), Dimension: (OpenNettyDimension) field.GetValue(null)!))
            .GroupBy(entry => entry.Dimension)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(entry => entry.Name)));

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("Lighting.DimmerLevelSpeed", "1", "1")]
    [InlineData("Lighting.DimmerStatus", "1", "4")]
    [InlineData("Lighting.DimmerStep", "1", "10")]
    [InlineData("Automation.ShutterStatus", "2", "10")]
    [InlineData("Automation.ShutterGoToLevel", "2", "11")]
    [InlineData("TemperatureControl.WaterHeatingMode", "4", "40")]
    [InlineData("TemperatureControl.SmartMeterIndexes", "4", "43")]
    [InlineData("Management.Time", "13", "0")]
    [InlineData("Management.Date", "13", "1")]
    [InlineData("Management.IpAddress", "13", "10")]
    [InlineData("Management.MacAddress", "13", "12")]
    public void Dimension_HasExpectedCategoryAndValue(string fullName, string category, string value)
    {
        var dimension = GetDimension(fullName);

        Assert.Equal(category, dimension.Category.Value);
        Assert.Equal(value, dimension.Value);
    }

    private static IEnumerable<FieldInfo> GetDimensionFields()
        => typeof(OpenNettyDimensions).GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(OpenNettyDimension));

    private static string GetFullName(FieldInfo field) => $"{field.DeclaringType!.Name}.{field.Name}";

    private static OpenNettyDimension GetDimension(string fullName)
        => (OpenNettyDimension) GetDimensionFields().Single(field => string.Equals(GetFullName(field), fullName, StringComparison.Ordinal)).GetValue(null)!;
}
