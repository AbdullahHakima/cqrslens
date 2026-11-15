using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Parity and edge-case tests covering the F1 performance optimizations:
/// enum token lookup parity, direct streaming JSON serialization parity,
/// batch project registration, directory-trie lookup, and indexed spine queries.
/// </summary>
public sealed class SpinePerfParityTests
{
    private static readonly string RepoRoot = FindRepositoryRoot();
    private static readonly DateTimeOffset FixedTimestamp = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions ReferenceJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TheoryData<string, AnalysisMode> FixtureAndModeCases() => new()
    {
        { Path.Combine("samples", "SampleShop"), AnalysisMode.Text },
        { Path.Combine("samples", "SampleShop"), AnalysisMode.Roslyn },
        { Path.Combine("tests", "fixtures", "GoldenCorpus"), AnalysisMode.Text },
        { Path.Combine("tests", "fixtures", "Adversarial"), AnalysisMode.Text },
        { Path.Combine("tests", "fixtures", "DiffPair", "base"), AnalysisMode.Text },
        { Path.Combine("tests", "fixtures", "DiffPair", "head"), AnalysisMode.Text },
        { Path.Combine("tests", "fixtures", "StrictFailure"), AnalysisMode.Text },
    };

    [Fact]
    public void JsonSpineNames_MatchesCamelCaseNamingPolicyForEveryEnumValue()
    {
        AssertEnumTokens<RelationKind>(JsonSpineNames.RelationKindName, JsonSpineNames.RelationKindEncoded);
        AssertEnumTokens<NodeReferenceKind>(JsonSpineNames.NodeReferenceKindName, JsonSpineNames.NodeReferenceKindEncoded);
        AssertEnumTokens<EvidenceStrength>(JsonSpineNames.EvidenceStrengthName, JsonSpineNames.EvidenceStrengthEncoded);
        AssertEnumTokens<EvidenceOrigin>(JsonSpineNames.EvidenceOriginName, JsonSpineNames.EvidenceOriginEncoded);
        AssertEnumTokens<ResolutionSemantics>(JsonSpineNames.ResolutionSemanticsName, JsonSpineNames.ResolutionSemanticsEncoded);
        AssertEnumTokens<EvidenceRule>(JsonSpineNames.EvidenceRuleName, JsonSpineNames.EvidenceRuleEncoded);
    }

    [Theory]
    [MemberData(nameof(FixtureAndModeCases))]
    public void JsonReportGenerator_Generate_IsByteIdenticalToDtoSerialization(string relativePath, AnalysisMode mode)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath);
        var result = Analyzer.Analyze(fullPath, mode, generatedAt: FixedTimestamp);

        var directJson = JsonReportGenerator.Generate(result);
        var referenceDoc = JsonReportGenerator.CreateDocument(result);
        var referenceJson = JsonSerializer.Serialize(referenceDoc, ReferenceJsonOptions) + "\n";

        Assert.Equal(referenceJson, directJson);
    }

    [Fact]
    public void JsonReportGenerator_DirectConverter_MatchesDtoSerializationWithAllOptionalFieldsAndSpecialChars()
    {
        var result = new AnalysisResult
        {
            RootPath = "/repo",
            GeneratedAt = FixedTimestamp,
        };

        var appProj = ProjectId.FromRelativePath("src/Shop.App/Shop.App.csproj");
        var apiProj = ProjectId.FromRelativePath("src/Shop.Api/Shop.Api.csproj");
        var appSym = SymbolId.Create(
            appProj,
            "Shop.App",
            new[] { "Outer<T>" },
            "My&Service",
            genericArity: 1,
            typeArguments: new[] { "Order+Item" });
        var epId = EndpointId.Create(apiProj, new SourceLocation("src/Shop.Api/Routes.cs", 14, 9), ordinal: 2);
        var compId = ComponentId.FromSymbol(appSym);

        var fromEndpoint = NodeReference.ForEndpoint(epId);
        var toMethod = NodeReference.ForMethod(appSym, "Execute<T>");
        var viaInterface = NodeReference.ForInterface(SymbolId.Create(appProj, "Shop.App", null, "IMyService"));
        var viaExternal = NodeReference.ForExternalMember("Ext.Type+Sub", "Prop");
        var viaComponent = NodeReference.ForComponent(compId);
        var toUnresolved = NodeReference.ForUnresolved("Missing<Foo>", "Ambiguous & <escaped> \"quote\" cafe\u0301");

        var occWithLocation = new EvidenceOccurrence(
            EvidenceRule.ServiceConfirmedBinding,
            EvidenceOrigin.CallSite,
            new SourceLocation("src/Shop.Api/Controller.cs", 42, 13),
            EvidenceStrength.Resolved,
            via: new[] { viaInterface, viaExternal },
            resolutionSemantics: ResolutionSemantics.Confirmed,
            scopeProjectName: "Shop.Api",
            isTestProject: true);

        var occOmittedLocation = new EvidenceOccurrence(
            EvidenceRule.ServiceRegistrationBinding,
            EvidenceOrigin.Configuration,
            location: null,
            EvidenceStrength.Inferred,
            via: new[] { viaComponent },
            locationOmittedReason: "Synthesized <binding> & \"fallback\"",
            resolutionSemantics: ResolutionSemantics.DeclaredBinding,
            scopeProjectName: null,
            isTestProject: false);

        result.Spine.AddOrMergeRelation(
            RelationKind.Calls,
            fromEndpoint,
            toMethod,
            occWithLocation,
            method: "Execute<T>&Log");
        result.Spine.AddOrMergeRelation(
            RelationKind.Relates,
            NodeReference.ForProject(apiProj),
            toUnresolved,
            occOmittedLocation,
            method: null);

        var directJson = JsonReportGenerator.Generate(result);
        var referenceJson = JsonSerializer.Serialize(JsonReportGenerator.CreateDocument(result), ReferenceJsonOptions) + "\n";

        Assert.Equal(referenceJson, directJson);

        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, directJson);
            var roundTripped = JsonReportGenerator.ReadDocument(tempFile);
            Assert.NotNull(roundTripped.Relations);
            Assert.Equal(2, roundTripped.Relations!.Count);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void ProjectsAddRange_BatchRegistration_MatchesSequentialAdditionAndEliminatesSyntheticChurn()
    {
        var projects = new List<ProjectInfo>
        {
            CreateProject("App", "/repo/src/App/App.csproj", "/repo/src/App", "Application", "Domain", "Shared"),
            CreateProject("Api", "/repo/src/Api/Api.csproj", "/repo/src/Api", "Presentation", "App"),
            CreateProject("Domain", "/repo/src/Domain/Domain.csproj", "/repo/src/Domain", "Domain"),
            CreateProject("Shared", "/repo/src/Common/Shared/Shared.csproj", "/repo/src/Common/Shared", "Domain"),
            CreateProject("Duplicate", "/repo/src/ModA/Duplicate/Duplicate.csproj", "/repo/src/ModA/Duplicate", "Application"),
            CreateProject("Duplicate", "/repo/src/ModB/Duplicate/Duplicate.csproj", "/repo/src/ModB/Duplicate", "Application"),
        };

        var sequential = new AnalysisResult { RootPath = "/repo", GeneratedAt = FixedTimestamp };
        foreach (var p in projects)
            sequential.Projects.Add(CloneProject(p));

        var batched = new AnalysisResult { RootPath = "/repo", GeneratedAt = FixedTimestamp };
        batched.Projects.AddRange(projects.Select(CloneProject));

        Assert.False(batched.Spine.HasAnySyntheticProjects);
        Assert.Equal(sequential.Projects.Select(p => p.FilePath), batched.Projects.Select(p => p.FilePath));
        Assert.Equal(
            sequential.Spine.Projects.Select(p => (p.Id, p.Name, p.Layer, p.FilePath, p.DirectoryPath)),
            batched.Spine.Projects.Select(p => (p.Id, p.Name, p.Layer, p.FilePath, p.DirectoryPath)));
        Assert.Equal(
            sequential.Spine.Relations.Select(r => (r.Key, r.OccurrenceCount, r.Strength)),
            batched.Spine.Relations.Select(r => (r.Key, r.OccurrenceCount, r.Strength)));

        // Adding empty range is a no-op and incremental Add afterward still works.
        batched.Projects.AddRange(Array.Empty<ProjectInfo>());
        batched.Projects.Add(CreateProject("Workers", "/repo/src/Workers/Workers.csproj", "/repo/src/Workers", "Infrastructure", "App"));
        Assert.Equal(7, batched.Spine.Projects.Count);
        Assert.Contains(batched.Spine.Relations, r => r.Kind == RelationKind.References && r.From.ProjectId?.Value == "src/Workers/Workers.csproj");
    }

    [Fact]
    public void ProjectDirTrie_ResolveProjectForLocation_HandlesRootNestedDeepManySiblingsAndBackslashes()
    {
        var result = new AnalysisResult { RootPath = "/repo", GeneratedAt = FixedTimestamp };

        // Register 10+ sibling directories out of alphabetical order (triggers sorted binary search in trie)
        // plus nested project and boundary prefix projects (Foo vs FooBar).
        var projects = new List<ProjectInfo>
        {
            CreateProject("RootApp", "/repo/RootApp.csproj", "/repo", "Presentation"),
            CreateProject("Zeta", "/repo/src/Zeta/Zeta.csproj", "/repo/src/Zeta", "Domain"),
            CreateProject("Alpha", "/repo/src/Alpha/Alpha.csproj", "/repo/src/Alpha", "Domain"),
            CreateProject("Foo", "/repo/src/Foo/Foo.csproj", "/repo/src/Foo", "Domain"),
            CreateProject("FooBar", "/repo/src/FooBar/FooBar.csproj", "/repo/src/FooBar", "Domain"),
            CreateProject("Mid01", "/repo/src/Mid01/Mid01.csproj", "/repo/src/Mid01", "Domain"),
            CreateProject("Mid02", "/repo/src/Mid02/Mid02.csproj", "/repo/src/Mid02", "Domain"),
            CreateProject("Mid03", "/repo/src/Mid03/Mid03.csproj", "/repo/src/Mid03", "Domain"),
            CreateProject("Mid04", "/repo/src/Mid04/Mid04.csproj", "/repo/src/Mid04", "Domain"),
            CreateProject("Mid05", "/repo/src/Mid05/Mid05.csproj", "/repo/src/Mid05", "Domain"),
            CreateProject("Mid06", "/repo/src/Mid06/Mid06.csproj", "/repo/src/Mid06", "Domain"),
            CreateProject("NestedTests", "/repo/src/Foo/Sub/NestedTests/NestedTests.csproj", "/repo/src/Foo/Sub/NestedTests", "Tests"),
        };
        result.Projects.AddRange(projects);

        var resolver = result.IdentityResolver;

        Assert.Null(resolver.ResolveProjectForLocation(null));

        Assert.Equal(
            "src/Alpha/Alpha.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation("src/Alpha/Services/Order.cs", 1, 1))?.Value);
        Assert.Equal(
            "src/Mid04/Mid04.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation(@"SRC\mid04\Handlers\Create.cs", 5, 1))?.Value);
        Assert.Equal(
            "src/FooBar/FooBar.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation("src/FooBar/Entity.cs", 1, 1))?.Value);
        Assert.Equal(
            "RootApp.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation("src/FooX/Entity.cs", 1, 1))?.Value);
        Assert.Equal(
            "src/Foo/Sub/NestedTests/NestedTests.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation("src/Foo/Sub/NestedTests/UnitTest1.cs", 1, 1))?.Value);
        Assert.Equal(
            "src/Foo/Foo.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation("src/Foo/Sub/Helper.cs", 1, 1))?.Value);
        Assert.Equal(
            "RootApp.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation("Program.cs", 1, 1))?.Value);
        Assert.Equal(
            "RootApp.csproj",
            resolver.ResolveProjectForLocation(new SourceLocation("other/Unmatched.cs", 1, 1))?.Value);
    }

    [Fact]
    public void ArchitectureSpine_IndexedQueriesAndDiscoveryOrder_StayConsistentAcrossRemovalsAndRehoming()
    {
        var spine = new ArchitectureSpine();
        var domainId = ProjectId.FromRelativePath("src/Domain/Domain.csproj");
        var appId = ProjectId.FromRelativePath("src/App/App.csproj");
        var apiId = ProjectId.FromRelativePath("src/Api/Api.csproj");
        var synthId = SpineIdentityResolver.SyntheticProjectId("MissingDomain");

        var occ = new EvidenceOccurrence(
            EvidenceRule.ProjectReference,
            EvidenceOrigin.ProjectFile,
            location: null,
            EvidenceStrength.Proven,
            locationOmittedReason: "test");

        var apiRef = NodeReference.ForProject(apiId);
        var appRef = NodeReference.ForProject(appId);
        var domainRef = NodeReference.ForProject(domainId);
        var synthRef = NodeReference.ForProject(synthId);

        var r1 = spine.AddOrMergeRelation(RelationKind.References, apiRef, appRef, occ);
        var r2 = spine.AddOrMergeRelation(RelationKind.References, appRef, synthRef, occ);
        var r3 = spine.AddOrMergeRelation(RelationKind.Relates, apiRef, domainRef, occ);

        Assert.True(spine.HasRelation(RelationKind.References));
        Assert.True(spine.HasRelation(RelationKind.Relates));
        Assert.False(spine.HasRelation(RelationKind.Wraps));

        Assert.True(spine.HasOutgoingRelation(apiRef, RelationKind.References));
        Assert.False(spine.HasOutgoingRelation(domainRef, RelationKind.References));
        Assert.Equal(1, spine.CountOutgoingRelations(apiRef, RelationKind.References));
        Assert.True(spine.HasIncomingRelation(appRef, RelationKind.References));
        Assert.Single(spine.GetIncomingRelationsInDiscoveryOrder(appRef, RelationKind.References));
        Assert.Equal(3, spine.RelationsInDiscoveryOrder.Count);

        // Rehome synthetic -> domain updates relation endpoint and keeps discovery/index queries consistent.
        spine.AddProject(new ProjectNode(domainId, "Domain", "/repo/src/Domain/Domain.csproj", "/repo/src/Domain", "Domain", LayerProvenance.Guessed));
        spine.RehomeSyntheticProject(synthId, domainId);

        Assert.Empty(spine.GetIncomingRelationsInDiscoveryOrder(synthRef, RelationKind.References));
        Assert.Single(spine.GetIncomingRelationsInDiscoveryOrder(domainRef, RelationKind.References));
        Assert.True(spine.HasIncomingRelation(domainRef, RelationKind.References));

        // Removing r1 and r3 leaves only App -> Domain References relation.
        Assert.True(spine.RemoveRelation(r1.Key));
        Assert.Equal(1, spine.RemoveRelations(rel => rel.Kind == RelationKind.Relates && rel.Key.Equals(r3.Key)));

        Assert.False(spine.HasRelation(RelationKind.Relates));
        Assert.False(spine.HasOutgoingRelation(apiRef, RelationKind.References));
        Assert.Equal(0, spine.CountOutgoingRelations(apiRef, RelationKind.References));
        Assert.False(spine.HasIncomingRelation(appRef, RelationKind.References));

        var remaining = Assert.Single(spine.RelationsInDiscoveryOrder);
        Assert.Equal(appRef, remaining.From);
        Assert.Equal(domainRef, remaining.To);
        Assert.Equal(spine.Relations.Count, spine.RelationsInDiscoveryOrder.Count);
        Assert.Equal(r2.OccurrenceCount, remaining.OccurrenceCount);

        var collected = new List<SpineRelation>();
        spine.CollectOutgoingRelationsUnordered(appRef, RelationKind.References, collected);
        Assert.Single(collected);
    }

    private static void AssertEnumTokens<TEnum>(Func<TEnum, string> getName, Func<TEnum, JsonEncodedText> getEncoded)
        where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var expected = JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
            Assert.Equal(expected, getName(value));
            Assert.Equal(expected, getEncoded(value).ToString());
        }
    }

    private static ProjectInfo CreateProject(string name, string filePath, string dirPath, string layer, params string[] references)
    {
        var p = new ProjectInfo
        {
            Name = name,
            FilePath = filePath,
            DirectoryPath = dirPath,
            Layer = layer,
        };
        foreach (var r in references)
            p.ReferenceNames.Add(r);
        return p;
    }

    private static ProjectInfo CloneProject(ProjectInfo source) =>
        CreateProject(source.Name, source.FilePath, source.DirectoryPath, source.Layer, source.ReferenceNames.ToArray());

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CqrsLens.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException("Could not find CqrsLens.sln above test directory.");
    }
}
