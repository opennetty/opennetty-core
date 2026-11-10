/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Polly;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettySessionOptionsTests
{
    private static readonly TimeSpan CancellationDelay = TimeSpan.FromMilliseconds(100);

    [Theory]
    [InlineData(MissingProperty.Gateway)]
    [InlineData(MissingProperty.Logger)]
    [InlineData(MissingProperty.SessionType)]
    public async Task SessionResiliencePipeline_MissingContextProperty_ThrowsInvalidOperationException(MissingProperty missing)
    {
        var pipeline = CreateDefaultPipeline();
        using var source = CreateCancellationTokenSource();
        var context = CreateContext(source.Token, includeGateway: missing is not MissingProperty.Gateway,
            logger: missing is MissingProperty.Logger ? null : Substitute.For<ILogger<OpenNettyWorker>>(),
            includeSessionType: missing is not MissingProperty.SessionType);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => pipeline.ExecuteAsync<int>(_ => CancelAndThrow(source, new IOException()), context).AsTask());

        Assert.Equal(SR.GetResourceString(SR.ID0067), exception.Message);
    }

    [Fact]
    public async Task SessionResiliencePipeline_FailureWithCancellationRequested_LogsErrorAndDoesNotRetry()
    {
        var pipeline = CreateDefaultPipeline();
        var logger = Substitute.For<ILogger<OpenNettyWorker>>();
        using var source = CreateCancellationTokenSource();
        var context = CreateContext(source.Token, logger: logger, sessionType: OpenNettySessionType.Event);
        var failure = new IOException();
        var attempts = 0;

        var exception = await Assert.ThrowsAsync<IOException>(() => pipeline.ExecuteAsync<int>(_ =>
        {
            attempts++;
            return CancelAndThrow(source, failure);
        }, context).AsTask());

        Assert.Same(failure, exception);
        Assert.Equal(1, attempts);

        var entry = Assert.Single(GetLogEntries(logger));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(6020, entry.EventId.Id);
        Assert.Same(failure, entry.Exception);
        Assert.Equal("An error occurred while trying to open a session of type Event to gateway Gateway.", entry.Message);
    }

    [Fact]
    public async Task SessionResiliencePipeline_FailureWithoutCancellationRequested_WaitsBeforeRetrying()
    {
        var pipeline = CreateDefaultPipeline();
        var logger = Substitute.For<ILogger<OpenNettyWorker>>();
        using var source = CreateCancellationTokenSource();
        var context = CreateContext(source.Token, logger: logger);
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ExecuteAsync<int>(_ =>
        {
            attempts++;
            source.CancelAfter(CancellationDelay);
            return ValueTask.FromException<int>(new IOException());
        }, context).AsTask());

        Assert.Equal(1, attempts);
        Assert.Equal(6020, Assert.Single(GetLogEntries(logger)).EventId.Id);
    }

    [Fact]
    public async Task SessionResiliencePipeline_FailureWithoutCancellationRequested_WaitsTenSecondsBeforeRetrying()
    {
        var timeProvider = new FakeTimeProvider();
        var pipeline = OpenNettySessionOptions.CreateDefaultResiliencePipeline(timeProvider);
        var logger = Substitute.For<ILogger<OpenNettyWorker>>();
        using var source = CreateCancellationTokenSource();
        var context = CreateContext(source.Token, logger: logger);
        var attempts = 0;

        var task = pipeline.ExecuteAsync<int>(_ =>
        {
            attempts++;

            return attempts is 1 ? ValueTask.FromException<int>(new IOException()) : CancelAndThrow(source, new IOException());
        }, context).AsTask();

        await Task.Delay(CancellationDelay, TestContext.Current.CancellationToken);
        Assert.Equal(1, attempts);

        timeProvider.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromMilliseconds(1));
        await Task.Delay(CancellationDelay, TestContext.Current.CancellationToken);
        Assert.Equal(1, attempts);

        timeProvider.Advance(TimeSpan.FromMilliseconds(1));
        await Assert.ThrowsAsync<IOException>(() => task);

        Assert.Equal(2, attempts);
        Assert.Equal(2, GetLogEntries(logger).Count(static entry => entry.EventId.Id is 6020));
    }

    [Fact]
    public async Task SessionResiliencePipeline_SuccessfulAttempt_IsNotLoggedAndIsRepeatedUntilCancellation()
    {
        var pipeline = CreateDefaultPipeline();
        var logger = Substitute.For<ILogger<OpenNettyWorker>>();
        using var source = CreateCancellationTokenSource();
        var context = CreateContext(source.Token, logger: logger);
        var attempts = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ExecuteAsync<int>(_ =>
        {
            attempts++;
            source.CancelAfter(CancellationDelay);
            return ValueTask.FromResult(0);
        }, context).AsTask());

        Assert.Equal(1, attempts);
        Assert.Empty(GetLogEntries(logger));
    }

    [Fact]
    public void CreateDefaults_NullDevice_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettySessionOptions.CreateDefaults(null!));

        Assert.Equal("device", exception.ParamName);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public void CreateDefaults_AnyProtocol_UsesTenSecondsConnectionNegotiationTimeout(OpenNettyProtocol protocol)
    {
        var options = OpenNettySessionOptions.CreateDefaults(CreateGatewayDevice(protocol));

        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectionNegotiationTimeout);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    public void CreateDefaults_NonZigbeeGateway_DisablesSupervisionMode(OpenNettyProtocol protocol)
    {
        var options = OpenNettySessionOptions.CreateDefaults(CreateGatewayDevice(protocol));

        Assert.False(options.EnableSupervisionMode);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("not a boolean", true)]
    public void CreateDefaults_ZigbeeGateway_EnablesSupervisionModeUnlessExplicitlyDisabled(string? setting, bool expected)
    {
        var device = CreateGatewayDevice(OpenNettyProtocol.Zigbee);

        if (setting is not null)
        {
            device.Settings = ImmutableDictionary<OpenNettySetting, string>.Empty
                .Add(OpenNettySettings.ZigbeeSupervisionMode, setting);
        }

        var options = OpenNettySessionOptions.CreateDefaults(device);

        Assert.Equal(expected, options.EnableSupervisionMode);
    }

    [Fact]
    public void CreateDefaultResiliencePipeline_NullTimeProvider_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettySessionOptions.CreateDefaultResiliencePipeline(null!));

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void CreateDefaultResiliencePipeline_ReturnsANewPipelineForEachCall()
    {
        var timeProvider = new FakeTimeProvider();

        Assert.NotSame(
            OpenNettySessionOptions.CreateDefaultResiliencePipeline(timeProvider),
            OpenNettySessionOptions.CreateDefaultResiliencePipeline(timeProvider));
    }

    [Fact]
    public void With_ModifiedProperty_ReturnsCopyWithNewValue()
    {
        var options = OpenNettySessionOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs));

        var copy = options with { ConnectionNegotiationTimeout = TimeSpan.FromSeconds(1) };

        Assert.Equal(TimeSpan.FromSeconds(1), copy.ConnectionNegotiationTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ConnectionNegotiationTimeout);
        Assert.Same(options.SessionResiliencePipeline, copy.SessionResiliencePipeline);
    }

    [Fact]
    public void Equals_CopyOfSameInstance_ReturnsTrue()
    {
        var options = OpenNettySessionOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs));

        var copy = options with { };

        Assert.Equal(options, copy);
        Assert.Equal(options.GetHashCode(), copy.GetHashCode());
    }

    public enum MissingProperty
    {
        Gateway,
        Logger,
        SessionType
    }

    private sealed record class LogEntry(LogLevel Level, EventId EventId, Exception? Exception, string Message);

    private static ResiliencePipeline CreateDefaultPipeline()
        => OpenNettySessionOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs)).SessionResiliencePipeline;

    private static CancellationTokenSource CreateCancellationTokenSource()
        => CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    private static ResilienceContext CreateContext(
        CancellationToken token, bool includeGateway = true, ILogger<OpenNettyWorker>? logger = null,
        bool includeSessionType = true, OpenNettySessionType sessionType = OpenNettySessionType.Command)
    {
        var context = ResilienceContextPool.Shared.Get(token);

        if (includeGateway)
        {
            var device = CreateGatewayDevice(OpenNettyProtocol.Scs);
            var gateway = OpenNettyGateway.Create(device, new DnsEndPoint("localhost", 20_000));

            context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
        }

        if (logger is not null)
        {
            context.Properties.Set(new ResiliencePropertyKey<ILogger<OpenNettyWorker>>(nameof(ILogger<>)), logger);
        }

        if (includeSessionType)
        {
            context.Properties.Set(new ResiliencePropertyKey<OpenNettySessionType>(nameof(OpenNettySessionType)), sessionType);
        }

        return context;
    }

    private static ValueTask<int> CancelAndThrow(CancellationTokenSource source, Exception exception)
    {
        source.Cancel();

        return ValueTask.FromException<int>(exception);
    }

    private static List<LogEntry> GetLogEntries(ILogger logger) => [.. logger.ReceivedCalls()
        .Where(static call => call.GetMethodInfo().Name is nameof(ILogger.Log))
        .Select(static call =>
        {
            var arguments = call.GetArguments();

            return new LogEntry((LogLevel) arguments[0]!, (EventId) arguments[1]!, (Exception?) arguments[3], Convert.ToString(arguments[2], CultureInfo.InvariantCulture)!);
        })];

    private static OpenNettyDevice CreateGatewayDevice(OpenNettyProtocol protocol) => protocol switch
    {
        OpenNettyProtocol.Scs    => OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F454", "Gateway", identifier: null),
        OpenNettyProtocol.Nitoo  => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null),
        OpenNettyProtocol.Zigbee => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88328", "Gateway", identifier: null),

        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };
}
