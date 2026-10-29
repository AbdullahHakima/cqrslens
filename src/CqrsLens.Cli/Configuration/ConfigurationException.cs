namespace CqrsLens;

/// <summary>A config error that should be reported as a CLI usage failure.</summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message)
        : base(message)
    {
    }
}
