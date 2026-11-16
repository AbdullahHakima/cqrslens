using System.Runtime.CompilerServices;
using System.Text.Json;

namespace CqrsLens;

/// <summary>Projects the analysis model onto the versioned JSON report contract.</summary>
internal static class JsonReportDocumentFactory
{
    internal static void WriteDirect(Utf8JsonWriter writer, AnalysisResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var flowEdges = SpineQueries.FlowEdges(result);
        var resolvedEdges = SpineQueries.ResolvedEdges(result);
        var serviceCallEdges = SpineQueries.ServiceCallEdges(result.Spine);
        var dispatchCallSites = SpineQueries.DispatchCallSites(result.Spine);
        var unresolvedDispatches = result.Spine.Unproven.UnresolvedDispatches;

        var rootName = new DirectoryInfo(result.RootPath).Name;
        var errorCount = 0;
        var activeCount = 0;
        var knownCount = 0;
        var checks = result.Checks;
        for (var i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            if (result.BaselineApplied && c.IsKnown)
            {
                knownCount++;
                continue;
            }
            activeCount++;
            if (c.Severity == CheckSeverity.Error)
                errorCount++;
        }
        var warningCount = activeCount - errorCount;

        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion"u8, JsonReportGenerator.CurrentSchemaVersion);
        writer.WriteString("toolVersion"u8, ToolInfo.Version);
        writer.WriteString("scanner"u8, result.ScannerName);
        writer.WriteString("generatedAt"u8, result.GeneratedAt.UtcDateTime.ToString("O"));
        writer.WriteNumber("elapsedMilliseconds"u8, result.ElapsedMilliseconds);
        writer.WriteString("rootName"u8, rootName);
        writer.WriteNumber("discoveredFileCount"u8, result.DiscoveredFileCount);
        writer.WriteNumber("fileCount"u8, result.FileCount);

        if (result.Watermark is not null)
            writer.WriteString("watermark"u8, result.Watermark);

        if (result.Conventions.Count > 0)
            WriteStringArray(writer, "conventions"u8, result.Conventions);

        writer.WriteBoolean("baselineApplied"u8, result.BaselineApplied);
        writer.WriteBoolean("canRunStrictChecks"u8, result.CanRunStrictChecks);

        writer.WriteStartObject("summary"u8);
        writer.WriteNumber("projects"u8, result.Projects.Count);
        writer.WriteNumber("requests"u8, result.Requests.Count);
        writer.WriteNumber("handlers"u8, result.Handlers.Count);
        writer.WriteNumber("validators"u8, result.Validators.Count);
        writer.WriteNumber("behaviors"u8, result.Behaviors.Count);
        writer.WriteNumber("dbContexts"u8, result.DbContexts.Count);
        writer.WriteNumber("notifications"u8, result.Notifications.Count);
        writer.WriteNumber("notificationHandlers"u8, result.NotificationHandlers.Count);
        writer.WriteNumber("endpoints"u8, result.Endpoints.Count);
        writer.WriteNumber("errors"u8, errorCount);
        writer.WriteNumber("warnings"u8, warningCount);
        writer.WriteNumber("knownFindings"u8, knownCount);
        writer.WriteNumber("diagnostics"u8, result.Diagnostics.Count);
        writer.WriteNumber("flowEdges"u8, flowEdges.Count);
        writer.WriteNumber("dispatchCallSites"u8, dispatchCallSites.Count);
        writer.WriteNumber("unresolvedDispatches"u8, unresolvedDispatches.Count);
        writer.WriteEndObject();

        WriteProjects(writer, result);
        WriteRequests(writer, result.Requests);
        WriteHandlers(writer, result.Handlers);
        WriteValidators(writer, result.Validators);
        WriteBehaviors(writer, result.Behaviors);
        WriteDbContexts(writer, result.DbContexts);
        WriteChecks(writer, result.Checks);
        WriteDiagnostics(writer, result.Diagnostics);
        WriteStringArray(writer, "skippedFiles"u8, result.SkippedFiles);
        WriteNotifications(writer, result.Notifications);
        WriteNotificationHandlers(writer, result.NotificationHandlers);

        if (result.Endpoints.Count > 0)
            WriteEndpoints(writer, result.Endpoints);
        if (flowEdges.Count > 0)
            WriteFlowEdges(writer, flowEdges);
        if (resolvedEdges.Count > 0)
            WriteResolvedEdges(writer, resolvedEdges);
        if (serviceCallEdges.Count > 0)
            WriteServiceCallEdges(writer, serviceCallEdges);
        if (dispatchCallSites.Count > 0)
            WriteDispatchCallSites(writer, dispatchCallSites);
        if (unresolvedDispatches.Count > 0)
        {
            writer.WritePropertyName("unresolvedDispatches"u8);
            WriteUnresolvedDispatches(writer, unresolvedDispatches);
        }
        if (result.Spine.UnindexedRelations.Count > 0)
        {
            writer.WritePropertyName("relations"u8);
            DirectSpineRelationsConverter.WriteSpineRelations(writer, result.Spine.Relations);
        }
        if (result.Spine.ComponentsCount > 0)
            WriteComponents(writer, result.Spine.Components);
        if (result.Spine.EndpointsCount > 0)
            WriteSpineEndpoints(writer, result.Spine.Endpoints);
        if (result.Spine.Unproven.TotalCount > 0)
            WriteUnproven(writer, result.Spine.Unproven);

        writer.WriteEndObject();
    }

    private static void WriteProjects(Utf8JsonWriter writer, AnalysisResult result)
    {
        writer.WriteStartArray("projects"u8);
        var projects = result.Projects;
        if (projects.Count == 1)
        {
            WriteProject(writer, result, projects[0]);
        }
        else if (projects.Count > 1)
        {
            WriteProjectsSorted(writer, result, projects);
        }
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteProjectsSorted(
        Utf8JsonWriter writer,
        AnalysisResult result,
        IReadOnlyList<ProjectInfo> projects)
    {
        var items = new (ProjectInfo Item, int Index)[projects.Count];
        for (var i = 0; i < projects.Count; i++)
            items[i] = (projects[i], i);

        Array.Sort(items, static (a, b) =>
        {
            var cmp = StringComparer.OrdinalIgnoreCase.Compare(a.Item.Name, b.Item.Name);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Item.FilePath, b.Item.FilePath);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < items.Length; i++)
            WriteProject(writer, result, items[i].Item);
    }

    private static void WriteProject(Utf8JsonWriter writer, AnalysisResult result, ProjectInfo p)
    {
        writer.WriteStartObject();
        writer.WriteString("name"u8, p.Name);
        writer.WriteString("layer"u8, p.Layer);
        WriteSortedStringArray(writer, "references"u8, SpineQueries.ProjectReferences(result, p));
        WriteSortedStringArray(writer, "packages"u8, p.PackageReferences);
        writer.WriteEndObject();
    }

    private static void WriteSortedStringArray(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        IReadOnlyList<string> values)
    {
        writer.WriteStartArray(propertyName);
        if (values.Count == 1)
        {
            writer.WriteStringValue(values[0]);
        }
        else if (values.Count > 1)
        {
            var copy = new (string Value, int Index)[values.Count];
            for (var i = 0; i < values.Count; i++)
                copy[i] = (values[i], i);

            Array.Sort(copy, static (a, b) =>
            {
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(a.Value, b.Value);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Value, b.Value);
                return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
            });

            for (var i = 0; i < copy.Length; i++)
                writer.WriteStringValue(copy[i].Value);
        }
        writer.WriteEndArray();
    }

    private static void WriteRequests(Utf8JsonWriter writer, IReadOnlyList<CqrsRequest> requests)
    {
        writer.WriteStartArray("requests"u8);
        if (requests.Count > 0)
            WriteRequestsActive(writer, requests);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteRequestsActive(Utf8JsonWriter writer, IReadOnlyList<CqrsRequest> requests)
    {
        for (var i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            writer.WriteStartObject();
            writer.WriteString("name"u8, r.Name);
            if (!string.IsNullOrEmpty(r.Namespace))
                writer.WriteString("namespace"u8, r.Namespace);
            writer.WriteString("fullName"u8, r.FullName);
            writer.WriteString("kind"u8, r.Kind);
            if (r.ResponseType is not null)
                writer.WriteString("responseType"u8, r.ResponseType);
            if (r.ViaMarker is not null)
                writer.WriteString("viaMarker"u8, r.ViaMarker);
            if (r.ProjectName is not null)
                writer.WriteString("project"u8, r.ProjectName);
            WriteStringArray(writer, "handlers"u8, r.Handlers);
            WriteStringArray(writer, "validators"u8, r.Validators);
            if (r.Properties.Count > 0)
            {
                writer.WriteStartArray("properties"u8);
                for (var p = 0; p < r.Properties.Count; p++)
                {
                    var prop = r.Properties[p];
                    writer.WriteStartObject();
                    writer.WriteString("name"u8, prop.Name);
                    writer.WriteString("type"u8, prop.Type);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            if (r.Entrypoints.Count > 0)
                WriteStringArray(writer, "entrypoints"u8, r.Entrypoints);
            if (r.TouchedEntities.Count > 0)
                WriteStringArray(writer, "touchedEntities"u8, r.TouchedEntities);
            if (r.PublishedNotifications.Count > 0)
                WriteStringArray(writer, "publishedNotifications"u8, r.PublishedNotifications);
            if (r.DispatchedRequests.Count > 0)
                WriteStringArray(writer, "dispatchedRequests"u8, r.DispatchedRequests);
            WriteLocation(writer, "location"u8, r.Location);
            writer.WriteEndObject();
        }
    }

    private static void WriteHandlers(Utf8JsonWriter writer, IReadOnlyList<HandlerInfo> handlers)
    {
        writer.WriteStartArray("handlers"u8);
        if (handlers.Count == 1)
            WriteHandler(writer, handlers[0]);
        else if (handlers.Count > 1)
            WriteHandlersSorted(writer, handlers);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteHandlersSorted(Utf8JsonWriter writer, IReadOnlyList<HandlerInfo> handlers)
    {
        var items = new (HandlerInfo Item, int Index)[handlers.Count];
        for (var i = 0; i < handlers.Count; i++)
            items[i] = (handlers[i], i);

        Array.Sort(items, static (a, b) =>
        {
            var cmp = string.CompareOrdinal(a.Item.FullName, b.Item.FullName);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Item.RequestName, b.Item.RequestName);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < items.Length; i++)
            WriteHandler(writer, items[i].Item);
    }

    private static void WriteHandler(Utf8JsonWriter writer, HandlerInfo h)
    {
        writer.WriteStartObject();
        writer.WriteString("name"u8, h.Name);
        if (!string.IsNullOrEmpty(h.Namespace))
            writer.WriteString("namespace"u8, h.Namespace);
        writer.WriteString("fullName"u8, h.FullName);
        writer.WriteString("requestName"u8, h.RequestName);
        if (h.Dependencies.Count > 0)
            WriteStringArray(writer, "dependencies"u8, h.Dependencies);
        if (h.TouchedEntities.Count > 0)
            WriteStringArray(writer, "touchedEntities"u8, h.TouchedEntities);
        if (h.PublishedNotifications.Count > 0)
            WriteStringArray(writer, "publishedNotifications"u8, h.PublishedNotifications);
        if (h.DispatchedRequests.Count > 0)
            WriteStringArray(writer, "dispatchedRequests"u8, h.DispatchedRequests);
        if (h.ProjectName is not null)
            writer.WriteString("project"u8, h.ProjectName);
        WriteLocation(writer, "location"u8, h.Location);
        writer.WriteEndObject();
    }

    private static void WriteValidators(Utf8JsonWriter writer, IReadOnlyList<ValidatorInfo> validators)
    {
        writer.WriteStartArray("validators"u8);
        if (validators.Count > 0)
            WriteValidatorsActive(writer, validators);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteValidatorsActive(Utf8JsonWriter writer, IReadOnlyList<ValidatorInfo> validators)
    {
        if (validators.Count == 1)
        {
            WriteValidator(writer, validators[0]);
            return;
        }

        var items = new (ValidatorInfo Item, int Index)[validators.Count];
        for (var i = 0; i < validators.Count; i++)
            items[i] = (validators[i], i);

        Array.Sort(items, static (a, b) =>
        {
            var cmp = string.CompareOrdinal(a.Item.FullName, b.Item.FullName);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Item.TargetType, b.Item.TargetType);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < items.Length; i++)
            WriteValidator(writer, items[i].Item);
    }

    private static void WriteValidator(Utf8JsonWriter writer, ValidatorInfo v)
    {
        writer.WriteStartObject();
        writer.WriteString("name"u8, v.Name);
        if (!string.IsNullOrEmpty(v.Namespace))
            writer.WriteString("namespace"u8, v.Namespace);
        writer.WriteString("fullName"u8, v.FullName);
        writer.WriteString("targetType"u8, v.TargetType);
        if (v.ProjectName is not null)
            writer.WriteString("project"u8, v.ProjectName);
        WriteLocation(writer, "location"u8, v.Location);
        writer.WriteEndObject();
    }

    private static void WriteBehaviors(Utf8JsonWriter writer, IReadOnlyList<BehaviorInfo> behaviors)
    {
        writer.WriteStartArray("behaviors"u8);
        if (behaviors.Count > 0)
            WriteBehaviorsActive(writer, behaviors);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteBehaviorsActive(Utf8JsonWriter writer, IReadOnlyList<BehaviorInfo> behaviors)
    {
        if (behaviors.Count == 1)
        {
            WriteBehavior(writer, behaviors[0]);
            return;
        }

        var items = new (BehaviorInfo Item, int Index)[behaviors.Count];
        for (var i = 0; i < behaviors.Count; i++)
            items[i] = (behaviors[i], i);

        Array.Sort(items, static (a, b) =>
        {
            var cmp = StringComparer.OrdinalIgnoreCase.Compare(a.Item.Name, b.Item.Name);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Item.ProjectName, b.Item.ProjectName);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < items.Length; i++)
            WriteBehavior(writer, items[i].Item);
    }

    private static void WriteBehavior(Utf8JsonWriter writer, BehaviorInfo b)
    {
        writer.WriteStartObject();
        writer.WriteString("name"u8, b.Name);
        if (b.ProjectName is not null)
            writer.WriteString("project"u8, b.ProjectName);
        WriteLocation(writer, "location"u8, b.Location);
        writer.WriteEndObject();
    }

    private static void WriteDbContexts(Utf8JsonWriter writer, IReadOnlyList<DbContextInfo> contexts)
    {
        writer.WriteStartArray("dbContexts"u8);
        if (contexts.Count > 0)
            WriteDbContextsActive(writer, contexts);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteDbContextsActive(Utf8JsonWriter writer, IReadOnlyList<DbContextInfo> contexts)
    {
        if (contexts.Count == 1)
        {
            WriteDbContext(writer, contexts[0]);
            return;
        }

        var items = new (DbContextInfo Item, int Index)[contexts.Count];
        for (var i = 0; i < contexts.Count; i++)
            items[i] = (contexts[i], i);

        Array.Sort(items, static (a, b) =>
        {
            var cmp = StringComparer.OrdinalIgnoreCase.Compare(a.Item.Name, b.Item.Name);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Item.Namespace, b.Item.Namespace);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < items.Length; i++)
            WriteDbContext(writer, items[i].Item);
    }

    private static void WriteDbContext(Utf8JsonWriter writer, DbContextInfo c)
    {
        writer.WriteStartObject();
        writer.WriteString("name"u8, c.Name);
        if (!string.IsNullOrEmpty(c.Namespace))
            writer.WriteString("namespace"u8, c.Namespace);
        if (c.ProjectName is not null)
            writer.WriteString("project"u8, c.ProjectName);
        WriteLocation(writer, "location"u8, c.Location);

        writer.WriteStartArray("dbSets"u8);
        for (var i = 0; i < c.DbSets.Count; i++)
        {
            var s = c.DbSets[i];
            writer.WriteStartObject();
            writer.WriteString("property"u8, s.Property);
            writer.WriteString("entity"u8, s.Entity);
            if (s.EntityFullName is not null)
                writer.WriteString("entityFullName"u8, s.EntityFullName);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteStartArray("entities"u8);
        if (c.Entities.Count == 1)
        {
            WriteEntity(writer, c.Entities[0]);
        }
        else if (c.Entities.Count > 1)
        {
            var entities = new (EntityInfo Item, int Index)[c.Entities.Count];
            for (var i = 0; i < c.Entities.Count; i++)
                entities[i] = (c.Entities[i], i);

            Array.Sort(entities, static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.Item.FullName, b.Item.FullName);
                return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
            });

            for (var i = 0; i < entities.Length; i++)
                WriteEntity(writer, entities[i].Item);
        }
        writer.WriteEndArray();

        writer.WriteStartArray("relationships"u8);
        if (c.Relationships.Count == 1)
        {
            WriteRelationship(writer, c.Relationships[0]);
        }
        else if (c.Relationships.Count > 1)
        {
            var rels = new (EntityRelationshipInfo Item, int Index)[c.Relationships.Count];
            for (var i = 0; i < c.Relationships.Count; i++)
                rels[i] = (c.Relationships[i], i);

            Array.Sort(rels, static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.Item.PrincipalEntity, b.Item.PrincipalEntity);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Item.DependentEntity, b.Item.DependentEntity);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Item.PrincipalNavigation, b.Item.PrincipalNavigation);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Item.DependentNavigation, b.Item.DependentNavigation);
                return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
            });

            for (var i = 0; i < rels.Length; i++)
                WriteRelationship(writer, rels[i].Item);
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteEntity(Utf8JsonWriter writer, EntityInfo e)
    {
        writer.WriteStartObject();
        writer.WriteString("name"u8, e.Name);
        if (!string.IsNullOrEmpty(e.Namespace))
            writer.WriteString("namespace"u8, e.Namespace);
        writer.WriteString("fullName"u8, e.FullName);
        if (e.ProjectName is not null)
            writer.WriteString("project"u8, e.ProjectName);
        WriteLocation(writer, "location"u8, e.Location);

        writer.WriteStartArray("properties"u8);
        for (var i = 0; i < e.Properties.Count; i++)
        {
            var p = e.Properties[i];
            writer.WriteStartObject();
            writer.WriteString("name"u8, p.Name);
            writer.WriteString("type"u8, p.Type);
            writer.WriteBoolean("isKey"u8, p.IsKey);
            writer.WriteBoolean("isForeignKey"u8, p.IsForeignKey);
            writer.WriteBoolean("isNavigation"u8, p.IsNavigation);
            writer.WriteBoolean("isCollection"u8, p.IsCollection);
            if (p.TargetEntity is not null)
                writer.WriteString("targetEntity"u8, p.TargetEntity);
            writer.WriteBoolean("isRequired"u8, p.IsRequired);
            WriteLocation(writer, "location"u8, p.Location);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
    }

    private static void WriteRelationship(Utf8JsonWriter writer, EntityRelationshipInfo r)
    {
        writer.WriteStartObject();
        writer.WriteString("principalEntity"u8, r.PrincipalEntity);
        writer.WriteString("dependentEntity"u8, r.DependentEntity);
        if (r.PrincipalNavigation is not null)
            writer.WriteString("principalNavigation"u8, r.PrincipalNavigation);
        if (r.DependentNavigation is not null)
            writer.WriteString("dependentNavigation"u8, r.DependentNavigation);
        if (r.ForeignKey is not null)
            writer.WriteString("foreignKey"u8, r.ForeignKey);
        writer.WriteBoolean("isRequired"u8, r.IsRequired);
        writer.WriteString("kind"u8, r.Kind switch
        {
            EntityRelationshipKind.OneToOne => "oneToOne"u8,
            EntityRelationshipKind.ManyToMany => "manyToMany"u8,
            _ => "oneToMany"u8,
        });
        if (r.ConfiguredBy is not null)
            writer.WriteString("configuredBy"u8, r.ConfiguredBy);
        writer.WriteEndObject();
    }

    private static void WriteChecks(Utf8JsonWriter writer, IReadOnlyList<CheckFinding> checks)
    {
        writer.WriteStartArray("checks"u8);
        if (checks.Count > 0)
            WriteChecksActive(writer, checks);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteChecksActive(Utf8JsonWriter writer, IReadOnlyList<CheckFinding> checks)
    {
        for (var i = 0; i < checks.Count; i++)
        {
            var c = checks[i];
            writer.WriteStartObject();
            writer.WriteString("code"u8, c.Code);
            writer.WriteString("severity"u8, c.Severity == CheckSeverity.Error ? "error"u8 : "warning"u8);
            writer.WriteString("message"u8, c.Message);
            if (c.Target is not null)
                writer.WriteString("target"u8, c.Target);
            if (c.ProjectName is not null)
                writer.WriteString("project"u8, c.ProjectName);
            writer.WriteBoolean("known"u8, c.IsKnown);
            WriteLocation(writer, "location"u8, c.Location);
            writer.WriteEndObject();
        }
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, IReadOnlyList<AnalysisDiagnostic> diagnostics)
    {
        writer.WriteStartArray("diagnostics"u8);
        if (diagnostics.Count > 0)
            WriteDiagnosticsActive(writer, diagnostics);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteDiagnosticsActive(Utf8JsonWriter writer, IReadOnlyList<AnalysisDiagnostic> diagnostics)
    {
        for (var i = 0; i < diagnostics.Count; i++)
        {
            var d = diagnostics[i];
            writer.WriteStartObject();
            writer.WriteString("code"u8, d.Code);
            writer.WriteString("severity"u8, d.Severity switch
            {
                AnalysisDiagnosticSeverity.Error => "error"u8,
                AnalysisDiagnosticSeverity.Warning => "warning"u8,
                _ => "info"u8,
            });
            writer.WriteString("message"u8, d.Message);
            if (d.ProjectName is not null)
                writer.WriteString("project"u8, d.ProjectName);
            WriteLocation(writer, "location"u8, d.Location);
            writer.WriteEndObject();
        }
    }

    private static void WriteNotifications(Utf8JsonWriter writer, IReadOnlyList<CqrsNotification> notifications)
    {
        writer.WriteStartArray("notifications"u8);
        if (notifications.Count > 0)
            WriteNotificationsActive(writer, notifications);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteNotificationsActive(Utf8JsonWriter writer, IReadOnlyList<CqrsNotification> notifications)
    {
        for (var i = 0; i < notifications.Count; i++)
        {
            var n = notifications[i];
            writer.WriteStartObject();
            writer.WriteString("name"u8, n.Name);
            if (!string.IsNullOrEmpty(n.Namespace))
                writer.WriteString("namespace"u8, n.Namespace);
            writer.WriteString("fullName"u8, n.FullName);
            if (n.ViaMarker is not null)
                writer.WriteString("viaMarker"u8, n.ViaMarker);
            if (n.ProjectName is not null)
                writer.WriteString("project"u8, n.ProjectName);
            WriteStringArray(writer, "handlers"u8, n.Handlers);
            WriteLocation(writer, "location"u8, n.Location);
            writer.WriteEndObject();
        }
    }

    private static void WriteNotificationHandlers(Utf8JsonWriter writer, IReadOnlyList<NotificationHandlerInfo> handlers)
    {
        writer.WriteStartArray("notificationHandlers"u8);
        if (handlers.Count > 0)
            WriteNotificationHandlersActive(writer, handlers);
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteNotificationHandlersActive(Utf8JsonWriter writer, IReadOnlyList<NotificationHandlerInfo> handlers)
    {
        if (handlers.Count == 1)
        {
            WriteNotificationHandler(writer, handlers[0]);
            return;
        }

        var items = new (NotificationHandlerInfo Item, int Index)[handlers.Count];
        for (var i = 0; i < handlers.Count; i++)
            items[i] = (handlers[i], i);

        Array.Sort(items, static (a, b) =>
        {
            var cmp = string.CompareOrdinal(a.Item.FullName, b.Item.FullName);
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a.Item.NotificationName, b.Item.NotificationName);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        for (var i = 0; i < items.Length; i++)
            WriteNotificationHandler(writer, items[i].Item);
    }

    private static void WriteNotificationHandler(Utf8JsonWriter writer, NotificationHandlerInfo h)
    {
        writer.WriteStartObject();
        writer.WriteString("name"u8, h.Name);
        if (!string.IsNullOrEmpty(h.Namespace))
            writer.WriteString("namespace"u8, h.Namespace);
        writer.WriteString("fullName"u8, h.FullName);
        writer.WriteString("notificationName"u8, h.NotificationName);
        if (h.Dependencies.Count > 0)
            WriteStringArray(writer, "dependencies"u8, h.Dependencies);
        if (h.TouchedEntities.Count > 0)
            WriteStringArray(writer, "touchedEntities"u8, h.TouchedEntities);
        if (h.PublishedNotifications.Count > 0)
            WriteStringArray(writer, "publishedNotifications"u8, h.PublishedNotifications);
        if (h.DispatchedRequests.Count > 0)
            WriteStringArray(writer, "dispatchedRequests"u8, h.DispatchedRequests);
        if (h.ProjectName is not null)
            writer.WriteString("project"u8, h.ProjectName);
        WriteLocation(writer, "location"u8, h.Location);
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteEndpoints(Utf8JsonWriter writer, IReadOnlyList<EndpointInfo> endpoints)
    {
        writer.WriteStartArray("endpoints"u8);
        for (var i = 0; i < endpoints.Count; i++)
        {
            var e = endpoints[i];
            writer.WriteStartObject();
            writer.WriteString("name"u8, e.Name);
            writer.WriteString("httpMethod"u8, e.HttpMethod);
            writer.WriteString("route"u8, e.Route);
            writer.WriteString("targetRequest"u8, e.TargetRequestName);
            if (e.TargetRequestFullName is not null)
                writer.WriteString("targetRequestFullName"u8, e.TargetRequestFullName);
            writer.WriteBoolean("inferred"u8, e.IsInferred);
            if (e.ProjectName is not null)
                writer.WriteString("project"u8, e.ProjectName);
            WriteLocation(writer, "location"u8, e.Location);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteFlowEdges(Utf8JsonWriter writer, IReadOnlyList<FlowEdge> flowEdges)
    {
        writer.WriteStartArray("flowEdges"u8);
        for (var i = 0; i < flowEdges.Count; i++)
        {
            var e = flowEdges[i];
            writer.WriteStartObject();
            writer.WriteString("kind"u8, e.Kind switch
            {
                FlowEdgeKind.EndpointToRequest => "endpointToRequest"u8,
                FlowEdgeKind.RequestToHandler => "requestToHandler"u8,
                FlowEdgeKind.HandlerToRequest => "handlerToRequest"u8,
                FlowEdgeKind.HandlerToNotification => "handlerToNotification"u8,
                FlowEdgeKind.HandlerToDbContext => "handlerToDbContext"u8,
                _ => "unknown"u8,
            });
            writer.WriteString("from"u8, e.From);
            writer.WriteString("to"u8, e.To);
            if (e.ProjectName is not null)
                writer.WriteString("project"u8, e.ProjectName);
            WriteLocation(writer, "location"u8, e.Location);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteResolvedEdges(Utf8JsonWriter writer, IReadOnlyList<ResolvedEdge> resolvedEdges)
    {
        writer.WriteStartArray("resolvedEdges"u8);
        for (var i = 0; i < resolvedEdges.Count; i++)
        {
            var e = resolvedEdges[i];
            writer.WriteStartObject();
            writer.WriteString("kind"u8, e.Kind == FlowEdgeKind.HandlerToDbContextResolved
                ? "handlerToDbContextResolved"u8
                : "unknown"u8);
            writer.WriteString("from"u8, e.From);
            writer.WriteString("via"u8, e.Via);
            writer.WriteString("to"u8, e.To);
            if (e.ProjectName is not null)
                writer.WriteString("project"u8, e.ProjectName);
            WriteLocation(writer, "location"u8, e.Location);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteServiceCallEdges(Utf8JsonWriter writer, IReadOnlyList<ServiceCallEdge> serviceCallEdges)
    {
        writer.WriteStartArray("serviceCallEdges"u8);
        for (var i = 0; i < serviceCallEdges.Count; i++)
        {
            var e = serviceCallEdges[i];
            writer.WriteStartObject();
            writer.WriteString("kind"u8, "serviceCall"u8);
            writer.WriteString("from"u8, e.From);
            writer.WriteString("via"u8, e.Via);
            writer.WriteString("method"u8, e.Method);
            writer.WriteString("to"u8, e.To);
            if (e.ProjectName is not null)
                writer.WriteString("project"u8, e.ProjectName);
            WriteLocation(writer, "location"u8, e.Location);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteDispatchCallSites(Utf8JsonWriter writer, IReadOnlyList<DispatchCallSite> dispatchCallSites)
    {
        writer.WriteStartArray("dispatchCallSites"u8);
        for (var i = 0; i < dispatchCallSites.Count; i++)
        {
            var s = dispatchCallSites[i];
            writer.WriteStartObject();
            writer.WriteString("target"u8, s.TargetFullName);
            writer.WriteString("targetKind"u8, s.TargetKind == DispatchTargetKind.Request
                ? "request"u8
                : "notification"u8);
            if (s.SenderFullName is not null)
                writer.WriteString("sender"u8, s.SenderFullName);
            if (s.ProjectName is not null)
                writer.WriteString("project"u8, s.ProjectName);
            writer.WriteBoolean("testProject"u8, s.IsTestProject);
            WriteLocation(writer, "location"u8, s.Location);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteUnresolvedDispatches(Utf8JsonWriter writer, IReadOnlyList<UnresolvedDispatch> unresolvedDispatches)
    {
        writer.WriteStartArray();
        for (var i = 0; i < unresolvedDispatches.Count; i++)
        {
            var u = unresolvedDispatches[i];
            writer.WriteStartObject();
            writer.WriteString("method"u8, u.Method);
            if (u.ReceiverName is not null)
                writer.WriteString("receiver"u8, u.ReceiverName);
            if (u.ProjectName is not null)
                writer.WriteString("project"u8, u.ProjectName);
            WriteLocation(writer, "location"u8, u.Location);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteComponents(Utf8JsonWriter writer, IReadOnlyList<ComponentNode> components)
    {
        writer.WriteStartArray("components"u8);
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            writer.WriteStartObject();
            writer.WriteString("id"u8, component.Id.Value);
            writer.WriteString("fullName"u8, component.FullName);
            writer.WriteString("name"u8, component.Name);
            if (!string.IsNullOrEmpty(component.Project.Value))
                writer.WriteString("project"u8, component.Project.Value);
            WriteLocation(writer, "location"u8, component.Location);

            writer.WriteStartArray("roles"u8);
            var sourceRoles = component.Roles;
            if (sourceRoles.Count == 1)
            {
                WriteRoleEvidence(writer, sourceRoles[0]);
            }
            else if (sourceRoles.Count > 1)
            {
                var sortedRoles = new ComponentRoleEvidence[sourceRoles.Count];
                for (var r = 0; r < sourceRoles.Count; r++)
                    sortedRoles[r] = sourceRoles[r];
                Array.Sort(sortedRoles, static (a, b) =>
                    string.CompareOrdinal(JsonSpineNames.ComponentRoleName(a.Role), JsonSpineNames.ComponentRoleName(b.Role)));
                for (var r = 0; r < sortedRoles.Length; r++)
                    WriteRoleEvidence(writer, sortedRoles[r]);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteRoleEvidence(Utf8JsonWriter writer, ComponentRoleEvidence roleEvidence)
    {
        writer.WriteStartObject();
        writer.WriteString("role"u8, JsonSpineNames.ComponentRoleUtf8(roleEvidence.Role));
        writer.WriteStartArray("evidence"u8);
        var evidence = roleEvidence.Evidence;
        for (var j = 0; j < evidence.Count; j++)
            DirectSpineRelationsConverter.WriteEvidence(writer, evidence[j]);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteSpineEndpoints(Utf8JsonWriter writer, IReadOnlyList<EndpointNode> endpoints)
    {
        writer.WriteStartArray("spineEndpoints"u8);
        for (var i = 0; i < endpoints.Count; i++)
        {
            var ep = endpoints[i];
            writer.WriteStartObject();
            writer.WriteString("id"u8, ep.Id.ToString());
            writer.WriteString("name"u8, ep.Name);
            writer.WriteString("httpMethod"u8, ep.HttpMethod);
            writer.WriteString("route"u8, ep.Route);
            writer.WriteString("verbStrength"u8, JsonSpineNames.EvidenceStrengthUtf8(ep.VerbStrength));
            writer.WriteString("routeStrength"u8, JsonSpineNames.EvidenceStrengthUtf8(ep.RouteStrength));
            writer.WriteBoolean("inferred"u8, ep.IsInferred);
            if (!string.IsNullOrEmpty(ep.Project.Value))
                writer.WriteString("project"u8, ep.Project.Value);
            WriteLocation(writer, "location"u8, ep.Location);
            if (ep.Evidence.Count > 0)
            {
                writer.WriteStartArray("evidence"u8);
                for (var j = 0; j < ep.Evidence.Count; j++)
                    DirectSpineRelationsConverter.WriteEvidence(writer, ep.Evidence[j]);
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteUnproven(Utf8JsonWriter writer, UnprovenPopulations unproven)
    {
        writer.WriteStartObject("unproven"u8);
        writer.WriteNumber("totalCount"u8, unproven.TotalCount);

        if (unproven.UnresolvedDispatches.Count > 0)
        {
            writer.WritePropertyName("unresolvedDispatches"u8);
            WriteUnresolvedDispatches(writer, unproven.UnresolvedDispatches);
        }
        if (unproven.AmbiguousResolutions.Count > 0)
            WriteAmbiguousResolutions(writer, unproven.AmbiguousResolutions);
        if (unproven.AmbiguousLinks.Count > 0)
            WriteUnprovenDiagnosticList(writer, "ambiguousLinks"u8, unproven.AmbiguousLinks);
        if (unproven.UnresolvedContracts.Count > 0)
            WriteUnprovenDiagnosticList(writer, "unresolvedContracts"u8, unproven.UnresolvedContracts);
        if (unproven.UnestablishedBehaviors.Count > 0)
            WriteUnestablishedBehaviors(writer, unproven.UnestablishedBehaviors);
        if (unproven.Observations.Count > 0)
            WriteUnprovenDiagnosticList(writer, "observations"u8, unproven.Observations);

        writer.WriteEndObject();
    }

    private static void WriteAmbiguousResolutions(
        Utf8JsonWriter writer,
        IReadOnlyList<AmbiguousResolutionEntry> entries)
    {
        writer.WriteStartArray("ambiguousResolutions"u8);
        AmbiguousResolutionEntry[] sorted;
        if (entries.Count == 1)
        {
            sorted = new[] { entries[0] };
        }
        else
        {
            sorted = new AmbiguousResolutionEntry[entries.Count];
            for (var i = 0; i < entries.Count; i++)
                sorted[i] = entries[i];
            Array.Sort(sorted, static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.InterfaceSignature, b.InterfaceSignature);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.InterfaceName, b.InterfaceName);
            });
        }

        for (var i = 0; i < sorted.Length; i++)
        {
            var entry = sorted[i];
            writer.WriteStartObject();
            writer.WriteString("interfaceSignature"u8, entry.InterfaceSignature);
            writer.WriteString("interfaceName"u8, entry.InterfaceName);
            WriteLocation(writer, "interfaceLocation"u8, entry.InterfaceLocation);

            writer.WriteStartArray("candidates"u8);
            ServiceCandidate[] cands;
            if (entry.Candidates.Count <= 1)
            {
                for (var c = 0; c < entry.Candidates.Count; c++)
                    WriteServiceCandidate(writer, entry.Candidates[c]);
            }
            else
            {
                cands = new ServiceCandidate[entry.Candidates.Count];
                for (var c = 0; c < entry.Candidates.Count; c++)
                    cands[c] = entry.Candidates[c];
                Array.Sort(cands, static (a, b) =>
                {
                    var cmp = string.CompareOrdinal(a.Type, b.Type);
                    if (cmp != 0) return cmp;
                    cmp = b.FromRegistration.CompareTo(a.FromRegistration);
                    return cmp != 0 ? cmp : b.DeclaredInScan.CompareTo(a.DeclaredInScan);
                });
                for (var c = 0; c < cands.Length; c++)
                    WriteServiceCandidate(writer, cands[c]);
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteServiceCandidate(Utf8JsonWriter writer, ServiceCandidate candidate)
    {
        writer.WriteStartObject();
        writer.WriteString("type"u8, candidate.Type);
        writer.WriteBoolean("declaredInScan"u8, candidate.DeclaredInScan);
        writer.WriteBoolean("fromRegistration"u8, candidate.FromRegistration);
        WriteLocation(writer, "location"u8, candidate.Location);
        WriteLocation(writer, "registrationLocation"u8, candidate.RegistrationLocation);
        writer.WriteEndObject();
    }

    private static void WriteUnestablishedBehaviors(
        Utf8JsonWriter writer,
        IReadOnlyList<UnestablishedBehaviorEntry> entries)
    {
        writer.WriteStartArray("unestablishedBehaviors"u8);
        if (entries.Count == 1)
        {
            WriteUnestablishedBehavior(writer, entries[0], JsonSpineNames.UnestablishedBehaviorReasonName(entries[0].Reason), EmptyToNull(entries[0].ProjectName));
        }
        else
        {
            var sorted = new (UnestablishedBehaviorEntry Entry, string Reason, string? Project)[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var b = entries[i];
                sorted[i] = (b, JsonSpineNames.UnestablishedBehaviorReasonName(b.Reason), EmptyToNull(b.ProjectName));
            }
            Array.Sort(sorted, static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.Entry.BehaviorName, b.Entry.BehaviorName);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Reason, b.Reason);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Project, b.Project);
            });
            for (var i = 0; i < sorted.Length; i++)
                WriteUnestablishedBehavior(writer, sorted[i].Entry, sorted[i].Reason, sorted[i].Project);
        }
        writer.WriteEndArray();
    }

    private static void WriteUnestablishedBehavior(
        Utf8JsonWriter writer,
        UnestablishedBehaviorEntry b,
        string reason,
        string? project)
    {
        writer.WriteStartObject();
        if (b.BehaviorComponentId is not null)
            writer.WriteString("behaviorComponentId"u8, b.BehaviorComponentId.Value);
        writer.WriteString("behaviorName"u8, b.BehaviorName);
        writer.WriteString("reason"u8, reason);
        var regSite = EmptyToNull(b.RegistrationSite);
        if (regSite is not null)
            writer.WriteString("registrationSite"u8, regSite);
        if (project is not null)
            writer.WriteString("project"u8, project);
        WriteLocation(writer, "location"u8, b.Location);
        writer.WriteEndObject();
    }

    private static void WriteUnprovenDiagnosticList(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        IReadOnlyList<UnprovenDiagnosticEntry> entries)
    {
        writer.WriteStartArray(propertyName);
        if (entries.Count == 1)
        {
            var e = entries[0];
            WriteUnprovenDiagnostic(writer, e, JsonSpineNames.UnprovenItemKindName(e.Kind), EmptyToNull(e.ProjectName));
        }
        else
        {
            var sorted = new (UnprovenDiagnosticEntry Entry, string Kind, string? Project)[entries.Count];
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                sorted[i] = (e, JsonSpineNames.UnprovenItemKindName(e.Kind), EmptyToNull(e.ProjectName));
            }
            Array.Sort(sorted, static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.Kind, b.Kind);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Entry.Code, b.Entry.Code);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Entry.Subject, b.Entry.Subject);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Project, b.Project);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Entry.Location?.File, b.Entry.Location?.File);
                if (cmp != 0) return cmp;
                cmp = (a.Entry.Location?.Line ?? 0).CompareTo(b.Entry.Location?.Line ?? 0);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Entry.Message, b.Entry.Message);
            });
            for (var i = 0; i < sorted.Length; i++)
                WriteUnprovenDiagnostic(writer, sorted[i].Entry, sorted[i].Kind, sorted[i].Project);
        }
        writer.WriteEndArray();
    }

    private static void WriteUnprovenDiagnostic(
        Utf8JsonWriter writer,
        UnprovenDiagnosticEntry e,
        string kind,
        string? project)
    {
        writer.WriteStartObject();
        writer.WriteString("kind"u8, kind);
        writer.WriteString("code"u8, e.Code);
        writer.WriteString("subject"u8, e.Subject);
        writer.WriteString("message"u8, e.Message);
        if (project is not null)
            writer.WriteString("project"u8, project);
        WriteLocation(writer, "location"u8, e.Location);
        writer.WriteEndObject();
    }

    private static void WriteStringArray(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        IReadOnlyList<string> values)
    {
        writer.WriteStartArray(propertyName);
        for (var i = 0; i < values.Count; i++)
            writer.WriteStringValue(values[i]);
        writer.WriteEndArray();
    }

    private static void WriteLocation(
        Utf8JsonWriter writer,
        ReadOnlySpan<byte> propertyName,
        SourceLocation? location)
    {
        if (location is null)
            return;
        writer.WriteStartObject(propertyName);
        if (location.File is not null)
            writer.WriteString("file"u8, location.File);
        writer.WriteNumber("line"u8, location.Line);
        writer.WriteNumber("column"u8, location.Column);
        writer.WriteEndObject();
    }

    internal static JsonReportDocument Create(AnalysisResult result)
        => CreateCore(result, streamRelations: false);

    internal static JsonReportDocument CreateForSerialization(AnalysisResult result)
        => CreateCore(result, streamRelations: true);

    private static JsonReportDocument CreateCore(AnalysisResult result, bool streamRelations)
    {
        ArgumentNullException.ThrowIfNull(result);

        var flowEdges = SpineQueries.FlowEdges(result);
        var resolvedEdges = SpineQueries.ResolvedEdges(result);
        var serviceCallEdges = SpineQueries.ServiceCallEdges(result.Spine);
        var dispatchCallSites = SpineQueries.DispatchCallSites(result.Spine);
        var unresolvedDispatches = result.Spine.Unproven.UnresolvedDispatches;

        var rootName = new DirectoryInfo(result.RootPath).Name;
        var activeChecks = result.BaselineApplied
            ? result.Checks.Where(c => !c.IsKnown).ToList()
            : result.Checks;
        var errorCount = activeChecks.Count(c => c.Severity == CheckSeverity.Error);
        var warningCount = activeChecks.Count - errorCount;
        var knownCount = result.BaselineApplied
            ? result.Checks.Count(c => c.IsKnown)
            : 0;

        return new JsonReportDocument
        {
            SchemaVersion = JsonReportGenerator.CurrentSchemaVersion,
            ToolVersion = ToolInfo.Version,
            Scanner = result.ScannerName,
            GeneratedAt = result.GeneratedAt.UtcDateTime.ToString("O"),
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            RootName = rootName,
            Watermark = result.Watermark,
            Conventions = result.Conventions.Count > 0 ? result.Conventions.ToList() : null,
            DiscoveredFileCount = result.DiscoveredFileCount,
            FileCount = result.FileCount,
            BaselineApplied = result.BaselineApplied,
            CanRunStrictChecks = result.CanRunStrictChecks,
            Summary = new JsonSummary
            {
                Projects = result.Projects.Count,
                Requests = result.Requests.Count,
                Handlers = result.Handlers.Count,
                Validators = result.Validators.Count,
                Behaviors = result.Behaviors.Count,
                DbContexts = result.DbContexts.Count,
                Notifications = result.Notifications.Count,
                NotificationHandlers = result.NotificationHandlers.Count,
                Endpoints = result.Endpoints.Count,
                Errors = errorCount,
                Warnings = warningCount,
                KnownFindings = knownCount,
                Diagnostics = result.Diagnostics.Count,
                FlowEdges = flowEdges.Count,
                DispatchCallSites = dispatchCallSites.Count,
                UnresolvedDispatches = unresolvedDispatches.Count,
            },
            Projects = result.Projects
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.FilePath, StringComparer.Ordinal)
                .Select(p => new JsonProject
                {
                    Name = p.Name,
                    Layer = p.Layer,
                    References = SpineQueries.ProjectReferences(result, p)
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r, StringComparer.Ordinal)
                        .ToList(),
                    Packages = p.PackageReferences
                        .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(r => r, StringComparer.Ordinal)
                        .ToList(),
                })
                .ToList(),
            Requests = result.Requests
                .Select(r => new JsonRequest
                {
                    Name = r.Name,
                    Namespace = EmptyToNull(r.Namespace),
                    FullName = r.FullName,
                    Kind = r.Kind,
                    ResponseType = r.ResponseType,
                    ViaMarker = r.ViaMarker,
                    Project = r.ProjectName,
                    Handlers = r.Handlers.ToList(),
                    Validators = r.Validators.ToList(),
                    Properties = r.Properties.Count > 0
                        ? r.Properties.Select(p => new JsonRequestProperty { Name = p.Name, Type = p.Type }).ToList()
                        : null,
                    Entrypoints = r.Entrypoints.Count > 0 ? r.Entrypoints.ToList() : null,
                    TouchedEntities = r.TouchedEntities.Count > 0 ? r.TouchedEntities.ToList() : null,
                    PublishedNotifications = r.PublishedNotifications.Count > 0 ? r.PublishedNotifications.ToList() : null,
                    DispatchedRequests = r.DispatchedRequests.Count > 0 ? r.DispatchedRequests.ToList() : null,
                    Location = ToJsonLocation(r.Location),
                })
                .ToList(),
            Handlers = result.Handlers
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.RequestName, StringComparer.Ordinal)
                .Select(h => new JsonHandler
                {
                    Name = h.Name,
                    Namespace = EmptyToNull(h.Namespace),
                    FullName = h.FullName,
                    RequestName = h.RequestName,
                    Dependencies = h.Dependencies.Count > 0 ? h.Dependencies.ToList() : null,
                    TouchedEntities = h.TouchedEntities.Count > 0 ? h.TouchedEntities.ToList() : null,
                    PublishedNotifications = h.PublishedNotifications.Count > 0 ? h.PublishedNotifications.ToList() : null,
                    DispatchedRequests = h.DispatchedRequests.Count > 0 ? h.DispatchedRequests.ToList() : null,
                    Project = h.ProjectName,
                    Location = ToJsonLocation(h.Location),
                })
                .ToList(),
            Validators = result.Validators
                .OrderBy(v => v.FullName, StringComparer.Ordinal)
                .ThenBy(v => v.TargetType, StringComparer.Ordinal)
                .Select(v => new JsonValidator
                {
                    Name = v.Name,
                    Namespace = EmptyToNull(v.Namespace),
                    FullName = v.FullName,
                    TargetType = v.TargetType,
                    Project = v.ProjectName,
                    Location = ToJsonLocation(v.Location),
                })
                .ToList(),
            Behaviors = result.Behaviors
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.ProjectName, StringComparer.Ordinal)
                .Select(b => new JsonBehavior
                {
                    Name = b.Name,
                    Project = b.ProjectName,
                    Location = ToJsonLocation(b.Location),
                })
                .ToList(),
            DbContexts = result.DbContexts
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Namespace, StringComparer.Ordinal)
                .Select(c => new JsonDbContext
                {
                    Name = c.Name,
                    Namespace = EmptyToNull(c.Namespace),
                    Project = c.ProjectName,
                    Location = ToJsonLocation(c.Location),
                    DbSets = c.DbSets
                        .Select(s => new JsonDbSet
                        {
                            Property = s.Property,
                            Entity = s.Entity,
                            EntityFullName = s.EntityFullName,
                        })
                        .ToList(),
                    Entities = c.Entities
                        .OrderBy(e => e.FullName, StringComparer.Ordinal)
                        .Select(e => new JsonEntity
                        {
                            Name = e.Name,
                            Namespace = EmptyToNull(e.Namespace),
                            FullName = e.FullName,
                            Project = e.ProjectName,
                            Location = ToJsonLocation(e.Location),
                            Properties = e.Properties
                                .Select(p => new JsonEntityProperty
                                {
                                    Name = p.Name,
                                    Type = p.Type,
                                    IsKey = p.IsKey,
                                    IsForeignKey = p.IsForeignKey,
                                    IsNavigation = p.IsNavigation,
                                    IsCollection = p.IsCollection,
                                    TargetEntity = p.TargetEntity,
                                    IsRequired = p.IsRequired,
                                    Location = ToJsonLocation(p.Location),
                                })
                                .ToList(),
                        })
                        .ToList(),
                    Relationships = c.Relationships
                        .OrderBy(r => r.PrincipalEntity, StringComparer.Ordinal)
                        .ThenBy(r => r.DependentEntity, StringComparer.Ordinal)
                        .ThenBy(r => r.PrincipalNavigation, StringComparer.Ordinal)
                        .ThenBy(r => r.DependentNavigation, StringComparer.Ordinal)
                        .Select(r => new JsonEntityRelationship
                        {
                            PrincipalEntity = r.PrincipalEntity,
                            DependentEntity = r.DependentEntity,
                            PrincipalNavigation = r.PrincipalNavigation,
                            DependentNavigation = r.DependentNavigation,
                            ForeignKey = r.ForeignKey,
                            IsRequired = r.IsRequired,
                            Kind = r.Kind switch
                            {
                                EntityRelationshipKind.OneToOne => "oneToOne",
                                EntityRelationshipKind.ManyToMany => "manyToMany",
                                _ => "oneToMany",
                            },
                            ConfiguredBy = r.ConfiguredBy,
                        })
                        .ToList(),
                })
                .ToList(),
            Checks = result.Checks
                .Select(c => new JsonCheckFinding
                {
                    Code = c.Code,
                    Severity = c.Severity == CheckSeverity.Error ? "error" : "warning",
                    Message = c.Message,
                    Target = c.Target,
                    Project = c.ProjectName,
                    Known = c.IsKnown,
                    Location = ToJsonLocation(c.Location),
                })
                .ToList(),
            Diagnostics = result.Diagnostics
                .Select(d => new JsonDiagnostic
                {
                    Code = d.Code,
                    Severity = d.Severity switch
                    {
                        AnalysisDiagnosticSeverity.Error => "error",
                        AnalysisDiagnosticSeverity.Warning => "warning",
                        _ => "info",
                    },
                    Message = d.Message,
                    Project = d.ProjectName,
                    Location = ToJsonLocation(d.Location),
                })
                .ToList(),
            SkippedFiles = result.SkippedFiles.ToList(),
            Notifications = result.Notifications
                .Select(n => new JsonNotification
                {
                    Name = n.Name,
                    Namespace = EmptyToNull(n.Namespace),
                    FullName = n.FullName,
                    ViaMarker = n.ViaMarker,
                    Project = n.ProjectName,
                    Handlers = n.Handlers.ToList(),
                    Location = ToJsonLocation(n.Location),
                })
                .ToList(),
            NotificationHandlers = result.NotificationHandlers
                .OrderBy(h => h.FullName, StringComparer.Ordinal)
                .ThenBy(h => h.NotificationName, StringComparer.Ordinal)
                .Select(h => new JsonNotificationHandler
                {
                    Name = h.Name,
                    Namespace = EmptyToNull(h.Namespace),
                    FullName = h.FullName,
                    NotificationName = h.NotificationName,
                    Dependencies = h.Dependencies.Count > 0 ? h.Dependencies.ToList() : null,
                    TouchedEntities = h.TouchedEntities.Count > 0 ? h.TouchedEntities.ToList() : null,
                    PublishedNotifications = h.PublishedNotifications.Count > 0 ? h.PublishedNotifications.ToList() : null,
                    DispatchedRequests = h.DispatchedRequests.Count > 0 ? h.DispatchedRequests.ToList() : null,
                    Project = h.ProjectName,
                    Location = ToJsonLocation(h.Location),
                })
                .ToList(),
            Endpoints = result.Endpoints.Count > 0
                ? result.Endpoints
                    .Select(e => new JsonEndpoint
                    {
                        Name = e.Name,
                        HttpMethod = e.HttpMethod,
                        Route = e.Route,
                        TargetRequest = e.TargetRequestName,
                        TargetRequestFullName = e.TargetRequestFullName,
                        Inferred = e.IsInferred,
                        Project = e.ProjectName,
                        Location = ToJsonLocation(e.Location),
                    })
                    .ToList()
                : null,
            FlowEdges = flowEdges.Count > 0
                ? flowEdges
                    .Select(e => new JsonFlowEdge
                    {
                        Kind = FlowEdgeKindName(e.Kind),
                        From = e.From,
                        To = e.To,
                        Project = e.ProjectName,
                        Location = ToJsonLocation(e.Location),
                    })
                    .ToList()
                : null,
            ResolvedEdges = resolvedEdges.Count > 0
                ? resolvedEdges
                    .Select(e => new JsonResolvedEdge
                    {
                        Kind = ResolvedEdgeKindName(e.Kind),
                        From = e.From,
                        Via = e.Via,
                        To = e.To,
                        Project = e.ProjectName,
                        Location = ToJsonLocation(e.Location),
                    })
                    .ToList()
                : null,
            ServiceCallEdges = serviceCallEdges.Count > 0
                ? serviceCallEdges
                    .Select(e => new JsonServiceCallEdge
                    {
                        Kind = "serviceCall",
                        From = e.From,
                        Via = e.Via,
                        Method = e.Method,
                        To = e.To,
                        Project = e.ProjectName,
                        Location = ToJsonLocation(e.Location),
                    })
                    .ToList()
                : null,
            DispatchCallSites = dispatchCallSites.Count > 0
                ? dispatchCallSites
                    .Select(s => new JsonDispatchCallSite
                    {
                        Target = s.TargetFullName,
                        TargetKind = s.TargetKind == DispatchTargetKind.Request ? "request" : "notification",
                        Sender = s.SenderFullName,
                        Project = s.ProjectName,
                        TestProject = s.IsTestProject,
                        Location = ToJsonLocation(s.Location),
                    })
                    .ToList()
                : null,
            UnresolvedDispatches = ToJsonUnresolvedDispatches(unresolvedDispatches),
            Relations = BuildRelationsField(result, streamRelations),
            Components = BuildComponentsField(result),
            SpineEndpoints = BuildSpineEndpointsField(result),
            Unproven = BuildUnprovenField(result),
        };
    }

    private static List<JsonSpineRelation>? BuildRelationsField(AnalysisResult result, bool streamRelations)
    {
        if (result.Spine.UnindexedRelations.Count == 0)
            return null;

        var spineRelations = result.Spine.Relations;
        if (streamRelations)
            return new DirectSpineRelationPlaceholder(spineRelations);

        var nodeCache = new Dictionary<NodeReference, JsonSpineNodeRef>(ReferenceEqualityComparer.Instance);
        var list = new List<JsonSpineRelation>(spineRelations.Count);
        for (var i = 0; i < spineRelations.Count; i++)
            list.Add(ToJsonSpineRelation(spineRelations[i], nodeCache));
        return list;
    }

    private static JsonSpineRelation ToJsonSpineRelation(
        SpineRelation relation,
        Dictionary<NodeReference, JsonSpineNodeRef> nodeCache)
    {
        List<JsonSpineNodeRef>? viaList = null;
        if (relation.Via.Count > 0)
        {
            viaList = new List<JsonSpineNodeRef>(relation.Via.Count);
            for (var i = 0; i < relation.Via.Count; i++)
                viaList.Add(GetOrCreateNodeRef(relation.Via[i], nodeCache));
        }

        var occurrences = BuildEvidenceList(relation.Occurrences, nodeCache);

        return new JsonSpineRelation
        {
            Kind = JsonSpineNames.RelationKindName(relation.Kind),
            From = GetOrCreateNodeRef(relation.From, nodeCache),
            To = GetOrCreateNodeRef(relation.To, nodeCache),
            Via = viaList,
            Method = EmptyToNull(relation.Method),
            Strength = JsonSpineNames.EvidenceStrengthName(relation.Strength),
            OccurrenceCount = relation.OccurrenceCount,
            Occurrences = occurrences,
        };
    }

    private static JsonSpineEvidence ToJsonSpineEvidence(
        EvidenceOccurrence occurrence,
        Dictionary<NodeReference, JsonSpineNodeRef> nodeCache)
    {
        List<JsonSpineNodeRef>? viaList = null;
        if (occurrence.Via.Count > 0)
        {
            viaList = new List<JsonSpineNodeRef>(occurrence.Via.Count);
            for (var i = 0; i < occurrence.Via.Count; i++)
                viaList.Add(GetOrCreateNodeRef(occurrence.Via[i], nodeCache));
        }

        return new JsonSpineEvidence
        {
            Rule = JsonSpineNames.EvidenceRuleName(occurrence.Rule),
            Origin = JsonSpineNames.EvidenceOriginName(occurrence.Origin),
            Strength = JsonSpineNames.EvidenceStrengthName(occurrence.Strength),
            Location = ToJsonLocation(occurrence.Location),
            LocationOmittedReason = occurrence.LocationOmittedReason,
            Via = viaList,
            ResolutionSemantics = occurrence.ResolutionSemantics is { } semantics
                ? JsonSpineNames.ResolutionSemanticsName(semantics)
                : null,
            ScopeProject = occurrence.ScopeProjectName,
            TestProject = occurrence.IsTestProject ? true : null,
            Detail = occurrence.Detail,
        };
    }

    private static List<JsonSpineEvidence> BuildEvidenceList(
        IReadOnlyList<EvidenceOccurrence> occSource,
        Dictionary<NodeReference, JsonSpineNodeRef> nodeCache)
    {
        var list = new List<JsonSpineEvidence>(occSource.Count);
        for (var i = 0; i < occSource.Count; i++)
            list.Add(ToJsonSpineEvidence(occSource[i], nodeCache));
        return list;
    }

    private static List<JsonSpineComponent>? BuildComponentsField(AnalysisResult result)
    {
        var components = result.Spine.Components;
        if (components.Count == 0)
            return null;

        var nodeCache = new Dictionary<NodeReference, JsonSpineNodeRef>(ReferenceEqualityComparer.Instance);
        var list = new List<JsonSpineComponent>(components.Count);
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            var sourceRoles = component.Roles;
            var roles = new List<JsonSpineRoleEvidence>(sourceRoles.Count);
            for (var r = 0; r < sourceRoles.Count; r++)
            {
                var roleEvidence = sourceRoles[r];
                roles.Add(new JsonSpineRoleEvidence
                {
                    Role = JsonSpineNames.ComponentRoleName(roleEvidence.Role),
                    Evidence = BuildEvidenceList(roleEvidence.Evidence, nodeCache),
                });
            }

            if (roles.Count > 1)
                roles.Sort(static (a, b) => string.CompareOrdinal(a.Role, b.Role));

            list.Add(new JsonSpineComponent
            {
                Id = component.Id.Value,
                FullName = component.FullName,
                Name = component.Name,
                Project = EmptyToNull(component.Project.Value),
                Location = ToJsonLocation(component.Location),
                Roles = roles,
            });
        }

        return list;
    }

    private static List<JsonSpineEndpoint>? BuildSpineEndpointsField(AnalysisResult result)
    {
        var endpoints = result.Spine.Endpoints;
        if (endpoints.Count == 0)
            return null;

        var nodeCache = new Dictionary<NodeReference, JsonSpineNodeRef>(ReferenceEqualityComparer.Instance);
        var list = new List<JsonSpineEndpoint>(endpoints.Count);
        for (var i = 0; i < endpoints.Count; i++)
        {
            var ep = endpoints[i];
            list.Add(new JsonSpineEndpoint
            {
                Id = ep.Id.ToString(),
                Name = ep.Name,
                HttpMethod = ep.HttpMethod,
                Route = ep.Route,
                VerbStrength = JsonSpineNames.EvidenceStrengthName(ep.VerbStrength),
                RouteStrength = JsonSpineNames.EvidenceStrengthName(ep.RouteStrength),
                Inferred = ep.IsInferred,
                Project = EmptyToNull(ep.Project.Value),
                Location = ToJsonLocation(ep.Location),
                Evidence = ep.Evidence.Count > 0 ? BuildEvidenceList(ep.Evidence, nodeCache) : null,
            });
        }

        return list;
    }

    private static JsonUnprovenPopulations? BuildUnprovenField(AnalysisResult result)
    {
        var unproven = result.Spine.Unproven;
        if (unproven.TotalCount == 0)
            return null;

        return new JsonUnprovenPopulations
        {
            TotalCount = unproven.TotalCount,
            UnresolvedDispatches = ToJsonUnresolvedDispatches(unproven.UnresolvedDispatches),
            AmbiguousResolutions = BuildAmbiguousResolutions(unproven.AmbiguousResolutions),
            AmbiguousLinks = BuildDiagnosticList(unproven.AmbiguousLinks),
            UnresolvedContracts = BuildDiagnosticList(unproven.UnresolvedContracts),
            UnestablishedBehaviors = BuildUnestablishedBehaviors(unproven.UnestablishedBehaviors),
            Observations = BuildDiagnosticList(unproven.Observations),
        };
    }

    private static List<JsonUnresolvedDispatch>? ToJsonUnresolvedDispatches(
        IReadOnlyList<UnresolvedDispatch> unresolvedDispatches)
    {
        if (unresolvedDispatches.Count == 0)
            return null;

        var list = new List<JsonUnresolvedDispatch>(unresolvedDispatches.Count);
        for (var i = 0; i < unresolvedDispatches.Count; i++)
        {
            var u = unresolvedDispatches[i];
            list.Add(new JsonUnresolvedDispatch
            {
                Method = u.Method,
                Receiver = u.ReceiverName,
                Project = u.ProjectName,
                Location = ToJsonLocation(u.Location),
            });
        }

        return list;
    }

    private static List<JsonAmbiguousResolution>? BuildAmbiguousResolutions(
        IReadOnlyList<AmbiguousResolutionEntry> entries)
    {
        if (entries.Count == 0)
            return null;

        var list = new List<JsonAmbiguousResolution>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var candidates = new List<JsonServiceCandidate>(entry.Candidates.Count);
            for (var c = 0; c < entry.Candidates.Count; c++)
            {
                var candidate = entry.Candidates[c];
                candidates.Add(new JsonServiceCandidate
                {
                    Type = candidate.Type,
                    DeclaredInScan = candidate.DeclaredInScan,
                    FromRegistration = candidate.FromRegistration,
                    Location = ToJsonLocation(candidate.Location),
                    RegistrationLocation = ToJsonLocation(candidate.RegistrationLocation),
                });
            }

            if (candidates.Count > 1)
            {
                candidates.Sort(static (a, b) =>
                {
                    var cmp = string.CompareOrdinal(a.Type, b.Type);
                    if (cmp != 0) return cmp;
                    cmp = b.FromRegistration.CompareTo(a.FromRegistration);
                    return cmp != 0 ? cmp : b.DeclaredInScan.CompareTo(a.DeclaredInScan);
                });
            }

            list.Add(new JsonAmbiguousResolution
            {
                InterfaceSignature = entry.InterfaceSignature,
                InterfaceName = entry.InterfaceName,
                InterfaceLocation = ToJsonLocation(entry.InterfaceLocation),
                Candidates = candidates,
            });
        }

        if (list.Count > 1)
        {
            list.Sort(static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.InterfaceSignature, b.InterfaceSignature);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.InterfaceName, b.InterfaceName);
            });
        }

        return list;
    }

    private static List<JsonUnestablishedBehavior>? BuildUnestablishedBehaviors(
        IReadOnlyList<UnestablishedBehaviorEntry> entries)
    {
        if (entries.Count == 0)
            return null;

        var list = new List<JsonUnestablishedBehavior>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var b = entries[i];
            list.Add(new JsonUnestablishedBehavior
            {
                BehaviorComponentId = b.BehaviorComponentId?.Value,
                BehaviorName = b.BehaviorName,
                Reason = JsonSpineNames.UnestablishedBehaviorReasonName(b.Reason),
                RegistrationSite = EmptyToNull(b.RegistrationSite),
                Project = EmptyToNull(b.ProjectName),
                Location = ToJsonLocation(b.Location),
            });
        }

        if (list.Count > 1)
        {
            list.Sort(static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.BehaviorName, b.BehaviorName);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Reason, b.Reason);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Project, b.Project);
            });
        }

        return list;
    }

    private static List<JsonUnprovenDiagnostic>? BuildDiagnosticList(
        IReadOnlyList<UnprovenDiagnosticEntry> entries)
    {
        if (entries.Count == 0)
            return null;

        var list = new List<JsonUnprovenDiagnostic>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            list.Add(new JsonUnprovenDiagnostic
            {
                Kind = JsonSpineNames.UnprovenItemKindName(e.Kind),
                Code = e.Code,
                Subject = e.Subject,
                Message = e.Message,
                Project = EmptyToNull(e.ProjectName),
                Location = ToJsonLocation(e.Location),
            });
        }

        if (list.Count > 1)
        {
            list.Sort(static (a, b) =>
            {
                var cmp = string.CompareOrdinal(a.Kind, b.Kind);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Code, b.Code);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Subject, b.Subject);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Project, b.Project);
                if (cmp != 0) return cmp;
                cmp = string.CompareOrdinal(a.Location?.File, b.Location?.File);
                if (cmp != 0) return cmp;
                cmp = (a.Location?.Line ?? 0).CompareTo(b.Location?.Line ?? 0);
                if (cmp != 0) return cmp;
                return string.CompareOrdinal(a.Message, b.Message);
            });
        }

        return list;
    }

    private static JsonSpineNodeRef GetOrCreateNodeRef(
        NodeReference reference,
        Dictionary<NodeReference, JsonSpineNodeRef> nodeCache)
    {
        if (nodeCache.TryGetValue(reference, out var cached))
            return cached;

        var created = new JsonSpineNodeRef
        {
            Kind = JsonSpineNames.NodeReferenceKindName(reference.Kind),
            Key = reference.Key,
            Name = reference.Name,
            ComponentId = reference.ComponentId?.Value,
            EndpointId = reference.EndpointId?.ToString(),
            ProjectId = reference.ProjectId?.Value,
            SymbolId = reference.SymbolId?.ToString(),
            Member = reference.MemberName,
            Reason = reference.Reason,
        };
        nodeCache[reference] = created;
        return created;
    }

    private static string FlowEdgeKindName(FlowEdgeKind kind) => kind switch
    {
        FlowEdgeKind.EndpointToRequest => "endpointToRequest",
        FlowEdgeKind.RequestToHandler => "requestToHandler",
        FlowEdgeKind.HandlerToRequest => "handlerToRequest",
        FlowEdgeKind.HandlerToNotification => "handlerToNotification",
        FlowEdgeKind.HandlerToDbContext => "handlerToDbContext",
        _ => "unknown",
    };

    private static string ResolvedEdgeKindName(FlowEdgeKind kind) => kind switch
    {
        FlowEdgeKind.HandlerToDbContextResolved => "handlerToDbContextResolved",
        _ => "unknown",
    };

    private static string? EmptyToNull(string? value)
        => string.IsNullOrEmpty(value) ? null : value;

    private static JsonLocation? ToJsonLocation(SourceLocation? location)
        => location is null
            ? null
            : new JsonLocation
            {
                File = location.File,
                Line = location.Line,
                Column = location.Column,
            };
}
