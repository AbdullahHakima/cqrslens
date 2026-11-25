namespace CqrsLens;

internal sealed record SliceTestPlan(
    string Request,
    string Status,
    IReadOnlyList<SliceTestScenario> Scenarios,
    IReadOnlyList<SliceTestTarget> Candidates,
    IReadOnlyList<SliceTestSuggestion> SuggestedTests,
    IReadOnlyList<SliceTestEvidence> ExistingTestDispatches,
    IReadOnlyList<string> OpenQuestions,
    IReadOnlyList<SliceTestStep> Steps,
    IReadOnlyList<SliceTestPayloadField>? Payload = null,
    IReadOnlyList<SliceTestCollaborator>? Collaborators = null,
    IReadOnlyList<string>? ActiveBehaviors = null,
    IReadOnlyList<TestProjectInfo>? TestProjects = null,
    IReadOnlyList<TestMethodReference>? DiscoveredTests = null,
    IReadOnlyList<SliceTestScenarioMapping>? ScenarioMappings = null);
