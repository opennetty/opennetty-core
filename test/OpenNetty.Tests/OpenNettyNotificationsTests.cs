/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyNotificationsTests
{
    private static readonly Type[] AllNotificationTypes =
    [
        typeof(OpenNettyNotifications.MessageError),
        typeof(OpenNettyNotifications.MessageReady),
        typeof(OpenNettyNotifications.MessageReceived),
        typeof(OpenNettyNotifications.MessageSent),
        typeof(OpenNettyNotifications.SessionClosed),
        typeof(OpenNettyNotifications.SessionOpen)
    ];

    public static TheoryData<Type> NotificationTypes { get; } = [.. AllNotificationTypes];

    [Fact]
    public void Notifications_AreAllTheNestedTypesOfTheClass()
    {
        var types = typeof(OpenNettyNotifications).GetNestedTypes(BindingFlags.Public);

        Assert.Equal(AllNotificationTypes.OrderBy(type => type.Name, StringComparer.Ordinal), types.OrderBy(type => type.Name, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(NotificationTypes))]
    public void Notification_IsASealedOpenNettyNotification(Type type)
    {
        Assert.True(type.IsSealed);
        Assert.True(type.IsSubclassOf(typeof(OpenNettyNotification)));
    }

    [Theory]
    [MemberData(nameof(NotificationTypes))]
    public void Notification_PropertiesAreRequiredAndInitOnly(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.All(properties, property =>
        {
            Assert.NotNull(property.GetCustomAttribute<RequiredMemberAttribute>());
            Assert.Contains(typeof(IsExternalInit), property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers());
        });
    }

    [Theory]
    [InlineData(typeof(OpenNettyNotifications.MessageError), new[] { "Exception", "Gateway", "Message", "Session", "Transaction" })]
    [InlineData(typeof(OpenNettyNotifications.MessageReady), new[] { "Gateway", "Message", "Options", "Transaction" })]
    [InlineData(typeof(OpenNettyNotifications.MessageReceived), new[] { "Gateway", "Message", "Session" })]
    [InlineData(typeof(OpenNettyNotifications.MessageSent), new[] { "Gateway", "Message", "Session", "Transaction" })]
    [InlineData(typeof(OpenNettyNotifications.SessionClosed), new[] { "Gateway", "Session" })]
    [InlineData(typeof(OpenNettyNotifications.SessionOpen), new[] { "Gateway", "Session" })]
    public void Notification_ExposesExpectedProperties(Type type, string[] expected)
    {
        var names = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name);

        Assert.Equivalent(expected, names);
    }

    [Fact]
    public void MessageError_ExposesTheValuesAssignedDuringInitialization()
    {
        var gateway = CreateUninitialized<OpenNettyGateway>();
        var exception = new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "Invalid frame.");
        var message = CreateUninitialized<OpenNettyMessage>();
        var session = CreateUninitialized<OpenNettySession>();
        var transaction = OpenNettyTransaction.Create();

        var notification = new OpenNettyNotifications.MessageError
        {
            Gateway = gateway,
            Exception = exception,
            Message = message,
            Session = session,
            Transaction = transaction
        };

        Assert.Same(gateway, notification.Gateway);
        Assert.Same(exception, notification.Exception);
        Assert.Same(message, notification.Message);
        Assert.Same(session, notification.Session);
        Assert.Equal(transaction, notification.Transaction);
    }

    [Fact]
    public void MessageReady_ExposesTheValuesAssignedDuringInitialization()
    {
        var gateway = CreateUninitialized<OpenNettyGateway>();
        var message = CreateUninitialized<OpenNettyMessage>();
        var options = CreateUninitialized<OpenNettyTransmissionOptions>();
        var transaction = OpenNettyTransaction.Create();

        var notification = new OpenNettyNotifications.MessageReady
        {
            Gateway = gateway,
            Message = message,
            Options = options,
            Transaction = transaction
        };

        Assert.Same(gateway, notification.Gateway);
        Assert.Same(message, notification.Message);
        Assert.Same(options, notification.Options);
        Assert.Equal(transaction, notification.Transaction);
    }

    [Fact]
    public void MessageReceived_ExposesTheValuesAssignedDuringInitialization()
    {
        var gateway = CreateUninitialized<OpenNettyGateway>();
        var message = CreateUninitialized<OpenNettyMessage>();
        var session = CreateUninitialized<OpenNettySession>();

        var notification = new OpenNettyNotifications.MessageReceived
        {
            Gateway = gateway,
            Message = message,
            Session = session
        };

        Assert.Same(gateway, notification.Gateway);
        Assert.Same(message, notification.Message);
        Assert.Same(session, notification.Session);
    }

    [Fact]
    public void MessageSent_ExposesTheValuesAssignedDuringInitialization()
    {
        var gateway = CreateUninitialized<OpenNettyGateway>();
        var message = CreateUninitialized<OpenNettyMessage>();
        var session = CreateUninitialized<OpenNettySession>();
        var transaction = OpenNettyTransaction.Create();

        var notification = new OpenNettyNotifications.MessageSent
        {
            Gateway = gateway,
            Message = message,
            Session = session,
            Transaction = transaction
        };

        Assert.Same(gateway, notification.Gateway);
        Assert.Same(message, notification.Message);
        Assert.Same(session, notification.Session);
        Assert.Equal(transaction, notification.Transaction);
    }

    [Fact]
    public void SessionClosed_ExposesTheValuesAssignedDuringInitialization()
    {
        var gateway = CreateUninitialized<OpenNettyGateway>();
        var session = CreateUninitialized<OpenNettySession>();

        var notification = new OpenNettyNotifications.SessionClosed { Gateway = gateway, Session = session };

        Assert.Same(gateway, notification.Gateway);
        Assert.Same(session, notification.Session);
    }

    [Fact]
    public void SessionOpen_ExposesTheValuesAssignedDuringInitialization()
    {
        var gateway = CreateUninitialized<OpenNettyGateway>();
        var session = CreateUninitialized<OpenNettySession>();

        var notification = new OpenNettyNotifications.SessionOpen { Gateway = gateway, Session = session };

        Assert.Same(gateway, notification.Gateway);
        Assert.Same(session, notification.Session);
    }

    private static T CreateUninitialized<T>() where T : class
        => (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
}
