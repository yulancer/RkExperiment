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
        var inputQueue = context.Configuration["Kafka:InputQueue"] ?? "rk.consumer-b.input";
        var groupId = context.Configuration["Kafka:GroupId"] ?? "rk-consumer-b";

        services.AutoRegisterHandlersFromAssemblyOf<OrderSubmittedEventHandler>();
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

        var handledTypes = GetHandledMessageTypes(typeof(OrderSubmittedEventHandler).Assembly)
            .Where(t => t == typeof(OrderSubmittedEvent) || t == typeof(PaymentCapturedEvent))
            .OrderBy(t => t.Name)
            .ToArray();

        foreach (var messageType in handledTypes)
        {
            await SubscribeAsync(messageType);
            _logger.LogInformation("Consumer B subscribed to {MessageType}", messageType.Name);
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

internal sealed class OrderSubmittedEventHandler : IHandleMessages<OrderSubmittedEvent>
{
    private readonly ILogger<OrderSubmittedEventHandler> _logger;
    private readonly IConfiguration _configuration;

    public OrderSubmittedEventHandler(ILogger<OrderSubmittedEventHandler> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Handle(OrderSubmittedEvent message)
    {
        var instance = _configuration["Service:InstanceName"] ?? "consumer-b";
        _logger.LogInformation(
            "B handled {MessageType} batch={Batch}, eventId={EventId}, orderId={OrderId}, amount={Amount}, instance={Instance}",
            nameof(OrderSubmittedEvent), message.Batch, message.EventId, message.OrderId, message.Amount, instance);

        await DelayIfConfigured();
    }

    private Task DelayIfConfigured()
    {
        var processingDelay = int.TryParse(_configuration["Service:ProcessingDelayMs"], out var parsed) ? parsed : 0;
        return processingDelay > 0 ? Task.Delay(processingDelay) : Task.CompletedTask;
    }
}

internal sealed class PaymentCapturedEventHandler : IHandleMessages<PaymentCapturedEvent>
{
    private readonly ILogger<PaymentCapturedEventHandler> _logger;
    private readonly IConfiguration _configuration;

    public PaymentCapturedEventHandler(ILogger<PaymentCapturedEventHandler> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task Handle(PaymentCapturedEvent message)
    {
        var instance = _configuration["Service:InstanceName"] ?? "consumer-b";
        _logger.LogInformation(
            "B handled {MessageType} batch={Batch}, eventId={EventId}, paymentId={PaymentId}, amount={Amount}, currency={Currency}, instance={Instance}",
            nameof(PaymentCapturedEvent), message.Batch, message.EventId, message.PaymentId, message.Amount, message.Currency, instance);

        var processingDelay = int.TryParse(_configuration["Service:ProcessingDelayMs"], out var parsed) ? parsed : 0;
        if (processingDelay > 0)
        {
            await Task.Delay(processingDelay);
        }
    }
}
