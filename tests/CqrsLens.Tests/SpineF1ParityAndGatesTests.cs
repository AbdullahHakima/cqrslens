using System.Text.Json;
using System.Text.Json.Nodes;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// CP2 and CP3 verification suite: single-source-of-truth parity, bidirectional migration,
/// Validates/TouchesData promotion, ResolvesTo semantics, Wraps applicability (F0 §4.3),
/// per-assignment LayerProvenance, additive JSON "relations" contract, and all F0 §8 gates
/// across in-repo fixtures.
/// </summary>
public sealed class SpineF1ParityAndGatesTests
{
    private static readonly string RepoRoot = FindRepositoryRoot();
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> InRepoFixtures() => new()
    {
        Path.Combine("tests", "fixtures", "GoldenCorpus"),
        Path.Combine("tests", "fixtures", "Adversarial"),
        Path.Combine("tests", "fixtures", "DiffPair", "base"),
        Path.Combine("tests", "fixtures", "DiffPair", "head"),
        Path.Combine("tests", "fixtures", "StrictFailure"),
        Path.Combine("samples", "SampleShop"),
    };

    [Theory]
    [MemberData(nameof(InRepoFixtures))]
    public void F0_Gates2To4_HoldForTextAndRoslynScansAcrossEveryInRepoFixture(string relativeFixture)
    {
        var fixturePath = Path.Combine(RepoRoot, relativeFixture);
        foreach (var mode in new[] { AnalysisMode.Text, AnalysisMode.Roslyn })
        {
            var result = Analyzer.Analyze(fixturePath, mode, generatedAt: FixedTimestamp);
            var spine = result.Spine;

            // Gate 2: Identity uniqueness across Projects, Components, Endpoints, and Relations.
            Assert.Equal(spine.Projects.Count, spine.Projects.Select(p => p.Id).Distinct().Count());
            Assert.Equal(spine.Components.Count, spine.Components.Select(c => c.Id).Distinct().Count());
            Assert.Equal(spine.Endpoints.Count, spine.Endpoints.Select(e => e.Id).Distinct().Count());
            Assert.Equal(spine.Relations.Count, spine.Relations.Select(r => r.Key).Distinct().Count());

            // Gate 3: Evidence completeness on every component role, endpoint, and relation.
            foreach (var comp in spine.Components)
            {
                Assert.True(comp.Roles.Count >= 1, $"Component '{comp.FullName}' has no roles.");
                foreach (var roleEv in comp.Roles)
                {
                    Assert.NotEmpty(roleEv.Evidence);
                    foreach (var occ in roleEv.Evidence)
                        AssertEvidenceValid(occ);
                }
            }

            foreach (var ep in spine.Endpoints)
            {
                Assert.True(ep.Evidence.Count >= 1, $"Endpoint '{ep.Name}' has no evidence.");
                foreach (var occ in ep.Evidence)
                    AssertEvidenceValid(occ);

                if (ep.IsInferred)
                {
                    Assert.True(
                        ep.VerbStrength != EvidenceStrength.Proven || ep.RouteStrength != EvidenceStrength.Proven,
                        $"Inferred endpoint '{ep.Name}' must not claim all Proven fact strengths.");
                }
            }

            foreach (var rel in spine.Relations)
            {
                Assert.True(rel.OccurrenceCount >= 1);
                foreach (var occ in rel.Occurrences)
                    AssertEvidenceValid(occ);
            }

            // Gate 4: Guessed projects must never carry Proven LayerStrength or Configured provenance.
            foreach (var proj in spine.Projects)
            {
                if (proj.LayerProvenance == LayerProvenance.Guessed)
                    Assert.NotEqual(EvidenceStrength.Proven, proj.LayerStrength);
            }

            // Gate 4: Behaviors without established applicability must never emit Wraps relations.
            var wrappedBehaviors = spine.Relations
                .Where(r => r.Kind == RelationKind.Wraps)
                .Select(r => r.From.ComponentId)
                .Where(id => id is not null)
                .ToHashSet();

            foreach (var unestablished in spine.Unproven.UnestablishedBehaviors)
            {
                if (unestablished.BehaviorComponentId is not null)
                    Assert.DoesNotContain(unestablished.BehaviorComponentId, wrappedBehaviors);
            }

            // Legacy edge projections remain 100% aligned with SpineQueries.
            Assert.Equal(
                SpineQueries.FlowEdges(result).Select(e => (e.Kind, e.From, e.To, e.ProjectName, e.Location)),
                result.FlowEdges.Select(e => (e.Kind, e.From, e.To, e.ProjectName, e.Location)));
            Assert.Equal(
                SpineQueries.ResolvedEdges(result).Select(e => (e.Kind, e.From, e.Via, e.To, e.ProjectName, e.Location)),
                result.ResolvedEdges.Select(e => (e.Kind, e.From, e.Via, e.To, e.ProjectName, e.Location)));
            Assert.Equal(
                SpineQueries.ServiceCallEdges(spine).Select(e => (e.From, e.Via, e.Method, e.To, e.ProjectName, e.Location)),
                result.ServiceCallEdges.Select(e => (e.From, e.Via, e.Method, e.To, e.ProjectName, e.Location)));
            Assert.Equal(
                SpineQueries.DispatchCallSites(spine).Select(s => (s.TargetFullName, s.TargetKind, s.SenderFullName, s.ProjectName, s.IsTestProject, s.Location)),
                result.DispatchCallSites.Select(s => (s.TargetFullName, s.TargetKind, s.SenderFullName, s.ProjectName, s.IsTestProject, s.Location)));
            Assert.Equal(spine.Unproven.UnresolvedDispatches, result.UnresolvedDispatches);
        }
    }

    [Theory]
    [MemberData(nameof(InRepoFixtures))]
    public void F0_Gate5_Determinism_RepeatedScansProduceByteIdenticalOutputs(string relativeFixture)
    {
        var fixturePath = Path.Combine(RepoRoot, relativeFixture);
        var options = new AnalysisOptions { GeneratedAt = FixedTimestamp };

        var first = Analyzer.Analyze(fixturePath, AnalysisMode.Text, options);
        var second = Analyzer.Analyze(fixturePath, AnalysisMode.Text, options);

        Assert.Equal(JsonReportGenerator.Generate(first), JsonReportGenerator.Generate(second));
        Assert.Equal(ReportGenerator.Generate(first), ReportGenerator.Generate(second));
        Assert.Equal(HtmlReportGenerator.Generate(first), HtmlReportGenerator.Generate(second));
        Assert.Equal(SarifReportGenerator.Generate(first), SarifReportGenerator.Generate(second));
        Assert.Equal(AgentReportGenerator.Generate(first), AgentReportGenerator.Generate(second));
    }

    [Fact]
    public void PromotesValidatesAndTouchesDataRelations_OnGoldenCorpus()
    {
        var goldenPath = Path.Combine(RepoRoot, "tests", "fixtures", "GoldenCorpus");
        var result = Analyzer.Analyze(goldenPath, AnalysisMode.Text, generatedAt: FixedTimestamp);

        var validates = result.Spine.GetRelations(RelationKind.Validates);
        Assert.NotEmpty(validates);
        Assert.Contains(validates, r =>
            r.From.SymbolId?.Name == "CreateOrderCommandValidator" &&
            r.To.SymbolId?.Name == "CreateOrderCommand");

        var touches = result.Spine.GetRelations(RelationKind.TouchesData);
        Assert.NotEmpty(touches);
        Assert.Contains(touches, r =>
            r.From.SymbolId?.Name == "CreateOrderCommandHandler" &&
            (r.To.MemberName == "Orders" || r.To.Name.EndsWith(".Orders", StringComparison.Ordinal)) &&
            r.Via.Any(v => v.Name.Contains("IOrderRepository", StringComparison.Ordinal)));
    }

    [Fact]
    public void ResolvesTo_CapturesCandidateVsBindingAndExcludesAmbiguous()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteProject("App", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        fixture.WriteFile("App", "Services.cs", """
            namespace App;
            public interface ICandidateOnly { void Ping(); }
            public sealed class CandidateImpl : ICandidateOnly { public void Ping() { } }

            public interface IConfirmedService { void Run(); }
            public sealed class ConfirmedImpl : IConfirmedService { public void Run() { } }

            public interface IDeclaredOnly { void Exec(); }
            public sealed class DeclaredPrimary : IDeclaredOnly { public void Exec() { } }
            public sealed class DeclaredSecondary : IDeclaredOnly { public void Exec() { } }

            public interface IAmbiguous { void DoWork(); }
            public sealed class AmbiguousOne : IAmbiguous { public void DoWork() { } }
            public sealed class AmbiguousTwo : IAmbiguous { public void DoWork() { } }

            public static class Startup
            {
                public static void Register(object services)
                {
                    services.AddScoped<IConfirmedService, ConfirmedImpl>();
                    services.AddScoped<IDeclaredOnly, DeclaredPrimary>();
                    services.AddScoped<IAmbiguous, AmbiguousOne>();
                    services.AddScoped<IAmbiguous, AmbiguousTwo>();
                }
            }
            """);

        var result = Analyzer.Analyze(fixture.RootPath, AnalysisMode.Roslyn, generatedAt: FixedTimestamp);
        var resolves = result.Spine.GetRelations(RelationKind.ResolvesTo);

        var candidateRel = Assert.Single(resolves, r =>
            (r.From.SymbolId?.FullName ?? r.From.Name) == "App.ICandidateOnly");
        Assert.Equal("App.CandidateImpl", candidateRel.To.SymbolId?.FullName ?? candidateRel.To.Name);
        Assert.Contains(candidateRel.Occurrences, o =>
            o.ResolutionSemantics == ResolutionSemantics.StaticCandidate &&
            o.Strength == EvidenceStrength.Inferred);

        var declaredRel = Assert.Single(resolves, r =>
            (r.From.SymbolId?.FullName ?? r.From.Name) == "App.IDeclaredOnly");
        Assert.Equal("App.DeclaredPrimary", declaredRel.To.SymbolId?.FullName ?? declaredRel.To.Name);
        Assert.Contains(declaredRel.Occurrences, o =>
            o.Rule == EvidenceRule.ServiceRegistrationBinding &&
            o.ResolutionSemantics == ResolutionSemantics.DeclaredBinding &&
            o.Strength == EvidenceStrength.Proven);

        var confirmedRel = Assert.Single(resolves, r =>
            (r.From.SymbolId?.FullName ?? r.From.Name) == "App.IConfirmedService");
        Assert.Equal("App.ConfirmedImpl", confirmedRel.To.SymbolId?.FullName ?? confirmedRel.To.Name);
        Assert.Contains(confirmedRel.Occurrences, o =>
            o.ResolutionSemantics == ResolutionSemantics.Confirmed &&
            o.Strength == EvidenceStrength.Proven);

        Assert.DoesNotContain(resolves, r =>
            (r.From.SymbolId?.Name ?? r.From.Name) == "IAmbiguous");
        Assert.Contains(result.Spine.Unproven.AmbiguousResolutions, a =>
            a.InterfaceName.EndsWith("IAmbiguous", StringComparison.Ordinal));
    }

    [Fact]
    public void Wraps_EnforcesClosedOpenGenericAndUnprovenRules_F0Section43()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteProject("App", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        fixture.WriteFile("App", "Contracts.cs", """
            using System.Threading;
            using System.Threading.Tasks;
            namespace MediatR
            {
                public interface IRequest<TResponse> { }
                public interface IRequestHandler<TRequest, TResponse> { Task<TResponse> Handle(TRequest r, CancellationToken ct); }
                public interface IPipelineBehavior<TRequest, TResponse> { }
            }
            namespace App
            {
                using MediatR;
                public interface ICommandMarker { }
                public sealed record TaggedCommand(string Id) : IRequest<string>, ICommandMarker;
                public sealed record PlainQuery(string Id) : IRequest<string>;
                public sealed class TaggedHandler : IRequestHandler<TaggedCommand, string>
                {
                    public Task<string> Handle(TaggedCommand r, CancellationToken ct) => Task.FromResult("");
                }
                public sealed class PlainHandler : IRequestHandler<PlainQuery, string>
                {
                    public Task<string> Handle(PlainQuery r, CancellationToken ct) => Task.FromResult("");
                }

                public sealed class ClosedTaggedBehavior : IPipelineBehavior<TaggedCommand, string> { }
                public sealed class ConstrainedOpenBehavior<TReq, TRes> : IPipelineBehavior<TReq, TRes>
                    where TReq : ICommandMarker { }
                public sealed class UnregisteredBehavior<TReq, TRes> : IPipelineBehavior<TReq, TRes> { }

                public static class Setup
                {
                    public static void Configure(object services)
                    {
                        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ConstrainedOpenBehavior<,>));
                    }
                }
            }
            """);

        var result = Analyzer.Analyze(fixture.RootPath, AnalysisMode.Roslyn, generatedAt: FixedTimestamp);
        var wraps = result.Spine.GetRelations(RelationKind.Wraps);

        // Closed binding targets ONLY TaggedCommand.
        var closedWraps = wraps.Where(r => r.From.SymbolId?.Name == "ClosedTaggedBehavior").ToList();
        var closedTarget = Assert.Single(closedWraps);
        Assert.Equal("TaggedCommand", closedTarget.To.SymbolId?.Name);
        Assert.Contains(closedTarget.Occurrences, o => o.Rule == EvidenceRule.BehaviorClosedBinding);

        // Constrained open-generic registration wraps ONLY TaggedCommand (satisfies ICommandMarker), not PlainQuery.
        var openWraps = wraps.Where(r => r.From.SymbolId?.Name == "ConstrainedOpenBehavior").ToList();
        var openTarget = Assert.Single(openWraps);
        Assert.Equal("TaggedCommand", openTarget.To.SymbolId?.Name);
        Assert.Contains(openTarget.Occurrences, o => o.Rule == EvidenceRule.BehaviorOpenGenericRegistration);

        // Unregistered behavior emits no Wraps edges and records NoRegistrationSite in Unproven.
        Assert.DoesNotContain(wraps, r => r.From.SymbolId?.Name == "UnregisteredBehavior");
        var unproven = Assert.Single(
            result.Spine.Unproven.UnestablishedBehaviors,
            b => b.BehaviorName.EndsWith("UnregisteredBehavior", StringComparison.Ordinal));
        Assert.Equal(UnestablishedBehaviorReason.NoRegistrationSite, unproven.Reason);
    }

    [Fact]
    public void LayerProvenance_TracksConfiguredOverridesIndependentlyFromGuessed()
    {
        using var fixture = new TempProjectFixture();
        fixture.WriteProject("CoreLogic", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        fixture.WriteProject("Sample.Domain", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        fixture.WriteFile("CoreLogic", "Marker.cs", "namespace CoreLogic; public sealed class Marker { }");
        fixture.WriteFile("Sample.Domain", "Entity.cs", "namespace Sample.Domain; public sealed class Entity { }");

        var unconfigured = Analyzer.Analyze(fixture.RootPath, AnalysisMode.Text, generatedAt: FixedTimestamp);
        Assert.All(unconfigured.Spine.Projects, p =>
        {
            Assert.Equal(LayerProvenance.Guessed, p.LayerProvenance);
            Assert.NotEqual(EvidenceStrength.Proven, p.LayerStrength);
        });

        var options = new AnalysisOptions
        {
            GeneratedAt = FixedTimestamp,
            LayerOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CoreLogic"] = "Application",
            },
        };
        var configured = Analyzer.Analyze(fixture.RootPath, AnalysisMode.Text, options);

        var overridden = Assert.Single(configured.Spine.Projects, p => p.Name == "CoreLogic");
        Assert.Equal("Application", overridden.Layer);
        Assert.Equal(LayerProvenance.Configured, overridden.LayerProvenance);
        Assert.Equal(EvidenceStrength.Proven, overridden.LayerStrength);

        var stillGuessed = Assert.Single(configured.Spine.Projects, p => p.Name == "Sample.Domain");
        Assert.Equal(LayerProvenance.Guessed, stillGuessed.LayerProvenance);
        Assert.NotEqual(EvidenceStrength.Proven, stillGuessed.LayerStrength);
    }

    [Fact]
    public void JsonReport_EmitsAdditiveTopLevelRelations_AndOmitsWhenEmpty()
    {
        var emptyResult = new AnalysisResult { RootPath = RepoRoot, GeneratedAt = FixedTimestamp };
        var emptyJson = JsonReportGenerator.Generate(emptyResult);
        var emptyObj = JsonNode.Parse(emptyJson)!.AsObject();
        Assert.False(emptyObj.ContainsKey("relations"));

        var goldenPath = Path.Combine(RepoRoot, "tests", "fixtures", "GoldenCorpus");
        var golden = Analyzer.Analyze(goldenPath, AnalysisMode.Text, generatedAt: FixedTimestamp);
        var json = JsonReportGenerator.Generate(golden);
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.True(root.ContainsKey("relations"));
        var relationsArray = root["relations"]!.AsArray();
        Assert.Equal(golden.Spine.Relations.Count, relationsArray.Count);

        var first = relationsArray[0]!.AsObject();
        Assert.False(string.IsNullOrEmpty(first["kind"]?.GetValue<string>()));
        Assert.NotNull(first["from"]);
        Assert.NotNull(first["to"]);
        Assert.True(first["occurrenceCount"]!.GetValue<int>() >= 1);
        var firstOcc = first["occurrences"]!.AsArray()[0]!.AsObject();
        Assert.False(string.IsNullOrEmpty(firstOcc["rule"]?.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(firstOcc["origin"]?.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(firstOcc["strength"]?.GetValue<string>()));

        // Round-trip reader accepts F1 JSON while schemaVersion remains 1.
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, json);
            var doc = JsonReportGenerator.ReadDocument(tempFile);
            Assert.Equal(1, doc.SchemaVersion);
            Assert.Equal(golden.Spine.Relations.Count, doc.Relations!.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void BidirectionalParity_MutationsThroughLegacyListsUpdateSpineAndProjectBack()
    {
        var result = new AnalysisResult { RootPath = "/repo", GeneratedAt = FixedTimestamp };
        var domain = new ProjectInfo { Name = "Domain", FilePath = "/repo/src/Domain/Domain.csproj", DirectoryPath = "/repo/src/Domain", Layer = "Domain" };
        var app = new ProjectInfo { Name = "App", FilePath = "/repo/src/App/App.csproj", DirectoryPath = "/repo/src/App", Layer = "Application" };
        app.ReferenceNames.Add("Domain");
        result.Projects.Add(domain);
        result.Projects.Add(app);

        var req = new CqrsRequest { Name = "DoCmd", Namespace = "App", Kind = "Command", ProjectName = "App" };
        var handler = new HandlerInfo { Name = "DoHandler", Namespace = "App", RequestName = "DoCmd", ProjectName = "App" };
        req.Handlers.Add("App.DoHandler");
        req.Validators.Add("App.DoValidator");
        handler.TouchedEntities.Add("Orders");
        result.Requests.Add(req);
        result.Handlers.Add(handler);

        result.FlowEdges.Add(new FlowEdge
        {
            Kind = FlowEdgeKind.EndpointToRequest,
            From = "PostDo",
            To = "App.DoCmd",
            ProjectName = "App",
        });
        result.ResolvedEdges.Add(new ResolvedEdge
        {
            Kind = FlowEdgeKind.HandlerToDbContextResolved,
            From = "App.DoHandler",
            Via = "App.IUnitOfWork",
            To = "App.AppDbContext",
            ProjectName = "App",
        });
        result.ServiceCallEdges.Add(new ServiceCallEdge
        {
            From = "App.DoHandler",
            Via = "App.IPayment",
            Method = "ChargeAsync",
            To = "App.StripePayment",
            ProjectName = "App",
        });
        result.DispatchCallSites.Add(new DispatchCallSite
        {
            TargetFullName = "App.DoCmd",
            TargetKind = DispatchTargetKind.Request,
            SenderFullName = "App.Caller",
            ProjectName = "App",
        });
        result.UnresolvedDispatches.Add(new UnresolvedDispatch
        {
            Method = "Send",
            ReceiverName = "_mediator",
            ProjectName = "App",
        });

        Assert.Contains(result.Spine.Relations, r => r.Kind == RelationKind.References);
        Assert.Contains(result.Spine.Relations, r => r.Kind == RelationKind.Serves);
        Assert.Contains(result.Spine.Relations, r => r.Kind == RelationKind.Validates);
        Assert.Contains(result.Spine.Relations, r => r.Kind == RelationKind.TouchesData);
        Assert.Contains(result.Spine.Relations, r => r.Kind == RelationKind.UsesDataResolved);
        Assert.Contains(result.Spine.Relations, r => r.Kind == RelationKind.Calls);
        Assert.Contains(result.Spine.Relations, r => r.Kind == RelationKind.Dispatches);
        Assert.Single(result.Spine.Unproven.UnresolvedDispatches);

        // Removing via legacy collection mutates underlying Spine.
        result.ResolvedEdges.Clear();
        Assert.DoesNotContain(result.Spine.Relations, r => r.Kind == RelationKind.UsesDataResolved);
    }

    private static void AssertEvidenceValid(EvidenceOccurrence occ)
    {
        Assert.True(Enum.IsDefined(occ.Rule), $"Undefined EvidenceRule: {occ.Rule}");
        Assert.True(
            occ.Location is not null || !string.IsNullOrWhiteSpace(occ.LocationOmittedReason),
            $"Evidence occurrence {occ.Rule} has neither Location nor LocationOmittedReason.");
    }

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CqrsLens.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not find CqrsLens.sln above test directory.");
    }

    private sealed class TempProjectFixture : IDisposable
    {
        public string RootPath { get; } = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-f1-test-" + Guid.NewGuid().ToString("N")[..8]);

        public TempProjectFixture() => Directory.CreateDirectory(RootPath);

        public void WriteProject(string projectName, string csprojContent)
        {
            var dir = Path.Combine(RootPath, projectName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, projectName + ".csproj"), csprojContent);
        }

        public void WriteFile(string projectName, string fileName, string content)
        {
            var dir = Path.Combine(RootPath, projectName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, fileName), content);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
