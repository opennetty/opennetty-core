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

public sealed class OpenNettyTransmissionOptionsTests
{
    [Theory]
    [InlineData(MissingProperty.Gateway)]
    [InlineData(MissingProperty.Logger)]
    [InlineData(MissingProperty.Message)]
    [InlineData(MissingProperty.Options)]
    public async Task OutgoingMessageResiliencePipeline_MissingContextProperty_ThrowsInvalidOperationException(MissingProperty missing)
    {
        var scenario = CreateScenario(OpenNettyProtocol.Scs, missing: missing);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.ExecuteAsync(new IOException()));

        Assert.Equal(SR.GetResourceString(SR.ID0067), exception.Message);
        Assert.Equal(1, scenario.Attempts);
    }

    [Fact]
    public async Task OutgoingMessageResiliencePipeline_SuccessfulAttempt_IsNeitherRetriedNorLogged()
    {
        var scenario = CreateScenario(OpenNettyProtocol.Scs);

        await scenario.ExecuteAsync(exception: null);

        Assert.Equal(1, scenario.Attempts);
        Assert.Empty(scenario.GetLogEntries());
    }

    [Fact]
    public async Task OutgoingMessageResiliencePipeline_RetriedFailure_LogsFailureAndRetransmission()
    {
        var scenario = CreateScenario(OpenNettyProtocol.Scs);
        var failure = new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "NACK");

        var exception = await Assert.ThrowsAsync<OpenNettyException>(() => scenario.ExecuteAsync(failure));

        Assert.Same(failure, exception);
        Assert.Equal(2, scenario.Attempts);

        var entries = scenario.GetLogEntries();
        Assert.Collection(entries,
            entry =>
            {
                Assert.Equal(6016, entry.EventId.Id);
                Assert.Same(failure, entry.Exception);
                Assert.Equal("An error occurred while sending a message to gateway Gateway: *1*1*11##.", entry.Message);
            },
            entry =>
            {
                Assert.Equal(6017, entry.EventId.Id);
                Assert.Null(entry.Exception);
                Assert.Equal("A message will be retransmitted by gateway Gateway: *1*1*11## (retransmission number n°1).", entry.Message);
            },
            entry =>
            {
                Assert.Equal(6016, entry.EventId.Id);
                Assert.Same(failure, entry.Exception);
            });
    }

    [Fact]
    public async Task OutgoingMessageResiliencePipeline_NitooInvalidFrame_UsesIncreasingRetransmissionDelays()
    {
        var scenario = CreateScenario(OpenNettyProtocol.Nitoo);
        var execution = scenario.StartAsync(new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "NACK"));
        Assert.Equal(1, scenario.Attempts);

        await scenario.AdvanceAsync(TimeSpan.FromMilliseconds(249));
        Assert.Equal(1, scenario.Attempts);
        await scenario.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, scenario.Attempts);

        await scenario.AdvanceAsync(TimeSpan.FromMilliseconds(499));
        Assert.Equal(2, scenario.Attempts);
        await scenario.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal(3, scenario.Attempts);

        await scenario.AdvanceAsync(TimeSpan.FromMilliseconds(999));
        Assert.Equal(3, scenario.Attempts);
        await scenario.AdvanceAsync(TimeSpan.FromMilliseconds(1));
        Assert.Equal(4, scenario.Attempts);

        await Assert.ThrowsAsync<OpenNettyException>(() => execution);
    }

    [Fact]
    public async Task OutgoingMessageResiliencePipeline_NonOpenNettyException_IsNotRetried()
    {
        var scenario = CreateScenario(OpenNettyProtocol.Scs);

        await Assert.ThrowsAsync<IOException>(() => scenario.ExecuteAsync(new IOException()));

        Assert.Equal(1, scenario.Attempts);
    }

    public static TheoryData<OpenNettyProtocol, OpenNettyMedium, OpenNettyErrorCode, bool, bool, int> RetransmissionScenarios => new()
    {
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.InvalidFrame, false, false, 3 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.InvalidFrame, false, true,  3 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.InvalidFrame, true,  false, 0 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.GatewayBusy, false, false, 0 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.NoAcknowledgementReceived, false, false, 0 },

        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.NoActionReceived, false, false, 2 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.NoDimensionReadReceived, false, false, 2 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.NoStatusReportReceived, false, false, 2 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.NoStatusReplyReceived, false, false, 2 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Radio, OpenNettyErrorCode.NoActionReceived, false, false, 2 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.NoActionReceived, false, true, 0 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Powerline, OpenNettyErrorCode.NoActionReceived, true, false, 0 },
        { OpenNettyProtocol.Nitoo, OpenNettyMedium.Infrared, OpenNettyErrorCode.NoActionReceived, false, false, 0 },

        { OpenNettyProtocol.Zigbee, OpenNettyMedium.Radio, OpenNettyErrorCode.InvalidFrame, false, false, 2 },
        { OpenNettyProtocol.Zigbee, OpenNettyMedium.Radio, OpenNettyErrorCode.GatewayBusy, false, false, 2 },
        { OpenNettyProtocol.Zigbee, OpenNettyMedium.Radio, OpenNettyErrorCode.GatewayBusy, true, false, 0 },
        { OpenNettyProtocol.Zigbee, OpenNettyMedium.Radio, OpenNettyErrorCode.NoStatusReportReceived, false, false, 2 },
        { OpenNettyProtocol.Zigbee, OpenNettyMedium.Radio, OpenNettyErrorCode.NoStatusReportReceived, false, true, 0 },
        { OpenNettyProtocol.Zigbee, OpenNettyMedium.Radio, OpenNettyErrorCode.NoAcknowledgementReceived, false, false, 0 },

        { OpenNettyProtocol.Scs, OpenNettyMedium.Bus, OpenNettyErrorCode.InvalidFrame, false, false, 1 },
        { OpenNettyProtocol.Scs, OpenNettyMedium.Bus, OpenNettyErrorCode.InvalidFrame, false, true, 1 },
        { OpenNettyProtocol.Scs, OpenNettyMedium.Bus, OpenNettyErrorCode.InvalidFrame, true, false, 0 },
        { OpenNettyProtocol.Scs, OpenNettyMedium.Bus, OpenNettyErrorCode.GatewayBusy, false, false, 1 },
        { OpenNettyProtocol.Scs, OpenNettyMedium.Bus, OpenNettyErrorCode.GatewayBusy, false, true, 0 },
        { OpenNettyProtocol.Scs, OpenNettyMedium.Bus, OpenNettyErrorCode.NoActionReceived, false, false, 0 },
        { OpenNettyProtocol.Scs, OpenNettyMedium.Bus, OpenNettyErrorCode.NoStatusReplyReceived, false, false, 0 }
    };

    [Theory]
    [MemberData(nameof(RetransmissionScenarios))]
    public async Task OutgoingMessageResiliencePipeline_OpenNettyException_IsRetriedAccordingToProtocolAndMedium(
        OpenNettyProtocol protocol, OpenNettyMedium medium, OpenNettyErrorCode code,
        bool disallowAll, bool disallowUnsafe, int expectedRetransmissions)
    {
        var scenario = CreateScenario(protocol, medium, disallowAll, disallowUnsafe);

        await Assert.ThrowsAsync<OpenNettyException>(() => scenario.ExecuteAsync(new OpenNettyException(code, "failure")));

        Assert.Equal(1 + expectedRetransmissions, scenario.Attempts);
    }

    [Fact]
    public void CreateDefaults_NullDevice_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyTransmissionOptions.CreateDefaults(null!));

        Assert.Equal("device", exception.ParamName);
    }

    [Fact]
    public void CreateDefaults_ScsDevice_UsesScsSettings()
    {
        var device = CreateGatewayDevice(OpenNettyProtocol.Scs);

        var options = OpenNettyTransmissionOptions.CreateDefaults(device);

        Assert.Equal(new OpenNettyTransmissionOptions
        {
            AcknowledgementTimeout                   = TimeSpan.FromSeconds(2),
            ActionValidationTimeout                  = TimeSpan.Zero,
            CustomValidationDelegate                 = null,
            DisallowAllRetransmissions               = false,
            DisallowUnsafeRetransmissions            = false,
            DisableAcknowledgementValidation         = false,
            DisableActionValidation                  = false,
            DisableStatusReportValidation            = false,
            KeepSessionAliveOnMissingAcknowledgement = false,
            MultipleDimensionReplyTimeout            = TimeSpan.FromSeconds(10),
            MultipleStatusReplyTimeout               = TimeSpan.FromSeconds(10),
            OutgoingMessageProcessingTimeout         = TimeSpan.FromSeconds(10),
            OutgoingMessageResiliencePipeline        = options.OutgoingMessageResiliencePipeline,
            PostSendingDelay                         = TimeSpan.Zero,
            StatusReportTimeout                      = TimeSpan.Zero,
            TimeProvider                             = TimeProvider.System,
            UniqueDimensionReplyTimeout              = TimeSpan.FromSeconds(2),
            UniqueStatusReplyTimeout                 = TimeSpan.FromSeconds(2)
        }, options);
    }

    [Fact]
    public void CreateDefaults_NitooDevice_UsesNitooSettings()
    {
        var device = CreateGatewayDevice(OpenNettyProtocol.Nitoo);

        var options = OpenNettyTransmissionOptions.CreateDefaults(device);

        Assert.Equal(new OpenNettyTransmissionOptions
        {
            AcknowledgementTimeout                   = TimeSpan.FromSeconds(7),
            ActionValidationTimeout                  = TimeSpan.FromSeconds(7),
            CustomValidationDelegate                 = null,
            DisallowAllRetransmissions               = false,
            DisallowUnsafeRetransmissions            = false,
            DisableAcknowledgementValidation         = false,
            DisableActionValidation                  = false,
            DisableStatusReportValidation            = false,
            KeepSessionAliveOnMissingAcknowledgement = false,
            MultipleDimensionReplyTimeout            = TimeSpan.Zero,
            MultipleStatusReplyTimeout               = TimeSpan.Zero,
            OutgoingMessageProcessingTimeout         = TimeSpan.FromSeconds(10),
            OutgoingMessageResiliencePipeline        = options.OutgoingMessageResiliencePipeline,
            PostSendingDelay                         = TimeSpan.FromMilliseconds(150),
            StatusReportTimeout                      = TimeSpan.Zero,
            TimeProvider                             = TimeProvider.System,
            UniqueDimensionReplyTimeout              = TimeSpan.FromSeconds(7),
            UniqueStatusReplyTimeout                 = TimeSpan.FromSeconds(7)
        }, options);
    }

    [Fact]
    public void CreateDefaults_ZigbeeDevice_UsesZigbeeSettings()
    {
        var device = CreateGatewayDevice(OpenNettyProtocol.Zigbee);

        var options = OpenNettyTransmissionOptions.CreateDefaults(device);

        Assert.Equal(new OpenNettyTransmissionOptions
        {
            AcknowledgementTimeout                   = TimeSpan.FromSeconds(2),
            ActionValidationTimeout                  = TimeSpan.Zero,
            CustomValidationDelegate                 = null,
            DisallowAllRetransmissions               = false,
            DisallowUnsafeRetransmissions            = false,
            DisableAcknowledgementValidation         = false,
            DisableActionValidation                  = false,
            DisableStatusReportValidation            = false,
            KeepSessionAliveOnMissingAcknowledgement = false,
            MultipleDimensionReplyTimeout            = TimeSpan.FromSeconds(10),
            MultipleStatusReplyTimeout               = TimeSpan.FromSeconds(10),
            OutgoingMessageProcessingTimeout         = TimeSpan.FromSeconds(10),
            OutgoingMessageResiliencePipeline        = options.OutgoingMessageResiliencePipeline,
            PostSendingDelay                         = TimeSpan.Zero,
            StatusReportTimeout                      = TimeSpan.FromSeconds(5),
            TimeProvider                             = TimeProvider.System,
            UniqueDimensionReplyTimeout              = TimeSpan.FromSeconds(2),
            UniqueStatusReplyTimeout                 = TimeSpan.FromSeconds(2)
        }, options);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Zigbee, null, false)]
    [InlineData(OpenNettyProtocol.Zigbee, "true", false)]
    [InlineData(OpenNettyProtocol.Zigbee, "false", true)]
    [InlineData(OpenNettyProtocol.Zigbee, "not a boolean", false)]
    [InlineData(OpenNettyProtocol.Scs, "false", true)]
    [InlineData(OpenNettyProtocol.Nitoo, "false", true)]
    public void CreateDefaults_SupervisionModeSetting_DeterminesWhetherStatusReportValidationIsDisabled(
        OpenNettyProtocol protocol, string? setting, bool expected)
    {
        var device = CreateGatewayDevice(protocol);

        if (setting is not null)
        {
            device.Settings = ImmutableDictionary<OpenNettySetting, string>.Empty
                .Add(OpenNettySettings.ZigbeeSupervisionMode, setting);
        }

        var options = OpenNettyTransmissionOptions.CreateDefaults(device);

        Assert.Equal(expected, options.DisableStatusReportValidation);
    }

    [Fact]
    public void With_ModifiedProperty_ReturnsCopyWithNewValue()
    {
        var options = OpenNettyTransmissionOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs));

        var copy = options with { DisallowAllRetransmissions = true };

        Assert.True(copy.DisallowAllRetransmissions);
        Assert.False(options.DisallowAllRetransmissions);
        Assert.Same(options.OutgoingMessageResiliencePipeline, copy.OutgoingMessageResiliencePipeline);
        Assert.NotEqual(options, copy);
    }

    [Fact]
    public void CreateDefaultResiliencePipeline_NullTimeProvider_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => OpenNettyTransmissionOptions.CreateDefaultResiliencePipeline(null!));

        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void CreateDefaultResiliencePipeline_ReturnsANewPipelineForEachCall()
    {
        var timeProvider = new FakeTimeProvider();

        Assert.NotSame(
            OpenNettyTransmissionOptions.CreateDefaultResiliencePipeline(timeProvider),
            OpenNettyTransmissionOptions.CreateDefaultResiliencePipeline(timeProvider));
    }

    [Fact]
    public void Equals_CopyOfSameInstance_ReturnsTrue()
    {
        var options = OpenNettyTransmissionOptions.CreateDefaults(CreateGatewayDevice(OpenNettyProtocol.Scs));

        var copy = options with { };

        Assert.Equal(options, copy);
        Assert.Equal(options.GetHashCode(), copy.GetHashCode());
    }

    public enum MissingProperty
    {
        None,
        Gateway,
        Logger,
        Message,
        Options
    }

    private sealed record class LogEntry(EventId EventId, Exception? Exception, string Message);

    private sealed class Scenario(
        ResiliencePipeline pipeline, FakeTimeProvider timeProvider, ResilienceContext context,
        ILogger<OpenNettyService> logger, CancellationTokenSource source)
    {
        public int Attempts { get; private set; }

        public Task StartAsync(Exception? exception) => pipeline.ExecuteAsync<int>(_ =>
        {
            Attempts++;

            return exception is null ? ValueTask.FromResult(0) : ValueTask.FromException<int>(exception);
        }, context).AsTask();

        public async Task ExecuteAsync(Exception? exception)
        {
            using (source)
            {
                var execution = StartAsync(exception);

                while (!execution.IsCompleted)
                {
                    await AdvanceAsync(TimeSpan.FromSeconds(1));
                }

                await execution;
            }
        }

        public async Task AdvanceAsync(TimeSpan duration)
        {
            timeProvider.Advance(duration);

            // Give the pipeline a chance to run the continuations triggered by the timers that elapsed.
            await Task.Delay(TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);
        }

        public List<LogEntry> GetLogEntries() => [.. logger.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name is nameof(ILogger.Log))
            .Select(static call =>
            {
                var arguments = call.GetArguments();

                return new LogEntry((EventId) arguments[1]!, (Exception?) arguments[3], Convert.ToString(arguments[2], CultureInfo.InvariantCulture)!);
            })];
    }

    private static Scenario CreateScenario(
        OpenNettyProtocol protocol, OpenNettyMedium? medium = null,
        bool disallowAll = false, bool disallowUnsafe = false, MissingProperty missing = MissingProperty.None)
    {
        var device = CreateGatewayDevice(protocol);
        var timeProvider = new FakeTimeProvider();
        var defaults = OpenNettyTransmissionOptions.CreateDefaults(device) with
        {
            OutgoingMessageResiliencePipeline = OpenNettyTransmissionOptions.CreateDefaultResiliencePipeline(timeProvider),
            TimeProvider = timeProvider
        };
        var logger = Substitute.For<ILogger<OpenNettyService>>();
        var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var context = ResilienceContextPool.Shared.Get(source.Token);

        if (missing is not MissingProperty.Gateway)
        {
            context.Properties.Set(
                new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)),
                OpenNettyGateway.Create(device, new DnsEndPoint("localhost", 20_000)));
        }

        if (missing is not MissingProperty.Logger)
        {
            context.Properties.Set(new ResiliencePropertyKey<ILogger<OpenNettyService>>(nameof(ILogger<>)), logger);
        }

        if (missing is not MissingProperty.Message)
        {
            context.Properties.Set(
                new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)),
                CreateMessage(protocol, medium));
        }

        if (missing is not MissingProperty.Options)
        {
            context.Properties.Set(
                new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)),
                defaults with { DisallowAllRetransmissions = disallowAll, DisallowUnsafeRetransmissions = disallowUnsafe });
        }

        return new Scenario(defaults.OutgoingMessageResiliencePipeline, timeProvider, context, logger, source);
    }

    private static OpenNettyMessage CreateMessage(OpenNettyProtocol protocol, OpenNettyMedium? medium)
    {
        var command = new OpenNettyCommand(OpenNettyCategories.Lighting, "1");

        return protocol switch
        {
            OpenNettyProtocol.Scs => OpenNettyMessage.CreateCommand(protocol, command,
                OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 1)),

            OpenNettyProtocol.Nitoo => OpenNettyMessage.CreateCommand(protocol, command,
                OpenNettyAddress.FromNitooAddress(0, 11), medium),

            OpenNettyProtocol.Zigbee => OpenNettyMessage.CreateCommand(protocol, command,
                OpenNettyAddress.FromDecimalZigbeeAddress(123, 1), medium),

            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };
    }

    private static OpenNettyDevice CreateGatewayDevice(OpenNettyProtocol protocol) => protocol switch
    {
        OpenNettyProtocol.Scs    => OpenNettyDevice.Create(OpenNettyBrand.BTicino, "F454", "Gateway", identifier: null),
        OpenNettyProtocol.Nitoo  => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88213", "Gateway", identifier: null),
        OpenNettyProtocol.Zigbee => OpenNettyDevice.Create(OpenNettyBrand.Legrand, "88328", "Gateway", identifier: null),

        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };
}
