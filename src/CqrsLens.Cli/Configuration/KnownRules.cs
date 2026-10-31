namespace CqrsLens;

/// <summary>Architecture rule codes accepted by the <c>rules</c> config section.</summary>
internal static class KnownRules
{
    public static readonly string[] All =
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
