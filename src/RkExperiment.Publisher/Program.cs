using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Kafka;
using Rebus.ServiceProvider;
using RkExperiment.Contracts;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var bootstrapServers = context.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";

        services.AddRebus((configure, provider) => configure
            .Logging(l => l.MicrosoftExtensionsLogging(provider.GetRequiredService<ILoggerFactory>()))
            .Transport(t => t.UseKafkaAsOneWayClient(bootstrapServers)));

        services.AddHostedService<PublisherWorker>();
    });

await builder.RunConsoleAsync();

internal sealed class PublisherWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PublisherWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IConfiguration _configuration;

    public PublisherWorker(
        IServiceProvider serviceProvider,
        ILogger<PublisherWorker> logger,
        IHostApplicationLifetime lifetime,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _lifetime = lifetime;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var count = int.TryParse(_configuration["Publisher:Count"], out var parsedCount) ? parsedCount : 5;
        var delayMs = int.TryParse(_configuration["Publisher:DelayMs"], out var parsedDelay) ? parsedDelay : 500;
        var sourceService = _configuration["Publisher:SourceService"] ?? "publisher";

        await Task.Delay(1500, stoppingToken);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Rebus.Bus.IBus>();

        for (var batch = 1; batch <= count && !stoppingToken.IsCancellationRequested; batch++)
        {
            var userRegistered = new UserRegisteredEvent(
                EventId: Guid.NewGuid(),
                SourceService: sourceService,
                Batch: batch,
                CreatedAt: DateTimeOffset.UtcNow,
                UserId: $"user-{batch:000}",
                Email: $"user{batch:000}@example.test");

            await bus.Publish(userRegistered);
            _logger.LogInformation("Published {MessageType} batch={Batch}, eventId={EventId}", nameof(UserRegisteredEvent), batch, userRegistered.EventId);
            await DelayAsync(delayMs, stoppingToken);

            var orderSubmitted = new OrderSubmittedEvent(
                EventId: Guid.NewGuid(),
                SourceService: sourceService,
                Batch: batch,
                CreatedAt: DateTimeOffset.UtcNow,
                OrderId: $"order-{batch:000}",
                Amount: 1000m + batch);

            await bus.Publish(orderSubmitted);
            _logger.LogInformation("Published {MessageType} batch={Batch}, eventId={EventId}", nameof(OrderSubmittedEvent), batch, orderSubmitted.EventId);
            await DelayAsync(delayMs, stoppingToken);

            var paymentCaptured = new PaymentCapturedEvent(
                EventId: Guid.NewGuid(),
                SourceService: sourceService,
                Batch: batch,
                CreatedAt: DateTimeOffset.UtcNow,
                PaymentId: $"payment-{batch:000}",
                Amount: 1000m + batch,
                Currency: "RUB");

            await bus.Publish(paymentCaptured);
            _logger.LogInformation("Published {MessageType} batch={Batch}, eventId={EventId}", nameof(PaymentCapturedEvent), batch, paymentCaptured.EventId);
            await DelayAsync(delayMs, stoppingToken);
        }

        _logger.LogInformation("Publisher finished. Stopping host.");
        _lifetime.StopApplication();
    }

    private static Task DelayAsync(int delayMs, CancellationToken cancellationToken) =>
        delayMs > 0 ? Task.Delay(delayMs, cancellationToken) : Task.CompletedTask;
}
