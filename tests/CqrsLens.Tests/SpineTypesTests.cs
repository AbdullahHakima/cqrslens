using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// CP1 unit tests for the F0 spine types: layered identities, role sets, typed references,
/// relation deduplication keys, structured multi-occurrence evidence, per-fact provenance,
/// and unproven populations.
/// </summary>
public sealed class SpineTypesTests
{
    private static readonly ProjectId AppProject = ProjectId.FromRelativePath("src/Shop.Application/Shop.Application.csproj");
    private static readonly ProjectId OtherProject = ProjectId.FromRelativePath("src/Billing.Application/Billing.Application.csproj");
    private static readonly ProjectId ApiProject = ProjectId.FromRelativePath("src/Shop.Api/Shop.Api.csproj");
    private static readonly ProjectId InfraProject = ProjectId.FromRelativePath("src/Shop.Infrastructure/Shop.Infrastructure.csproj");

    [Fact]
    public void ProjectId_NormalizesSeparatorsAndRelativePath()
    {
        var fromWin = ProjectId.FromRelativePath(@".\src\Shop.Api\Shop.Api.csproj");
        var fromUnix = ProjectId.FromRelativePath("src/Shop.Api/Shop.Api.csproj");
        var fromFile = ProjectId.FromProjectFile(
            "/repo/src/Shop.Api/Shop.Api.csproj",
            "/repo");

        Assert.Equal("src/Shop.Api/Shop.Api.csproj", fromWin.Value);
        Assert.Equal(fromUnix, fromWin);
        Assert.Equal(fromFile, fromWin);
    }

    [Fact]
    public void SymbolId_DistinguishesProjectNamespaceContainingTypesAndGenerics()
    {
        var baseSymbol = SymbolId.Create(AppProject, "Shop.Orders", Array.Empty<string>(), "CreateOrder");
        var sameNameDiffProject = SymbolId.Create(OtherProject, "Shop.Orders", Array.Empty<string>(), "CreateOrder");
        var sameNameDiffNs = SymbolId.Create(AppProject, "Shop.Catalog", Array.Empty<string>(), "CreateOrder");
        var nestedSymbol = SymbolId.Create(AppProject, "Shop.Orders", new[] { "CreateOrder" }, "Handler");
        var topLevelSameName = SymbolId.Create(AppProject, "Shop.Orders", Array.Empty<string>(), "Handler");
        var openGeneric = SymbolId.Create(AppProject, "Shop.Behaviors", Array.Empty<string>(), "LoggingBehavior", genericArity: 2);
        var closedGenericOrder = SymbolId.Create(
            AppProject,
            "Shop.Behaviors",
            Array.Empty<string>(),
            "LoggingBehavior",
            genericArity: 2,
            typeArguments: new[] { "CreateOrder", "Guid" });
        var closedGenericCancel = SymbolId.Create(
            AppProject,
            "Shop.Behaviors",
            Array.Empty<string>(),
            "LoggingBehavior",
            genericArity: 2,
            typeArguments: new[] { "CancelOrder", "Unit" });

        Assert.Equal("Shop.Orders.CreateOrder", baseSymbol.QualifiedName);
        Assert.Equal("Shop.Orders.CreateOrder.Handler", nestedSymbol.QualifiedName);
        Assert.Equal("Shop.Behaviors.LoggingBehavior`2", openGeneric.QualifiedName);
        Assert.Equal("Shop.Behaviors.LoggingBehavior`2<CreateOrder, Guid>", closedGenericOrder.QualifiedName);

        Assert.True(openGeneric.IsOpenGeneric);
        Assert.False(openGeneric.IsClosedGeneric);
        Assert.True(closedGenericOrder.IsClosedGeneric);
        Assert.False(closedGenericOrder.IsOpenGeneric);

        var all = new[]
        {
            baseSymbol,
            sameNameDiffProject,
            sameNameDiffNs,
            nestedSymbol,
            topLevelSameName,
            openGeneric,
            closedGenericOrder,
            closedGenericCancel,
        };

        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void SymbolId_RejectsMismatchedGenericArityAndArguments()
    {
        Assert.Throws<ArgumentException>(() => SymbolId.Create(
            AppProject,
            "Shop",
            null,
            "Behavior",
            genericArity: 2,
            typeArguments: new[] { "OnlyOne" }));
    }

    [Fact]
    public void EndpointId_IsMappingSiteStable_AcrossRouteVerbChangesLambdasAndMultiMappings()
    {
        var lambdaSite = new SourceLocation("src/Shop.Api/Program.cs", 24, 5);
        var endpointDeclared = EndpointId.Create(ApiProject, lambdaSite, ordinal: 0);
        var endpointSameSiteDifferentOrdinal = EndpointId.Create(ApiProject, lambdaSite, ordinal: 1);
        var endpointSameSiteWinSeparators = EndpointId.Create(
            ApiProject,
            new SourceLocation(@"src\Shop.Api\Program.cs", 24, 5),
            ordinal: 0);

        // Route and verb are properties on EndpointNode, never part of EndpointId.
        var nodeWithDeclaredRoute = new EndpointNode(
            endpointDeclared,
            name: "MapPost",
            httpMethod: "POST",
            route: "/api/orders",
            verbStrength: EvidenceStrength.Proven,
            routeStrength: EvidenceStrength.Proven,
            evidence: new EvidenceOccurrence(
                EvidenceRule.EndpointMapping,
                EvidenceOrigin.MappingOrAttribute,
                lambdaSite,
                EvidenceStrength.Proven));

        var nodeWithInferredRoute = new EndpointNode(
            endpointSameSiteWinSeparators,
            name: "MapPost",
            httpMethod: "POST",
            route: "/api/OrdersGroup",
            verbStrength: EvidenceStrength.Proven,
            routeStrength: EvidenceStrength.Inferred,
            evidence: new EvidenceOccurrence(
                EvidenceRule.EndpointMapping,
                EvidenceOrigin.MappingOrAttribute,
                lambdaSite,
                EvidenceStrength.Inferred));

        Assert.Equal(nodeWithDeclaredRoute.Id, nodeWithInferredRoute.Id);
        Assert.NotEqual(endpointDeclared, endpointSameSiteDifferentOrdinal);
        Assert.False(nodeWithDeclaredRoute.IsInferred);
        Assert.True(nodeWithInferredRoute.IsInferred);
        Assert.Equal(EvidenceStrength.Proven, nodeWithInferredRoute.VerbStrength);
        Assert.Equal(EvidenceStrength.Inferred, nodeWithInferredRoute.RouteStrength);
    }

    [Fact]
    public void ComponentNode_MergesMultipleRolesOnOneSymbolWithoutDuplicatingNode()
    {
        var spine = new ArchitectureSpine();
        var symbol = SymbolId.Create(AppProject, "Shop.Application", null, "OrderProcessor");
        var componentId = ComponentId.FromSymbol(symbol);
        var loc = new SourceLocation("src/Shop.Application/OrderProcessor.cs", 10, 14);

        var handlerEvidence1 = new EvidenceOccurrence(
            EvidenceRule.HandlerMarker,
            EvidenceOrigin.Declaration,
            loc,
            EvidenceStrength.Proven);
        var handlerEvidence2 = new EvidenceOccurrence(
            EvidenceRule.HandlerMarker,
            EvidenceOrigin.Declaration,
            new SourceLocation("src/Shop.Application/OrderProcessor.cs", 11, 14),
            EvidenceStrength.Proven);
        var notifHandlerEvidence = new EvidenceOccurrence(
            EvidenceRule.NotificationHandlerMarker,
            EvidenceOrigin.Declaration,
            loc,
            EvidenceStrength.Proven);

        spine.AddOrMergeComponent(componentId, loc, ComponentRole.Handler, handlerEvidence1);
        spine.AddOrMergeComponent(componentId, loc, ComponentRole.Handler, handlerEvidence2);
        spine.AddOrMergeComponent(componentId, loc, ComponentRole.NotificationHandler, notifHandlerEvidence);

        var component = Assert.Single(spine.Components);
        Assert.Equal(componentId, component.Id);
        Assert.True(component.HasRole(ComponentRole.Handler));
        Assert.True(component.HasRole(ComponentRole.NotificationHandler));
        Assert.False(component.HasRole(ComponentRole.Validator));
        Assert.Equal(2, component.Roles.Count);
        Assert.Equal(2, component.GetRoleEvidence(ComponentRole.Handler).Count);
        Assert.Single(component.GetRoleEvidence(ComponentRole.NotificationHandler));
        Assert.Single(spine.GetComponentsByRole(ComponentRole.Handler));
        Assert.Single(spine.GetComponentsByRole(ComponentRole.NotificationHandler));
    }

    [Fact]
    public void ComponentRoleEvidence_RequiresAtLeastOneOccurrence()
    {
        Assert.Throws<ArgumentException>(() =>
            new ComponentRoleEvidence(ComponentRole.Handler, Array.Empty<EvidenceOccurrence>()));
    }

    [Fact]
    public void RelationDedupKey_SameKeyMergesOccurrences_DifferentViaOrderCreatesDistinctRelations()
    {
        var spine = new ArchitectureSpine();
        var handlerSymbol = SymbolId.Create(AppProject, "Shop.Application", null, "CreateOrderHandler");
        var repoInterface = SymbolId.Create(AppProject, "Shop.Domain", null, "IOrderRepository");
        var repoClass = SymbolId.Create(InfraProject, "Shop.Infrastructure", null, "OrderRepository");
        var entitySymbol = SymbolId.Create(AppProject, "Shop.Domain", null, "Order");

        var from = NodeReference.ForComponent(ComponentId.FromSymbol(handlerSymbol));
        var to = NodeReference.ForMember(entitySymbol, "Orders");
        var viaInterface = NodeReference.ForInterface(repoInterface);
        var viaRepo = NodeReference.ForType(repoClass);

        var site1 = new SourceLocation("src/Shop.Application/CreateOrderHandler.cs", 25, 9);
        var site2 = new SourceLocation("src/Shop.Application/CreateOrderHandler.cs", 42, 9);

        // Two observations with the same (Kind, From, To, ordered Via, Method) merge into 1 relation with 2 occurrences.
        var occ1 = new EvidenceOccurrence(
            EvidenceRule.DeepSliceDataAccess,
            EvidenceOrigin.CallSite,
            site1,
            EvidenceStrength.Resolved,
            via: new[] { viaInterface, viaRepo });
        var occ2 = new EvidenceOccurrence(
            EvidenceRule.DeepSliceDataAccess,
            EvidenceOrigin.CallSite,
            site2,
            EvidenceStrength.Resolved,
            via: new[] { viaInterface, viaRepo });

        // Different Via order => distinct logical relation (F0 §5.1 v2.1).
        var occReversedVia = new EvidenceOccurrence(
            EvidenceRule.DeepSliceDataAccess,
            EvidenceOrigin.CallSite,
            site1,
            EvidenceStrength.Resolved,
            via: new[] { viaRepo, viaInterface });

        spine.AddOrMergeRelation(RelationKind.TouchesData, from, to, occ1);
        spine.AddOrMergeRelation(RelationKind.TouchesData, from, to, occ2);
        spine.AddOrMergeRelation(RelationKind.TouchesData, from, to, occReversedVia);

        Assert.Equal(2, spine.Relations.Count);

        var forwardRelation = spine.Relations.Single(r => r.Via[0].Equals(viaInterface));
        var reversedRelation = spine.Relations.Single(r => r.Via[0].Equals(viaRepo));

        Assert.Equal(2, forwardRelation.OccurrenceCount);
        Assert.Equal(new[] { site1, site2 }, forwardRelation.Occurrences.Select(o => o.Location).ToArray());
        Assert.Equal(1, reversedRelation.OccurrenceCount);
    }

    [Fact]
    public void RelationDedupKey_DistinguishesMethodOnCalls()
    {
        var spine = new ArchitectureSpine();
        var handlerSymbol = SymbolId.Create(AppProject, "Shop.Application", null, "CreateOrderHandler");
        var repoInterface = SymbolId.Create(AppProject, "Shop.Domain", null, "IOrderRepository");
        var repoClass = SymbolId.Create(InfraProject, "Shop.Infrastructure", null, "OrderRepository");

        var from = NodeReference.ForComponent(ComponentId.FromSymbol(handlerSymbol));
        var via = new[] { NodeReference.ForInterface(repoInterface) };
        var toAdd = NodeReference.ForMethod(repoClass, "AddAsync");
        var toSave = NodeReference.ForMethod(repoClass, "SaveAsync");

        var site1 = new SourceLocation("src/Shop.Application/CreateOrderHandler.cs", 30, 9);
        var site2 = new SourceLocation("src/Shop.Application/CreateOrderHandler.cs", 35, 9);
        var site3 = new SourceLocation("src/Shop.Application/CreateOrderHandler.cs", 38, 9);

        spine.AddOrMergeRelation(
            RelationKind.Calls,
            from,
            toAdd,
            new EvidenceOccurrence(EvidenceRule.InjectedMemberInvocation, EvidenceOrigin.CallSite, site1, EvidenceStrength.Resolved, via),
            method: "AddAsync");
        spine.AddOrMergeRelation(
            RelationKind.Calls,
            from,
            toAdd,
            new EvidenceOccurrence(EvidenceRule.InjectedMemberInvocation, EvidenceOrigin.CallSite, site2, EvidenceStrength.Resolved, via),
            method: "AddAsync");
        spine.AddOrMergeRelation(
            RelationKind.Calls,
            from,
            toSave,
            new EvidenceOccurrence(EvidenceRule.InjectedMemberInvocation, EvidenceOrigin.CallSite, site3, EvidenceStrength.Resolved, via),
            method: "SaveAsync");

        Assert.Equal(2, spine.Relations.Count);
        var addRel = spine.Relations.Single(r => r.Method == "AddAsync");
        var saveRel = spine.Relations.Single(r => r.Method == "SaveAsync");
        Assert.Equal(2, addRel.OccurrenceCount);
        Assert.Equal(1, saveRel.OccurrenceCount);
    }

    [Fact]
    public void EvidenceOccurrence_RequiresLocationOrExplicitOmittedReason()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceOccurrence(
            EvidenceRule.ProjectReference,
            EvidenceOrigin.ProjectFile,
            location: null,
            EvidenceStrength.Proven,
            locationOmittedReason: null));

        var valid = new EvidenceOccurrence(
            EvidenceRule.ProjectReference,
            EvidenceOrigin.ProjectFile,
            location: null,
            EvidenceStrength.Proven,
            locationOmittedReason: "Project reference recorded in .csproj without line offset");
        Assert.Null(valid.Location);
        Assert.NotNull(valid.LocationOmittedReason);
    }

    [Fact]
    public void SpineRelation_RejectsOccurrenceWithMismatchedVia()
    {
        var handlerSymbol = SymbolId.Create(AppProject, "Shop.Application", null, "CreateOrderHandler");
        var contextSymbol = SymbolId.Create(InfraProject, "Shop.Infrastructure", null, "ShopDbContext");
        var ifaceA = NodeReference.ForInterface(SymbolId.Create(AppProject, "Shop.Application", null, "IShopDbContext"));
        var ifaceB = NodeReference.ForInterface(SymbolId.Create(AppProject, "Shop.Application", null, "IOtherDbContext"));
        var loc = new SourceLocation("src/Shop.Infrastructure/ShopDbContext.cs", 5, 1);

        var relation = new SpineRelation(
            RelationKind.UsesDataResolved,
            NodeReference.ForComponent(ComponentId.FromSymbol(handlerSymbol)),
            NodeReference.ForComponent(ComponentId.FromSymbol(contextSymbol)),
            new EvidenceOccurrence(EvidenceRule.ResolvedDbContextDependency, EvidenceOrigin.Declaration, loc, EvidenceStrength.Resolved, new[] { ifaceA }));

        Assert.Throws<ArgumentException>(() => relation.AddOccurrence(
            new EvidenceOccurrence(EvidenceRule.ResolvedDbContextDependency, EvidenceOrigin.Declaration, loc, EvidenceStrength.Resolved, new[] { ifaceB })));
    }

    [Fact]
    public void ProjectNode_And_UnprovenPopulations_TrackProvenanceAndNonEdgeFactsSeparately()
    {
        var spine = new ArchitectureSpine();
        var configuredProject = new ProjectNode(
            AppProject,
            "Shop.Application",
            "/repo/src/Shop.Application/Shop.Application.csproj",
            "/repo/src/Shop.Application",
            "Application",
            LayerProvenance.Configured,
            new[] { "MediatR" });
        var guessedProject = new ProjectNode(
            InfraProject,
            "Shop.Infrastructure",
            "/repo/src/Shop.Infrastructure/Shop.Infrastructure.csproj",
            "/repo/src/Shop.Infrastructure",
            "Infrastructure",
            LayerProvenance.Guessed);

        spine.AddProject(configuredProject);
        spine.AddProject(guessedProject);

        Assert.Equal(EvidenceStrength.Proven, configuredProject.LayerStrength);
        Assert.Equal(EvidenceStrength.Inferred, guessedProject.LayerStrength);
        Assert.Equal(new[] { "Application", "Infrastructure" }, spine.Layers);

        // PackageReference alone never enters Relations or External references (F0 §3.2 v2.1).
        Assert.Empty(spine.Relations);

        // Unproven items live exclusively in UnprovenPopulations, never in Relations.
        spine.Unproven.AddUnresolvedDispatch(new UnresolvedDispatch
        {
            Method = "Send",
            ReceiverName = "_mediator",
            ProjectName = "Shop.Api",
            Location = new SourceLocation("src/Shop.Api/OrdersController.cs", 39, 9),
        });
        spine.Unproven.AddAmbiguousResolution(new AmbiguousResolutionEntry(
            "Shop.Domain.IService",
            "Shop.Domain.IService",
            new[]
            {
                new ServiceCandidate { Type = "Shop.Infrastructure.ServiceA", DeclaredInScan = true },
                new ServiceCandidate { Type = "Shop.Infrastructure.ServiceB", DeclaredInScan = true },
            }));
        spine.Unproven.AddUnestablishedBehavior(new UnestablishedBehaviorEntry(
            ComponentId.FromSymbol(SymbolId.Create(AppProject, "Shop.Application", null, "LoggingBehavior", 2)),
            "LoggingBehavior",
            UnestablishedBehaviorReason.NoRegistrationSite,
            ProjectName: "Shop.Application"));

        Assert.Empty(spine.Relations);
        Assert.Equal(3, spine.Unproven.TotalCount);
    }

    [Fact]
    public void ArchitectureSpine_OrderingIsDeterministicRegardlessOfInsertionOrder()
    {
        var locA = new SourceLocation("src/Shop.Application/A.cs", 1, 1);
        var locB = new SourceLocation("src/Shop.Application/B.cs", 1, 1);
        var compA = ComponentId.FromSymbol(SymbolId.Create(AppProject, "Shop", null, "AlphaRequest"));
        var compB = ComponentId.FromSymbol(SymbolId.Create(AppProject, "Shop", null, "BetaHandler"));
        var occ = new EvidenceOccurrence(EvidenceRule.RequestMarker, EvidenceOrigin.Declaration, locA, EvidenceStrength.Proven);

        var spine1 = new ArchitectureSpine();
        spine1.AddOrMergeComponent(compA, locA, ComponentRole.Request, occ);
        spine1.AddOrMergeComponent(compB, locB, ComponentRole.Handler, occ);
        spine1.AddOrMergeRelation(RelationKind.Serves, NodeReference.ForComponent(compA), NodeReference.ForComponent(compB), occ);

        var spine2 = new ArchitectureSpine();
        spine2.AddOrMergeComponent(compB, locB, ComponentRole.Handler, occ);
        spine2.AddOrMergeComponent(compA, locA, ComponentRole.Request, occ);
        spine2.AddOrMergeRelation(RelationKind.Serves, NodeReference.ForComponent(compA), NodeReference.ForComponent(compB), occ);

        Assert.Equal(spine1.Components.Select(c => c.Id), spine2.Components.Select(c => c.Id));
        Assert.Equal(spine1.Relations.Select(r => r.Key), spine2.Relations.Select(r => r.Key));
    }
}
