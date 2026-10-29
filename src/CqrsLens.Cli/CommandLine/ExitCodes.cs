namespace CqrsLens;

/// <summary>Process exit codes shared by the CLI, tests and CI integrations.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Usage = 1;
    public const int PathNotFound = 2;
    public const int NoProjects = 3;
    public const int StrictFailure = 4;
    public const int Unexpected = 5;
}
