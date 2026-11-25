using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public sealed class ArchitectureProposalTests
{
    [Fact]
    public void NewProjectCanJoinTheArchitectureWithoutChangingTheScan()
    {
        var result = Scan(Project("Shop.Domain", "Domain"));
        var evaluation = ArchitectureProposalEvaluator.Evaluate(result,
            [new("Shop.Payments.Application", "Application")],
            [new("shop.payments.application", "SHOP.DOMAIN")]);

        Assert.Equal("checks_passed", evaluation.Status);
        Assert.Empty(evaluation.Findings);
        Assert.Single(result.Projects);
        Assert.Empty(result.Projects[0].ReferenceNames);
        Assert.Empty(result.Checks);
    }

    [Fact]
    public void EvaluatesAllChangesTogetherSoRemovingAnEdgeCanAvoidACycle()
    {
        var result = Scan(
            Project("Shop.Api", "Api", "Shop.Infrastructure"),
            Project("Shop.Infrastructure", "Infrastructure"));

        var cycle = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new("Shop.Infrastructure", "Shop.Api")]);
        var resolved = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new("Shop.Infrastructure", "Shop.Api"), new("Shop.Api", "Shop.Infrastructure", "remove")]);

        Assert.Equal("conflicts", cycle.Status);
        var finding = Assert.Single(cycle.Findings);
        Assert.Equal(ArchitectureChecker.CircularProjectDependencyCode, finding.Code);
        Assert.Equal(new[] { "Shop.Infrastructure", "Shop.Api", "Shop.Infrastructure" }, finding.DependencyPath);
        Assert.Equal("Shop.Infrastructure/Shop.Infrastructure.csproj", finding.File);
        Assert.Equal("checks_passed", resolved.Status);
        Assert.Equal("Shop.Infrastructure", Assert.Single(result.Projects[0].ReferenceNames));
    }

    [Fact]
    public void CallsOutOutwardDependenciesAndProductionReferencesToTests()
    {
        var result = Scan(
            Project("Shop.Domain", "Domain"),
            Project("Shop.Api", "Api"),
            Project("Shop.Tests", "Tests"));
        var evaluation = ArchitectureProposalEvaluator.Evaluate(result,
            [new("Shop.Application", "Application")],
            [new("Shop.Domain", "Shop.Api"), new("Shop.Application", "Shop.Tests")]);

        Assert.Equal("conflicts", evaluation.Status);
        Assert.Contains(evaluation.Findings, issue => issue.Code == ArchitectureChecker.LayerViolationCode);
        Assert.Contains(evaluation.Findings, issue => issue.Code == "ProductionReferenceToTests");
        Assert.All(evaluation.Findings, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Suggestion)));
    }

    [Fact]
    public void UsesExplicitPlannedLayersInAnEmptyRepository()
    {
        var evaluation = ArchitectureProposalEvaluator.Evaluate(Scan(),
            [new("Policy", "domain"), new("UseCases", "APPLICATION")],
            [new("Policy", "UseCases")]);

        Assert.Equal("conflicts", evaluation.Status);
        Assert.Equal(ArchitectureChecker.LayerViolationCode, Assert.Single(evaluation.Findings).Code);
    }

    [Fact]
    public void ScansAndProposalsBothFlagDomainReferencesToApplication()
    {
        var observed = Scan(Project("Shop.Domain", "Domain", "Shop.Application"), Project("Shop.Application", "Application"));
        var finding = Assert.Single(ArchitectureChecker.RunChecks(observed));

        Assert.Equal(ArchitectureChecker.LayerViolationCode, finding.Code);
        Assert.Equal(CheckSeverity.Error, finding.Severity);
    }

    [Theory]
    [InlineData(null, "checks_passed")]
    [InlineData(CheckSeverity.Warning, "review")]
    public void AppliesConfiguredLayerRuleSeverity(CheckSeverity? severity, string status)
    {
        var result = Scan(Project("Shop.Application", "Application"), Project("Shop.Infrastructure", "Infrastructure"));
        result.LayerRuleOverrides = new Dictionary<string, CheckSeverity?>
        {
            [ArchitectureChecker.ApplicationLayerViolationCode] = severity,
        };

        var evaluation = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new("Shop.Application", "Shop.Infrastructure")]);

        Assert.Equal(status, evaluation.Status);
    }

    [Fact]
    public void InvalidProposalCannotSilentlyBindAnAmbiguousOrMissingProject()
    {
        var result = Scan(
            Project("Worker", "Other"),
            Project("worker", "Other"),
            Project("Shop.Api", "Api"));
        var evaluation = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new("Shop.Api", "Worker"), new("Shop.Api", "Missing")]);

        Assert.Equal("invalid", evaluation.Status);
        var ambiguous = Assert.Single(evaluation.Findings, issue => issue.Code == ArchitectureChecker.AmbiguousProjectNameCode);
        Assert.Equal(2, ambiguous.Candidates!.Count);
        Assert.Contains(evaluation.Findings, issue => issue.Code == "UnknownProject");
    }

    [Fact]
    public void RootRelativePathsCanIdentifyProjectsWithTheSameName()
    {
        var first = ProjectInDirectory("Worker", "Application", Path.Combine("services", "first"));
        var second = ProjectInDirectory("Worker", "Infrastructure", Path.Combine("services", "second"));
        var evaluation = ArchitectureProposalEvaluator.Evaluate(Scan(first, second), [],
            [new(@"services\first\Worker.csproj", "./services/second/Worker.csproj")]);

        Assert.Equal("conflicts", evaluation.Status);
        var finding = Assert.Single(evaluation.Findings);
        Assert.Equal(ArchitectureChecker.ApplicationLayerViolationCode, finding.Code);
        Assert.Equal(new("services/first/Worker.csproj", "services/second/Worker.csproj"), finding.Reference);
    }

    [Fact]
    public void ExistingChecksAndUnrelatedCyclesDoNotBecomeProposalFindings()
    {
        var result = Scan(
            Project("Existing.Api", "Api", "Existing.Domain"),
            Project("Existing.Domain", "Domain", "Existing.Api"),
            Project("New.Api", "Api"),
            Project("New.Domain", "Domain"));
        result.Checks.AddRange(ArchitectureChecker.RunChecks(result));

        var evaluation = ArchitectureProposalEvaluator.Evaluate(result, [], [new("New.Api", "New.Domain")]);

        Assert.NotEmpty(result.Checks);
        Assert.Equal("checks_passed", evaluation.Status);
        Assert.Empty(evaluation.Findings);
    }

    [Fact]
    public void AnUnresolvedReferenceOnTheReturnPathMakesCycleCheckInconclusive()
    {
        var from = Project("Shop.Api", "Api");
        var to = Project("Shop.Domain", "Domain");
        to.ReferenceIncludes.Add("../External/External.csproj");
        var evaluation = ArchitectureProposalEvaluator.Evaluate(Scan(from, to), [], [new(from.Name, to.Name)]);

        Assert.Equal("review", evaluation.Status);
        Assert.Equal("UnresolvedProjectReference", Assert.Single(evaluation.Findings).Code);
    }

    [Fact]
    public void ResolvesExistingEdgesByPathEvenWhenAnIntermediateProjectNameIsAmbiguous()
    {
        var from = Project("Shop.Api", "Api");
        var to = Project("Shop.Domain", "Domain");
        var oneWorker = Project("Worker", "Other");
        var anotherWorker = ProjectInDirectory("Worker", "Other", Path.Combine("alt", "Worker"));
        to.ReferenceIncludes.Add("../Worker/Worker.csproj");
        oneWorker.ReferenceIncludes.Add("../Shop.Api/Shop.Api.csproj");

        var evaluation = ArchitectureProposalEvaluator.Evaluate(Scan(from, to, oneWorker, anotherWorker), [], [new(from.Name, to.Name)]);

        Assert.Equal("conflicts", evaluation.Status);
        Assert.Contains(evaluation.Findings, issue => issue.DependencyPath?.Contains("Worker/Worker.csproj") == true);
    }

    [Fact]
    public void ConflictingOperationsAreRejectedBeforeCheckingTheGraph()
    {
        var result = Scan(Project("Shop.Api", "Api"), Project("Shop.Domain", "Domain"));
        var evaluation = ArchitectureProposalEvaluator.Evaluate(result, [],
            [new("Shop.Api", "Shop.Domain"), new("shop.api", "Shop.Domain", "remove")]);

        Assert.Equal("invalid", evaluation.Status);
        Assert.Equal("ConflictingReferenceChanges", Assert.Single(evaluation.Findings).Code);
    }

    private static AnalysisResult Scan(params ProjectInfo[] projects)
    {
        var result = new AnalysisResult { RootPath = Path.GetTempPath() };
        result.Projects.AddRange(projects);
        return result;
    }

    private static ProjectInfo Project(string name, string layer, params string[] references)
    {
        var project = ProjectInDirectory(name, layer, name);
        project.ReferenceNames.AddRange(references);
        return project;
    }

    private static ProjectInfo ProjectInDirectory(string name, string layer, string folder)
    {
        var directory = Path.Combine(Path.GetTempPath(), folder);
        return new ProjectInfo
        {
            Name = name,
            Layer = layer,
            DirectoryPath = directory,
            FilePath = Path.Combine(directory, name + ".csproj"),
        };
    }
}
