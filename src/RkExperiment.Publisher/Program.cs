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
        var count = int.TryParse(_configuration["Publisher:Count"], out var parsedCount) ? parsedCount : 10;
        var delayMs = int.TryParse(_configuration["Publisher:DelayMs"], out var parsedDelay) ? parsedDelay : 500;
        var sourceService = _configuration["Publisher:SourceService"] ?? "publisher";

        await Task.Delay(1500, stoppingToken);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<Rebus.Bus.IBus>();

        for (var i = 1; i <= count && !stoppingToken.IsCancellationRequested; i++)
        {
            var message = new DemoEvent(
                EventId: Guid.NewGuid(),
                SourceService: sourceService,
                Sequence: i,
                CreatedAt: DateTimeOffset.UtcNow,
                Payload: $"demo-payload-{i}");

            await bus.Publish(message);
            _logger.LogInformation("Published event {Sequence} ({EventId})", message.Sequence, message.EventId);

            if (delayMs > 0)
            {
                await Task.Delay(delayMs, stoppingToken);
            }
        }

        _logger.LogInformation("Publisher finished. Stopping host.");
        _lifetime.StopApplication();
    }
}
