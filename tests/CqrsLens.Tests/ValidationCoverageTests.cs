using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 6 Stage 1 aspect H: the validation-coverage figure. Coverage uses the
/// model's request↔validator linkage only (no name matching); zero-request states
/// show "no requests", and Markdown/HTML agree.
/// </summary>
public sealed class ValidationCoverageTests
{
    private static AnalysisResult Fixture()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };

        result.Requests.Add(Request("AlphaCommand", "App.Application", covered: true));
        result.Requests.Add(Request("BetaCommand", "App.Application", covered: false));
        result.Requests.Add(Request("GammaCommand", "App.Application", covered: false));
        result.Requests.Add(Request("DeltaCommand", "App.Legacy", covered: true));

        // A validator whose target name matches BetaCommand but is NOT linked to it:
        // coverage must not count it (no name matching).
        result.Validators.Add(new ValidatorInfo
        {
            Name = "BetaCommandValidator",
            Namespace = "App.Legacy",
            TargetType = "BetaCommand",
            ProjectName = "App.Legacy",
        });
        return result;
    }

    private static CqrsRequest Request(string name, string project, bool covered)
    {
        var request = new CqrsRequest { Name = name, Namespace = project, ProjectName = project };
        if (covered)
            request.Validators.Add($"{project}.{name}Validator");
        return request;
    }

    [Fact]
    public void CoverageMath_IsPerProjectAndSolutionWide_WithOneDecimal()
    {
        var view = ValidationCoverageView.Build(Fixture());

        var app = view.Projects.Single(p => p.Project == "App.Application");
        Assert.Equal(3, app.Requests);
        Assert.Equal(1, app.Covered);
        Assert.Equal(33.3, app.Percent);

        var legacy = view.Projects.Single(p => p.Project == "App.Legacy");
        Assert.Equal(1, legacy.Requests);
        Assert.Equal(1, legacy.Covered);
        Assert.Equal(100.0, legacy.Percent);

        Assert.Equal("All projects", view.Solution.Project);
        Assert.Equal(4, view.Solution.Requests);
        Assert.Equal(2, view.Solution.Covered);
        Assert.Equal(50.0, view.Solution.Percent);
        Assert.Equal(1, view.Solution.Validators);
    }

    [Fact]
    public void UnlinkedValidatorWithMatchingName_DoesNotCountAsCoverage()
    {
        var view = ValidationCoverageView.Build(Fixture());
        // BetaCommand has no linked validator even though a BetaCommandValidator exists.
        Assert.Equal(1, view.Projects.Single(p => p.Project == "App.Application").Covered);
    }

    [Fact]
    public void ZeroRequests_ShowsNoRequestsAndStillShowsValidatorCount()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };
        result.Validators.Add(new ValidatorInfo { Name = "AValidator", Namespace = "P", TargetType = "A", ProjectName = "P" });
        result.Validators.Add(new ValidatorInfo { Name = "BValidator", Namespace = "P", TargetType = "B", ProjectName = "P" });

        var view = ValidationCoverageView.Build(result);

        var project = Assert.Single(view.Projects);
        Assert.Equal(0, project.Requests);
        Assert.Null(project.Percent);
        Assert.Equal("no requests", ValidationCoverageView.CoverageText(project));
        Assert.Equal(2, project.Validators);

        Assert.Null(view.Solution.Percent);
        Assert.Equal("no requests", ValidationCoverageView.CoverageText(view.Solution));
        Assert.Equal(2, view.Solution.Validators);
    }

    [Fact]
    public void ProjectsAreOrderedByName_AndSolutionComesLast()
    {
        var view = ValidationCoverageView.Build(Fixture());
        Assert.Equal(new[] { "App.Application", "App.Legacy" }, view.Projects.Select(p => p.Project));
    }

    [Fact]
    public void MarkdownAndHtml_PresentTheSameNumbers()
    {
        var result = Fixture();
        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        foreach (var output in new[] { md, html })
        {
            Assert.Contains("Validation Coverage", output);
            Assert.Contains("33.3%", output);
            Assert.Contains("100.0%", output);
            Assert.Contains("50.0%", output);
            Assert.DoesNotContain("health", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("grade", output, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ZeroRequestCorpus_RendersNoRequestsInBothFormats()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };
        result.Validators.Add(new ValidatorInfo { Name = "AValidator", Namespace = "P", TargetType = "A", ProjectName = "P" });

        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        Assert.Contains("no requests", md);
        Assert.Contains("no requests", html);
        Assert.DoesNotContain("0.0%", md);
        Assert.DoesNotContain("0.0%", html);
    }
}
