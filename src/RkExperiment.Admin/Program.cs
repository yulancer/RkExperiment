using Confluent.Kafka;
using Confluent.Kafka.Admin;

var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "localhost:9092";
var topicName = Environment.GetEnvironmentVariable("TOPIC_NAME") ?? "DemoEvent";
var partitions = int.TryParse(Environment.GetEnvironmentVariable("TOPIC_PARTITIONS"), out var parsedPartitions) ? parsedPartitions : 3;
var replicationFactor = short.TryParse(Environment.GetEnvironmentVariable("TOPIC_REPLICATION_FACTOR"), out var parsedReplication) ? parsedReplication : (short)1;

Console.WriteLine($"Bootstrap servers : {bootstrapServers}");
Console.WriteLine($"Topic             : {topicName}");
Console.WriteLine($"Partitions        : {partitions}");
Console.WriteLine($"ReplicationFactor : {replicationFactor}");

using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrapServers }).Build();
try
{
    await admin.CreateTopicsAsync(new TopicSpecification[]
    {
        new TopicSpecification
        {
            Name = topicName,
            NumPartitions = partitions,
            ReplicationFactor = replicationFactor
        }
    });
    Console.WriteLine("Topic creation request sent.");
}
catch (CreateTopicsException e)
{
    Console.WriteLine($"An error occured creating topic {e.Results[0].Topic}: {e.Results[0].Error.Reason}");
}
