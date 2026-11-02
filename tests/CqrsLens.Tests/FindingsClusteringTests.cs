using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 6 Stage 1 aspect J: findings clustering and aggregate metrics. Cluster
/// totals must equal the finding totals exactly, and every finding must land in
/// exactly one module/feature cluster (unresolvable targets under (unassigned)).
/// </summary>
public sealed class FindingsClusteringTests
{
    private static AnalysisResult Fixture()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };

        result.Requests.Add(Request("CreateOrderCommand", "App.Application.Features.Orders"));
        result.Requests.Add(Request("GetBillQuery", "App.Application.Features.Billing"));
        var notification = new CqrsNotification
        {
            Name = "OrderPlaced",
            Namespace = "App.Application.Features.Orders",
            ProjectName = "App.Application",
        };
        result.Notifications.Add(notification);

        result.Checks.Add(Finding(CheckSeverity.Warning, ArchitectureChecker.CommandWithoutValidatorCode,
            "App.Application.Features.Orders.CreateOrderCommand", "App.Application"));
        result.Checks.Add(Finding(CheckSeverity.Warning, ArchitectureChecker.CommandWithoutValidatorCode,
            "App.Application.Features.Billing.GetBillQuery", "App.Application"));
        result.Checks.Add(Finding(CheckSeverity.Warning, ArchitectureChecker.OrphanNotificationCode,
            "App.Application.Features.Orders.OrderPlaced", "App.Application"));
        // Target cannot be resolved to a shape: must fall into (unassigned) feature, never dropped.
        result.Checks.Add(Finding(CheckSeverity.Error, ArchitectureChecker.MissingHandlerCode,
            "GhostCommand", "App.Application"));
        // Project-level finding with no namespace: (unassigned) feature, its own module.
        result.Checks.Add(Finding(CheckSeverity.Warning, ArchitectureChecker.AmbiguousProjectNameCode,
            "Shared", "Shared"));

        return result;
    }

    private static CqrsRequest Request(string name, string ns)
    {
        var request = new CqrsRequest { Name = name, Namespace = ns, ProjectName = "App.Application" };
        request.Handlers.Add($"{ns}.{name}Handler");
        return request;
    }

    private static CheckFinding Finding(CheckSeverity severity, string code, string target, string project)
        => new(severity, code, $"{code} on {target}.", Target: target, ProjectName: project);

    [Fact]
    public void ClusterTotals_EqualFindingTotals_AndArithmeticHolds()
    {
        var result = Fixture();
        var metrics = FindingsClustering.Analyze(result);

        Assert.Equal(result.Checks.Count, metrics.Total);
        Assert.Equal(1, metrics.Errors);
        Assert.Equal(4, metrics.Warnings);

        Assert.Equal(metrics.Total, metrics.ByRule.Sum(r => r.Total));
        Assert.Equal(metrics.Total, metrics.ByModule.Sum(m => m.Total));
        Assert.Equal(metrics.Total, metrics.Clusters.Sum(c => c.Total));
        Assert.Equal(metrics.Errors, metrics.Clusters.Sum(c => c.Errors));
        Assert.Equal(metrics.Warnings, metrics.Clusters.Sum(c => c.Warnings));

        // By-rule breakdown equals the raw finding counts.
        Assert.Equal(2, metrics.ByRule.Single(r => r.Code == ArchitectureChecker.CommandWithoutValidatorCode).Total);
        Assert.Equal(1, metrics.ByRule.Single(r => r.Code == ArchitectureChecker.OrphanNotificationCode).Total);
        Assert.Equal(1, metrics.ByRule.Single(r => r.Code == ArchitectureChecker.MissingHandlerCode).Total);
        Assert.Equal(1, metrics.ByRule.Single(r => r.Code == ArchitectureChecker.AmbiguousProjectNameCode).Total);
    }

    [Fact]
    public void FeatureAssignment_ReusesTheCqrsMapFeatureDefinition()
    {
        var metrics = FindingsClustering.Analyze(Fixture());

        var orders = metrics.Clusters.Single(c => c.Module == "App.Application" && c.Feature == "Orders");
        Assert.Equal(2, orders.Total); // validator + notification both live in the Orders feature
        Assert.Equal(2, orders.Warnings);

        Assert.Contains(metrics.Clusters, c => c.Module == "App.Application" && c.Feature == "Billing" && c.Total == 1);

        // Sanity: the reused definition yields the same feature for the same namespace/project.
        Assert.Equal("Orders", CqrsRequestOrdering.FeatureName("App.Application.Features.Orders", "App.Application"));
    }

    [Fact]
    public void UnresolvableTargets_LandInTheUnassignedBucket_ExactlyOnce()
    {
        var metrics = FindingsClustering.Analyze(Fixture());

        var ghost = metrics.Clusters.Single(c =>
            c.Module == "App.Application" && c.Feature == FindingsClustering.Unassigned);
        Assert.Equal(1, ghost.Total);
        Assert.Equal(1, ghost.Errors);

        var shared = metrics.Clusters.Single(c =>
            c.Module == "Shared" && c.Feature == FindingsClustering.Unassigned);
        Assert.Equal(1, shared.Total);

        // No finding was dropped or double counted.
        Assert.Equal(metrics.Total, metrics.Clusters.Sum(c => c.Total));
    }

    [Fact]
    public void NullProjectAndTarget_BothGoUnassigned()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };
        result.Checks.Add(new CheckFinding(CheckSeverity.Warning, "SomeRule", "message"));

        var metrics = FindingsClustering.Analyze(result);

        var cluster = Assert.Single(metrics.Clusters);
        Assert.Equal(FindingsClustering.Unassigned, cluster.Module);
        Assert.Equal(FindingsClustering.Unassigned, cluster.Feature);
        Assert.Equal(1, cluster.Total);
    }

    [Fact]
    public void Baseline_SkipsKnownFindings()
    {
        var result = Fixture();
        result.BaselineApplied = true;
        result.Checks[0] = result.Checks[0] with { IsKnown = true };

        var metrics = FindingsClustering.Analyze(result);

        Assert.Equal(result.Checks.Count - 1, metrics.Total);
        Assert.DoesNotContain(metrics.ByRule, r => r.Code == ArchitectureChecker.CommandWithoutValidatorCode && r.Total == 2);
    }
}
