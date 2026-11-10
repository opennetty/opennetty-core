/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/opennetty/opennetty-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System.Globalization;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Xunit;

namespace OpenNetty.Tests;

public sealed class OpenNettySessionTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NegotiationTimeout = TimeSpan.FromMilliseconds(500);

    private static readonly OpenNettyCapability[] AllSessionCapabilities =
    [
        OpenNettyCapabilities.OpenWebNetCommandSession,
        OpenNettyCapabilities.OpenWebNetEventSession,
        OpenNettyCapabilities.OpenWebNetGenericSession
    ];

    private const string Ack = "*#*1##";
    private const string Nack = "*#*0##";
    private const string BusyNack = "*#*6##";

    [Fact]
    public async Task SendAsync_NullMessage_ThrowsArgumentNullException()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await harness.Session.SendAsync(null!, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("message", exception.ParamName);
    }

    [Fact]
    public async Task SendAsync_DisposedSession_ThrowsObjectDisposedException()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.Session.DisposeAsync();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await harness.Session.SendAsync(ScsLightOn(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0009), exception.ObjectName);
    }

    [Fact]
    public async Task SendAsync_MessageOfAnotherProtocol_ThrowsInvalidOperationException()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        var message = OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, "*1*1*1234##");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await harness.Session.SendAsync(message, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SR.FormatID0010(nameof(OpenNettyProtocol.Nitoo), harness.Gateway.Device.Name), exception.Message);
    }

    [Fact]
    public async Task SendAsync_AcknowledgedMessage_SendsFrameToGateway()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(ScsLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);

        await sending;
    }

    [Fact]
    public async Task SendAsync_NoExplicitOptions_UsesGatewayDefaultTransmissionOptions()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, configure: options => options with
        {
            DefaultTransmissionOptions = options.DefaultTransmissionOptions with { DisableAcknowledgementValidation = true }
        });
        await harness.ConnectAsync();

        await harness.Session.SendAsync(ScsLightOn(), cancellationToken: TestContext.Current.CancellationToken);

        await harness.Peer.ExpectAsync("*1*1*21##");
    }

    [Fact]
    public async Task SendAsync_AcknowledgementValidationDisabled_DoesNotWaitForGateway()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var options = harness.Options() with { DisableAcknowledgementValidation = true };

        await harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken);

        await harness.Peer.ExpectAsync("*1*1*21##");
    }

    [Fact]
    public async Task SendAsync_NoAcknowledgement_ThrowsNoAcknowledgementReceived()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var options = harness.Options() with { AcknowledgementTimeout = ShortTimeout };

        var exception = await Assert.ThrowsAsync<OpenNettyException>(
            async () => await harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken));

        AssertError(exception, OpenNettyErrorCode.NoAcknowledgementReceived, SR.ID0012);
    }

    [Fact]
    public async Task SendAsync_UnrelatedFramesOnly_ThrowsNoAcknowledgementReceived()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var options = harness.Options() with { AcknowledgementTimeout = ShortTimeout };

        var sending = harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.SendAsync("*1*0*22##");

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        Assert.Equal(OpenNettyErrorCode.NoAcknowledgementReceived, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_NegativeAcknowledgement_ThrowsInvalidFrame()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(ScsLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Nack);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0014);
    }

    [Fact]
    public async Task SendAsync_BusyNegativeAcknowledgementFollowedByNegativeAcknowledgement_ThrowsGatewayBusy()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(ZigbeeLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*00#9##");
        await harness.Peer.SendAsync(BusyNack);
        await harness.Peer.SendAsync(Nack);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        AssertError(exception, OpenNettyErrorCode.GatewayBusy, SR.ID0013);
    }

    [Fact]
    public async Task SendAsync_BusyNegativeAcknowledgementWithoutNegativeAcknowledgement_ThrowsNoAcknowledgementReceived()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { AcknowledgementTimeout = ShortTimeout };

        var sending = harness.Session.SendAsync(ZigbeeLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*00#9##");
        await harness.Peer.SendAsync(BusyNack);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        AssertError(exception, OpenNettyErrorCode.NoAcknowledgementReceived, SR.ID0012);
    }

    [Fact]
    public async Task SendAsync_ConcurrentSend_ThrowsInvalidOperationException()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var options = harness.Options();
        var first = harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0011), exception.Message);
        await harness.Peer.SendAsync(Ack);
        await first;
    }

    [Fact]
    public async Task SendAsync_AfterFailedSend_ReleasesLock()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var failing = harness.Options() with { AcknowledgementTimeout = ShortTimeout };
        await Assert.ThrowsAsync<OpenNettyException>(
            async () => await harness.Session.SendAsync(ScsLightOn(), failing, TestContext.Current.CancellationToken));
        await harness.Peer.ExpectAsync("*1*1*21##");

        var sending = harness.Session.SendAsync(ScsLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);

        await sending;
    }

    [Fact]
    public async Task SendAsync_CanceledWhileWaitingForAcknowledgement_ThrowsOperationCanceledException()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var sending = harness.Session.SendAsync(ScsLightOn(), harness.Options(), source.Token).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sending);
    }

    [Fact]
    public async Task SendAsync_CanceledDuringCustomValidation_ThrowsOperationCanceledException()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var validating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = harness.Options() with
        {
            CustomValidationDelegate = async (_, token) =>
            {
                validating.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, token);
                return true;
            }
        };
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var sending = harness.Session.SendAsync(ScsLightOn(), options, source.Token).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);
        await validating.Task.WaitAsync(LongTimeout, TimeProvider.System, TestContext.Current.CancellationToken);

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sending);
    }

    [Fact]
    public async Task SendAsync_AlreadyCanceledToken_ThrowsOperationCanceledException()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await harness.Session.SendAsync(ScsLightOn(), harness.Options(), source.Token));
    }

    [Fact]
    public async Task SendAsync_PostSendingDelay_IsAppliedUsingConfiguredTimeProvider()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var timeProvider = new ImmediateTimeProvider();
        var options = harness.Options() with { PostSendingDelay = TimeSpan.FromMilliseconds(150), TimeProvider = timeProvider };

        var sending = harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);
        await sending;

        Assert.Equal([TimeSpan.FromMilliseconds(150)], timeProvider.Delays);
    }

    [Fact]
    public async Task SendAsync_NoPostSendingDelay_DoesNotWait()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var timeProvider = new ImmediateTimeProvider();
        var options = harness.Options() with { PostSendingDelay = TimeSpan.Zero, TimeProvider = timeProvider };

        var sending = harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);
        await sending;

        Assert.Empty(timeProvider.Delays);
    }

    [Fact]
    public async Task SendAsync_NitooValidAction_Completes()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(NitooLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234##");
        await harness.Peer.SendAsync(Ack);
        await harness.Peer.SendAsync("*1000*72*1234##");

        await sending;
    }

    [Fact]
    public async Task SendAsync_NitooInvalidAction_ThrowsInvalidAction()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(NitooLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234##");
        await harness.Peer.SendAsync(Ack);
        await harness.Peer.SendAsync("*1000*73*1234##");

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        Assert.Equal(OpenNettyErrorCode.InvalidAction, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_NitooNoActionReply_ThrowsNoActionReceived()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { ActionValidationTimeout = ShortTimeout };

        var sending = harness.Session.SendAsync(NitooLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234##");
        await harness.Peer.SendAsync(Ack);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        Assert.Equal(OpenNettyErrorCode.NoActionReceived, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_NitooInvalidAction_ReportsRejectionByRemoteDevice()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(NitooLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234##");
        await harness.Peer.SendAsync(Ack);
        await harness.Peer.SendAsync("*1000*73*1234##");

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        AssertError(exception, OpenNettyErrorCode.InvalidAction, SR.ID0031);
    }

    [Fact]
    public async Task SendAsync_NitooNoActionReply_ReportsMissingAcknowledgementFromRemoteDevice()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { ActionValidationTimeout = ShortTimeout };

        var sending = harness.Session.SendAsync(NitooLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234##");
        await harness.Peer.SendAsync(Ack);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        AssertError(exception, OpenNettyErrorCode.NoActionReceived, SR.ID0032);
    }

    [Theory]
    [InlineData("*1000*72*1235##")]
    [InlineData("*1000*74*1234##")]
    [InlineData("*1*1*1234##")]
    public async Task SendAsync_NitooUnrelatedActionReply_ThrowsNoActionReceived(string reply)
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { ActionValidationTimeout = ShortTimeout };

        var sending = harness.Session.SendAsync(NitooLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234##");
        await harness.Peer.SendAsync(Ack);
        await harness.Peer.SendAsync(reply);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        Assert.Equal(OpenNettyErrorCode.NoActionReceived, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_NitooActionValidationDisabled_DoesNotWaitForActionReply()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { DisableActionValidation = true };

        var sending = harness.Session.SendAsync(NitooLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234##");
        await harness.Peer.SendAsync(Ack);

        await sending;
    }

    [Fact]
    public async Task SendAsync_ZigbeeStatusReport_Completes()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(ZigbeeUnicastLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234501#9##");
        await harness.Peer.SendAsync(Ack);
        await harness.Peer.SendAsync("*1*1*1234501#9##");

        await sending;
    }

    [Fact]
    public async Task SendAsync_ZigbeeNoStatusReport_ThrowsNoStatusReportReceived()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { StatusReportTimeout = ShortTimeout };

        var sending = harness.Session.SendAsync(ZigbeeUnicastLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234501#9##");
        await harness.Peer.SendAsync(Ack);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        AssertError(exception, OpenNettyErrorCode.NoStatusReportReceived, SR.ID0137);
    }

    [Theory]
    [InlineData("*1*1*1234502#9##")]
    [InlineData("*2*1*1234501#9##")]
    public async Task SendAsync_ZigbeeStatusReportForAnotherAddressOrCategory_ThrowsNoStatusReportReceived(string report)
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { StatusReportTimeout = ShortTimeout };

        var sending = harness.Session.SendAsync(ZigbeeUnicastLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234501#9##");
        await harness.Peer.SendAsync(Ack);
        await harness.Peer.SendAsync(report);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        Assert.Equal(OpenNettyErrorCode.NoStatusReportReceived, exception.ErrorCode);
    }

    [Fact]
    public async Task SendAsync_ZigbeeStatusReportValidationDisabled_DoesNotWaitForStatusReport()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        await harness.ConnectAsync();
        var options = harness.Options() with { DisableStatusReportValidation = true };

        var sending = harness.Session.SendAsync(ZigbeeUnicastLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*1234501#9##");
        await harness.Peer.SendAsync(Ack);

        await sending;
    }

    [Fact]
    public async Task SendAsync_ZigbeeMessageWithoutDeviceAddress_DoesNotWaitForStatusReport()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic);
        await harness.ConnectAsync();

        var sending = harness.Session.SendAsync(ZigbeeLightOn(), harness.Options(), TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*00#9##");
        await harness.Peer.SendAsync(Ack);

        await sending;
    }

    [Fact]
    public async Task SendAsync_CustomValidationAccepted_Completes()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var options = harness.Options() with { CustomValidationDelegate = static (_, _) => ValueTask.FromResult(true) };

        var sending = harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);

        await sending;
    }

    [Fact]
    public async Task SendAsync_CustomValidationRejected_ThrowsCustomValidationFailed()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var options = harness.Options() with { CustomValidationDelegate = static (_, _) => ValueTask.FromResult(false) };

        var sending = harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await sending);
        AssertError(exception, OpenNettyErrorCode.CustomValidationFailed, SR.ID0138);
    }

    [Fact]
    public async Task SendAsync_CustomValidation_ReceivesMessagesReturnedByGateway()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.ConnectAsync();
        var options = harness.Options() with
        {
            CustomValidationDelegate = static async (messages, token) => await messages
                .Where(static message => message.Type is OpenNettyMessageType.BusCommand)
                .FirstOrDefault()
                .RunAsync(token) is not null
        };

        var sending = harness.Session.SendAsync(ScsLightOn(), options, TestContext.Current.CancellationToken).AsTask();
        await harness.Peer.ExpectAsync("*1*1*21##");
        await harness.Peer.SendAsync(Ack);
        await harness.Peer.SendAsync("*1*1*21##");

        await sending;
    }

    [Fact]
    public async Task CreateAsync_NullGateway_ThrowsArgumentNullException()
    {
        var exception = await Assert.ThrowsAsync<ArgumentNullException>(async () => await OpenNettySession.CreateAsync(
            null!, OpenNettySessionType.Command, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("gateway", exception.ParamName);
    }

    [Fact]
    public async Task CreateAsync_UndefinedSessionType_ThrowsArgumentOutOfRangeException()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var gateway = server.CreateGateway(OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs, AllSessionCapabilities));

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await OpenNettySession.CreateAsync(
            gateway, (OpenNettySessionType) 42, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("type", exception.ParamName);
        Assert.StartsWith(SR.GetResourceString(SR.ID0016), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OpenNettySessionType.Command)]
    [InlineData(OpenNettySessionType.Event)]
    [InlineData(OpenNettySessionType.Generic)]
    public async Task CreateAsync_SessionTypeNotSupportedByGateway_ThrowsInvalidOperationException(OpenNettySessionType type)
    {
        await using var server = new OpenNettyLoopbackGateway();
        var capabilities = AllSessionCapabilities.Where(capability => capability != GetCapability(type)).ToArray();
        var gateway = server.CreateGateway(OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs, capabilities));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await OpenNettySession.CreateAsync(
            gateway, type, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(SR.GetResourceString(SR.ID0017), exception.Message);
    }

    [Theory]
    [InlineData(OpenNettySessionType.Command, "*99*9##")]
    [InlineData(OpenNettySessionType.Event, "*99*1##")]
    public async Task CreateAsync_ScsSession_NegotiatesRequestedSessionType(OpenNettySessionType type, string request)
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, type, script: peer => peer.AcceptSessionAsync(request));

        Assert.Equal(type, harness.Session.Type);
        Assert.Equal(OpenNettyProtocol.Scs, harness.Session.Protocol);
        Assert.Same(harness.Gateway, harness.Session.Gateway);
        Assert.NotEqual(Guid.Empty, harness.Session.Id);
    }

    [Fact]
    public async Task CreateAsync_ConnectionClosedBeforeAcknowledgement_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command,
            peer => Task.Run(peer.Disconnect, TestContext.Current.CancellationToken));

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Fact]
    public async Task CreateAsync_ConnectionNotAcknowledged_ThrowsInvalidFrameAndClosesConnection()
    {
        var exception = await FailToNegotiateAsync(
            OpenNettyProtocol.Scs, OpenNettySessionType.Command,
            script: peer => peer.SendAsync(Nack),
            afterFailure: peer => peer.ExpectClosedAsync());

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0018);
    }

    [Fact]
    public async Task CreateAsync_ConnectionClosedAfterSessionRequest_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            peer.Disconnect();
        });

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Theory]
    [InlineData(Nack)]
    [InlineData("*1*1*21##")]
    public async Task CreateAsync_SessionRequestRejected_ThrowsInvalidFrame(string reply)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync(reply);
        });

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0018);
    }

    [Fact]
    public async Task CreateAsync_NoReplyFromGateway_ThrowsNegotiationTimeout()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command,
            peer => Task.CompletedTask, configure: WithNegotiationTimeout(NegotiationTimeout));

        AssertError(exception, OpenNettyErrorCode.NegotiationTimeout, SR.ID0024);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task CreateAsync_ExplicitSessionOptions_OverrideGatewayDefaults()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var gateway = server.CreateGateway(OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs, AllSessionCapabilities));
        var options = gateway.Options.DefaultSessionOptions with { ConnectionNegotiationTimeout = NegotiationTimeout };

        var creating = OpenNettySession.CreateAsync(
            gateway, OpenNettySessionType.Command, options, TestContext.Current.CancellationToken).AsTask();
        await server.AcceptAsync();

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await creating);
        Assert.Equal(OpenNettyErrorCode.NegotiationTimeout, exception.ErrorCode);
    }

    [Fact]
    public async Task CreateAsync_CanceledByCaller_ThrowsOperationCanceledExceptionAndClosesConnection()
    {
        await using var server = new OpenNettyLoopbackGateway();
        var gateway = server.CreateGateway(OpenNettyLoopbackGateway.CreateDevice(OpenNettyProtocol.Scs, AllSessionCapabilities));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var creating = OpenNettySession.CreateAsync(gateway, OpenNettySessionType.Command, cancellationToken: source.Token).AsTask();
        var peer = await server.AcceptAsync();

        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await creating);
        await peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task CreateAsync_LegacyAuthentication_SendsObfuscatedPassword()
    {
        await using var harness = await SessionHarness.OpenAsync(
            OpenNettyProtocol.Scs, OpenNettySessionType.Command, password: "12345", script: async peer =>
            {
                await peer.SendAsync(Ack);
                await peer.ExpectAsync("*99*9##");
                await peer.SendAsync("*#603356072##");
                await peer.ExpectAsync("*#25280520##");
                await peer.SendAsync(Ack);
            });

        Assert.Equal(OpenNettySessionType.Command, harness.Session.Type);
    }

    [Theory]
    [InlineData(Nack, OpenNettyErrorCode.AuthenticationInvalid, nameof(SR.ID0022))]
    [InlineData("*1*1*21##", OpenNettyErrorCode.InvalidFrame, nameof(SR.ID0023))]
    public async Task CreateAsync_LegacyAuthenticationNotAccepted_ThrowsExpectedError(string reply, OpenNettyErrorCode code, string resource)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*#603356072##");
            await peer.ExpectAsync("*#25280520##");
            await peer.SendAsync(reply);
        }, password: "12345");

        AssertError(exception, code, resource);
    }

    [Fact]
    public async Task CreateAsync_LegacyAuthenticationConnectionClosed_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*#603356072##");
            await peer.ExpectAsync("*#25280520##");
            peer.Disconnect();
        }, password: "12345");

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateAsync_LegacyAuthenticationWithoutPassword_ThrowsAuthenticationRequired(string? password)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*#603356072##");
        }, password);

        AssertError(exception, OpenNettyErrorCode.AuthenticationRequired, SR.ID0020);
    }

    [Theory]
    [InlineData("abc12")]
    [InlineData("1234567890")]
    [InlineData("12 34")]
    public async Task CreateAsync_LegacyAuthenticationWithUnsupportedPassword_ThrowsAuthenticationInvalid(string password)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*#603356072##");
        }, password);

        AssertError(exception, OpenNettyErrorCode.AuthenticationInvalid, SR.ID0097);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task CreateAsync_DigestAuthentication_ExchangesValidDigests(string method)
    {
        await using var harness = await SessionHarness.OpenAsync(
            OpenNettyProtocol.Scs, OpenNettySessionType.Command, password: "secret", script: peer => ExchangeDigestsAsync(
                peer, method, "secret", async (peer, digest) =>
                {
                    await peer.SendAsync($"*#{digest}##");
                    await peer.ExpectAsync(Ack);
                }));

        Assert.Equal(OpenNettySessionType.Command, harness.Session.Type);
    }

    [Fact]
    public async Task CreateAsync_DigestAuthenticationWithInvalidServerDigest_ThrowsInvalidFrame()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, peer => ExchangeDigestsAsync(
            peer, "1", "secret", (peer, digest) => peer.SendAsync($"*#{ToDigits(SHA1.HashData([1, 2, 3]))}##")), password: "secret");

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0023);
    }

    [Fact]
    public async Task CreateAsync_DigestAuthenticationRejectedByGateway_ThrowsAuthenticationInvalid()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, peer => ExchangeDigestsAsync(
            peer, "2", "secret", (peer, digest) => peer.SendAsync(Nack)), password: "secret");

        AssertError(exception, OpenNettyErrorCode.AuthenticationInvalid, SR.ID0022);
    }

    [Fact]
    public async Task CreateAsync_DigestAuthenticationConnectionClosedBeforeServerDigest_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, peer => ExchangeDigestsAsync(
            peer, "1", "secret", (peer, digest) => Task.Run(peer.Disconnect, TestContext.Current.CancellationToken)), password: "secret");

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Fact]
    public async Task CreateAsync_DigestAuthenticationWithUnsupportedMethod_ThrowsAuthenticationMethodUnsupported()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*98*3##");
        }, password: "secret");

        AssertError(exception, OpenNettyErrorCode.AuthenticationMethodUnsupported, SR.ID0019);
    }

    [Fact]
    public async Task CreateAsync_DigestAuthenticationWithoutPassword_ThrowsAuthenticationRequired()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*98*1##");
        });

        AssertError(exception, OpenNettyErrorCode.AuthenticationRequired, SR.ID0020);
    }

    [Fact]
    public async Task CreateAsync_DigestAuthenticationConnectionClosedBeforeNonce_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*98*1##");
            await peer.ExpectAsync(Ack);
            peer.Disconnect();
        }, password: "secret");

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Theory]
    [InlineData("*#*1##")]
    [InlineData("*#0102##")]
    public async Task CreateAsync_DigestAuthenticationWithInvalidNonce_ThrowsInvalidFrame(string nonce)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*98*1##");
            await peer.ExpectAsync(Ack);
            await peer.SendAsync(nonce);
        }, password: "secret");

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0021);
    }

    [Fact]
    public async Task CreateAsync_DigestAuthenticationWithMalformedNonce_ThrowsInvalidFrame()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command, async peer =>
        {
            await peer.SendAsync(Ack);
            await peer.ExpectAsync("*99*9##");
            await peer.SendAsync("*98*1##");
            await peer.ExpectAsync(Ack);
            await peer.SendAsync("*#12345##");
        }, password: "secret");

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0021);
    }

    [Fact]
    public async Task CreateAsync_NitooGenericSession_RequestsFirmwareVersion()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic);

        Assert.Equal(OpenNettySessionType.Generic, harness.Session.Type);
        Assert.Equal(OpenNettyProtocol.Nitoo, harness.Session.Protocol);
    }

    [Fact]
    public async Task CreateAsync_NitooGenericSession_DiscardsUnrelatedFramesBeforeFirmwareVersion()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic, script: async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            await peer.SendAsync("*1*1*1234##");
            await peer.SendAsync(Ack);
            await peer.SendAsync("*#13**16*1*2*3##");
        });

        Assert.Equal(OpenNettySessionType.Generic, harness.Session.Type);
    }

    [Fact]
    public async Task CreateAsync_NitooGenericSessionClosedBeforeFirmwareVersion_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            peer.Disconnect();
        });

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Fact]
    public async Task CreateAsync_NitooGenericSessionWithoutFirmwareVersion_ThrowsInvalidFrame()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Nitoo, OpenNettySessionType.Generic,
            peer => peer.ExpectAsync("*#13**16##"), configure: WithNegotiationTimeout(NegotiationTimeout));

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0101);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Theory]
    [InlineData(true, "*13*66*##")]
    [InlineData(false, "*13*67*##")]
    public async Task CreateAsync_ZigbeeGenericSession_ConfiguresSupervisionMode(bool enabled, string request)
    {
        await using var harness = await SessionHarness.OpenAsync(
            OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic, configure: options => options with
            {
                DefaultSessionOptions = options.DefaultSessionOptions with { EnableSupervisionMode = enabled }
            },
            script: async peer =>
            {
                await peer.ExpectAsync(request);
                await peer.SendAsync(Ack);
            });

        Assert.Equal(OpenNettyProtocol.Zigbee, harness.Session.Protocol);
    }

    [Fact]
    public async Task CreateAsync_ZigbeeGenericSession_DiscardsUnrelatedFramesBeforeAcknowledgement()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic, script: async peer =>
        {
            await peer.ExpectAsync("*13*66*##");
            await peer.SendAsync("*1*1*1234501#9##");
            await peer.SendAsync(Ack);
        });

        Assert.Equal(OpenNettySessionType.Generic, harness.Session.Type);
    }

    [Theory]
    [InlineData(Nack)]
    [InlineData(BusyNack)]
    public async Task CreateAsync_ZigbeeSupervisionModeRejected_ThrowsInvalidFrame(string reply)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*13*66*##");
            await peer.SendAsync(reply);
        });

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0096);
    }

    [Fact]
    public async Task CreateAsync_ZigbeeGenericSessionClosedBeforeAcknowledgement_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*13*66*##");
            peer.Disconnect();
        });

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Fact]
    public async Task CreateAsync_ZigbeeGenericSessionWithoutAcknowledgement_ThrowsInvalidFrame()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Zigbee, OpenNettySessionType.Generic,
            peer => peer.ExpectAsync("*13*66*##"), configure: WithNegotiationTimeout(NegotiationTimeout));

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0096);
        Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerException);
    }

    [Fact]
    public async Task CreateAsync_ScsGenericSessionAcknowledgedBeforeFirmwareVersion_CreatesSession()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Generic, script: async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            await peer.SendAsync(Ack);
            await peer.SendAsync("*#13**16*1*2*3##");
        });

        Assert.Equal(OpenNettySessionType.Generic, harness.Session.Type);
    }

    [Fact]
    public async Task CreateAsync_ScsGenericSessionAcknowledgedAfterFirmwareVersion_CreatesSession()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Generic, script: async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            await peer.SendAsync("*#13**16*1*2*3##");
            await peer.SendAsync(Ack);
        });

        Assert.Equal(OpenNettySessionType.Generic, harness.Session.Type);
    }

    [Theory]
    [InlineData(Nack)]
    [InlineData(BusyNack)]
    public async Task CreateAsync_ScsGenericSessionRejected_ThrowsInvalidFrame(string reply)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            await peer.SendAsync(reply);
        });

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0101);
    }

    [Theory]
    [InlineData(Nack)]
    [InlineData(BusyNack)]
    public async Task CreateAsync_ScsGenericSessionRejectedAfterFirmwareVersion_ThrowsInvalidFrame(string reply)
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            await peer.SendAsync("*#13**16*1*2*3##");
            await peer.SendAsync(reply);
        });

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0101);
    }

    [Fact]
    public async Task CreateAsync_ScsGenericSessionClosedImmediately_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            peer.Disconnect();
        });

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Fact]
    public async Task CreateAsync_ScsGenericSessionClosedAfterAcknowledgement_ThrowsConnectionClosed()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            await peer.SendAsync(Ack);
            peer.Disconnect();
        });

        AssertError(exception, OpenNettyErrorCode.ConnectionClosed, SR.ID0100);
    }

    [Fact]
    public async Task CreateAsync_ScsGenericSessionWithoutFirmwareVersion_ThrowsInvalidFrame()
    {
        var exception = await FailToNegotiateAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Generic, async peer =>
        {
            await peer.ExpectAsync("*#13**16##");
            await peer.SendAsync(Ack);
        }, configure: WithNegotiationTimeout(NegotiationTimeout));

        AssertError(exception, OpenNettyErrorCode.InvalidFrame, SR.ID0101);
    }

    [Fact]
    public async Task SubscribeAsync_ConnectedSession_DeliversIncomingMessagesInOrder()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        var observer = new RecordingObserver();
        await using var subscription = await harness.Session.SubscribeAsync(observer);
        await harness.ConnectAsync();

        await harness.Peer.SendAsync("*1*1*21##");
        await harness.Peer.SendAsync("*1*0*22##");

        Assert.Equal("*1*1*21##", (await observer.NextAsync()).ToString());
        Assert.Equal("*1*0*22##", (await observer.NextAsync()).ToString());
    }

    [Fact]
    public async Task SubscribeAsync_MultipleObservers_EachObserverReceivesMessages()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        var first = new RecordingObserver();
        var second = new RecordingObserver();
        await using var firstSubscription = await harness.Session.SubscribeAsync(first);
        await using var secondSubscription = await harness.Session.SubscribeAsync(second);
        await harness.ConnectAsync();

        await harness.Peer.SendAsync("*1*1*21##");

        Assert.Equal("*1*1*21##", (await first.NextAsync()).ToString());
        Assert.Equal("*1*1*21##", (await second.NextAsync()).ToString());
    }

    [Fact]
    public async Task SubscribeAsync_FrameThatCannotBeConverted_IsIgnored()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        var observer = new RecordingObserver();
        await using var subscription = await harness.Session.SubscribeAsync(observer);
        await harness.ConnectAsync();

        await harness.Peer.SendAsync(BusyNack);
        await harness.Peer.SendAsync("*1*1*21##");

        Assert.Equal("*1*1*21##", (await observer.NextAsync()).ToString());
    }

    [Fact]
    public async Task SubscribeAsync_GatewayClosesConnection_CompletesObserver()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Event);
        var observer = new RecordingObserver();
        await using var subscription = await harness.Session.SubscribeAsync(observer);
        await harness.ConnectAsync();

        harness.Peer.Disconnect();

        await observer.Completed.WaitAsync(LongTimeout, TimeProvider.System, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DisposeAsync_ClosesConnectionWithGateway()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);

        await harness.Session.DisposeAsync();

        await harness.Peer.ExpectClosedAsync();
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await harness.Session.DisposeAsync();

        var exception = await Record.ExceptionAsync(async () => await harness.Session.DisposeAsync());

        Assert.Null(exception);
    }

    [Fact]
    public async Task Equals_SameInstance_ReturnsTrue()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        var session = harness.Session;
        var same = harness.Session;

        Assert.True(session.Equals(same));
        Assert.True(session.Equals((object) same));
        Assert.True(session == same);
        Assert.False(session != same);
        Assert.Equal(session.GetHashCode(), same.GetHashCode());
    }

    [Fact]
    public async Task Equals_DifferentSessions_ReturnsFalse()
    {
        await using var first = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);
        await using var second = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);

        Assert.False(first.Session.Equals(second.Session));
        Assert.False(first.Session.Equals((object) second.Session));
        Assert.False(first.Session == second.Session);
        Assert.True(first.Session != second.Session);
        Assert.NotEqual(first.Session.Id, second.Session.Id);
    }

    [Fact]
    public async Task Equals_Null_ReturnsFalse()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);

        Assert.False(harness.Session.Equals((OpenNettySession?) null));
        Assert.False(harness.Session.Equals((object?) null));
        Assert.False(harness.Session == null);
        Assert.False(null == harness.Session);
        Assert.True(harness.Session != null);
    }

    [Fact]
    public async Task Equals_ObjectOfAnotherType_ReturnsFalse()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);

        Assert.False(harness.Session.Equals(harness.Session.Id));
    }

    [Fact]
    public async Task ToString_ReturnsSessionIdentifier()
    {
        await using var harness = await SessionHarness.OpenAsync(OpenNettyProtocol.Scs, OpenNettySessionType.Command);

        Assert.Equal(harness.Session.Id.ToString(), harness.Session.ToString());
        Assert.Equal(harness.Session.Id.GetHashCode(), harness.Session.GetHashCode());
    }

    [Fact]
    public void EqualityOperators_BothNull_AreEqual()
    {
        OpenNettySession? left = null;
        OpenNettySession? right = null;

        Assert.True(left == right);
        Assert.False(left != right);
    }

    private static OpenNettyCapability GetCapability(OpenNettySessionType type) => type switch
    {
        OpenNettySessionType.Command => OpenNettyCapabilities.OpenWebNetCommandSession,
        OpenNettySessionType.Event   => OpenNettyCapabilities.OpenWebNetEventSession,
        _                            => OpenNettyCapabilities.OpenWebNetGenericSession
    };

    private static OpenNettyMessage ScsLightOn() => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Scs, "*1*1*21##");

    private static OpenNettyMessage NitooLightOn() => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Nitoo, "*1*1*1234##");

    private static OpenNettyMessage ZigbeeLightOn() => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, "*1*1*00#9##");

    private static OpenNettyMessage ZigbeeUnicastLightOn() => OpenNettyMessage.CreateFromFrame(OpenNettyProtocol.Zigbee, "*1*1*1234501#9##");

    private static Func<OpenNettyGatewayOptions, OpenNettyGatewayOptions> WithNegotiationTimeout(TimeSpan timeout)
        => options => options with { DefaultSessionOptions = options.DefaultSessionOptions with { ConnectionNegotiationTimeout = timeout } };

    private static void AssertError(OpenNettyException exception, OpenNettyErrorCode code, string resource)
    {
        Assert.Equal(code, exception.ErrorCode);
        Assert.Equal(SR.GetResourceString(resource), exception.Message);
    }

    private static async Task<OpenNettyException> FailToNegotiateAsync(
        OpenNettyProtocol protocol, OpenNettySessionType type, Func<OpenNettyLoopbackGateway.Peer, Task> script,
        string? password = null, Func<OpenNettyGatewayOptions, OpenNettyGatewayOptions>? configure = null,
        Func<OpenNettyLoopbackGateway.Peer, Task>? afterFailure = null)
    {
        await using var server = new OpenNettyLoopbackGateway();
        var gateway = server.CreateGateway(OpenNettyLoopbackGateway.CreateDevice(protocol, AllSessionCapabilities), password, configure);
        var creating = OpenNettySession.CreateAsync(gateway, type, cancellationToken: TestContext.Current.CancellationToken).AsTask();

        var peer = await server.AcceptAsync();
        await script(peer);

        var exception = await Assert.ThrowsAsync<OpenNettyException>(async () => await creating);

        if (afterFailure is not null)
        {
            await afterFailure(peer);
        }

        return exception;
    }

    private static async Task ExchangeDigestsAsync(
        OpenNettyLoopbackGateway.Peer peer, string method, string password, Func<OpenNettyLoopbackGateway.Peer, string, Task> reply)
    {
        var hash = method is "1" ? (Func<byte[], byte[]>) SHA1.HashData : SHA256.HashData;
        var serverNonce = string.Concat(Enumerable.Repeat("0123456789abcdef", 4))[..(method is "1" ? 40 : 64)];
        var hashedPassword = Convert.ToHexStringLower(hash(Encoding.UTF8.GetBytes(password)));

        await peer.SendAsync(Ack);
        await peer.ExpectAsync("*99*9##");
        await peer.SendAsync($"*98*{method}##");
        await peer.ExpectAsync(Ack);
        await peer.SendAsync($"*#{ToDigits(Convert.FromHexString(serverNonce))}##");

        var response = await peer.ReceiveAsync() ?? throw new InvalidOperationException();
        var clientNonce = FromDigits(response.Fields[0].Parameters[1].Value);

        Assert.Equal(
            ToDigits(hash(Encoding.UTF8.GetBytes($"{serverNonce}{clientNonce}736F70653E636F70653E{hashedPassword}"))),
            response.Fields[1].Parameters[0].Value);

        await reply(peer, ToDigits(hash(Encoding.UTF8.GetBytes($"{serverNonce}{clientNonce}{hashedPassword}"))));
    }

    private static string ToDigits(byte[] value)
        => string.Concat(Convert.ToHexString(value).Select(static digit => Convert.ToInt32(digit.ToString(), 16).ToString("00", CultureInfo.InvariantCulture)));

    private static string FromDigits(string value)
        => string.Concat(Enumerable.Range(0, value.Length / 2).Select(index => int.Parse(value.AsSpan(index * 2, 2), CultureInfo.InvariantCulture).ToString("x", CultureInfo.InvariantCulture)));

    private sealed class SessionHarness : IAsyncDisposable
    {
        private readonly OpenNettyLoopbackGateway _server;
        private IAsyncDisposable? _connection;

        private SessionHarness(
            OpenNettyLoopbackGateway server, OpenNettyGateway gateway, OpenNettyLoopbackGateway.Peer peer, OpenNettySession session)
            => (_server, Gateway, Peer, Session) = (server, gateway, peer, session);

        public OpenNettyGateway Gateway { get; }

        public OpenNettyLoopbackGateway.Peer Peer { get; }

        public OpenNettySession Session { get; }

        public static async Task<SessionHarness> OpenAsync(
            OpenNettyProtocol protocol, OpenNettySessionType type,
            Func<OpenNettyGatewayOptions, OpenNettyGatewayOptions>? configure = null,
            string? password = null, Func<OpenNettyLoopbackGateway.Peer, Task>? script = null)
        {
            var server = new OpenNettyLoopbackGateway();
            var gateway = server.CreateGateway(OpenNettyLoopbackGateway.CreateDevice(protocol, AllSessionCapabilities), password, configure);
            var creating = OpenNettySession.CreateAsync(gateway, type, cancellationToken: TestContext.Current.CancellationToken).AsTask();

            var peer = await server.AcceptAsync();
            await (script ?? (peer => peer.NegotiateSessionAsync(protocol, type)))(peer);

            return new SessionHarness(server, gateway, peer, await creating);
        }

        public async Task ConnectAsync() => _connection = await Session.ConnectAsync();

        public OpenNettyTransmissionOptions Options() => Gateway.Options.DefaultTransmissionOptions with
        {
            AcknowledgementTimeout = LongTimeout,
            ActionValidationTimeout = LongTimeout,
            PostSendingDelay = TimeSpan.Zero,
            StatusReportTimeout = LongTimeout
        };

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            await Session.DisposeAsync();
            await _server.DisposeAsync();
        }
    }

    private sealed class ImmediateTimeProvider : TimeProvider
    {
        private readonly List<TimeSpan> _delays = [];

        public IReadOnlyList<TimeSpan> Delays
        {
            get
            {
                lock (_delays)
                {
                    return [.. _delays];
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_delays)
            {
                _delays.Add(dueTime);
            }

            _ = Task.Run(() => callback(state));

            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingObserver : IAsyncObserver<OpenNettyMessage>
    {
        private readonly Channel<OpenNettyMessage> _messages = Channel.CreateUnbounded<OpenNettyMessage>();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Completed => _completed.Task;

        public async Task<OpenNettyMessage> NextAsync()
            => await _messages.Reader.ReadAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(LongTimeout, TimeProvider.System, TestContext.Current.CancellationToken);

        public ValueTask OnNextAsync(OpenNettyMessage value)
        {
            _messages.Writer.TryWrite(value);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnErrorAsync(Exception error)
        {
            _completed.TrySetException(error);
            return ValueTask.CompletedTask;
        }

        public ValueTask OnCompletedAsync()
        {
            _completed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
