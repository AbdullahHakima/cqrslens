using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// F2 single-pass resolver over production components for:
/// <list type="bullet">
/// <item>R-CALL: open-generic, partial-class, and property/<c>[FromServices]</c> direct member calls.</item>
/// <item>R-DATA (Option a): direct Linq2DB/generic repository method invocations (<see cref="RelationKind.TouchesData"/>).</item>
/// <item>R-PUB: event publish calls carrying resolved event types (<see cref="EvidenceRule.EventPublishCallSite"/>).</item>
/// <item>R-UNPROVEN: convention registrations, service locators, dynamic reflection, unimplemented interfaces, and unresolved event consumers.</item>
/// </list>
/// </summary>
internal static partial class ComponentCallAndDataResolver
{
    [GeneratedRegex(
        @"\[\s*FromServices(?:Attribute)?\s*\]\s*(?<type>[A-Za-z_][\w.<>,\s\?\[\]]*?)\s+(?<name>@?[A-Za-z_]\w*)")]
    private static partial Regex FromServicesRegex();

    [GeneratedRegex(
        @"public\s+static\s+(?:async\s+)?[^\(\{;=]+?\s+(?<method>[A-Za-z_]\w*)\s*(?:<(?<tparams>[^>]*)>)?\s*\(\s*this\s+(?<iface>[A-Za-z_][\w.<>,\s]*?)\s+(?<thisParam>@?[A-Za-z_]\w*)")]
    private static partial Regex ExtensionPublishHeaderRegex();

    [GeneratedRegex(
        @"\bnew\s+(?<type>[A-Za-z_][\w.]*(?:\s*<[^;{}()]*>)?)\s*(?:\(|\{)")]
    private static partial Regex NewEventRegex();

    [GeneratedRegex(
        @"\b[A-Za-z_]*PluginManager\s*<\s*(?<contract>[A-Za-z_][\w.]*)\s*>")]
    private static partial Regex PluginManagerContractRegex();

    private static class NonEntityNames
    {
        internal static readonly HashSet<string> Set = new(StringComparer.OrdinalIgnoreCase)
        {
            "object", "string", "bool", "byte", "sbyte", "short", "ushort", "int", "uint",
            "long", "ulong", "float", "double", "decimal", "char", "Guid", "DateTime",
            "DateTimeOffset", "TimeSpan", "DateOnly", "TimeOnly", "BaseEntity", "Entity",
        };
    }

    public static void Analyze(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result)
    {
        if (parsedFiles.Count == 0 || result.Spine.ComponentsCount == 0)
            return;

        var conventionLines = RecordStartupConventionRegistrations(parsedFiles, catalog, lineIndices, result);

        List<ComponentNode>? componentList = null;
        foreach (var comp in result.Spine.Components)
        {
            if (comp.HasRole(ComponentRole.Controller) ||
                comp.HasRole(ComponentRole.Service) ||
                comp.HasRole(ComponentRole.Repository) ||
                comp.HasRole(ComponentRole.Handler) ||
                comp.HasRole(ComponentRole.NotificationHandler))
            {
                (componentList ??= new List<ComponentNode>()).Add(comp);
            }
        }

        ServiceResolutionLookup? lookup = null;
        HashSet<string>? injectedSignatures = null;
        HashSet<string>? injectedIdentities = null;

        if (componentList is not null)
        {
            AnalyzeComponents(
                parsedFiles,
                componentList,
                catalog,
                hierarchy,
                lineIndices,
                result,
                ref lookup,
                ref injectedSignatures,
                ref injectedIdentities);
        }

        ScanProductionFilesUnproven(
            parsedFiles,
            catalog,
            hierarchy,
            lineIndices,
            conventionLines,
            ref lookup,
            ref injectedSignatures,
            ref injectedIdentities,
            result);
    }

    private sealed class ComponentScanState
    {
        public readonly Dictionary<(CSharpScanner.ClassDecl Decl, string Name), (bool Valid, NodeReference? Ref)> ConcreteEntityCache = new();
        public readonly HashSet<(ComponentId, string)> UnimplementedReported = new();
        public readonly Dictionary<string, string> TypeByMember = new(StringComparer.Ordinal);
        public readonly Dictionary<string, ResolvedServiceTarget?> TargetByMember = new(StringComparer.Ordinal);
        public readonly List<MemberInvocationHit<ResolvedServiceTarget?>> HitsBuffer = new();
        public readonly Dictionary<RelationKey, HashSet<SourceLocation>> RecordedLocationsCache = new();
        public readonly Dictionary<(string Impl, int Arity, string? Project), ComponentNode?> TargetComponentCache = new();
        public Dictionary<(string InterfaceIdentity, string MethodName), ExtensionPublishInfo>? ExtensionPublishMap;
        public Dictionary<ComponentId, string>? RepoEntityByComponent;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AnalyzeComponents(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        List<ComponentNode> componentList,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result,
        ref ServiceResolutionLookup? lookup,
        ref HashSet<string>? injectedSignatures,
        ref HashSet<string>? injectedIdentities)
    {
        var membersBuffer = new List<(string Type, string Name)>();
        var constructorTypes = new HashSet<string>(StringComparer.Ordinal);
        ComponentScanState? state = null;

        for (var i = 0; i < componentList.Count; i++)
        {
            var comp = componentList[i];
            var entry = catalog.TryGetById(comp.Id);
            if (entry is null || !entry.IsConcrete || entry.IsTestProject)
                continue;

            membersBuffer.Clear();
            constructorTypes.Clear();
            var hasFromServices = false;

            foreach (var part in entry.Declarations)
            {
                if (catalog.IsTestProject(part.File.ProjectName))
                    continue;

                var partClasses = catalog.GetScan(part.File.Path)?.Classes;
                if (partClasses is null)
                    continue;

                var beforeCount = membersBuffer.Count;
                DependencyExtractor.ExtractParameters(
                    part.Declaration, part.File.Code, part.Mask, partClasses, membersBuffer);
                for (var idx = beforeCount; idx < membersBuffer.Count; idx++)
                    constructorTypes.Add(NormalizeType(membersBuffer[idx].Type));

                if (!hasFromServices && part.File.Code.Contains("FromServices", StringComparison.Ordinal))
                    hasFromServices = true;
            }

            if (constructorTypes.Count == 0 && !hasFromServices)
                continue;

            state ??= new ComponentScanState();
            AnalyzeComponentMembers(
                parsedFiles,
                comp,
                entry,
                membersBuffer,
                constructorTypes,
                hasFromServices,
                state,
                catalog,
                hierarchy,
                lineIndices,
                result,
                ref lookup,
                ref injectedSignatures,
                ref injectedIdentities);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AnalyzeComponentMembers(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ComponentNode comp,
        ScannedComponentEntry entry,
        List<(string Type, string Name)> membersBuffer,
        HashSet<string> constructorTypes,
        bool hasFromServices,
        ComponentScanState state,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result,
        ref ServiceResolutionLookup? lookup,
        ref HashSet<string>? injectedSignatures,
        ref HashSet<string>? injectedIdentities)
    {
        state.TypeByMember.Clear();

        foreach (var part in entry.Declarations)
        {
            if (catalog.IsTestProject(part.File.ProjectName))
                continue;

            var partClasses = catalog.GetScan(part.File.Path)?.Classes;
            if (partClasses is null)
                continue;

            ReceiverTypeMapBuilder.CollectClassLevelDeclarations(
                part.Declaration, part.File.Code, part.Mask, partClasses, membersBuffer);

            foreach (var prop in part.Declaration.Properties)
            {
                if (prop.Name.Length == 0) continue;
                var norm = NormalizeType(prop.Type);
                if (!membersBuffer.Contains((norm, prop.Name)))
                    membersBuffer.Add((norm, prop.Name));
            }

            // Include [FromServices] action/method parameters as valid DI injected receivers.
            if (hasFromServices && part.File.Code.Contains("FromServices", StringComparison.Ordinal))
            {
                var bodyStart = part.Declaration.BodySpan.Start;
                var bodyEnd = Math.Min(part.File.Code.Length, part.Declaration.BodySpan.End);
                foreach (Match fsMatch in FromServicesRegex().Matches(part.File.Code, bodyStart))
                {
                    if (fsMatch.Index >= bodyEnd) break;
                    if (!part.Mask.IsCode(fsMatch.Index)) continue;
                    if (!ReferenceEquals(DeepSliceSyntax.InnermostClass(partClasses, fsMatch.Index), part.Declaration))
                        continue;
                    var fsType = NormalizeType(fsMatch.Groups["type"].Value);
                    var fsName = fsMatch.Groups["name"].Value.TrimStart('@');
                    if (fsType.Length > 0 && fsName.Length > 0)
                    {
                        constructorTypes.Add(fsType);
                        membersBuffer.Add((fsType, fsName));
                    }
                }
            }
        }

        foreach (var (type, name) in membersBuffer)
        {
            if (name.Length == 0 || type.Length == 0) continue;
            if (!constructorTypes.Contains(type)) continue;
            state.TypeByMember.TryAdd(name, type);
        }

        if (state.TypeByMember.Count == 0)
            return;

        lookup ??= new ServiceResolutionLookup(result, hierarchy);
        injectedSignatures ??= new HashSet<string>(StringComparer.Ordinal);
        injectedIdentities ??= new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in entry.Declarations)
        {
            if (catalog.IsTestProject(part.File.ProjectName))
                continue;
            if (!lineIndices.TryGetValue(part.File.Path, out var lineIndex))
                continue;

            // Resolve member types once per declaring partial part (using this part's usings/namespace).
            state.TargetByMember.Clear();
            foreach (var (memberName, memberType) in state.TypeByMember)
            {
                var resolvedTarget = lookup.Resolve(memberType, part.Declaration);
                state.TargetByMember[memberName] = resolvedTarget;
                if (resolvedTarget is not null)
                {
                    injectedSignatures.Add(resolvedTarget.InterfaceSignature);
                    injectedIdentities.Add(resolvedTarget.InterfaceIdentity);
                }
            }

            ScanComponentPartInvocations(
                comp,
                part.Declaration,
                part.File,
                part.Mask,
                lineIndex,
                parsedFiles,
                state,
                catalog,
                hierarchy,
                result);
        }
    }

    private static void ScanComponentPartInvocations(
        ComponentNode comp,
        CSharpScanner.ClassDecl declaration,
        SourceFile file,
        SourceMask mask,
        SourceLineIndex lineIndex,
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ComponentScanState state,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        var code = file.Code;
        var bodyStart = declaration.BodySpan.Start;
        var bodyEnd = Math.Min(code.Length, declaration.BodySpan.End);
        if (bodyStart < 0 || bodyEnd <= bodyStart)
            return;

        var fromRef = NodeReference.ForComponent(comp.Id);
        var resolver = result.IdentityResolver;
        var scanClasses = catalog.GetScanClasses(file.Path);
        var multiClass = scanClasses.Count > 1;

        state.HitsBuffer.Clear();
        MemberInvocationScanner.FindMatches(code, mask, bodyStart, bodyEnd, state.TargetByMember, state.HitsBuffer);

        foreach (var hit in state.HitsBuffer)
        {
            var target = hit.Target;
            if (target is null)
                continue;

            var method = hit.Method;
            if (method.Length == 0) continue;

            if (multiClass &&
                !ReferenceEquals(DeepSliceSyntax.InnermostClass(scanClasses, hit.Index), declaration))
            {
                continue;
            }

            if (target.IsUnimplementedInterface)
            {
                if (state.UnimplementedReported.Add((comp.Id, target.InterfaceSignature)))
                {
                    var unimplLoc = lineIndex.Create(hit.Index);
                    result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
                        UnprovenItemKind.UnimplementedInterface,
                        "CQLENS_F2_UNIMPLEMENTED_INTERFACE",
                        target.InterfaceSignature,
                        $"Component '{comp.FullName}' invokes '{target.InterfaceSignature}.{method}' which has no resolved implementation.",
                        unimplLoc,
                        file.ProjectName));
                }
                continue;
            }

            if (!target.IsResolved || target.IsDbContext)
                continue;

            if (string.Equals(target.ImplementationFullName, comp.FullName, StringComparison.Ordinal))
                continue;

            var location = lineIndex.Create(hit.Index);
            var targetKey = (target.ImplementationFullName, target.ImplementationArity, file.ProjectName);
            if (!state.TargetComponentCache.TryGetValue(targetKey, out var targetComp))
            {
                var targetEntry = catalog.ResolveByFullName(
                    target.ImplementationFullName,
                    preferredProject: file.ProjectName,
                    preferredArity: target.ImplementationArity);
                if (targetEntry is not null && result.Spine.TryGetComponent(targetEntry.Id, out var foundComp))
                    targetComp = foundComp;
                state.TargetComponentCache[targetKey] = targetComp;
            }

            var viaRef = target.IsConcrete
                ? (targetComp is not null
                    ? NodeReference.ForComponent(targetComp.Id)
                    : resolver.ResolveTypeOrComponentRef(target.ImplementationFullName, file.ProjectName))
                : resolver.ResolveInterfaceRef(target.InterfaceSignature, target.InterfaceLocation);

            var toRef = targetComp is not null
                ? NodeReference.ForMethod(targetComp.Symbol, method)
                : resolver.ResolveMethodTargetRef(target.ImplementationFullName, method, file.ProjectName);

            var callStrength = target.IsConcrete ? EvidenceStrength.Proven : EvidenceStrength.Resolved;

            // Check R-PUB event publish call sites first (either direct Publish/PublishAsync or 1-hop extension method).
            var eventType = TryResolvePublishedEventType(
                code, mask, hit.Index, hit.TypeArgs, hit.OpenParenIndex, method, target, declaration, parsedFiles, hierarchy, state);

            if (eventType is not null)
            {
                var publishOcc = new EvidenceOccurrence(
                    EvidenceRule.EventPublishCallSite,
                    EvidenceOrigin.CallSite,
                    location,
                    callStrength,
                    via: [viaRef],
                    scopeProjectName: file.ProjectName,
                    detail: eventType);

                result.Spine.UpgradeOrMergeRelationOccurrence(
                    RelationKind.Calls,
                    fromRef,
                    toRef,
                    publishOcc,
                    replaceRuleAtSameLocation: EvidenceRule.InjectedMemberInvocation,
                    method: method);

                result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.UnresolvedEventConsumer,
                    "CQLENS_F2_EVENT_CONSUMER",
                    eventType,
                    $"Event '{eventType}' is published through '{target.InterfaceIdentity}.{method}', without statically linked consumer handlers.",
                    location,
                    file.ProjectName));
            }
            else
            {
                var callKey = new RelationKey(RelationKind.Calls, fromRef, toRef, [viaRef], method);
                if (!AlreadyRecordedAtLocation(result.Spine, callKey, location, state.RecordedLocationsCache))
                {
                    result.Spine.AddOrMergeRelation(
                        RelationKind.Calls,
                        fromRef,
                        toRef,
                        new EvidenceOccurrence(
                            EvidenceRule.ComponentMemberInvocation,
                            EvidenceOrigin.CallSite,
                            location,
                            callStrength,
                            via: [viaRef],
                            scopeProjectName: file.ProjectName),
                        method: method);
                }
            }

            // Check R-DATA (Option a): direct call on a Repository component with a concrete entity type argument.
            if (targetComp is not null && targetComp.HasRole(ComponentRole.Repository))
            {
                state.RepoEntityByComponent ??= BuildRepositoryEntityMap(catalog, hierarchy, result);
                TryEmitRepositoryTouchesData(
                    comp,
                    targetComp,
                    target,
                    declaration,
                    file,
                    method,
                    fromRef,
                    viaRef,
                    location,
                    state.RepoEntityByComponent,
                    state.ConcreteEntityCache,
                    catalog,
                    hierarchy,
                    result);
            }
        }
    }

    private static void TryEmitRepositoryTouchesData(
        ComponentNode comp,
        ComponentNode repoComp,
        ResolvedServiceTarget target,
        CSharpScanner.ClassDecl declaration,
        SourceFile file,
        string method,
        NodeReference fromRef,
        NodeReference viaInterfaceRef,
        SourceLocation? location,
        Dictionary<ComponentId, string> repoEntityByComponent,
        Dictionary<(CSharpScanner.ClassDecl Decl, string Name), (bool Valid, NodeReference? Ref)> concreteEntityCache,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (string.Equals(repoComp.FullName, comp.FullName, StringComparison.Ordinal))
            return;

        string? entityCandidate = null;
        if (target.InterfaceTypeArgs.Count > 0)
        {
            entityCandidate = target.InterfaceTypeArgs[0];
        }
        else if (repoEntityByComponent.TryGetValue(repoComp.Id, out var fixedEntity))
        {
            entityCandidate = fixedEntity;
        }

        if (string.IsNullOrWhiteSpace(entityCandidate))
            return;

        var entityName = ServiceResolutionLookup.CleanTypeReference(entityCandidate);
        if (entityName.Length == 0 || NonEntityNames.Set.Contains(CSharpScanner.SimpleName(entityName)))
            return;

        // Guard against open type parameters on the enclosing generic service/class (e.g. TAttribute).
        if (declaration.TypeParameters.Contains(entityName, StringComparer.Ordinal))
            return;

        var cacheKey = (declaration, entityName);
        if (!concreteEntityCache.TryGetValue(cacheKey, out var resolvedEntity))
        {
            var entityRef = ResolveConcreteEntityRef(entityName, declaration, catalog, hierarchy, result);
            resolvedEntity = (entityRef is not null, entityRef);
            concreteEntityCache[cacheKey] = resolvedEntity;
        }

        if (!resolvedEntity.Valid || resolvedEntity.Ref is null)
            return;

        var viaPath = new NodeReference[]
        {
            viaInterfaceRef,
            NodeReference.ForComponent(repoComp.Id),
            NodeReference.ForMethod(repoComp.Symbol, method),
        };

        result.Spine.AddOrMergeRelation(
            RelationKind.TouchesData,
            fromRef,
            resolvedEntity.Ref,
            new EvidenceOccurrence(
                EvidenceRule.RepositoryDataAccess,
                EvidenceOrigin.CallSite,
                location,
                target.IsConcrete ? EvidenceStrength.Proven : EvidenceStrength.Resolved,
                via: viaPath,
                scopeProjectName: file.ProjectName));
    }

    private static NodeReference? ResolveConcreteEntityRef(
        string entityTypeWritten,
        CSharpScanner.ClassDecl contextDecl,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        var (baseName, args) = CSharpScanner.ParseTypeReference(entityTypeWritten);
        if (baseName.Length == 0)
            return null;

        var entityDecl = hierarchy.TryResolveClass(
            baseName, contextDecl.Namespace, contextDecl.Usings, contextDecl.ContainingTypes, args.Count, out _);
        if (entityDecl is null || entityDecl.IsAbstract)
            return null;

        var entry = catalog.GetByDeclaration(entityDecl);
        if (entry is not null && entry.IsTestProject)
            return null;

        if (entry is not null && result.Spine.TryGetComponent(entry.Id, out var existingComp))
            return NodeReference.ForComponent(existingComp!.Id);

        if (entry is not null)
        {
            var projectId = result.IdentityResolver.ResolveProjectId(entry.ProjectName);
            return NodeReference.ForType(SymbolId.Create(
                projectId,
                entityDecl.Namespace,
                entityDecl.ContainingTypes,
                entityDecl.Name,
                entityDecl.TypeParameters.Count));
        }

        return null;
    }

    private static string? TryResolvePublishedEventType(
        string code,
        SourceMask mask,
        int invocationIndex,
        string? typeArgs,
        int openParen,
        string method,
        ResolvedServiceTarget target,
        CSharpScanner.ClassDecl declaration,
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        TypeHierarchy hierarchy,
        ComponentScanState state)
    {
        var isDirectPublish = method is "Publish" or "PublishAsync";
        ExtensionPublishInfo? extInfo = null;
        if (!isDirectPublish)
        {
            state.ExtensionPublishMap ??= BuildExtensionPublishIndex(parsedFiles, hierarchy);
            if (!state.ExtensionPublishMap.TryGetValue((target.InterfaceIdentity, method), out extInfo))
                return null;
        }

        var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, openParen);
        var argSlice = closeParen > openParen + 1
            ? code[(openParen + 1)..closeParen].Trim()
            : "";

        if (extInfo is not null)
        {
            if (extInfo.UsesMethodTypeArgument)
            {
                if (!string.IsNullOrWhiteSpace(typeArgs))
                {
                    var callTypeArgs = CSharpScanner.SplitTopLevel(typeArgs);
                    if (callTypeArgs.Count > 0 && extInfo.EventTypeWritten is not null)
                    {
                        var concreteArg = ResolveTypeFullName(callTypeArgs[0], declaration, hierarchy) ?? callTypeArgs[0];
                        var rawExt = ReplaceGenericPlaceholder(extInfo.EventTypeWritten, concreteArg);
                        return ResolveTypeFullName(rawExt, extInfo.DeclaringClass, hierarchy) ?? rawExt;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(extInfo.EventTypeWritten))
                return ResolveTypeFullName(extInfo.EventTypeWritten, extInfo.DeclaringClass, hierarchy) ?? extInfo.EventTypeWritten;

            return null;
        }

        if (!string.IsNullOrWhiteSpace(typeArgs))
        {
            var args = CSharpScanner.SplitTopLevel(typeArgs);
            if (args.Count > 0)
            {
                var resolved = ResolveTypeFullName(args[0], declaration, hierarchy);
                if (resolved is not null)
                    return resolved;
            }
        }

        if (argSlice.Length == 0)
            return null;

        var newMatch = NewEventRegex().Match(argSlice);
        if (newMatch.Success)
        {
            var rawType = newMatch.Groups["type"].Value;
            return ResolveTypeFullName(rawType, declaration, hierarchy) ?? rawType;
        }

        // Handle variable identifier passed to PublishAsync(orderPlacedEvent) initialized nearby in method.
        var firstArg = CSharpScanner.SplitTopLevel(argSlice).FirstOrDefault()?.Trim().TrimStart('@');
        if (!string.IsNullOrEmpty(firstArg) && IsSimpleIdentifier(firstArg))
        {
            var windowStart = Math.Max(declaration.BodySpan.Start, invocationIndex - 2000);
            var lookback = code[windowStart..invocationIndex];
            var assignPattern = $@"(?:\bvar\s+|\b[A-Za-z_][\w.<>]*\s+){Regex.Escape(firstArg)}\s*=\s*(?:await\s+)?new\s+(?<type>[A-Za-z_][\w.]*(?:\s*<[^;{{}}()]*>)?)";
            var assignMatches = Regex.Matches(lookback, assignPattern, RegexOptions.RightToLeft);
            foreach (Match m in assignMatches)
            {
                if (!mask.IsCode(windowStart + m.Index)) continue;
                var rawType = m.Groups["type"].Value;
                return ResolveTypeFullName(rawType, declaration, hierarchy) ?? rawType;
            }
        }

        return null;
    }

    private static string? ResolveTypeFullName(
        string writtenType,
        CSharpScanner.ClassDecl contextDecl,
        TypeHierarchy hierarchy)
    {
        var cleaned = ServiceResolutionLookup.CleanTypeReference(writtenType);
        if (cleaned.Length == 0)
            return null;

        var (name, args) = CSharpScanner.ParseTypeReference(cleaned);
        if (name.Length == 0)
            return null;

        var resolvedClass = hierarchy.TryResolveClass(
            name, contextDecl.Namespace, contextDecl.Usings, contextDecl.ContainingTypes, args.Count, out _);
        if (resolvedClass is not null)
        {
            if (args.Count == 0)
                return resolvedClass.FullName;

            var resolvedArgs = new string[args.Count];
            for (var i = 0; i < args.Count; i++)
                resolvedArgs[i] = ResolveTypeFullName(args[i], contextDecl, hierarchy) ?? args[i].Trim();
            return $"{resolvedClass.FullName}<{string.Join(", ", resolvedArgs)}>";
        }

        var resolvedInterface = hierarchy.ResolveInterfaceReference(
            name, contextDecl.Namespace, contextDecl.Usings, contextDecl.ContainingTypes, args.Count, out _);
        if (resolvedInterface is not null)
            return resolvedInterface;

        return null;
    }

    private static string ReplaceGenericPlaceholder(string template, string replacement)
    {
        var lt = template.IndexOf('<');
        var gt = template.LastIndexOf('>');
        if (lt > 0 && gt > lt)
            return $"{template[..(lt + 1)]}{replacement}{template[gt..]}";
        return template;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<(string InterfaceIdentity, string MethodName), ExtensionPublishInfo> BuildExtensionPublishIndex(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        TypeHierarchy hierarchy)
    {
        var index = new Dictionary<(string, string), ExtensionPublishInfo>();

        foreach (var (file, scan, mask) in parsedFiles)
        {
            var code = file.Code;
            if (!code.Contains("this ", StringComparison.Ordinal) ||
                !code.Contains("Publish", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var decl in scan.Classes)
            {
                if (decl.BodySpan.Start < 0 || decl.BodySpan.End <= decl.BodySpan.Start)
                    continue;

                var bodyEnd = Math.Min(code.Length, decl.BodySpan.End);
                foreach (Match match in ExtensionPublishHeaderRegex().Matches(code, decl.BodySpan.Start))
                {
                    if (match.Index >= bodyEnd) break;
                    if (!mask.IsCode(match.Index)) continue;

                    var method = match.Groups["method"].Value;
                    var rawIface = ServiceResolutionLookup.CleanTypeReference(match.Groups["iface"].Value);
                    var (ifaceName, ifaceArgs) = CSharpScanner.ParseTypeReference(rawIface);
                    var ifaceIdentity = hierarchy.ResolveInterfaceReference(
                        ifaceName, decl.Namespace, decl.Usings, decl.ContainingTypes, ifaceArgs.Count, out _)
                        ?? (ifaceArgs.Count > 0
                            ? CSharpScanner.TypeIdentity(CSharpScanner.SimpleName(ifaceName), ifaceArgs.Count)
                            : CSharpScanner.SimpleName(ifaceName));

                    var thisParam = match.Groups["thisParam"].Value.TrimStart('@');
                    var sigEnd = DeepSliceSyntax.FindMethodEnd(code, mask, match.Index + match.Length, bodyEnd);
                    if (sigEnd <= match.Index + match.Length) continue;

                    var methodBody = code[(match.Index + match.Length)..sigEnd];
                    var publishPattern = $@"\b{Regex.Escape(thisParam)}\s*\.\s*(?<pub>Publish(?:Async)?)\s*\(\s*(?:await\s+)?new\s+(?<evt>[A-Za-z_][\w.]*(?:\s*<[^;{{}}()]*>)?)";
                    var pubMatch = Regex.Match(methodBody, publishPattern);
                    if (!pubMatch.Success) continue;

                    var tparams = match.Groups["tparams"].Value;
                    var evtWritten = pubMatch.Groups["evt"].Value.Trim();
                    var usesTypeParam = !string.IsNullOrWhiteSpace(tparams) && evtWritten.Contains('<');

                    index.TryAdd(
                        (ifaceIdentity, method),
                        new ExtensionPublishInfo(
                            ifaceIdentity,
                            method,
                            pubMatch.Groups["pub"].Value,
                            evtWritten,
                            usesTypeParam,
                            decl,
                            file));
                }
            }
        }

        return index;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<ComponentId, string> BuildRepositoryEntityMap(
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        var map = new Dictionary<ComponentId, string>();
        var buffer = new List<ImplementedInterface>();

        foreach (var comp in result.Spine.Components)
        {
            if (!comp.HasRole(ComponentRole.Repository))
                continue;

            var entry = catalog.TryGetById(comp.Id);
            if (entry is null || !entry.IsConcrete)
                continue;

            foreach (var part in entry.Declarations)
            {
                buffer.Clear();
                hierarchy.CollectImplementedInterfaces(part.Declaration, buffer);
                foreach (var iface in buffer)
                {
                    if (iface.IsOpenGeneric || iface.Arguments.Count == 0)
                        continue;

                    var simple = CSharpSyntax.SimpleNameFromIdentity(iface.Identity);
                    if (ServiceRegistrationParser.IsRepositoryContractSimpleName(simple))
                    {
                        map.TryAdd(comp.Id, iface.Arguments[0]);
                        break;
                    }
                }
            }
        }

        return map;
    }

    private static class TokenTables
    {
        internal static readonly string[] ServiceLocatorTokens =
        {
            "EngineContext.Current.Resolve<",
            "EngineContext.Current.ResolveAll<",
            "EngineContext.Current.Resolve(",
            ".GetRequiredService<",
            ".GetService<",
        };

        internal static readonly string[] DynamicReflectionTokens =
        {
            "Activator.CreateInstance",
            "ActivatorUtilities.CreateInstance",
            "Type.GetType(",
            "ResolveUnregistered(",
            "FindClassesOfType",
        };

        internal static readonly string[] ConventionDiMarkers =
        {
            "RegisterAssemblyTypes",
            ".Scan(",
            "FindClassesOfType",
            ".GetTypes(",
        };
    }

    private static HashSet<(string FilePath, int Line)>? RecordStartupConventionRegistrations(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog catalog,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result)
    {
        HashSet<(string FilePath, int Line)>? recordedLines = null;
        for (var i = 0; i < parsedFiles.Count; i++)
        {
            var (file, _, mask) = parsedFiles[i];
            if (catalog.IsTestProject(file.ProjectName))
                continue;

            var code = file.Code;
            if (!code.Contains("RegisterAssemblyTypes", StringComparison.Ordinal) &&
                !code.Contains(".Scan(", StringComparison.Ordinal) &&
                !code.Contains("FindClassesOfType", StringComparison.Ordinal) &&
                !code.Contains(".GetTypes(", StringComparison.Ordinal))
            {
                continue;
            }

            RecordFileConventionRegistrations(file, mask, lineIndices, result, ref recordedLines);
        }

        return recordedLines;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordFileConventionRegistrations(
        SourceFile file,
        SourceMask mask,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result,
        ref HashSet<(string FilePath, int Line)>? recordedLines)
    {
        var code = file.Code;
        var hasReg = code.Contains("AddScoped", StringComparison.Ordinal) ||
                     code.Contains("AddTransient", StringComparison.Ordinal) ||
                     code.Contains("AddSingleton", StringComparison.Ordinal) ||
                     code.Contains("RegisterType", StringComparison.Ordinal) ||
                     code.Contains("RegisterAssemblyTypes", StringComparison.Ordinal);

        var hasConventionKeyword =
            code.Contains("RegisterAssemblyTypes", StringComparison.Ordinal) ||
            code.Contains(".Scan(", StringComparison.Ordinal) ||
            (hasReg && code.Contains("FindClassesOfType", StringComparison.Ordinal)) ||
            (hasReg && code.Contains(".GetTypes(", StringComparison.Ordinal));

        if (!hasConventionKeyword || !lineIndices.TryGetValue(file.Path, out var lineIndex))
            return;

        foreach (var marker in TokenTables.ConventionDiMarkers)
        {
            if (!code.Contains(marker, StringComparison.Ordinal))
                continue;

            var pos = 0;
            while (pos < code.Length)
            {
                var idx = code.IndexOf(marker, pos, StringComparison.Ordinal);
                if (idx < 0) break;
                pos = idx + marker.Length;
                if (!mask.IsCode(idx)) continue;

                var loc = lineIndex.Create(idx);
                if (loc is not null)
                    (recordedLines ??= new HashSet<(string FilePath, int Line)>()).Add((file.Path, loc.Line));
                result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.ConventionRegistration,
                    "CQLENS_F2_CONVENTION_DI",
                    marker.TrimStart('.').TrimEnd('('),
                    $"Dynamic convention/assembly-scanning registration detected in '{lineIndex.RelativePath}'.",
                    loc,
                    file.ProjectName));
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ScanProductionFilesUnproven(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        HashSet<(string FilePath, int Line)>? conventionLines,
        ref ServiceResolutionLookup? lookup,
        ref HashSet<string>? injectedSignatures,
        ref HashSet<string>? injectedIdentities,
        AnalysisResult result)
    {
        HashSet<string>? unimplementedPluginContracts = null;
        HashSet<string>? pluginContractsSeen = null;

        for (var i = 0; i < parsedFiles.Count; i++)
        {
            var (file, scan, mask) = parsedFiles[i];
            if (catalog.IsTestProject(file.ProjectName))
                continue;

            var code = file.Code;
            var hasLocator =
                code.Contains("EngineContext", StringComparison.Ordinal) ||
                code.Contains(".GetRequiredService<", StringComparison.Ordinal) ||
                code.Contains(".GetService<", StringComparison.Ordinal);
            var hasReflection =
                code.Contains("FindClassesOfType", StringComparison.Ordinal) ||
                code.Contains("CreateInstance", StringComparison.Ordinal) ||
                code.Contains("Type.GetType(", StringComparison.Ordinal) ||
                code.Contains("ResolveUnregistered(", StringComparison.Ordinal);
            var hasPluginManager = code.Contains("PluginManager", StringComparison.Ordinal);

            if (!hasLocator && !hasReflection && !hasPluginManager)
                continue;

            if (!lineIndices.TryGetValue(file.Path, out var lineIndex))
                continue;

            if (hasLocator || hasReflection)
            {
                lookup ??= new ServiceResolutionLookup(result, hierarchy);
                injectedSignatures ??= new HashSet<string>(StringComparer.Ordinal);
                injectedIdentities ??= new HashSet<string>(StringComparer.Ordinal);

                RecordFileRuntimeTokens(
                    file, scan, mask, lineIndex, lookup, hasLocator, hasReflection,
                    conventionLines, injectedSignatures, injectedIdentities, result);
            }

            if (hasPluginManager)
            {
                lookup ??= new ServiceResolutionLookup(result, hierarchy);
                unimplementedPluginContracts ??= new HashSet<string>(StringComparer.Ordinal);
                pluginContractsSeen ??= new HashSet<string>(StringComparer.Ordinal);

                RecordPluginManagerContractsInFile(
                    file, scan, mask, lineIndex, lookup, unimplementedPluginContracts, pluginContractsSeen, result);
            }
        }

        if (result.Spine.Unproven.AmbiguousResolutions.Count > 0)
        {
            PruneAmbiguousResolutions(result, injectedSignatures, injectedIdentities);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PruneAmbiguousResolutions(
        AnalysisResult result,
        HashSet<string>? injectedSignatures,
        HashSet<string>? injectedIdentities)
    {
        result.Spine.Unproven.PruneAmbiguousResolutions(entry =>
            HasExplicitRegistration(entry) ||
            (injectedSignatures is not null && injectedSignatures.Contains(entry.InterfaceSignature)) ||
            (injectedIdentities is not null && injectedIdentities.Contains(entry.InterfaceName)));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordFileRuntimeTokens(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        SourceLineIndex lineIndex,
        ServiceResolutionLookup lookup,
        bool hasLocator,
        bool hasReflection,
        HashSet<(string FilePath, int Line)>? conventionLines,
        HashSet<string> injectedSignatures,
        HashSet<string> injectedIdentities,
        AnalysisResult result)
    {
        var code = file.Code;

        if (hasLocator)
        {
            foreach (var locatorToken in TokenTables.ServiceLocatorTokens)
            {
                if (!code.Contains(locatorToken, StringComparison.Ordinal))
                    continue;

                var pos = 0;
                while (pos < code.Length)
                {
                    var idx = code.IndexOf(locatorToken, pos, StringComparison.Ordinal);
                    if (idx < 0) break;
                    pos = idx + locatorToken.Length;
                    if (!mask.IsCode(idx)) continue;

                    var enclosing = DeepSliceSyntax.InnermostClass(scan.Classes, idx);
                    if (locatorToken.EndsWith('<') && enclosing is not null)
                    {
                        var typeArg = ExtractTypeArgument(code, pos, Math.Min(code.Length, pos + 160));
                        if (typeArg.Length > 0)
                        {
                            var resolved = lookup.Resolve(typeArg, enclosing);
                            if (resolved is not null)
                            {
                                injectedSignatures.Add(resolved.InterfaceSignature);
                                injectedIdentities.Add(resolved.InterfaceIdentity);
                            }
                        }
                    }

                    var subject = enclosing?.FullName ?? lineIndex.RelativePath;
                    var loc = lineIndex.Create(idx);
                    result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
                        UnprovenItemKind.ServiceLocator,
                        "CQLENS_F2_SERVICE_LOCATOR",
                        subject,
                        $"Runtime service locator '{locatorToken.TrimStart('.').TrimEnd('<', '(')}' used inside '{subject}'.",
                        loc,
                        file.ProjectName));
                }
            }
        }

        if (hasReflection)
        {
            foreach (var reflToken in TokenTables.DynamicReflectionTokens)
            {
                if (!code.Contains(reflToken, StringComparison.Ordinal))
                    continue;

                var pos = 0;
                while (pos < code.Length)
                {
                    var idx = code.IndexOf(reflToken, pos, StringComparison.Ordinal);
                    if (idx < 0) break;
                    pos = idx + reflToken.Length;
                    if (!mask.IsCode(idx)) continue;

                    var loc = lineIndex.Create(idx);
                    if (loc is not null &&
                        reflToken == "FindClassesOfType" &&
                        conventionLines is not null &&
                        conventionLines.Contains((file.Path, loc.Line)))
                    {
                        continue;
                    }

                    var enclosing = DeepSliceSyntax.InnermostClass(scan.Classes, idx);
                    var subject = enclosing?.FullName ?? lineIndex.RelativePath;
                    result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
                        UnprovenItemKind.DynamicReflection,
                        "CQLENS_F2_DYNAMIC_REFLECTION",
                        subject,
                        $"Dynamic reflection API '{reflToken.TrimEnd('(')}' used inside '{subject}'.",
                        loc,
                        file.ProjectName));
                }
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RecordPluginManagerContractsInFile(
        SourceFile file,
        CSharpScanner.FileScan scan,
        SourceMask mask,
        SourceLineIndex lineIndex,
        ServiceResolutionLookup lookup,
        HashSet<string> unimplementedPluginContracts,
        HashSet<string> pluginContractsSeen,
        AnalysisResult result)
    {
        if (scan.Classes.Count == 0)
            return;

        foreach (Match match in PluginManagerContractRegex().Matches(file.Code))
        {
            if (!mask.IsCode(match.Index))
                continue;

            var declaring = DeepSliceSyntax.InnermostClass(scan.Classes, match.Index) ?? scan.Classes[0];
            var contractArg = match.Groups["contract"].Value.Trim();
            if (contractArg.Length == 0 || declaring.TypeParameters.Contains(contractArg))
                continue;

            var target = lookup.Resolve(contractArg, declaring);
            if (target is null)
                continue;

            var siteLoc = lineIndex.Create(match.Index);
            var siteKey = $"{declaring.FullName}->{target.InterfaceSignature}";
            if (pluginContractsSeen.Add(siteKey))
            {
                result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.DynamicReflection,
                    "CQLENS_F2_DYNAMIC_REFLECTION",
                    target.InterfaceSignature,
                    $"Plugin contract '{target.InterfaceSignature}' is resolved dynamically through plugin manager discovery in '{declaring.FullName}', not static DI.",
                    siteLoc,
                    file.ProjectName));
            }

            if (target.IsUnimplementedInterface && unimplementedPluginContracts.Add(target.InterfaceSignature))
            {
                result.Spine.Unproven.AddObservation(new UnprovenDiagnosticEntry(
                    UnprovenItemKind.UnimplementedInterface,
                    "CQLENS_F2_UNIMPLEMENTED_INTERFACE",
                    target.InterfaceSignature,
                    $"Plugin contract interface '{target.InterfaceSignature}' has no implementing types in scanned source (referenced via '{declaring.FullName}').",
                    target.InterfaceLocation ?? siteLoc,
                    file.ProjectName));
            }
        }
    }

    private static bool HasExplicitRegistration(AmbiguousResolutionEntry entry)
    {
        for (var i = 0; i < entry.Candidates.Count; i++)
        {
            if (entry.Candidates[i].FromRegistration)
                return true;
        }
        return false;
    }

    private static string ExtractTypeArgument(string code, int start, int limit)
    {
        var depth = 1;
        for (var i = start; i < limit; i++)
        {
            var ch = code[i];
            if (ch == '<') depth++;
            else if (ch == '>')
            {
                depth--;
                if (depth == 0)
                    return code[start..i].Trim();
            }
            else if (ch == ';' || ch == '{' || ch == '}')
            {
                return string.Empty;
            }
        }
        return string.Empty;
    }

    private static bool AlreadyRecordedAtLocation(
        ArchitectureSpine spine,
        RelationKey key,
        SourceLocation? location,
        Dictionary<RelationKey, HashSet<SourceLocation>> recordedLocationsCache)
    {
        if (location is null)
            return false;

        if (recordedLocationsCache.TryGetValue(key, out var cached))
            return !cached.Add(location);

        if (!spine.TryGetRelation(key, out var existing))
            return false;

        var occs = existing!.Occurrences;
        if (occs.Count <= 8)
        {
            for (var i = 0; i < occs.Count; i++)
            {
                if (Equals(occs[i].Location, location))
                    return true;
            }
            return false;
        }

        var set = new HashSet<SourceLocation>(occs.Count + 4);
        for (var i = 0; i < occs.Count; i++)
        {
            if (occs[i].Location is { } loc)
                set.Add(loc);
        }
        recordedLocationsCache[key] = set;
        return !set.Add(location);
    }

    private static bool IsSimpleIdentifier(string value)
    {
        if (value.Length == 0 || (!char.IsLetter(value[0]) && value[0] != '_'))
            return false;
        for (var i = 1; i < value.Length; i++)
        {
            if (!char.IsLetterOrDigit(value[i]) && value[i] != '_')
                return false;
        }
        return true;
    }

    private static string NormalizeType(string type) => MemberInvocationScanner.CollapseWhitespace(type);
}
