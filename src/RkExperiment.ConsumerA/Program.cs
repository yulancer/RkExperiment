using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Kafka;
using Rebus.Kafka.Configs;
using Rebus.ServiceProvider;
using RkExperiment.Contracts;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var bootstrapServers = context.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var inputQueue = context.Configuration["Kafka:InputQueue"] ?? "rk.consumer-a.input";
        var groupId = context.Configuration["Kafka:GroupId"] ?? "rk-consumer-a";
        var autoCreateTopics = !bool.TryParse(context.Configuration["Kafka:AllowAutoCreateTopics"], out var parsed) || parsed;

        var consumerConfig = new ConsumerAndBehaviorConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            AllowAutoCreateTopics = autoCreateTopics,
            BehaviorConfig = new ConsumerBehaviorConfig
            {
                CommitPeriod = 1
            }
        };

        services.AutoRegisterHandlersFromAssemblyOf<DemoEventHandler>();
        services.AddRebus((configure, provider) => configure
            .Logging(l => l.MicrosoftExtensionsLogging(provider.GetRequiredService<ILoggerFactory>()))
            .Transport(t => t.UseKafka(bootstrapServers, inputQueue, groupId))
            .Options(o =>
            {
                o.SetNumberOfWorkers(1);
                o.SetMaxParallelism(1);
            }));

        services.AddHostedService<SubscriptionStarter>();
    });

await builder.RunConsoleAsync();

internal sealed class SubscriptionStarter : BackgroundService
{
    private readonly Rebus.Bus.IBus _bus;
    private readonly ILogger<SubscriptionStarter> _logger;

    public SubscriptionStarter(Rebus.Bus.IBus bus, ILogger<SubscriptionStarter> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken);
        await _bus.Subscribe<DemoEvent>();
        _logger.LogInformation("Consumer A subscribed to {MessageType}", nameof(DemoEvent));
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}

internal sealed class DemoEventHandler : IHandleMessages<DemoEvent>
{
    private readonly ILogger<DemoEventHandler> _logger;
    private readonly IConfiguration _configuration;

    public DemoEventHandler(ILogger<DemoEventHandler> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task Handle(DemoEvent message)
    {
        var instance = _configuration["Service:InstanceName"] ?? "consumer-a-1";
        _logger.LogInformation("A handled event seq={Sequence}, eventId={EventId}, instance={Instance}", message.Sequence, message.EventId, instance);
        return Task.CompletedTask;
    }
}
