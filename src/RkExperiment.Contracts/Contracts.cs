namespace RkExperiment.Contracts;

public record DemoEvent(
    Guid EventId,
    string SourceService,
    int Sequence,
    DateTimeOffset CreatedAt,
    string Payload);
