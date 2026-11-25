using Xunit;

namespace CqrsLens.Tests;

public sealed class SliceDesignEvaluatorTests
{
    private const string CreateOrder = "Golden.Application.Orders.CreateOrderCommand";
    private static readonly SliceDesign Design = new(
        1, "Golden.Application", CreateOrder, "Command", "Guid", CreateOrder + "Handler",
        CreateOrder + "Validator", new("POST", "/api/orders"),
        [new("creates order", "The order lines are valid", "The customer places the order", "The order is saved")]);

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void BothScannersMatchTheGoldenSliceWithSourceEvidence(AnalysisMode mode)
    {
        var scan = Analyzer.Analyze(GoldenRoot(), mode);

        var evaluation = SliceDesignEvaluator.Evaluate(scan, Design);

        Assert.Equal("matched", evaluation.Status);
        Assert.Equal(new[] { "project", "request", "kind", "responseType", "handler", "validator", "endpoint" },
            evaluation.Checks.Select(check => check.Field));
        Assert.All(evaluation.Checks, check =>
        {
            Assert.Equal("matched", check.Status);
            Assert.NotEmpty(check.Observed);
            Assert.All(check.Evidence, location => Assert.False(Path.IsPathRooted(location.File)));
            Assert.NotEmpty(check.Evidence);
        });
        Assert.Equal(Design.Scenarios, evaluation.Design.Scenarios);
        Assert.Contains("Run the acceptance tests", evaluation.Scope);
    }

    [Fact]
    public void ReportsObservedDriftAndRetainsOtherMatches()
    {
        var scan = Analyzer.Analyze(GoldenRoot());
        var design = Design with
        {
            Kind = "Query",
            ResponseType = "string",
            Handler = "Golden.Application.Orders.ReplacementHandler",
            Endpoint = new("GET", "/api/orders/{id:guid}"),
        };

        var evaluation = SliceDesignEvaluator.Evaluate(scan, design);

        Assert.Equal("mismatch", evaluation.Status);
        Assert.Equal("Command", Assert.Single(Check(evaluation, "kind").Observed));
        Assert.Equal("Guid", Assert.Single(Check(evaluation, "responseType").Observed));
        Assert.Equal(CreateOrder + "Handler", Assert.Single(Check(evaluation, "handler").Observed));
        Assert.Equal("matched", Check(evaluation, "validator").Status);
        var route = Check(evaluation, "endpoint");
        Assert.Equal("mismatch", route.Status);
        Assert.Contains("GetOrderQuery", Assert.Single(route.Observed));
        Assert.Contains(route.Evidence, location => location.File.EndsWith("OrderEndpoints.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectAndTypeIdentityAreCheckedSeparately()
    {
        var scan = Analyzer.Analyze(GoldenRoot());
        var otherOwner = SliceDesignEvaluator.Evaluate(scan, Design with { Project = "Golden.Domain" });
        var differentCase = SliceDesignEvaluator.Evaluate(scan, Design with { Request = CreateOrder.ToLowerInvariant() });
        var shortName = SliceDesignEvaluator.Evaluate(scan, Design with { Request = "CreateOrderCommand" });

        Assert.Equal("mismatch", Check(otherOwner, "request").Status);
        Assert.Contains("Golden.Application.csproj", Assert.Single(Check(otherOwner, "request").Observed));
        Assert.Equal("not_determinable", Check(otherOwner, "handler").Status);
        Assert.Equal("missing", Check(differentCase, "request").Status);
        Assert.Equal("missing", Check(shortName, "request").Status);
    }

    [Fact]
    public void AValidDesignBeforeImplementationReportsMissingRequest()
    {
        var result = AnalyzeEmptyProject();

        var evaluation = SliceDesignEvaluator.Evaluate(result, Design);

        Assert.Equal("missing", evaluation.Status);
        Assert.Equal("matched", Check(evaluation, "project").Status);
        Assert.Equal("missing", Check(evaluation, "request").Status);
        Assert.Equal("not_determinable", Check(evaluation, "handler").Status);
        Assert.Equal("missing", Check(evaluation, "endpoint").Status);
    }

    [Fact]
    public void AFilteredScanDoesNotDeclareAbsentSource()
    {
        var scan = Analyzer.Analyze(GoldenRoot(), AnalysisMode.Text, new AnalysisOptions { Include = ["does-not-exist/**/*.cs"] });

        var evaluation = SliceDesignEvaluator.Evaluate(scan, Design);

        Assert.Equal("not_determinable", evaluation.Status);
        Assert.Equal("not_determinable", Check(evaluation, "request").Status);
        Assert.Equal("not_determinable", Check(evaluation, "endpoint").Status);
    }

    [Fact]
    public void DuplicateProjectNamesCanBeSelectedByPathWithoutBorrowingEndpointEvidence()
    {
        var result = AnalyzeEmptyProject();
        result.Projects.Add(Project(result.RootPath, "nested/alternative"));
        result.Requests.Add(Request("Golden.Application/CreateOrder.cs"));
        result.Requests.Add(Request("nested/alternative/CreateOrder.cs"));
        result.Endpoints.Add(Endpoint());

        var ambiguous = SliceDesignEvaluator.Evaluate(result, Design with { Handler = null, Validator = null });
        var scoped = SliceDesignEvaluator.Evaluate(result, Design with
        {
            Project = "./nested/alternative/Golden.Application.csproj",
            Handler = null,
            Validator = null,
        });

        Assert.Equal("not_determinable", Check(ambiguous, "project").Status);
        Assert.Equal(2, Check(ambiguous, "project").Evidence.Count);
        Assert.Equal("matched", Check(scoped, "project").Status);
        Assert.Equal("matched", Check(scoped, "request").Status);
        Assert.Equal("nested/alternative/CreateOrder.cs", Assert.Single(Check(scoped, "request").Evidence).File);
        Assert.Equal("not_determinable", Check(scoped, "endpoint").Status);
    }

    [Fact]
    public void AGuessedRouteCannotConfirmTheDesign()
    {
        var result = AnalyzeEmptyProject();
        result.Requests.Add(Request("Golden.Application/CreateOrder.cs"));
        result.Endpoints.Add(Endpoint(inferred: true));

        var evaluation = SliceDesignEvaluator.Evaluate(result, Design with { Handler = null, Validator = null });

        Assert.Equal("not_determinable", evaluation.Status);
        Assert.Equal("not_determinable", Check(evaluation, "endpoint").Status);
    }

    [Fact]
    public void GlobalNamespaceRequestCanDeclareNoResponse()
    {
        var result = AnalyzeEmptyProject();
        result.Requests.Add(new CqrsRequest { Name = "RefreshCommand", ProjectName = Design.Project });

        var evaluation = SliceDesignEvaluator.Evaluate(result, Design with
        {
            Request = "RefreshCommand",
            ResponseType = null,
            Handler = null,
            Validator = null,
            Endpoint = null,
        });

        Assert.Equal("matched", evaluation.Status);
        Assert.Equal("void", Assert.Single(Check(evaluation, "responseType").Observed));
    }

    [Fact]
    public void HandlerIdentityCannotBeProvenWithDuplicateDeclarations()
    {
        var result = Analyzer.Analyze(GoldenRoot());
        result.Handlers.Add(new HandlerInfo
        {
            Name = "CreateOrderCommandHandler",
            Namespace = "Golden.Application.Orders",
            RequestName = "CreateOrderCommand",
        });

        var evaluation = SliceDesignEvaluator.Evaluate(result, Design);

        Assert.Equal("not_determinable", evaluation.Status);
        Assert.Equal("not_determinable", Check(evaluation, "handler").Status);
    }

    [Fact]
    public void DuplicateScenarioNamesAreRejected()
    {
        var scenario = Assert.Single(Design.Scenarios);

        Assert.Throws<ArgumentException>(() => SliceDesignEvaluator.Validate(Design with
        {
            Scenarios = [scenario, scenario with { Name = scenario.Name.ToUpperInvariant() }],
        }));
    }

    private static AnalysisResult AnalyzeEmptyProject()
    {
        var result = new AnalysisResult { RootPath = Path.GetTempPath() };
        result.Projects.Add(Project(result.RootPath, Design.Project));
        return result;
    }

    private static ProjectInfo Project(string root, string relativeDirectory)
    {
        var directory = Path.Combine(root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        return new ProjectInfo
        {
            Name = Design.Project,
            DirectoryPath = directory,
            FilePath = Path.Combine(directory, "Golden.Application.csproj"),
        };
    }

    private static CqrsRequest Request(string file) => new()
    {
        Name = "CreateOrderCommand",
        Namespace = "Golden.Application.Orders",
        ProjectName = Design.Project,
        ResponseType = Design.ResponseType,
        Location = new(file, 1, 1),
    };

    private static EndpointInfo Endpoint(bool inferred = false) => new()
    {
        Name = "Create",
        HttpMethod = "POST",
        Route = "/api/orders",
        TargetRequestName = "CreateOrderCommand",
        TargetRequestFullName = CreateOrder,
        IsInferred = inferred,
        Location = new("Golden.Api/OrdersController.cs", 3, 1),
    };

    private static SliceDesignCheck Check(SliceDesignEvaluation evaluation, string field)
        => Assert.Single(evaluation.Checks, check => check.Field == field);

    private static string GoldenRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CqrsLens.sln")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "tests", "fixtures", "GoldenCorpus");
    }
}
