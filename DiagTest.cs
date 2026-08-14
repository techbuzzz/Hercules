// Minimal diagnostic - run with: dotnet script DiagTest.cs
using System.Collections.Concurrent;
using Hercules.Mesh.Abstractions;

// Recreate the exact scenario
var queue = new Hercules.Mesh.InProcess.InProcessTaskQueue();

var task = new MeshTask
{
    QueueName = "dlq-q",
    Intent = "failing-max",
    MaxRetries = 1
};

var enqueued = await queue.EnqueueAsync(task);
Console.WriteLine($"Enqueued task ID: {enqueued.Id}, MaxRetries: {enqueued.Task.MaxRetries}");

var dequeued = await queue.DequeueAsync("dlq-q", TimeSpan.FromSeconds(5));
Console.WriteLine($"Dequeued task ID: {dequeued?.Id}, MaxRetries: {dequeued?.Task.MaxRetries}, RetryCount: {dequeued?.RetryCount}");

if (dequeued != null)
{
    await queue.FailAsync(dequeued.Id, "error", retry: 1);
    Console.WriteLine("FailAsync called");
}
else
{
    Console.WriteLine("Dequeued was null!");
}

var dlq = await queue.GetDeadLetterQueueAsync("dlq-q-dlq");
Console.WriteLine($"DLQ count: {dlq.Count}");
foreach (var item in dlq)
{
    Console.WriteLine($"DLQ item: {item.Task.Intent}, MaxRetries: {item.Task.MaxRetries}");
}
