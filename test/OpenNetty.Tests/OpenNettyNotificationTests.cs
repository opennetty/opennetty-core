/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyNotificationTests
{
    [Fact]
    public void Notification_IsAbstract()
    {
        Assert.True(typeof(OpenNettyNotification).IsAbstract);
    }

    [Fact]
    public void Gateway_ReturnsTheGatewayAssignedDuringInitialization()
    {
        var gateway = CreateUninitialized<OpenNettyGateway>();

        var notification = new TestNotification { Gateway = gateway };

        Assert.Same(gateway, notification.Gateway);
    }

    [Fact]
    public void Gateway_IsRequiredAndInitOnly()
    {
        var property = typeof(OpenNettyNotification).GetProperty(nameof(OpenNettyNotification.Gateway))!;

        Assert.NotNull(property.GetCustomAttribute<RequiredMemberAttribute>());
        Assert.Contains(typeof(IsExternalInit), property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
    }

    private static T CreateUninitialized<T>() where T : class
        => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));

    private sealed class TestNotification : OpenNettyNotification;
}
