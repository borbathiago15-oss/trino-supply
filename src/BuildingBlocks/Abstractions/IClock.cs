namespace TrinoSupply.BuildingBlocks.Abstractions;

/// <summary>Relógio injetável (determinismo em testes — QA-001 §3).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
