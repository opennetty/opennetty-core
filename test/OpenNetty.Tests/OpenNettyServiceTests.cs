/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Polly;
using Polly.Retry;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettyServiceTests
{
    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var options = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();
        var pipeline = Substitute.For<IOpenNettyPipeline>();

        Assert.Throws<ArgumentNullException>("logger", () => new OpenNettyService(null!, options, pipeline));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ILogger<OpenNettyService>>();
        var pipeline = Substitute.For<IOpenNettyPipeline>();

        Assert.Throws<ArgumentNullException>("options", () => new OpenNettyService(logger, null!, pipeline));
    }

    [Fact]
    public void Constructor_NullPipeline_ThrowsArgumentNullException()
    {
        var logger = Substitute.For<ILogger<OpenNettyService>>();
        var options = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();

        Assert.Throws<ArgumentNullException>("pipeline", () => new OpenNettyService(logger, options, null!));
    }

    [Fact]
    public void Constructor_ValidArguments_CreatesService()
    {
        var logger = Substitute.For<ILogger<OpenNettyService>>();
        var options = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();
        var pipeline = Substitute.For<IOpenNettyPipeline>();

        var service = new OpenNettyService(logger, options, pipeline);

        Assert.IsAssignableFrom<IOpenNettyService>(service);
    }

    public static TheoryData<ServiceOperation> AllOperations => [.. Enum.GetValues<ServiceOperation>()];

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task Operation_UndefinedProtocol_ThrowsInvalidEnumArgumentException(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<InvalidEnumArgumentException>(
            () => InvokeAsync(harness.Service, operation, (OpenNettyProtocol) 42));

        Assert.Equal("protocol", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task Operation_GatewayWithDifferentProtocol_ThrowsInvalidOperationException(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(harness.Service, operation, OpenNettyProtocol.Zigbee, harness.Gateway(OpenNettyProtocol.Scs)));

        Assert.Equal(SR.GetResourceString(SR.ID0027), exception.Message);
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
    public async Task Operation_NoGatewayConfiguredForProtocol_ThrowsInvalidOperationException(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Equal(SR.GetResourceString(SR.ID0028), exception.Message);
    }

    public static TheoryData<ServiceOperation> EnumerationOperations =>
    [
        ServiceOperation.EnumerateDimensions,
        ServiceOperation.EnumerateStatuses
    ];

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_NitooProtocol_ThrowsInvalidOperationException(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Nitoo);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeAsync(harness.Service, operation, OpenNettyProtocol.Nitoo));

        Assert.Equal(SR.GetResourceString(SR.ID0026), exception.Message);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_NotEnumerated_DoesNotPublishAnything(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        await InvokeAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        Assert.Empty(harness.Pipeline.Ready);
        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    public static TheoryData<ServiceOperation, OpenNettyProtocol> EnumerationOperationsByProtocol
    {
        get
        {
            var data = new TheoryData<ServiceOperation, OpenNettyProtocol>();

            foreach (var operation in new[] { ServiceOperation.EnumerateDimensions, ServiceOperation.EnumerateStatuses })
            {
                data.Add(operation, OpenNettyProtocol.Scs);
                data.Add(operation, OpenNettyProtocol.Zigbee);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EnumerationOperationsByProtocol))]
    public async Task Enumeration_RepliesFollowedByAcknowledgement_YieldsRepliesInOrder(
        ServiceOperation operation, OpenNettyProtocol protocol)
    {
        var harness = new ServiceHarness(protocol);
        var first = CreateDeviceAddress(protocol, unit: 1);
        var second = CreateDeviceAddress(protocol, unit: 2);
        harness.ReplyWithMessages(
            harness.CreateSession(protocol),
            CreateEnumerationReply(operation, protocol, first, "1"),
            CreateEnumerationReply(operation, protocol, second, "2"),
            CreateAcknowledgement(protocol));

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, protocol));

        Assert.Equal([(first, "1"), (second, "2")], items);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_AcknowledgementOnly_YieldsNothing(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateAcknowledgement(OpenNettyProtocol.Scs));

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Empty(items);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_RepliesAfterAcknowledgement_AreNotYielded(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "1"),
            CreateAcknowledgement(OpenNettyProtocol.Scs),
            CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "2"));

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Equal([(address, "1")], items);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_NoAcknowledgementBeforeTimeout_CompletesWithReceivedReplies(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "1"));
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options));

        Assert.Equal([(address, "1")], items);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_NoReplyBeforeTimeout_YieldsNothing(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options));

        Assert.Empty(items);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperationsByProtocol))]
    public async Task Enumeration_NegativeAcknowledgement_ThrowsInvalidFrame(ServiceOperation operation, OpenNettyProtocol protocol)
    {
        var harness = new ServiceHarness(protocol);
        harness.ReplyWithMessages(harness.CreateSession(protocol), CreateNegativeAcknowledgement(protocol));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            async () => await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, protocol)));

        Assert.Equal(OpenNettyErrorCode.InvalidFrame, exception.ErrorCode);
        Assert.Equal(SR.GetResourceString(SR.ID0014), exception.Message);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_BusyNegativeAcknowledgementOnZigbee_ThrowsGatewayBusy(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Zigbee), CreateBusyNegativeAcknowledgement());

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            async () => await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Zigbee)));

        Assert.Equal(OpenNettyErrorCode.GatewayBusy, exception.ErrorCode);
        Assert.Equal(SR.GetResourceString(SR.ID0013), exception.Message);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_NegativeAcknowledgementAfterReply_YieldsReplyThenThrows(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "1"),
            CreateNegativeAcknowledgement(OpenNettyProtocol.Scs));
        var items = new List<(OpenNettyAddress Address, string Marker)>();

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () =>
        {
            await foreach (var item in EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs)
                .WithCancellation(TestContext.Current.CancellationToken))
            {
                items.Add(item);
            }
        });

        Assert.Equal(OpenNettyErrorCode.InvalidFrame, exception.ErrorCode);
        Assert.Equal([(address, "1")], items);
    }

    public static TheoryData<ServiceOperation, EnumerationNoise> EnumerationOperationsByNoise
    {
        get
        {
            var data = new TheoryData<ServiceOperation, EnumerationNoise>();

            foreach (var operation in new[] { ServiceOperation.EnumerateDimensions, ServiceOperation.EnumerateStatuses })
            {
                foreach (var noise in Enum.GetValues<EnumerationNoise>())
                {
                    data.Add(operation, noise);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EnumerationOperationsByNoise))]
    public async Task Enumeration_IgnoredNotification_IsSkipped(ServiceOperation operation, EnumerationNoise noise)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.ReplyWithNotifications(
            session,
            CreateNoise(harness, operation, noise, session),
            CreateMessageReceived(session, CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "1")),
            CreateMessageReceived(session, CreateAcknowledgement(OpenNettyProtocol.Scs)));

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Equal([(address, "1")], items);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperationsByProtocol))]
    public async Task Enumeration_RepliesFromEventSession_AreIgnored(ServiceOperation operation, OpenNettyProtocol protocol)
    {
        var harness = new ServiceHarness(protocol);
        var address = CreateDeviceAddress(protocol);
        harness.ReplyWithMessages(
            harness.CreateSession(protocol, OpenNettySessionType.Event),
            CreateEnumerationReply(operation, protocol, address, "1"),
            CreateAcknowledgement(protocol));
        var options = CreateShortTimeoutOptions(harness.Gateway(protocol));

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, protocol, options: options));

        Assert.Empty(items);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_ExplicitOptions_PublishesCopyWithoutAcknowledgementValidation(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateAcknowledgement(OpenNettyProtocol.Scs));
        var options = harness.Gateway(OpenNettyProtocol.Scs).Options.DefaultTransmissionOptions with { PostSendingDelay = TimeSpan.FromSeconds(3) };

        await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options));

        Assert.Equal(options with { DisableAcknowledgementValidation = true }, harness.Pipeline.SingleReady.Options);
        Assert.False(options.DisableAcknowledgementValidation);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_ExplicitGateway_UsesProvidedGateway(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Scs);
        harness.Options.Gateways[1] = CreateGateway(OpenNettyProtocol.Scs, port: 20001);
        var gateway = harness.Options.Gateways[1];
        harness.ReplyWithMessages(CreateSession(gateway), CreateAcknowledgement(OpenNettyProtocol.Scs));

        await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs, gateway: gateway));

        Assert.Same(gateway, harness.Pipeline.SingleReady.Gateway);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_NoExplicitGateway_UsesFirstGatewayMatchingProtocol(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Scs);
        harness.Options.Gateways[1] = CreateGateway(OpenNettyProtocol.Scs, port: 20001);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateAcknowledgement(OpenNettyProtocol.Scs));

        await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Same(harness.Options.Gateways[0], harness.Pipeline.SingleReady.Gateway);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_MessageError_ThrowsReportedException(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        var expected = new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, "No acknowledgement.");
        harness.Pipeline.Responder = ready => [CreateMessageError(ready, session, expected)];

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            async () => await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs)));

        Assert.Same(expected, exception);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_NoWorkerResponds_ThrowsNoWorkerAvailable(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            async () => await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options)));

        Assert.Equal(OpenNettyErrorCode.NoWorkerAvailable, exception.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_RetryPipeline_RetriesFailedTransmission(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.Pipeline.Responder = ready => harness.Pipeline.Ready.Count is 1
            ? [CreateMessageError(ready, session, new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "Busy."))]
            : [CreateMessageSent(ready, session), CreateMessageReceived(session, CreateAcknowledgement(OpenNettyProtocol.Scs))];

        var options = harness.Gateway(OpenNettyProtocol.Scs).Options.DefaultTransmissionOptions with
        {
            OutgoingMessageResiliencePipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
            {
                Delay = TimeSpan.Zero,
                MaxRetryAttempts = 1,
                ShouldHandle = new PredicateBuilder().Handle<OpenNettyException>()
            }).Build()
        };

        var items = await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options));

        Assert.Empty(items);
        Assert.Equal(2, harness.Pipeline.Ready.Count);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_CompletedEnumeration_ReleasesPipelineSubscriptions(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateAcknowledgement(OpenNettyProtocol.Scs));

        await CollectAsync(EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_AbandonedEnumeration_ReleasesPipelineSubscriptions(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "1"),
            CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "2"));

        await foreach (var _ in EnumerateMarkersAsync(harness.Service, operation, OpenNettyProtocol.Scs)
            .WithCancellation(TestContext.Current.CancellationToken))
        {
            break;
        }

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Theory]
    [MemberData(nameof(EnumerationOperations))]
    public async Task Enumeration_CancellationRequestedWhileWaiting_ThrowsOperationCanceledException(ServiceOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "1"));
        using var source = new CancellationTokenSource();

        await using var enumerator = EnumerateMarkersCoreAsync(harness.Service, operation, OpenNettyProtocol.Scs, null, null, source.Token)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal((address, "1"), enumerator.Current);

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task EnumerateDimensionsAsync_RequestedDimensionAndAddress_PublishesDimensionRequestWithoutAcknowledgementValidation()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);
        var address = CreateDeviceAddress(OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Zigbee), CreateAcknowledgement(OpenNettyProtocol.Zigbee));

        await CollectAsync(harness.Service.EnumerateDimensionsAsync(
            OpenNettyProtocol.Zigbee, TestDimension, address, OpenNettyMedium.Radio, OpenNettyMode.Unicast,
            cancellationToken: TestContext.Current.CancellationToken));

        var ready = harness.Pipeline.SingleReady;
        Assert.Equal(OpenNettyMessageType.DimensionRequest, ready.Message.Type);
        Assert.Equal(TestDimension, ready.Message.Dimension);
        Assert.Equal(address, ready.Message.Address);
        Assert.Equal(OpenNettyMedium.Radio, ready.Message.Medium);
        Assert.Equal(OpenNettyMode.Unicast, ready.Message.Mode);
        Assert.Equal(
            harness.Gateway(OpenNettyProtocol.Zigbee).Options.DefaultTransmissionOptions with { DisableAcknowledgementValidation = true },
            ready.Options);
    }

    [Fact]
    public async Task EnumerateStatusesAsync_RequestedCategoryAndAddress_PublishesStatusRequestWithoutAcknowledgementValidation()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);
        var address = CreateDeviceAddress(OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Zigbee), CreateAcknowledgement(OpenNettyProtocol.Zigbee));

        await CollectAsync(harness.Service.EnumerateStatusesAsync(
            OpenNettyProtocol.Zigbee, OpenNettyCategories.Lighting, address, OpenNettyMedium.Radio, OpenNettyMode.Unicast,
            cancellationToken: TestContext.Current.CancellationToken));

        var ready = harness.Pipeline.SingleReady;
        Assert.Equal(OpenNettyMessageType.StatusRequest, ready.Message.Type);
        Assert.Equal(OpenNettyCategories.Lighting, ready.Message.Category!.Value);
        Assert.Equal(address, ready.Message.Address);
        Assert.Equal(OpenNettyMedium.Radio, ready.Message.Medium);
        Assert.Equal(OpenNettyMode.Unicast, ready.Message.Mode);
        Assert.Equal(
            harness.Gateway(OpenNettyProtocol.Zigbee).Options.DefaultTransmissionOptions with { DisableAcknowledgementValidation = true },
            ready.Options);
    }

    [Fact]
    public async Task EnumerateStatusesAsync_ReplyWithoutFilter_IsIgnoredWhenCategoryDiffers()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, OtherCategoryCommand, address),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand, address),
            CreateAcknowledgement(OpenNettyProtocol.Scs));

        var items = await CollectAsync(harness.Service.EnumerateStatusesAsync(
            OpenNettyProtocol.Scs, OpenNettyCategories.Lighting, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([(address, TestCommand)], items);
    }

    [Fact]
    public async Task EnumerateStatusesAsync_Filter_ReplacesDefaultCategoryCheck()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        var received = new List<OpenNettyCommand>();
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, OtherCategoryCommand, address),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand, address),
            CreateAcknowledgement(OpenNettyProtocol.Scs));

        var items = await CollectAsync(harness.Service.EnumerateStatusesAsync(
            OpenNettyProtocol.Scs, OpenNettyCategories.Lighting,
            filter: command =>
            {
                received.Add(command);
                return ValueTask.FromResult(command == OtherCategoryCommand);
            },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([(address, OtherCategoryCommand)], items);
        Assert.Equal([OtherCategoryCommand, TestCommand], received);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs, "*1*1*11##")]
    [InlineData(OpenNettyProtocol.Nitoo, "*1*1*17##")]
    [InlineData(OpenNettyProtocol.Zigbee, "*1*1*10001#9##")]
    public async Task ExecuteCommandAsync_Address_PublishesBusCommandForProtocol(OpenNettyProtocol protocol, string frame)
    {
        var harness = new ServiceHarness(protocol);
        harness.ReplyWithMessages(harness.CreateSession(protocol));
        var address = CreateDeviceAddress(protocol);

        await harness.Service.ExecuteCommandAsync(protocol, TestCommand, address, cancellationToken: TestContext.Current.CancellationToken);

        var message = harness.Pipeline.SingleReady.Message;
        Assert.Equal(frame, message.Frame.ToString());
        Assert.Equal(protocol, message.Protocol);
        Assert.Equal(OpenNettyMessageType.BusCommand, message.Type);
        Assert.Equal(TestCommand, message.Command);
        Assert.Equal(address, message.Address);
    }

    [Fact]
    public async Task ExecuteCommandAsync_NitooMulticastRadio_PublishesMessageWithModeAndMedium()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Nitoo);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Nitoo));

        await harness.Service.ExecuteCommandAsync(
            OpenNettyProtocol.Nitoo, TestCommand, CreateDeviceAddress(OpenNettyProtocol.Nitoo),
            OpenNettyMedium.Radio, OpenNettyMode.Multicast, cancellationToken: TestContext.Current.CancellationToken);

        var message = harness.Pipeline.SingleReady.Message;
        Assert.Equal("*1*1*#17#1##", message.Frame.ToString());
        Assert.Equal(OpenNettyMode.Multicast, message.Mode);
        Assert.Equal(OpenNettyMedium.Radio, message.Medium);
    }

    [Fact]
    public async Task ExecuteCommandAsync_AddressIncompatibleWithProtocol_ThrowsInvalidOperationException()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await harness.Service.ExecuteCommandAsync(
                OpenNettyProtocol.Scs, TestCommand, CreateDeviceAddress(OpenNettyProtocol.Nitoo),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0050), exception.Message);
        Assert.Empty(harness.Pipeline.Ready);
    }

    public static TheoryData<DispatchingOperation> DispatchingOperations => [.. Enum.GetValues<DispatchingOperation>()];

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_NoOptions_UsesGatewayDefaultOptions(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var gateway = harness.Gateway(OpenNettyProtocol.Scs);

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        var ready = harness.Pipeline.SingleReady;
        Assert.Equal(gateway, ready.Gateway);
        Assert.Equal(gateway.Options.DefaultTransmissionOptions, ready.Options);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_ExplicitOptions_UsesProvidedOptions(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var options = harness.Gateway(OpenNettyProtocol.Scs).Options.DefaultTransmissionOptions with
        {
            PostSendingDelay = TimeSpan.FromSeconds(3)
        };

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options);

        Assert.Same(options, harness.Pipeline.SingleReady.Options);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_NoExplicitGateway_UsesFirstGatewayMatchingProtocol(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Scs);
        harness.Options.Gateways[1] = CreateGateway(OpenNettyProtocol.Scs, port: 20001);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        Assert.Same(harness.Options.Gateways[0], harness.Pipeline.SingleReady.Gateway);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_ExplicitGateway_UsesProvidedGateway(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Scs);
        harness.Options.Gateways[1] = CreateGateway(OpenNettyProtocol.Scs, port: 20001);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var gateway = harness.Options.Gateways[1];

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs, gateway);

        Assert.Same(gateway, harness.Pipeline.SingleReady.Gateway);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_MessageErrorReceived_ThrowsReportedException(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        var expected = new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "The gateway is busy.");
        harness.Pipeline.Responder = ready => [CreateMessageError(ready, session, expected)];

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            () => InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Same(expected, exception);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_NoNotificationReceived_ThrowsNoWorkerAvailable(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            () => InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options));

        Assert.Equal(OpenNettyErrorCode.NoWorkerAvailable, exception.ErrorCode);
        Assert.Equal(SR.GetResourceString(SR.ID0033), exception.Message);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_CompletedOperation_ReleasesPipelineSubscriptions(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_CancelledToken_ThrowsOperationCanceledException(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs, cancellationToken: source.Token));
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_DefaultResiliencePipeline_SucceedsWhenMessageIsSent(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var gateway = harness.Gateway(OpenNettyProtocol.Scs);
        var options = OpenNettyTransmissionOptions.CreateDefaults(gateway.Device);

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options);

        Assert.Same(options, harness.Pipeline.SingleReady.Options);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_RetryPipeline_RetriesFailedTransmissionWithNewTransaction(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.Pipeline.Responder = ready => harness.Pipeline.Ready.Count is 1
            ? [CreateMessageError(ready, session, new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "Busy."))]
            : [CreateMessageSent(ready, session)];

        var options = harness.Gateway(OpenNettyProtocol.Scs).Options.DefaultTransmissionOptions with
        {
            OutgoingMessageResiliencePipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
            {
                Delay = TimeSpan.Zero,
                MaxRetryAttempts = 1,
                ShouldHandle = new PredicateBuilder().Handle<OpenNettyException>()
            }).Build()
        };

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options);

        var ready = harness.Pipeline.Ready;
        Assert.Equal(2, ready.Count);
        Assert.Same(ready[0].Message, ready[1].Message);
        Assert.NotEqual(ready[0].Transaction, ready[1].Transaction);
    }

    [Theory]
    [MemberData(nameof(DispatchingOperations))]
    public async Task DispatchingOperation_ResiliencePipeline_ReceivesContextProperties(DispatchingOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var gateway = harness.Gateway(OpenNettyProtocol.Scs);

        OpenNettyGateway? capturedGateway = null;
        ILogger<OpenNettyService>? capturedLogger = null;
        OpenNettyMessage? capturedMessage = null;
        OpenNettyTransmissionOptions? capturedOptions = null;

        var options = gateway.Options.DefaultTransmissionOptions with
        {
            OutgoingMessageResiliencePipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = arguments =>
                {
                    var properties = arguments.Context.Properties;
                    properties.TryGetValue(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), out capturedGateway);
                    properties.TryGetValue(new ResiliencePropertyKey<ILogger<OpenNettyService>>(nameof(ILogger<>)), out capturedLogger);
                    properties.TryGetValue(new ResiliencePropertyKey<OpenNettyMessage>(nameof(OpenNettyMessage)), out capturedMessage);
                    properties.TryGetValue(new ResiliencePropertyKey<OpenNettyTransmissionOptions>(nameof(OpenNettyTransmissionOptions)), out capturedOptions);

                    return ValueTask.FromResult(false);
                }
            }).Build()
        };

        await InvokeDispatchingAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options);

        Assert.Same(gateway, capturedGateway);
        Assert.Same(harness.Logger, capturedLogger);
        Assert.Same(harness.Pipeline.SingleReady.Message, capturedMessage);
        Assert.Same(options, capturedOptions);
    }

    public static TheoryData<QueryOperation, NonUniqueAddress> QueryOperationsByNonUniqueAddress
    {
        get
        {
            var data = new TheoryData<QueryOperation, NonUniqueAddress>();

            foreach (var operation in Enum.GetValues<QueryOperation>())
            {
                foreach (var address in Enum.GetValues<NonUniqueAddress>())
                {
                    data.Add(operation, address);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(QueryOperationsByNonUniqueAddress))]
    public async Task Query_AddressNotPointingToUniqueDevice_ThrowsInvalidOperationException(
        QueryOperation operation, NonUniqueAddress kind)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Nitoo, OpenNettyProtocol.Zigbee);
        var (protocol, address, message) = CreateNonUniqueAddress(kind);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeQueryAsync(harness.Service, operation, protocol, address));

        Assert.Equal(message, exception.Message);
        Assert.Empty(harness.Pipeline.Ready);
    }

    public static TheoryData<QueryOperation> QueryOperations => [.. Enum.GetValues<QueryOperation>()];

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_UndefinedProtocol_ThrowsInvalidEnumArgumentException(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<InvalidEnumArgumentException>(
            () => InvokeQueryAsync(harness.Service, operation, (OpenNettyProtocol) 42));

        Assert.Equal("protocol", exception.ParamName);
    }

    public static TheoryData<QueryOperation, OpenNettyProtocol> QueryOperationsByProtocol
    {
        get
        {
            var data = new TheoryData<QueryOperation, OpenNettyProtocol>();

            foreach (var operation in Enum.GetValues<QueryOperation>())
            {
                foreach (var protocol in new[] { OpenNettyProtocol.Scs, OpenNettyProtocol.Nitoo, OpenNettyProtocol.Zigbee })
                {
                    data.Add(operation, protocol);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(QueryOperationsByProtocol))]
    public async Task Query_MatchingReply_ReturnsRequestedData(QueryOperation operation, OpenNettyProtocol protocol)
    {
        var harness = new ServiceHarness(protocol);
        var address = CreateDeviceAddress(protocol);
        var session = harness.CreateSession(protocol);
        harness.ReplyWithMessages(session, CreateReply(operation, protocol, address, "1"));

        var result = await InvokeQueryAsync(harness.Service, operation, protocol, address);

        Assert.Equal("1", result);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_NoAddress_ReturnsReplyWithoutAddress(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateReply(operation, OpenNettyProtocol.Scs, null, "1"));

        var result = await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        Assert.Equal("1", result);
        Assert.Null(harness.Pipeline.SingleReady.Message.Address);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_NoOptions_UsesGatewayDefaultOptionsWithoutChangingThem(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateReply(operation, OpenNettyProtocol.Scs, null, "1"));

        await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        Assert.Equal(harness.Gateway(OpenNettyProtocol.Scs).Options.DefaultTransmissionOptions, harness.Pipeline.SingleReady.Options);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_ExplicitOptions_UsesProvidedOptions(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateReply(operation, OpenNettyProtocol.Scs, null, "1"));
        var options = harness.Gateway(OpenNettyProtocol.Scs).Options.DefaultTransmissionOptions with { PostSendingDelay = TimeSpan.FromSeconds(3) };

        await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options);

        Assert.Same(options, harness.Pipeline.SingleReady.Options);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_ExplicitGateway_UsesProvidedGatewayAndIgnoresNotificationsFromOthers(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Scs);
        harness.Options.Gateways[1] = CreateGateway(OpenNettyProtocol.Scs, port: 20001);
        var gateway = harness.Options.Gateways[1];
        var session = CreateSession(gateway);
        harness.ReplyWithMessages(session, CreateReply(operation, OpenNettyProtocol.Scs, null, "1"));

        var result = await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs, gateway: gateway);

        Assert.Equal("1", result);
        Assert.Same(gateway, harness.Pipeline.SingleReady.Gateway);
    }

    public static TheoryData<QueryOperation, IgnoredReply> QueryOperationsByIgnoredReply
    {
        get
        {
            var data = new TheoryData<QueryOperation, IgnoredReply>();

            foreach (var operation in Enum.GetValues<QueryOperation>())
            {
                foreach (var reply in Enum.GetValues<IgnoredReply>())
                {
                    data.Add(operation, reply);
                }
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(QueryOperationsByIgnoredReply))]
    public async Task Query_IgnoredReply_IsSkippedBeforeMatchingReply(QueryOperation operation, IgnoredReply ignored)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.ReplyWithNotifications(
            session,
            CreateIgnoredReply(harness, operation, ignored, session),
            CreateMessageReceived(session, CreateReply(operation, OpenNettyProtocol.Scs, address, "1")));

        var result = await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs, address);

        Assert.Equal("1", result);
    }

    [Theory]
    [MemberData(nameof(QueryOperationsByProtocol))]
    public async Task Query_ReplyFromEventSession_IsIgnored(QueryOperation operation, OpenNettyProtocol protocol)
    {
        var harness = new ServiceHarness(protocol);
        var address = CreateDeviceAddress(protocol);
        var session = harness.CreateSession(protocol, OpenNettySessionType.Event);
        harness.ReplyWithMessages(session, CreateReply(operation, protocol, address, "1"));
        var options = CreateShortTimeoutOptions(harness.Gateway(protocol));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            () => InvokeQueryAsync(harness.Service, operation, protocol, address, options: options));

        Assert.Equal(GetNoReplyErrorCode(operation), exception.ErrorCode);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_FirstMatchingReplyReceived_ReturnsIt(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            CreateReply(operation, OpenNettyProtocol.Scs, null, "1"),
            CreateReply(operation, OpenNettyProtocol.Scs, null, "2"));

        var result = await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        Assert.Equal("1", result);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_NoReplyBeforeTimeout_ThrowsOpenNettyException(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            () => InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options));

        Assert.Equal(GetNoReplyErrorCode(operation), exception.ErrorCode);
        Assert.Equal(SR.GetResourceString(operation is QueryOperation.GetDimension ? SR.ID0029 : SR.ID0030), exception.Message);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_MessageError_ThrowsReportedException(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        var expected = new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "Invalid.");
        harness.Pipeline.Responder = ready => [CreateMessageError(ready, session, expected)];

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            () => InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs));

        Assert.Same(expected, exception);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_CancelledToken_ThrowsOperationCanceledException(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateReply(operation, OpenNettyProtocol.Scs, null, "1"));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs, cancellationToken: source.Token));
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_Completed_ReleasesPipelineSubscriptions(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), CreateReply(operation, OpenNettyProtocol.Scs, null, "1"));

        await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs);

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_Failed_ReleasesPipelineSubscriptions(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        await Assert.ThrowsAsync<OpenNettyException>(
            () => InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options));

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Theory]
    [MemberData(nameof(QueryOperations))]
    public async Task Query_RetryPipeline_RetriesWhenNoReplyIsReceived(QueryOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.Pipeline.Responder = ready => harness.Pipeline.Ready.Count is 1
            ? [CreateMessageSent(ready, session)]
            : [CreateMessageSent(ready, session), CreateMessageReceived(session, CreateReply(operation, OpenNettyProtocol.Scs, null, "1"))];

        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs)) with
        {
            OutgoingMessageResiliencePipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
            {
                Delay = TimeSpan.Zero,
                MaxRetryAttempts = 1,
                ShouldHandle = new PredicateBuilder().Handle<OpenNettyException>()
            }).Build()
        };

        var result = await InvokeQueryAsync(harness.Service, operation, OpenNettyProtocol.Scs, options: options);

        Assert.Equal("1", result);
        Assert.Equal(2, harness.Pipeline.Ready.Count);
    }

    [Fact]
    public async Task GetDimensionAsync_Address_PublishesDimensionRequest()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);
        var address = CreateDeviceAddress(OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Zigbee), CreateReply(QueryOperation.GetDimension, OpenNettyProtocol.Zigbee, address, "1"));

        await harness.Service.GetDimensionAsync(
            OpenNettyProtocol.Zigbee, TestDimension, address, OpenNettyMedium.Radio, OpenNettyMode.Unicast,
            cancellationToken: TestContext.Current.CancellationToken);

        var message = harness.Pipeline.SingleReady.Message;
        Assert.Equal(OpenNettyMessageType.DimensionRequest, message.Type);
        Assert.Equal(TestDimension, message.Dimension);
        Assert.Equal(address, message.Address);
        Assert.Equal(OpenNettyMedium.Radio, message.Medium);
        Assert.Equal(OpenNettyMode.Unicast, message.Mode);
    }

    [Fact]
    public async Task GetDimensionAsync_ReplyForOtherDimension_IsSkippedBeforeMatchingReply()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var otherDimension = new OpenNettyDimension(OpenNettyCategories.Lighting, "2");
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, otherDimension, ["2"]),
            OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["1"]));

        var result = await harness.Service.GetDimensionAsync(
            OpenNettyProtocol.Scs, TestDimension, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["1"], result);
    }

    [Fact]
    public async Task GetDimensionAsync_MultipleValues_ReturnsAllValues()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["1", "2", "3"]));

        var result = await harness.Service.GetDimensionAsync(
            OpenNettyProtocol.Scs, TestDimension, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["1", "2", "3"], result);
    }

    [Fact]
    public async Task GetStatusAsync_Address_PublishesStatusRequest()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);
        var address = CreateDeviceAddress(OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Zigbee), CreateReply(QueryOperation.GetStatus, OpenNettyProtocol.Zigbee, address, "1"));

        await harness.Service.GetStatusAsync(
            OpenNettyProtocol.Zigbee, OpenNettyCategories.Lighting, address, OpenNettyMedium.Radio, OpenNettyMode.Unicast,
            cancellationToken: TestContext.Current.CancellationToken);

        var message = harness.Pipeline.SingleReady.Message;
        Assert.Equal(OpenNettyMessageType.StatusRequest, message.Type);
        Assert.Equal(OpenNettyCategories.Lighting, message.Category!.Value);
        Assert.Equal(address, message.Address);
        Assert.Equal(OpenNettyMedium.Radio, message.Medium);
        Assert.Equal(OpenNettyMode.Unicast, message.Mode);
    }

    [Fact]
    public async Task GetStatusAsync_ReplyForOtherCategory_IsSkippedBeforeMatchingReply()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, OtherCategoryCommand),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand));

        var result = await harness.Service.GetStatusAsync(
            OpenNettyProtocol.Scs, OpenNettyCategories.Lighting, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TestCommand, result);
    }

    [Fact]
    public async Task GetStatusAsync_FilterAcceptsCommand_ReturnsCommandEvenIfCategoryDiffers()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, OtherCategoryCommand));

        var result = await harness.Service.GetStatusAsync(
            OpenNettyProtocol.Scs, OpenNettyCategories.Lighting,
            filter: static _ => ValueTask.FromResult(true),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(OtherCategoryCommand, result);
    }

    [Fact]
    public async Task GetStatusAsync_FilterRejectsCommand_SkipsItBeforeMatchingReply()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var rejected = new OpenNettyCommand(OpenNettyCategories.Lighting, "2");
        var received = new List<OpenNettyCommand>();
        harness.ReplyWithMessages(
            harness.CreateSession(OpenNettyProtocol.Scs),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, rejected),
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand));

        var result = await harness.Service.GetStatusAsync(
            OpenNettyProtocol.Scs, OpenNettyCategories.Lighting,
            filter: command =>
            {
                received.Add(command);
                return ValueTask.FromResult(command == TestCommand);
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(TestCommand, result);
        Assert.Equal([rejected, TestCommand], received);
    }

    [Fact]
    public async Task GetStatusAsync_FilterRejectsEveryCommand_ThrowsNoStatusReplyReceived()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand));
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await harness.Service.GetStatusAsync(
            OpenNettyProtocol.Scs, OpenNettyCategories.Lighting,
            filter: static _ => ValueTask.FromResult(false), options: options,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(OpenNettyErrorCode.NoStatusReplyReceived, exception.ErrorCode);
    }

    public static TheoryData<MessageOperation> MessageOperations => [.. Enum.GetValues<MessageOperation>()];

    [Theory]
    [MemberData(nameof(MessageOperations))]
    public async Task MessageOperation_NullMessage_ThrowsArgumentNullException(MessageOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        await Assert.ThrowsAsync<ArgumentNullException>("message", () => InvokeMessageOperationAsync(harness.Service, operation, null!));
    }

    [Theory]
    [MemberData(nameof(MessageOperations))]
    public async Task MessageOperation_GatewayWithDifferentProtocol_ThrowsInvalidOperationException(MessageOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Zigbee, TestCommand);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeMessageOperationAsync(harness.Service, operation, message, harness.Gateway(OpenNettyProtocol.Scs)));

        Assert.Equal(SR.GetResourceString(SR.ID0027), exception.Message);
    }

    [Theory]
    [MemberData(nameof(MessageOperations))]
    public async Task MessageOperation_NoGatewayConfiguredForProtocol_ThrowsInvalidOperationException(MessageOperation operation)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => InvokeMessageOperationAsync(harness.Service, operation, message));

        Assert.Equal(SR.GetResourceString(SR.ID0028), exception.Message);
    }

    [Fact]
    public void ObserveMessagesAsync_NotSubscribed_DoesNotPublishAnything()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand);

        _ = harness.Service.ObserveMessagesAsync(message, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(harness.Pipeline.Ready);
        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Fact]
    public async Task ObserveMessagesAsync_Subscribed_PublishesMessageToGatewayMatchingItsProtocol()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Zigbee));
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Zigbee, TestCommand);

        await using var subscription = await harness.Service
            .ObserveMessagesAsync(message, cancellationToken: TestContext.Current.CancellationToken)
            .SubscribeAsync(new MessageObserverFake());

        var ready = harness.Pipeline.SingleReady;
        Assert.Same(message, ready.Message);
        Assert.Equal(harness.Gateway(OpenNettyProtocol.Zigbee), ready.Gateway);
        Assert.Equal(harness.Gateway(OpenNettyProtocol.Zigbee).Options.DefaultTransmissionOptions, ready.Options);
    }

    [Fact]
    public async Task ObserveMessagesAsync_ExplicitGatewayAndOptions_UsesThem()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Scs);
        harness.Options.Gateways[1] = CreateGateway(OpenNettyProtocol.Scs, port: 20001);
        var gateway = harness.Options.Gateways[1];
        harness.ReplyWithMessages(CreateSession(gateway));
        var options = gateway.Options.DefaultTransmissionOptions with { PostSendingDelay = TimeSpan.FromSeconds(3) };
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand);

        await using var subscription = await harness.Service
            .ObserveMessagesAsync(message, gateway, options, TestContext.Current.CancellationToken)
            .SubscribeAsync(new MessageObserverFake());

        var ready = harness.Pipeline.SingleReady;
        Assert.Same(gateway, ready.Gateway);
        Assert.Same(options, ready.Options);
    }

    [Fact]
    public async Task ObserveMessagesAsync_MessagesReceivedWhileSending_AreReplayedToObserver()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var reply = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["1"]);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs), reply);
        var observer = new MessageObserverFake();

        await using var subscription = await harness.Service
            .ObserveMessagesAsync(OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: TestContext.Current.CancellationToken)
            .SubscribeAsync(observer);

        Assert.Equal(reply, await observer.ReadAsync());
    }

    [Fact]
    public async Task ObserveMessagesAsync_MessagesReceivedAfterSubscription_AreForwardedInOrder()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(session);
        var observer = new MessageObserverFake();
        var first = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["1"]);
        var second = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["2"]);

        await using var subscription = await harness.Service
            .ObserveMessagesAsync(OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: TestContext.Current.CancellationToken)
            .SubscribeAsync(observer);

        await harness.Pipeline.EmitAsync(CreateMessageReceived(session, first));
        await harness.Pipeline.EmitAsync(CreateMessageReceived(session, second));

        Assert.Equal(first, await observer.ReadAsync());
        Assert.Equal(second, await observer.ReadAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObserveMessagesAsync_MessagesFromOtherSessionsOrGateways_AreIgnored(bool otherGateway)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(session);
        var observer = new MessageObserverFake();
        var ignored = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["2"]);
        var expected = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["1"]);

        await using var subscription = await harness.Service
            .ObserveMessagesAsync(OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: TestContext.Current.CancellationToken)
            .SubscribeAsync(observer);

        var otherSession = harness.CreateSession(OpenNettyProtocol.Scs);
        await harness.Pipeline.EmitAsync(otherGateway
            ? CreateMessageReceived(session, ignored, CreateGateway(OpenNettyProtocol.Scs, port: 20001))
            : CreateMessageReceived(otherSession, ignored));
        await harness.Pipeline.EmitAsync(CreateMessageReceived(session, expected));

        Assert.Equal(expected, await observer.ReadAsync());
    }

    [Fact]
    public async Task ObserveMessagesAsync_NotificationsOtherThanReceivedMessages_AreIgnored()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(session);
        var observer = new MessageObserverFake();
        var expected = OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, TestDimension, ["1"]);

        await using var subscription = await harness.Service
            .ObserveMessagesAsync(OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: TestContext.Current.CancellationToken)
            .SubscribeAsync(observer);

        await harness.Pipeline.EmitAsync(new OpenNettyNotifications.SessionOpen { Gateway = session.Gateway, Session = session });
        await harness.Pipeline.EmitAsync(CreateMessageReceived(session, expected));

        Assert.Equal(expected, await observer.ReadAsync());
    }

    [Fact]
    public async Task ObserveMessagesAsync_SubscriptionDisposed_ReleasesPipelineSubscriptions()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var subscription = await harness.Service
            .ObserveMessagesAsync(OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: TestContext.Current.CancellationToken)
            .SubscribeAsync(new MessageObserverFake());
        Assert.NotEqual(0, harness.Pipeline.ObserverCount);

        await subscription.DisposeAsync();

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Fact]
    public async Task ObserveMessagesAsync_MessageError_ThrowsReportedExceptionWhenSubscribing()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        var expected = new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "Busy.");
        harness.Pipeline.Responder = ready => [CreateMessageError(ready, session, expected)];
        var observable = harness.Service.ObserveMessagesAsync(
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            async () => await observable.SubscribeAsync(new MessageObserverFake()));

        Assert.Same(expected, exception);
    }

    [Fact]
    public async Task ObserveMessagesAsync_NoWorkerResponds_ThrowsNoWorkerAvailableWhenSubscribing()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));
        var observable = harness.Service.ObserveMessagesAsync(
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), options: options, cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            async () => await observable.SubscribeAsync(new MessageObserverFake()));

        Assert.Equal(OpenNettyErrorCode.NoWorkerAvailable, exception.ErrorCode);
    }

    [Fact]
    public async Task ObserveMessagesAsync_CancelledToken_ThrowsOperationCanceledExceptionWhenSubscribing()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var observable = harness.Service.ObserveMessagesAsync(
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: source.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await observable.SubscribeAsync(new MessageObserverFake()));
    }

    [Fact]
    public async Task ObserveMessagesAsync_MessageError_ReleasesPipelineSubscriptions()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.Pipeline.Responder = ready => [CreateMessageError(ready, session, new OpenNettyException(OpenNettyErrorCode.GatewayBusy, "Busy."))];
        var observable = harness.Service.ObserveMessagesAsync(
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<OpenNettyException>(async () => await observable.SubscribeAsync(new MessageObserverFake()));

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Theory]
    [InlineData(OpenNettyProtocol.Scs)]
    [InlineData(OpenNettyProtocol.Nitoo)]
    [InlineData(OpenNettyProtocol.Zigbee)]
    public async Task SendMessageAsync_Message_PublishesSameMessageToGatewayMatchingItsProtocol(OpenNettyProtocol protocol)
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs, OpenNettyProtocol.Nitoo, OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(protocol));
        var message = OpenNettyMessage.CreateCommand(protocol, TestCommand);

        await harness.Service.SendMessageAsync(message, cancellationToken: TestContext.Current.CancellationToken);

        var ready = harness.Pipeline.SingleReady;
        Assert.Same(message, ready.Message);
        Assert.Equal(harness.Gateway(protocol), ready.Gateway);
    }

    [Fact]
    public async Task SetDimensionAsync_Values_PublishesDimensionSetMessage()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Zigbee);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Zigbee));
        var address = CreateDeviceAddress(OpenNettyProtocol.Zigbee);

        await harness.Service.SetDimensionAsync(
            OpenNettyProtocol.Zigbee, TestDimension, ["1", "2"], address,
            cancellationToken: TestContext.Current.CancellationToken);

        var message = harness.Pipeline.SingleReady.Message;
        Assert.Equal(OpenNettyMessageType.DimensionSet, message.Type);
        Assert.Equal(TestDimension, message.Dimension);
        Assert.Equal(address, message.Address);
        Assert.Equal(["1", "2"], message.Values);
    }

    [Fact]
    public async Task SetDimensionAsync_EmptyValues_ThrowsInvalidOperationException()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await harness.Service.SetDimensionAsync(
                OpenNettyProtocol.Scs, TestDimension, [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0061), exception.Message);
        Assert.Empty(harness.Pipeline.Ready);
    }

    [Fact]
    public async Task SetDimensionAsync_DefaultValues_ThrowsInvalidOperationException()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await harness.Service.SetDimensionAsync(
                OpenNettyProtocol.Scs, TestDimension, default, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0061), exception.Message);
    }

    [Fact]
    public async Task DispatchMessageAsync_NullMessage_ThrowsArgumentNullException()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var gateway = harness.Gateway(OpenNettyProtocol.Scs);

        await Assert.ThrowsAsync<ArgumentNullException>("message", async () =>
            await harness.Service.DispatchAsync(null!, gateway, gateway.Options.DefaultTransmissionOptions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DispatchMessageAsync_NullGateway_ThrowsArgumentNullException()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand);
        var options = harness.Gateway(OpenNettyProtocol.Scs).Options.DefaultTransmissionOptions;

        await Assert.ThrowsAsync<ArgumentNullException>("gateway", async () =>
            await harness.Service.DispatchAsync(message, null!, options, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DispatchMessageAsync_MessageSent_ReturnsSessionThatSentTheMessage()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(session);

        var result = await DispatchAsync(harness);

        Assert.Same(session, result);
    }

    [Fact]
    public async Task DispatchMessageAsync_Message_PublishesMessageReadyNotification()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        var gateway = harness.Gateway(OpenNettyProtocol.Scs);
        var message = OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand);
        var options = gateway.Options.DefaultTransmissionOptions with { PostSendingDelay = TimeSpan.FromSeconds(1) };

        await harness.Service.DispatchAsync(message, gateway, options, TestContext.Current.CancellationToken);

        var ready = harness.Pipeline.SingleReady;
        Assert.Same(message, ready.Message);
        Assert.Same(gateway, ready.Gateway);
        Assert.Same(options, ready.Options);
        Assert.NotEqual(default, ready.Transaction);
    }

    [Fact]
    public async Task DispatchMessageAsync_EachCall_UsesNewTransaction()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));

        await DispatchAsync(harness);
        await DispatchAsync(harness);

        var ready = harness.Pipeline.Ready;
        Assert.NotEqual(ready[0].Transaction, ready[1].Transaction);
    }

    [Fact]
    public async Task DispatchMessageAsync_Message_ObservesPipelineBeforePublishing()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));

        await DispatchAsync(harness);

        Assert.Equal(1, harness.Pipeline.ObserverCountAtLastPublish);
    }

    [Fact]
    public async Task DispatchMessageAsync_CompletedDispatch_ReleasesPipelineSubscription()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));

        await DispatchAsync(harness);

        Assert.Equal(0, harness.Pipeline.ObserverCount);
    }

    [Fact]
    public async Task DispatchMessageAsync_CancellationToken_IsForwardedToPipeline()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        harness.ReplyWithMessages(harness.CreateSession(OpenNettyProtocol.Scs));
        using var source = new CancellationTokenSource();

        await DispatchAsync(harness, source.Token);

        Assert.Equal(source.Token, harness.Pipeline.LastPublishToken);
    }

    [Fact]
    public async Task DispatchMessageAsync_CancellationRequestedWhileWaiting_ThrowsOperationCanceledException()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        using var source = new CancellationTokenSource();
        harness.Pipeline.Responder = _ =>
        {
            source.Cancel();
            return [];
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await DispatchAsync(harness, source.Token));
    }

    [Fact]
    public async Task DispatchMessageAsync_MessageError_ThrowsReportedException()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        var expected = new OpenNettyException(OpenNettyErrorCode.NoAcknowledgementReceived, "No acknowledgement.");
        harness.Pipeline.Responder = ready => [CreateMessageError(ready, session, expected)];

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await DispatchAsync(harness));

        Assert.Same(expected, exception);
    }

    [Fact]
    public async Task DispatchMessageAsync_NoNotification_ThrowsNoWorkerAvailable()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await DispatchAsync(harness, options: options));

        Assert.Equal(OpenNettyErrorCode.NoWorkerAvailable, exception.ErrorCode);
        Assert.Equal(SR.GetResourceString(SR.ID0033), exception.Message);
    }

    [Fact]
    public async Task DispatchMessageAsync_OnlyNotificationsFromOtherTransactions_ThrowsNoWorkerAvailable()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.Pipeline.Responder = ready =>
        [
            CreateMessageSent(ready, session, OpenNettyTransaction.Create()),
            CreateMessageError(ready, session, new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "Invalid."), OpenNettyTransaction.Create())
        ];
        var options = CreateShortTimeoutOptions(harness.Gateway(OpenNettyProtocol.Scs));

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await DispatchAsync(harness, options: options));

        Assert.Equal(OpenNettyErrorCode.NoWorkerAvailable, exception.ErrorCode);
    }

    [Fact]
    public async Task DispatchMessageAsync_NotificationsFromOtherGateways_AreIgnored()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        var otherGateway = CreateGateway(OpenNettyProtocol.Scs, port: 20001);
        harness.Pipeline.Responder = ready =>
        [
            CreateMessageSent(ready, CreateSession(otherGateway), gateway: otherGateway),
            CreateMessageSent(ready, session)
        ];

        var result = await DispatchAsync(harness);

        Assert.Same(session, result);
    }

    [Fact]
    public async Task DispatchMessageAsync_UnrelatedNotifications_AreIgnored()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        harness.Pipeline.Responder = ready =>
        [
            CreateMessageReceived(session, CreateAcknowledgement(OpenNettyProtocol.Scs)),
            CreateMessageSent(ready, session)
        ];

        var result = await DispatchAsync(harness);

        Assert.Same(session, result);
    }

    [Fact]
    public async Task DispatchMessageAsync_FirstTransactionNotificationWins_ReturnsItsOutcome()
    {
        var harness = new ServiceHarness(OpenNettyProtocol.Scs);
        var session = harness.CreateSession(OpenNettyProtocol.Scs);
        var expected = new OpenNettyException(OpenNettyErrorCode.InvalidFrame, "Invalid.");
        harness.Pipeline.Responder = ready =>
        [
            CreateMessageError(ready, session, expected),
            CreateMessageSent(ready, session)
        ];

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await DispatchAsync(harness));

        Assert.Same(expected, exception);
    }

    public enum DispatchingOperation
    {
        ExecuteCommand,
        SetDimension,
        SendMessage
    }

    public enum MessageOperation
    {
        SendMessage,
        ObserveMessages
    }

    public enum QueryOperation
    {
        GetDimension,
        GetStatus
    }

    public enum IgnoredReply
    {
        FromOtherSession,
        ForOtherAddress,
        ForOtherProtocol,
        FromOtherGateway,
        Acknowledgement
    }

    public enum NonUniqueAddress
    {
        NitooWithoutIdentifier,
        ZigbeeWithoutIdentifier,
        ScsLightPointArea,
        ScsLightPointGeneral,
        ScsLightPointGroup
    }

    public enum EnumerationNoise
    {
        ReplyFromOtherSession,
        AcknowledgementFromOtherSession,
        NegativeAcknowledgementFromOtherSession,
        ReplyFromOtherGateway,
        ReplyForOtherProtocol,
        ReplyWithoutAddress,
        ReplyForOtherDimensionOrCategory
    }

    private static async Task InvokeDispatchingAsync(
        OpenNettyService service,
        DispatchingOperation operation,
        OpenNettyProtocol protocol,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions? options = null,
        CancellationToken? cancellationToken = null)
    {
        var token = cancellationToken ?? TestContext.Current.CancellationToken;

        switch (operation)
        {
            case DispatchingOperation.ExecuteCommand:
                await service.ExecuteCommandAsync(protocol, TestCommand, gateway: gateway, options: options, cancellationToken: token);
                break;

            case DispatchingOperation.SetDimension:
                await service.SetDimensionAsync(protocol, TestDimension, ["1"], gateway: gateway, options: options, cancellationToken: token);
                break;

            case DispatchingOperation.SendMessage:
                await service.SendMessageAsync(OpenNettyMessage.CreateCommand(protocol, TestCommand), gateway, options, token);
                break;

            default:
                throw new NotSupportedException();
        }
    }

    private static async Task InvokeMessageOperationAsync(
        OpenNettyService service,
        MessageOperation operation,
        OpenNettyMessage message,
        OpenNettyGateway? gateway = null)
    {
        switch (operation)
        {
            case MessageOperation.SendMessage:
                await service.SendMessageAsync(message, gateway, cancellationToken: TestContext.Current.CancellationToken);
                break;

            case MessageOperation.ObserveMessages:
                _ = service.ObserveMessagesAsync(message, gateway, cancellationToken: TestContext.Current.CancellationToken);
                break;

            default:
                throw new NotSupportedException();
        }
    }

    private static ValueTask<OpenNettySession> DispatchAsync(
        ServiceHarness harness, CancellationToken? cancellationToken = null, OpenNettyTransmissionOptions? options = null)
    {
        var gateway = harness.Gateway(OpenNettyProtocol.Scs);

        return harness.Service.DispatchAsync(
            OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, TestCommand), gateway,
            options ?? gateway.Options.DefaultTransmissionOptions,
            cancellationToken ?? TestContext.Current.CancellationToken);
    }

    private static async Task<string> InvokeQueryAsync(
        OpenNettyService service,
        QueryOperation operation,
        OpenNettyProtocol protocol,
        OpenNettyAddress? address = null,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions? options = null,
        CancellationToken? cancellationToken = null)
    {
        var token = cancellationToken ?? TestContext.Current.CancellationToken;

        if (operation is QueryOperation.GetDimension)
        {
            var values = await service.GetDimensionAsync(
                protocol, TestDimension, address, gateway: gateway, options: options, cancellationToken: token);

            return string.Join(',', values);
        }

        var command = await service.GetStatusAsync(
            protocol, OpenNettyCategories.Lighting, address, gateway: gateway, options: options, cancellationToken: token);

        return command.Value;
    }

    private static OpenNettyMessage CreateReply(
        QueryOperation operation, OpenNettyProtocol protocol, OpenNettyAddress? address, string marker)
        => operation is QueryOperation.GetDimension
            ? OpenNettyMessage.CreateDimensionRead(protocol, TestDimension, [marker], address)
            : OpenNettyMessage.CreateCommand(protocol, new OpenNettyCommand(OpenNettyCategories.Lighting, marker), address);

    private static OpenNettyErrorCode GetNoReplyErrorCode(QueryOperation operation)
        => operation is QueryOperation.GetDimension
            ? OpenNettyErrorCode.NoDimensionReadReceived
            : OpenNettyErrorCode.NoStatusReplyReceived;

    private static OpenNettyNotifications.MessageReceived CreateIgnoredReply(
        ServiceHarness harness, QueryOperation operation, IgnoredReply ignored, OpenNettySession session)
    {
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        var otherAddress = OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, 2);

        return ignored switch
        {
            IgnoredReply.FromOtherSession => CreateMessageReceived(
                harness.CreateSession(OpenNettyProtocol.Scs),
                CreateReply(operation, OpenNettyProtocol.Scs, address, "2")),

            IgnoredReply.ForOtherAddress => CreateMessageReceived(
                session, CreateReply(operation, OpenNettyProtocol.Scs, otherAddress, "2")),

            IgnoredReply.ForOtherProtocol => CreateMessageReceived(
                session, CreateReply(operation, OpenNettyProtocol.Nitoo, CreateDeviceAddress(OpenNettyProtocol.Nitoo), "2")),

            IgnoredReply.FromOtherGateway => CreateMessageReceived(
                session, CreateReply(operation, OpenNettyProtocol.Scs, address, "2"), CreateGateway(OpenNettyProtocol.Scs, port: 20001)),

            IgnoredReply.Acknowledgement => CreateMessageReceived(
                session, CreateAcknowledgement(OpenNettyProtocol.Scs)),

            _ => throw new NotSupportedException()
        };
    }

    private static (OpenNettyProtocol Protocol, OpenNettyAddress Address, string Message) CreateNonUniqueAddress(NonUniqueAddress kind)
        => kind switch
        {
            NonUniqueAddress.NitooWithoutIdentifier => (
                OpenNettyProtocol.Nitoo, OpenNettyAddress.FromNitooAddress(0, 1), SR.GetResourceString(SR.ID0128)),

            NonUniqueAddress.ZigbeeWithoutIdentifier => (
                OpenNettyProtocol.Zigbee, OpenNettyAddress.FromDecimalZigbeeAddress(0, 5), SR.GetResourceString(SR.ID0128)),

            NonUniqueAddress.ScsLightPointArea => (
                OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, null), SR.GetResourceString(SR.ID0129)),

            NonUniqueAddress.ScsLightPointGeneral => (
                OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, true, null, null, null), SR.GetResourceString(SR.ID0129)),

            NonUniqueAddress.ScsLightPointGroup => (
                OpenNettyProtocol.Scs, OpenNettyAddress.FromScsLightPointAddress(0, false, 5, null, null), SR.GetResourceString(SR.ID0129)),

            _ => throw new NotSupportedException()
        };

    private static IAsyncEnumerable<(OpenNettyAddress Address, string Marker)> EnumerateMarkersAsync(
        OpenNettyService service,
        ServiceOperation operation,
        OpenNettyProtocol protocol,
        OpenNettyGateway? gateway = null,
        OpenNettyTransmissionOptions? options = null)
        => EnumerateMarkersCoreAsync(service, operation, protocol, gateway, options, TestContext.Current.CancellationToken);

    private static async IAsyncEnumerable<(OpenNettyAddress Address, string Marker)> EnumerateMarkersCoreAsync(
        OpenNettyService service,
        ServiceOperation operation,
        OpenNettyProtocol protocol,
        OpenNettyGateway? gateway,
        OpenNettyTransmissionOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (operation is ServiceOperation.EnumerateDimensions)
        {
            await foreach (var (address, values) in service.EnumerateDimensionsAsync(
                protocol, TestDimension, gateway: gateway, options: options, cancellationToken: cancellationToken))
            {
                yield return (address, string.Join(',', values));
            }

            yield break;
        }

        await foreach (var (address, command) in service.EnumerateStatusesAsync(
            protocol, OpenNettyCategories.Lighting, gateway: gateway, options: options, cancellationToken: cancellationToken))
        {
            yield return (address, command.Value);
        }
    }

    private static OpenNettyMessage CreateEnumerationReply(
        ServiceOperation operation, OpenNettyProtocol protocol, OpenNettyAddress? address, string marker)
        => CreateReply(
            operation is ServiceOperation.EnumerateDimensions ? QueryOperation.GetDimension : QueryOperation.GetStatus,
            protocol, address, marker);

    private static OpenNettyNotifications.MessageReceived CreateNoise(
        ServiceHarness harness, ServiceOperation operation, EnumerationNoise noise, OpenNettySession session)
    {
        var address = CreateDeviceAddress(OpenNettyProtocol.Scs);
        var otherSession = harness.CreateSession(OpenNettyProtocol.Scs);

        return noise switch
        {
            EnumerationNoise.ReplyFromOtherSession => CreateMessageReceived(
                otherSession, CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "2")),

            EnumerationNoise.AcknowledgementFromOtherSession => CreateMessageReceived(
                otherSession, CreateAcknowledgement(OpenNettyProtocol.Scs)),

            EnumerationNoise.NegativeAcknowledgementFromOtherSession => CreateMessageReceived(
                otherSession, CreateNegativeAcknowledgement(OpenNettyProtocol.Scs)),

            EnumerationNoise.ReplyFromOtherGateway => CreateMessageReceived(
                session, CreateEnumerationReply(operation, OpenNettyProtocol.Scs, address, "2"),
                CreateGateway(OpenNettyProtocol.Scs, port: 20001)),

            EnumerationNoise.ReplyForOtherProtocol => CreateMessageReceived(
                session, CreateEnumerationReply(operation, OpenNettyProtocol.Nitoo, CreateDeviceAddress(OpenNettyProtocol.Nitoo), "2")),

            EnumerationNoise.ReplyWithoutAddress => CreateMessageReceived(
                session, CreateEnumerationReply(operation, OpenNettyProtocol.Scs, null, "2")),

            EnumerationNoise.ReplyForOtherDimensionOrCategory => CreateMessageReceived(
                session,
                operation is ServiceOperation.EnumerateDimensions
                    ? OpenNettyMessage.CreateDimensionRead(OpenNettyProtocol.Scs, new OpenNettyDimension(OpenNettyCategories.Lighting, "2"), ["2"], address)
                    : OpenNettyMessage.CreateCommand(OpenNettyProtocol.Scs, OtherCategoryCommand, address)),

            _ => throw new NotSupportedException()
        };
    }

    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly OpenNettyDimension TestDimension = new(OpenNettyCategories.Lighting, "1");
    private static readonly OpenNettyCommand TestCommand = new(OpenNettyCategories.Lighting, "1");
    private static readonly OpenNettyCommand OtherCategoryCommand = new(OpenNettyCategories.Automation, "1");

    public enum ServiceOperation
    {
        EnumerateDimensions,
        EnumerateStatuses,
        ExecuteCommand,
        GetDimension,
        GetStatus,
        SetDimension
    }

    private static async Task InvokeAsync(
        OpenNettyService service,
        ServiceOperation operation,
        OpenNettyProtocol protocol,
        OpenNettyGateway? gateway = null)
    {
        var token = TestContext.Current.CancellationToken;

        switch (operation)
        {
            case ServiceOperation.EnumerateDimensions:
                _ = service.EnumerateDimensionsAsync(protocol, TestDimension, gateway: gateway, cancellationToken: token);
                break;

            case ServiceOperation.EnumerateStatuses:
                _ = service.EnumerateStatusesAsync(protocol, OpenNettyCategories.Lighting, gateway: gateway, cancellationToken: token);
                break;

            case ServiceOperation.ExecuteCommand:
                await service.ExecuteCommandAsync(protocol, TestCommand, gateway: gateway, cancellationToken: token);
                break;

            case ServiceOperation.GetDimension:
                await service.GetDimensionAsync(protocol, TestDimension, gateway: gateway, cancellationToken: token);
                break;

            case ServiceOperation.GetStatus:
                await service.GetStatusAsync(protocol, OpenNettyCategories.Lighting, gateway: gateway, cancellationToken: token);
                break;

            case ServiceOperation.SetDimension:
                await service.SetDimensionAsync(protocol, TestDimension, ["1"], gateway: gateway, cancellationToken: token);
                break;

            default:
                throw new NotSupportedException();
        }
    }

    private static OpenNettyGateway CreateGateway(OpenNettyProtocol protocol, int port = 20000)
    {
        var (brand, model) = protocol switch
        {
            OpenNettyProtocol.Scs    => (OpenNettyBrand.BTicino, "MyHOMEServer1"),
            OpenNettyProtocol.Nitoo  => (OpenNettyBrand.Legrand, "88213"),
            OpenNettyProtocol.Zigbee => (OpenNettyBrand.Legrand, "88328"),

            _ => throw new NotSupportedException()
        };

        var device = OpenNettyDevice.Create(brand, model, "Gateway", identifier: null);
        var gateway = OpenNettyGateway.Create(device, new IPEndPoint(IPAddress.Loopback, port));

        return gateway with
        {
            Options = gateway.Options with
            {
                DefaultTransmissionOptions = gateway.Options.DefaultTransmissionOptions with
                {
                    OutgoingMessageResiliencePipeline = ResiliencePipeline.Empty,
                    OutgoingMessageProcessingTimeout = LongTimeout,
                    MultipleDimensionReplyTimeout = LongTimeout,
                    MultipleStatusReplyTimeout = LongTimeout,
                    UniqueDimensionReplyTimeout = LongTimeout,
                    UniqueStatusReplyTimeout = LongTimeout
                }
            }
        };
    }

    private static OpenNettyTransmissionOptions CreateShortTimeoutOptions(OpenNettyGateway gateway)
        => gateway.Options.DefaultTransmissionOptions with
        {
            OutgoingMessageProcessingTimeout = ShortTimeout,
            MultipleDimensionReplyTimeout = ShortTimeout,
            MultipleStatusReplyTimeout = ShortTimeout,
            UniqueDimensionReplyTimeout = ShortTimeout,
            UniqueStatusReplyTimeout = ShortTimeout
        };

    private static OpenNettyAddress CreateDeviceAddress(OpenNettyProtocol protocol, byte unit = 1) => protocol switch
    {
        OpenNettyProtocol.Scs    => OpenNettyAddress.FromScsLightPointAddress(0, false, null, 1, unit),
        OpenNettyProtocol.Nitoo  => OpenNettyAddress.FromNitooAddress(1, unit),
        OpenNettyProtocol.Zigbee => OpenNettyAddress.FromDecimalZigbeeAddress(100, unit),

        _ => throw new NotSupportedException()
    };

    private static OpenNettySession CreateSession(
        OpenNettyGateway gateway, OpenNettySessionType type = OpenNettySessionType.Command)
    {
        var constructor = typeof(OpenNettySession).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();

        return (OpenNettySession) constructor.Invoke([gateway, type, new ConnectionFake()]);
    }

    private static OpenNettyMessage CreateAcknowledgement(OpenNettyProtocol protocol)
        => OpenNettyMessage.CreateFromFrame(protocol, "*#*1##");

    private static OpenNettyMessage CreateNegativeAcknowledgement(OpenNettyProtocol protocol)
        => OpenNettyMessage.CreateFromFrame(protocol, "*#*0##");

    private static OpenNettyMessage CreateBusyNegativeAcknowledgement()
        => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, "*#*6##");

    private static OpenNettyNotifications.MessageSent CreateMessageSent(
        OpenNettyNotifications.MessageReady ready, OpenNettySession session,
        OpenNettyTransaction? transaction = null, OpenNettyGateway? gateway = null) => new()
        {
            Gateway = gateway ?? ready.Gateway,
            Message = ready.Message,
            Session = session,
            Transaction = transaction ?? ready.Transaction
        };

    private static OpenNettyNotifications.MessageError CreateMessageError(
        OpenNettyNotifications.MessageReady ready, OpenNettySession session, OpenNettyException exception,
        OpenNettyTransaction? transaction = null, OpenNettyGateway? gateway = null) => new()
        {
            Exception = exception,
            Gateway = gateway ?? ready.Gateway,
            Message = ready.Message,
            Session = session,
            Transaction = transaction ?? ready.Transaction
        };

    private static OpenNettyNotifications.MessageReceived CreateMessageReceived(
        OpenNettySession session, OpenNettyMessage message, OpenNettyGateway? gateway = null) => new()
        {
            Gateway = gateway ?? session.Gateway,
            Message = message,
            Session = session
        };

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var items = new List<T>();

        await foreach (var item in source.WithCancellation(TestContext.Current.CancellationToken))
        {
            items.Add(item);
        }

        return items;
    }

    private sealed class ServiceHarness
    {
        public ServiceHarness(params OpenNettyProtocol[] protocols)
        {
            foreach (var protocol in protocols)
            {
                Options.Gateways.Add(CreateGateway(protocol));
            }

            var monitor = Substitute.For<IOptionsMonitor<OpenNettyOptions>>();
            monitor.CurrentValue.Returns(Options);

            Service = new ExposedService(Logger, monitor, Pipeline);
        }

        public ILogger<OpenNettyService> Logger { get; } = Substitute.For<ILogger<OpenNettyService>>();

        public OpenNettyOptions Options { get; } = new();

        public PipelineFake Pipeline { get; } = new();

        public ExposedService Service { get; }

        public OpenNettyGateway Gateway(OpenNettyProtocol protocol)
            => Options.Gateways.First(gateway => gateway.Protocol == protocol);

        public OpenNettySession CreateSession(OpenNettyProtocol protocol, OpenNettySessionType type = OpenNettySessionType.Command)
            => OpenNettyServiceTests.CreateSession(Gateway(protocol), type);

        public void ReplyWithMessages(OpenNettySession session, params OpenNettyMessage[] messages)
            => Pipeline.Responder = ready =>
            [
                CreateMessageSent(ready, session),
                .. messages.Select(message => CreateMessageReceived(session, message))
            ];

        public void ReplyWithNotifications(OpenNettySession session, params OpenNettyNotification[] notifications)
            => Pipeline.Responder = ready => [CreateMessageSent(ready, session), .. notifications];
    }

    private sealed class ExposedService : OpenNettyService
    {
        public ExposedService(
            ILogger<OpenNettyService> logger,
            IOptionsMonitor<OpenNettyOptions> options,
            IOpenNettyPipeline pipeline)
            : base(logger, options, pipeline)
        {
        }

        public ValueTask<OpenNettySession> DispatchAsync(
            OpenNettyMessage message,
            OpenNettyGateway gateway,
            OpenNettyTransmissionOptions options,
            CancellationToken cancellationToken)
            => DispatchMessageAsync(message, gateway, options, cancellationToken);
    }

    private sealed class PipelineFake : IOpenNettyPipeline
    {
        private readonly List<IAsyncObserver<OpenNettyNotification>> _observers = [];
        private readonly List<OpenNettyNotifications.MessageReady> _ready = [];
        private readonly Lock _lock = new();

        public Func<OpenNettyNotifications.MessageReady, IEnumerable<OpenNettyNotification>> Responder { get; set; } = static _ => [];

        public int ObserverCount
        {
            get
            {
                lock (_lock)
                {
                    return _observers.Count;
                }
            }
        }

        public int ObserverCountAtLastPublish { get; private set; }

        public CancellationToken LastPublishToken { get; private set; }

        public IReadOnlyList<OpenNettyNotifications.MessageReady> Ready
        {
            get
            {
                lock (_lock)
                {
                    return [.. _ready];
                }
            }
        }

        public OpenNettyNotifications.MessageReady SingleReady => Assert.Single(Ready);

        public ValueTask<IAsyncDisposable> ConnectAsync() => new(new NoopDisposable());

        public ValueTask<IAsyncDisposable> SubscribeAsync(IAsyncObserver<OpenNettyNotification> observer)
        {
            lock (_lock)
            {
                _observers.Add(observer);
            }

            return new(new Subscription(this, observer));
        }

        public async ValueTask PublishAsync(OpenNettyNotification notification, CancellationToken cancellationToken = default)
        {
            ObserverCountAtLastPublish = ObserverCount;
            LastPublishToken = cancellationToken;

            if (notification is not OpenNettyNotifications.MessageReady ready)
            {
                await EmitAsync(notification);
                return;
            }

            lock (_lock)
            {
                _ready.Add(ready);
            }

            foreach (var response in Responder(ready))
            {
                await EmitAsync(response);
            }
        }

        public async ValueTask EmitAsync(OpenNettyNotification notification)
        {
            IAsyncObserver<OpenNettyNotification>[] observers;

            lock (_lock)
            {
                observers = [.. _observers];
            }

            foreach (var observer in observers)
            {
                await observer.OnNextAsync(notification);
            }
        }

        private sealed class Subscription(PipelineFake pipeline, IAsyncObserver<OpenNettyNotification> observer) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                lock (pipeline._lock)
                {
                    pipeline._observers.Remove(observer);
                }

                return ValueTask.CompletedTask;
            }
        }

        private sealed class NoopDisposable : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class MessageObserverFake : IAsyncObserver<OpenNettyMessage>
    {
        private readonly Channel<OpenNettyMessage> _channel = Channel.CreateUnbounded<OpenNettyMessage>();

        public ValueTask OnNextAsync(OpenNettyMessage value) => _channel.Writer.WriteAsync(value);

        public ValueTask OnErrorAsync(Exception error)
        {
            _channel.Writer.TryComplete(error);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnCompletedAsync()
        {
            _channel.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public ValueTask<OpenNettyMessage> ReadAsync() => _channel.Reader.ReadAsync(TestContext.Current.CancellationToken);
    }

    private sealed class ConnectionFake : OpenNettyConnection
    {
        public override OpenNettyConnectionType Type => OpenNettyConnectionType.Tcp;

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public override ValueTask<OpenNettyFrame?> ReceiveAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override ValueTask SendAsync(OpenNettyFrame frame, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        protected internal override ValueTask InitializeAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
