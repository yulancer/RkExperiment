using Confluent.Kafka;
using Confluent.Kafka.Admin;

var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
var topicNames = (Environment.GetEnvironmentVariable("TOPIC_NAMES")
                  ?? Environment.GetEnvironmentVariable("TOPIC_NAME")
                  ?? "---Topic---.RkExperiment.Contracts.OrderSubmittedEvent_RkExperiment.Contracts")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var partitions = int.TryParse(Environment.GetEnvironmentVariable("TOPIC_PARTITIONS"), out var parsedPartitions) ? parsedPartitions : 6;
var replicationFactor = short.TryParse(Environment.GetEnvironmentVariable("TOPIC_REPLICATION_FACTOR"), out var parsedReplication) ? parsedReplication : (short)1;

Console.WriteLine($"Bootstrap servers : {bootstrapServers}");
Console.WriteLine($"Topics            : {string.Join(", ", topicNames)}");
Console.WriteLine($"Partitions        : {partitions}");
Console.WriteLine($"ReplicationFactor : {replicationFactor}");

using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
try
{
    await admin.CreateTopicsAsync(topicNames
        .Select(topicName => new TopicSpecification
        {
            Name = topicName,
            NumPartitions = partitions,
            ReplicationFactor = replicationFactor
        })
        .ToArray());

    Console.WriteLine("Topic creation request sent.");
}
catch (CreateTopicsException e)
{
    foreach (var result in e.Results)
    {
        Console.WriteLine($"An error occured creating topic {result.Topic}: {result.Error.Reason}");
    }
}
