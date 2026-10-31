namespace CqrsLens;

/// <summary>Raised for malformed command-line input and converted into a usage error.</summary>
internal sealed class CliParseException : Exception
{
    public CliParseException(string message, bool showUsage = false)
        : base(message)
    {
        ShowUsage = showUsage;
    }

    public bool ShowUsage { get; }
}
