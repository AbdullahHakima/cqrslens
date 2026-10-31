namespace CqrsLens;

/// <summary>The outcome of parsing a command line.</summary>
public enum CliCommandKind
{
    Run,
    Help,
    Version,
    Error,
}
