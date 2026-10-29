namespace CqrsLens;

/// <summary>A user-facing failure that maps to a specific process exit code.</summary>
internal sealed class CliFailureException : Exception
{
    public CliFailureException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
