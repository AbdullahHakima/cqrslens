using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Phase 6 Stage 1 aspect G: the endpoint catalog. One row per endpoint, inferred
/// endpoints marked, unresolved targets explicit, honest empty state, MD/HTML parity.
/// </summary>
public sealed class EndpointCatalogTests
{
    private static AnalysisResult Fixture()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };
        result.Projects.Add(new ProjectInfo
        {
            Name = "App.Api",
            FilePath = "/tmp/App.Api/App.Api.csproj",
            DirectoryPath = "/tmp/App.Api",
            Layer = "Api",
        });

        var query = new CqrsRequest
        {
            Name = "GetOrdersQuery",
            Namespace = "App.Application.Orders",
            ProjectName = "App.Application",
        };
        query.Handlers.Add("App.Application.Orders.GetOrdersHandler");
        result.Requests.Add(query);
        result.Handlers.Add(new HandlerInfo
        {
            Name = "GetOrdersHandler",
            Namespace = "App.Application.Orders",
            RequestName = "GetOrdersQuery",
            ProjectName = "App.Application",
        });

        result.Endpoints.Add(Endpoint("OrdersController.Get", "GET", "/orders", "GetOrdersQuery", "App.Application.Orders.GetOrdersQuery", inferred: false));
        result.Endpoints.Add(Endpoint("OrdersController.Create", "POST", "/orders", "GetOrdersQuery", "App.Application.Orders.GetOrdersQuery", inferred: true));
        // No target request name at all: must render as an explicit unresolved target.
        result.Endpoints.Add(Endpoint("ExternalController.Legacy", "GET", "/legacy", "", null, inferred: false));

        return result;
    }

    private static EndpointInfo Endpoint(
        string name, string verb, string route, string target, string? targetFull, bool inferred)
        => new()
        {
            Name = name,
            HttpMethod = verb,
            Route = route,
            TargetRequestName = target,
            TargetRequestFullName = targetFull,
            ProjectName = "App.Api",
            IsInferred = inferred,
        };

    private static AnalysisResult EmptyFixture()
    {
        var result = new AnalysisResult { RootPath = "/tmp/app" };
        result.Projects.Add(new ProjectInfo
        {
            Name = "App",
            FilePath = "/tmp/App/App.csproj",
            DirectoryPath = "/tmp/App",
            Layer = "Api",
        });
        return result;
    }

    [Fact]
    public void RowCount_ReconcilesWithTheModel_AndOrderIsDeterministic()
    {
        var result = Fixture();
        var view = EndpointCatalogView.Build(result);

        Assert.Equal(result.Endpoints.Count, view.EndpointCount);
        Assert.Equal(3, view.EndpointCount);
        Assert.Equal(1, view.InferredCount);
        Assert.Equal(1, view.ProjectCount);

        // Ordered by project, then route, then name.
        Assert.Equal(new[] { "ExternalController.Legacy", "OrdersController.Create", "OrdersController.Get" },
            view.Rows.Select(r => r.Endpoint));
        Assert.Equal(result.Endpoints.Count, view.Rows.Count);
        Assert.Equal(result.Endpoints.Count,
            view.Rows.Select(r => r.Endpoint).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void HandlerComesOnlyFromTheExistingRequestHandlerPairing_UnresolvedIsExplicit()
    {
        var view = EndpointCatalogView.Build(Fixture());

        var resolved = view.Rows.Single(r => r.Endpoint == "OrdersController.Get");
        Assert.Equal("GetOrdersQuery", resolved.Target);
        Assert.Equal("GetOrdersHandler", resolved.Handler);

        var unresolved = view.Rows.Single(r => r.Endpoint == "ExternalController.Legacy");
        Assert.Equal("unresolved", unresolved.Target);
        Assert.Equal("—", unresolved.Handler);
    }

    [Fact]
    public void InferredEndpoints_AreMarked()
    {
        var view = EndpointCatalogView.Build(Fixture());
        Assert.True(view.Rows.Single(r => r.Endpoint == "OrdersController.Create").Inferred);

        var md = ReportGenerator.Generate(Fixture());
        var html = HtmlReportGenerator.Generate(Fixture(), DiagramRenderMode.Svg);
        Assert.Contains("| Inferred |", md);
        Assert.Contains("yes", md);
        Assert.Contains("<th>Inferred</th>", html);
    }

    [Fact]
    public void EmptyCorpus_RendersTheSectionWithAnExplicitEmptyState()
    {
        var md = ReportGenerator.Generate(EmptyFixture());
        var html = HtmlReportGenerator.Generate(EmptyFixture(), DiagramRenderMode.Svg);

        Assert.Contains("## API Endpoints", md);
        Assert.Contains("No endpoints discovered.", md);
        Assert.Contains("API Endpoints", html);
        Assert.Contains("No endpoints discovered.", html);
    }

    [Fact]
    public void MarkdownAndHtml_PresentTheSameRowsAndNumbers()
    {
        var result = Fixture();
        var view = EndpointCatalogView.Build(result);
        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);

        const string summary = "3 endpoint(s)";
        Assert.Contains(summary, md);
        Assert.Contains(summary, html);
        Assert.Contains("(1 inferred)", md);
        Assert.Contains("(1 inferred)", html);

        foreach (var row in view.Rows)
        {
            Assert.Contains(row.Endpoint, md);
            Assert.Contains(row.Endpoint, html);
            Assert.Contains(row.Route, md);
            Assert.Contains(row.Route, html);
        }
    }
}
