namespace CqrsLens;

/// <summary>
/// Unified origin vocabulary for every evidence occurrence in the spine (F0 §5.2).
/// </summary>
public enum EvidenceOrigin
{
    Declaration,
    Registration,
    CallSite,
    MappingOrAttribute,
    ProjectFile,
    Configuration,
}
