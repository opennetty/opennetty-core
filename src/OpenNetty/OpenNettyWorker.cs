using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Polly;

namespace OpenNetty;

/// <summary>
/// Represents a worker responsible for processing incoming and outgoing notifications.
/// </summary>
public class OpenNettyWorker : IOpenNettyWorker
{
    private readonly OpenNettyLogger<OpenNettyWorker> _logger;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyWorker"/> class.
    /// </summary>
    /// <param name="logger">The OpenNetty logger.</param>
    public OpenNettyWorker(OpenNettyLogger<OpenNettyWorker> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public Task ProcessNotificationsAsync(
        OpenNettyGateway gateway,
        ChannelReader<OpenNettyNotification> reader,
        ChannelWriter<OpenNettyNotification> writer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);

        _logger.WorkerStarting(gateway);

        List<Task> tasks = [];

        if (gateway.Device.Definition.Capabilities.Contains(OpenNettyCapabilities.OpenWebNetGenericSession))
        {
            tasks.Add(CreateSharedSessionWorkerAsync(gateway, OpenNettySessionType.Generic, cancellationToken));
        }

        if (gateway.Device.Definition.Capabilities.Contains(OpenNettyCapabilities.OpenWebNetEventSession))
        {
            tasks.Add(CreateSharedSessionWorkerAsync(gateway, OpenNettySessionType.Event, cancellationToken));
        }

        if (gateway.Device.Definition.Capabilities.Contains(OpenNettyCapabilities.OpenWebNetCommandSession))
        {
            for (var index = 0; index < gateway.Options.MaximumConcurrentCommandSessions; index++)
            {
                tasks.Add(CreateAdHocSessionWorkerAsync(gateway, OpenNettySessionType.Command,
                    gateway.Options.CommandSessionMaximumLifetime, cancellationToken));
            }
        }

        _logger.WorkerStarted(gateway);

        return Task.WhenAll(tasks);

        async Task CreateSharedSessionWorkerAsync(OpenNettyGateway gateway, OpenNettySessionType type, CancellationToken cancellationToken)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
            context.Properties.Set(new ResiliencePropertyKey<OpenNettySessionType>(nameof(OpenNettySessionType)), type);

            _logger.TaskRunnerScheduled(gateway, type);

            await gateway.Options.SessionResiliencePipeline.ExecuteAsync(async context =>
            {
                using var source = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                await using var session = await OpenNettySession.CreateAsync(gateway, type, source.Token);

                _logger.SessionOpen(gateway, type, session);

                try
                {
                    await using (await session.SubscribeAsync(
                        async message =>
                        {
                            _logger.MessageReceived(message, gateway, session);

                            await writer.WriteAsync(new OpenNettyNotifications.MessageReceived
                            {
                                Gateway = gateway,
                                Message = message,
                                Session = session
                            });
                        },
                        async exception => await source.CancelAsync(),
                        async () => await source.CancelAsync()))

                    await using (await session.ConnectAsync())
                    {
                        if (type is OpenNettySessionType.Generic)
                        {
                            await foreach (var notification in reader.ReadAllAsync(source.Token))
                            {
                                if (notification is OpenNettyNotifications.MessageReady
                                {
                                    Message    : OpenNettyMessage message,
                                    Options    : OpenNettyTransmissionOptions options,
                                    Transaction: OpenNettyTransaction transaction
                                })
                                {
                                    await SendMessageAsync(gateway, session, message, options, transaction, source.Token);
                                }
                            }
                        }

                        else
                        {
                            await WaitCancellationAsync(source.Token);
                        }
                    }

                    _logger.SessionClosed(session);
                }

                catch (OperationCanceledException) when (source.Token.IsCancellationRequested)
                {
                    _logger.SessionClosed(session);
                }
            }, context);
        }

        async Task CreateAdHocSessionWorkerAsync(
            OpenNettyGateway gateway, OpenNettySessionType type, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var context = ResilienceContextPool.Shared.Get(cancellationToken);
            context.Properties.Set(new ResiliencePropertyKey<OpenNettyGateway>(nameof(OpenNettyGateway)), gateway);
            context.Properties.Set(new ResiliencePropertyKey<OpenNettySessionType>(nameof(OpenNettySessionType)), type);

            _logger.TaskRunnerScheduled(gateway, type);

            await gateway.Options.SessionResiliencePipeline.ExecuteAsync(async context =>
            {
                // Wait until a new notification is ready to be processed.
                while (await reader.WaitToReadAsync(context.CancellationToken))
                {
                    if (!reader.TryRead(out OpenNettyNotification? notification) ||
                        notification is not OpenNettyNotifications.MessageReady)
                    {
                        continue;
                    }

                    using var source = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                    await using var session = await OpenNettySession.CreateAsync(gateway, type, source.Token);

                    _logger.SessionOpen(gateway, type, session);

                    try
                    {
                        await using var subscription = await session.SubscribeAsync(
                            async message =>
                            {
                                _logger.MessageReceived(message, gateway, session);

                                await writer.WriteAsync(new OpenNettyNotifications.MessageReceived
                                {
                                    Gateway = gateway,
                                    Message = message,
                                    Session = session
                                });
                            },
                            async exception => await source.CancelAsync(),
                            async () => await source.CancelAsync());

                        await using var connection = await session.ConnectAsync();
                        var stopwatch = Stopwatch.StartNew();

                        do
                        {
                            // If a message is ready to be sent, send it immediately.
                            if (notification is OpenNettyNotifications.MessageReady
                            {
                                Message    : OpenNettyMessage message,
                                Options    : OpenNettyTransmissionOptions options,
                                Transaction: OpenNettyTransaction transaction
                            })
                            {
                                await SendMessageAsync(gateway, session, message, options, transaction, context.CancellationToken);

                                // Reset the stopwatch after successfully sending a message.
                                stopwatch.Restart();
                            }

                            // Otherwise, wait for a new notification to be published: if no notification is published
                            // within a short period of time, exit the loop to re-evaluate whether the session should be
                            // closed by OpenNetty for inactivity before it is terminated by the OpenWebNet gateway itself.
                            else
                            {
                                await await Task.WhenAny(
                                    WaitCancellationAsync(source.Token),
                                    reader.WaitToReadAsync(context.CancellationToken).AsTask(),
                                    Task.Delay(TimeSpan.FromSeconds(1), context.CancellationToken));
                            }
                        }

                        // If an additional message is ready to be sent, re-use the current session
                        // to send it. Otherwise, stop iterating so that the session can be closed.
                        while (reader.TryRead(out notification) || stopwatch.Elapsed < timeout);

                        _logger.SessionClosed(session);
                    }

                    catch (OperationCanceledException) when (source.Token.IsCancellationRequested)
                    {
                        _logger.SessionClosed(session);
                    }
                }
            }, context);
        }

        async Task SendMessageAsync(
            OpenNettyGateway gateway, OpenNettySession session, OpenNettyMessage message,
            OpenNettyTransmissionOptions options, OpenNettyTransaction transaction, CancellationToken cancellationToken)
        {
            try
            {
                await session.SendAsync(message, options, cancellationToken);
            }

            catch (OpenNettyException exception) when (exception.ErrorCode is OpenNettyErrorCode.GatewayBusy)
            {
                _logger.GatewayBusy(message, gateway, session);

                await writer.WriteAsync(cancellationToken: cancellationToken, item: new OpenNettyNotifications.GatewayBusy
                {
                    Gateway = gateway,
                    Message = message,
                    Session = session,
                    Transaction = transaction
                });

                return;
            }

            catch (OpenNettyException exception) when (exception.ErrorCode is OpenNettyErrorCode.InvalidAction)
            {
                _logger.InvalidAction(message, gateway, session);

                await writer.WriteAsync(cancellationToken: cancellationToken, item: new OpenNettyNotifications.InvalidAction
                {
                    Gateway = gateway,
                    Message = message,
                    Session = session,
                    Transaction = transaction
                });

                return;
            }

            catch (OpenNettyException exception) when (exception.ErrorCode is OpenNettyErrorCode.NoActionReceived)
            {
                _logger.NoActionReceived(message, gateway, session);

                await writer.WriteAsync(cancellationToken: cancellationToken, item: new OpenNettyNotifications.NoActionReceived
                {
                    Gateway = gateway,
                    Message = message,
                    Session = session,
                    Transaction = transaction
                });

                return;
            }

            catch (OpenNettyException exception) when (exception.ErrorCode is OpenNettyErrorCode.NoAcknowledgementReceived)
            {
                _logger.NoAcknowledgementReceived(message, gateway, session);

                await writer.WriteAsync(cancellationToken: cancellationToken, item: new OpenNettyNotifications.NoAcknowledgmentReceived
                {
                    Gateway = gateway,
                    Message = message,
                    Session = session,
                    Transaction = transaction
                });

                // Note: these exceptions may indicate the session is stale and are re-thrown to ensure it is discarded.
                throw;
            }

            catch (OpenNettyException exception) when (exception.ErrorCode is OpenNettyErrorCode.InvalidFrame)
            {
                _logger.InvalidFrame(message, gateway, session);

                await writer.WriteAsync(cancellationToken: cancellationToken, item: new OpenNettyNotifications.InvalidFrame
                {
                    Gateway = gateway,
                    Message = message,
                    Session = session,
                    Transaction = transaction
                });

                return;
            }

            _logger.MessageSent(message, gateway, session);

            await writer.WriteAsync(cancellationToken: cancellationToken, item: new OpenNettyNotifications.MessageSent
            {
                Gateway = gateway,
                Message = message,
                Session = session,
                Transaction = transaction
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static async Task WaitCancellationAsync(CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = cancellationToken.Register(static state => ((TaskCompletionSource) state!).SetResult(), source);
            await source.Task;
        }
    }
}
