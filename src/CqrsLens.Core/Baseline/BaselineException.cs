namespace CqrsLens;

/// <summary>A baseline error that should be reported as a CLI usage failure.</summary>
public sealed class BaselineException : Exception
{
    public BaselineException(string message) : base(message)
    {
    }
}
