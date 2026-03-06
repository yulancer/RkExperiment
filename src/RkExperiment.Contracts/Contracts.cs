namespace RkExperiment.Contracts;

public interface IExperimentEvent
{
    Guid EventId { get; }
    string SourceService { get; }
    int Batch { get; }
    DateTimeOffset CreatedAt { get; }
}

public record UserRegisteredEvent(
    Guid EventId,
    string SourceService,
    int Batch,
    DateTimeOffset CreatedAt,
    string UserId,
    string Email) : IExperimentEvent;

public record OrderSubmittedEvent(
    Guid EventId,
    string SourceService,
    int Batch,
    DateTimeOffset CreatedAt,
    string OrderId,
    decimal Amount) : IExperimentEvent;

public record PaymentCapturedEvent(
    Guid EventId,
    string SourceService,
    int Batch,
    DateTimeOffset CreatedAt,
    string PaymentId,
    decimal Amount,
    string Currency) : IExperimentEvent;
