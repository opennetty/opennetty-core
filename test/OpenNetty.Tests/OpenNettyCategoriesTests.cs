/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyCategoriesTests
{
    [Theory]
    [InlineData(nameof(OpenNettyCategories.Lighting), "1")]
    [InlineData(nameof(OpenNettyCategories.Automation), "2")]
    [InlineData(nameof(OpenNettyCategories.TemperatureControl), "4")]
    [InlineData(nameof(OpenNettyCategories.Management), "13")]
    [InlineData(nameof(OpenNettyCategories.ActuatorsControl), "14")]
    [InlineData(nameof(OpenNettyCategories.Scenarios), "15")]
    [InlineData(nameof(OpenNettyCategories.ScenariosPlus), "25")]
    [InlineData(nameof(OpenNettyCategories.Diagnostics), "1000")]
    public void Category_HasExpectedWhoValue(string name, string expected)
    {
        var category = GetCategory(name);

        Assert.Equal(expected, category.Value);
        Assert.Empty(category.Parameters);
    }

    [Fact]
    public void Categories_AreAllExposedAsInitializedCategories()
    {
        var names = typeof(OpenNettyCategories)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.Name);

        Assert.All(names, name => Assert.NotNull(GetCategory(name).Value));
    }

    [Fact]
    public void Categories_HaveUniqueValues()
    {
        var values = typeof(OpenNettyCategories)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => ((OpenNettyCategory) field.GetValue(null)!).Value);

        Assert.Equal(values.Count(), values.Distinct(StringComparer.Ordinal).Count());
    }

    private static OpenNettyCategory GetCategory(string name)
        => (OpenNettyCategory) typeof(OpenNettyCategories).GetField(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
}
