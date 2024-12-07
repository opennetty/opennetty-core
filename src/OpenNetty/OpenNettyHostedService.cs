using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace OpenNetty;

/// <summary>
/// Contains the logic necessary to connect the event pipeline when the application is starting up.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class OpenNettyHostedService : BackgroundService
{
    private readonly OpenNettyEvents _events;
    private readonly IEnumerable<IOpenNettyHandler> _handlers;
    private readonly OpenNettyLogger<OpenNettyHostedService> _logger;
    private readonly IOptionsMonitor<OpenNettyOptions> _options;
    private readonly IOpenNettyPipeline _pipeline;
    private readonly IOpenNettyWorker _worker;

    /// <summary>
    /// Creates a new instance of the <see cref="OpenNettyHostedService"/> class.
    /// </summary>
    /// <param name="events">The OpenNetty events.</param>
    /// <param name="handlers">The OpenNetty handlers.</param>
    /// <param name="logger">The OpenNetty logger.</param>
    /// <param name="options">The OpenNetty options.</param>
    /// <param name="pipeline">The OpenNetty pipeline.</param>
    /// <param name="worker">The OpenNetty worker.</param>
    public OpenNettyHostedService(
        OpenNettyEvents events,
        IEnumerable<IOpenNettyHandler> handlers,
        OpenNettyLogger<OpenNettyHostedService> logger,
        IOptionsMonitor<OpenNettyOptions> options,
        IOpenNettyPipeline pipeline,
        IOpenNettyWorker worker)
    {
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _worker = worker ?? throw new ArgumentNullException(nameof(worker));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.HostedServiceStarting();

        await using var subscriptions = new CompositeAsyncDisposable();

        List<Task> tasks = [];

        try
        {
            // Always invoke the handlers registered in the dependency injection container before notifications
            // start being processed to avoid race conditions and ensure no notification will be missed.
            foreach (var handler in _handlers)
            {
                await subscriptions.AddAsync(await handler.SubscribeAsync());
            }

            foreach (var gateway in _options.CurrentValue.Gateways)
            {
                // Create the unbounded channel that will be used to push notifications to the worker.
                var output = Channel.CreateUnbounded<OpenNettyNotification>(new UnboundedChannelOptions
                {
                    AllowSynchronousContinuations = false,
                    SingleReader = false,
                    SingleWriter = true
                });

                // Create the unbounded channel that will be used to receive notifications from the worker.
                var input = Channel.CreateUnbounded<OpenNettyNotification>(new UnboundedChannelOptions
                {
                    AllowSynchronousContinuations = false,
                    SingleReader = true,
                    SingleWriter = false
                });

                // Monitor all the notifications that should be handled by the worker and copy them to the output channel.
                await subscriptions.AddAsync(await _pipeline
                    .Where(notification => notification.Gateway == gateway)
                    .Do(notification => output.Writer.WriteAsync(notification))
                    .Retry()
                    .SubscribeAsync(static notification => ValueTask.CompletedTask));

                // Monitor all the notifications pushed by the worker and dispatch them using the events pipeline.
                tasks.Add(Task.Run(async () =>
                {
                    while (await input.Reader.WaitToReadAsync(stoppingToken))
                    {
                        while (input.Reader.TryRead(out OpenNettyNotification? notification))
                        {
                            await _pipeline.PublishAsync(notification, stoppingToken);
                        }
                    }
                }, stoppingToken));

                // Ask the worker to process incoming and outgoing notifications for this gateway.
                tasks.Add(_worker.ProcessNotificationsAsync(gateway, output.Reader, input.Writer, stoppingToken));
            }

            // Connect the observable instances to allow observers to start processing notifications.
            await subscriptions.AddAsync(await _events.ConnectAsync());
            await subscriptions.AddAsync(await _pipeline.ConnectAsync());

            _logger.HostedServiceStarted();

            await Task.WhenAll(tasks);
        }

        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.HostedServiceStopped();
        }

        catch (Exception exception)
        {
            _logger.HostedServiceFailed(exception);

            throw;
        }
    }
}
