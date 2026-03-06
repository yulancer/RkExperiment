using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Kafka;
using Rebus.ServiceProvider;
using RkExperiment.Contracts;
using System.Reflection;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var bootstrapServers = context.Configuration["Kafka:BootstrapServers"] ?? "localhost:9092";
        var inputQueue = context.Configuration["Kafka:InputQueue"] ?? "rk.consumer-a.input";
        var groupId = context.Configuration["Kafka:GroupId"] ?? "rk-consumer-a";

        services.AutoRegisterHandlersFromAssemblyOf<UserRegisteredEventHandler>();
        services.AddRebus((configure, provider) => configure
            .Logging(l => l.MicrosoftExtensionsLogging(provider.GetRequiredService<ILoggerFactory>()))
            .Transport(t => t.UseKafka(bootstrapServers, inputQueue, groupId))
            .Options(o =>
            {
                o.SetNumberOfWorkers(1);
                o.SetMaxParallelism(1);
            }));

        services.AddHostedService<AutoSubscriptionStarter>();
    });

await builder.RunConsoleAsync();

internal sealed class AutoSubscriptionStarter : BackgroundService
{
    private readonly Rebus.Bus.IBus _bus;
    private readonly ILogger<AutoSubscriptionStarter> _logger;

    public AutoSubscriptionStarter(Rebus.Bus.IBus bus, ILogger<AutoSubscriptionStarter> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1000, stoppingToken);

        var handledTypes = GetHandledMessageTypes(typeof(UserRegisteredEventHandler).Assembly)
            .Where(t => t == typeof(UserRegisteredEvent) || t == typeof(OrderSubmittedEvent))
            .OrderBy(t => t.Name)
            .ToArray();

        foreach (var messageType in handledTypes)
        {
            await SubscribeAsync(messageType);
            _logger.LogInformation("Consumer A subscribed to {MessageType}", messageType.Name);
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private Task SubscribeAsync(Type messageType)
    {
        var method = typeof(Rebus.Bus.IBus)
            .GetMethods()
            .Single(m => m.Name == nameof(Rebus.Bus.IBus.Subscribe) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

        return (Task)method.MakeGenericMethod(messageType).Invoke(_bus, Array.Empty<object>())!;
    }

    private static IReadOnlyCollection<Type> GetHandledMessageTypes(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces())
            .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHandleMessages<>))
            .Select(i => i.GetGenericArguments()[0])
            .Distinct()
            .ToArray();
}

internal sealed class UserRegisteredEventHandler : IHandleMessages<UserRegisteredEvent>
{
    private readonly ILogger<UserRegisteredEventHandler> _logger;
    private readonly IConfiguration _configuration;

    public UserRegisteredEventHandler(ILogger<UserRegisteredEventHandler> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task Handle(UserRegisteredEvent message)
    {
        var instance = _configuration["Service:InstanceName"] ?? "consumer-a-1";
        _logger.LogInformation(
            "A handled {MessageType} batch={Batch}, eventId={EventId}, userId={UserId}, instance={Instance}",
            nameof(UserRegisteredEvent), message.Batch, message.EventId, message.UserId, instance);
        return Task.CompletedTask;
    }
}

internal sealed class OrderSubmittedEventHandler : IHandleMessages<OrderSubmittedEvent>
{
    private readonly ILogger<OrderSubmittedEventHandler> _logger;
    private readonly IConfiguration _configuration;

    public OrderSubmittedEventHandler(ILogger<OrderSubmittedEventHandler> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task Handle(OrderSubmittedEvent message)
    {
        var instance = _configuration["Service:InstanceName"] ?? "consumer-a-1";
        _logger.LogInformation(
            "A handled {MessageType} batch={Batch}, eventId={EventId}, orderId={OrderId}, amount={Amount}, instance={Instance}",
            nameof(OrderSubmittedEvent), message.Batch, message.EventId, message.OrderId, message.Amount, instance);
        return Task.CompletedTask;
    }
}
