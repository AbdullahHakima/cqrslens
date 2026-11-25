namespace CqrsLens;

/// <summary>How a discovered system entrypoint executes or dispatches its workload.</summary>
public enum SystemExecutionStyle
{
    Cqrs,
    Direct,
    Worker,
    EventConsumer,
}
