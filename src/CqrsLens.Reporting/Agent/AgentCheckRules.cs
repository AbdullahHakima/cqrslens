namespace CqrsLens;

/// <summary>The eleven architecture rule codes the scan can emit, in <see cref="ArchitectureChecker"/> order.</summary>
internal static class AgentCheckRules
{
    internal static readonly IReadOnlyList<string> All = new[]
    {
        ArchitectureChecker.MissingHandlerCode,
        ArchitectureChecker.MultipleHandlersCode,
        ArchitectureChecker.LayerViolationCode,
        ArchitectureChecker.ApplicationLayerViolationCode,
        ArchitectureChecker.CircularProjectDependencyCode,
        ArchitectureChecker.CommandWithoutValidatorCode,
        ArchitectureChecker.OrphanValidatorCode,
        ArchitectureChecker.OrphanNotificationCode,
        ArchitectureChecker.AmbiguousProjectNameCode,
        ArchitectureChecker.UnreachableRequestCode,
        ArchitectureChecker.UnpublishedNotificationCode,
    };
}
