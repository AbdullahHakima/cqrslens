namespace CqrsLens;

/// <summary>A schema-valid v1 CQRS slice design drafted from static scan facts.</summary>
public sealed record SliceDesignDraft(
    string Status,
    string Request,
    SliceDesign? Design,
    IReadOnlyList<string> Gaps,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<string> Candidates,
    string Scope = "Drafted from static scan facts. Replace starter scenarios with concrete acceptance criteria before strict enforcement.");
