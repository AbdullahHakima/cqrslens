namespace CqrsLens;

/// <summary>A diff input or comparison error that should be reported as a CLI usage failure.</summary>
public sealed class DiffException : Exception
{
    public DiffException(string message) : base(message)
    {
    }
}
