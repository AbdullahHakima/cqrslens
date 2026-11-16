using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Implements F2 R-CTRL (ASP.NET Core / MVC / WebAPI controller role discovery by
/// inheritance or attributes, guarding <c>[NonController]</c> and abstract/static classes)
/// and R-EXP (<see cref="RelationKind.Exposes"/> links from controllers to endpoints).
/// </summary>
internal static partial class ControllerRoleRules
{
    private const int MaxInheritanceDepth = 16;

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][\w.]*?)(?:Attribute)?\s*(?:\(|$)", RegexOptions.Singleline)]
    private static partial Regex AttributeNamePattern();

    [GeneratedRegex(@"\[\s*(?<attr>Http(?:Get|Post|Put|Delete|Patch|Head|Options)|AcceptVerbs|Route)(?:Attribute)?\s*(?:\(|\]|,)", RegexOptions.Singleline)]
    private static partial Regex ActionAttributePattern();

    /// <summary>Discovers <see cref="ComponentRole.Controller"/> components across the catalog.</summary>
    public static int ApplyControllers(
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        if (!MayContainControllers(catalog))
            return 0;

        return ApplyControllersCore(catalog, hierarchy, result);
    }

    private static bool MayContainControllers(ScannedTypeCatalog catalog)
    {
        var entries = catalog.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            var decls = entries[i].Declarations;
            for (var j = 0; j < decls.Count; j++)
            {
                var code = decls[j].File.Code;
                if (code.Contains("Controller", StringComparison.Ordinal) ||
                    code.Contains("Route", StringComparison.Ordinal) ||
                    code.Contains("[Http", StringComparison.Ordinal) ||
                    code.Contains("[AcceptVerbs", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ApplyControllersCore(
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy,
        AnalysisResult result)
    {
        var discovered = 0;
        foreach (var entry in catalog.Entries)
        {
            if (!entry.IsConcrete) continue;

            var detection = DetectControllerEvidence(entry, catalog, hierarchy);
            if (detection is null) continue;
            if (HasNonControllerInHierarchy(entry, catalog, hierarchy)) continue;

            var (origin, location) = detection.Value;
            result.Spine.AddOrMergeComponent(
                entry.Id,
                entry.PrimaryLocation,
                ComponentRole.Controller,
                new EvidenceOccurrence(
                    EvidenceRule.ControllerDiscovery,
                    origin,
                    location ?? entry.PrimaryLocation,
                    EvidenceStrength.Proven,
                    scopeProjectName: entry.ProjectName,
                    isTestProject: entry.IsTestProject));
            discovered++;
        }

        return discovered;
    }

    /// <summary>Emits <see cref="RelationKind.Exposes"/> from each discovered controller to its endpoints.</summary>
    public static void ApplyExposes(ScannedTypeCatalog catalog, AnalysisResult result)
    {
        foreach (var endpoint in result.Endpoints)
        {
            if (endpoint.SpineId is null || endpoint.ControllerId is null)
                continue;

            if (!result.Spine.TryGetComponent(endpoint.ControllerId, out var controller) ||
                !controller!.HasRole(ComponentRole.Controller))
            {
                continue;
            }

            result.Spine.AddOrMergeRelation(
                RelationKind.Exposes,
                NodeReference.ForComponent(controller.Id),
                NodeReference.ForEndpoint(endpoint.SpineId),
                new EvidenceOccurrence(
                    EvidenceRule.ControllerEndpointExposure,
                    EvidenceOrigin.MappingOrAttribute,
                    endpoint.Location ?? controller.Location,
                    endpoint.IsInferred ? EvidenceStrength.Inferred : EvidenceStrength.Proven,
                    scopeProjectName: endpoint.ProjectName,
                    isTestProject: catalog.IsTestProject(endpoint.ProjectName)));
        }
    }

    private static (EvidenceOrigin Origin, SourceLocation? Location)? DetectControllerEvidence(
        ScannedComponentEntry entry,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy)
    {
        foreach (var part in entry.Declarations)
        {
            if (InheritsAspNetController(part.Declaration, hierarchy))
                return (EvidenceOrigin.Declaration, part.Location);
        }

        foreach (var part in entry.Declarations)
        {
            if (HasHeaderControllerAttribute(part.File.Code, part.Mask, part.Declaration.DeclarationIndex) ||
                HasBodyActionAttribute(part.Declaration, part.File, part.Mask, catalog))
            {
                return (EvidenceOrigin.MappingOrAttribute, part.Location);
            }
        }

        return null;
    }

    private static bool InheritsAspNetController(
        CSharpScanner.ClassDecl declaration,
        TypeHierarchy hierarchy)
    {
        if (string.IsNullOrWhiteSpace(declaration.BaseList))
            return false;

        var queue = new Queue<(CSharpScanner.ClassDecl Decl, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue((declaration, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            if (depth >= MaxInheritanceDepth || string.IsNullOrWhiteSpace(current.BaseList))
                continue;

            var visitKey = CSharpSyntax.TypeIdentity(current.FullName, current.TypeParameters.Count);
            if (!visited.Add(visitKey))
                continue;

            foreach (var part in CSharpScanner.SplitTopLevel(current.BaseList))
            {
                var (baseName, args) = CSharpSyntax.ParseTypeReference(part);
                if (baseName.Length == 0) continue;

                var resolvedClass = hierarchy.TryResolveClass(
                    baseName, current.Namespace, current.Usings, current.ContainingTypes, args.Count, out _);

                if (resolvedClass is not null)
                {
                    if (IsFrameworkControllerBase(resolvedClass.Namespace, resolvedClass.Name))
                        return true;

                    queue.Enqueue((resolvedClass, depth + 1));
                    continue;
                }

                var simple = CSharpSyntax.SimpleName(baseName);
                if (simple is "Controller" or "ControllerBase" or "ApiController")
                    return true;
            }
        }

        return false;
    }

    private static bool HasNonControllerInHierarchy(
        ScannedComponentEntry entry,
        ScannedTypeCatalog catalog,
        TypeHierarchy hierarchy)
    {
        var queue = new Queue<(CSharpScanner.ClassDecl Decl, SourceFile File, SourceMask Mask, int Depth)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in entry.Declarations)
            queue.Enqueue((part.Declaration, part.File, part.Mask, 0));

        while (queue.Count > 0)
        {
            var (current, file, mask, depth) = queue.Dequeue();
            if (HasSpecificHeaderAttribute(file.Code, mask, current.DeclarationIndex, isNonControllerCheck: true))
                return true;

            if (depth >= MaxInheritanceDepth || string.IsNullOrWhiteSpace(current.BaseList))
                continue;

            var key = CSharpSyntax.TypeIdentity(current.FullName, current.TypeParameters.Count);
            if (!visited.Add(key))
                continue;

            foreach (var basePart in CSharpScanner.SplitTopLevel(current.BaseList))
            {
                var (baseName, args) = CSharpSyntax.ParseTypeReference(basePart);
                if (baseName.Length == 0) continue;

                var resolvedBase = hierarchy.TryResolveClass(
                    baseName, current.Namespace, current.Usings, current.ContainingTypes, args.Count, out _);
                if (resolvedBase is null) continue;

                var resolvedEntry = catalog.GetByDeclaration(resolvedBase);
                if (resolvedEntry is null) continue;

                foreach (var baseDeclPart in resolvedEntry.Declarations)
                    queue.Enqueue((baseDeclPart.Declaration, baseDeclPart.File, baseDeclPart.Mask, depth + 1));
            }
        }

        return false;
    }

    private static bool IsFrameworkControllerBase(string? ns, string simpleName)
        => simpleName is "Controller" or "ControllerBase" or "ApiController" &&
           !string.IsNullOrEmpty(ns) &&
           (ns.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
            ns.StartsWith("System.Web", StringComparison.Ordinal));

    private static bool HasHeaderControllerAttribute(string code, SourceMask mask, int declarationIndex)
        => HasSpecificHeaderAttribute(code, mask, declarationIndex, isNonControllerCheck: false);

    private static bool HasSpecificHeaderAttribute(
        string code,
        SourceMask mask,
        int declarationIndex,
        bool isNonControllerCheck)
    {
        var start = Math.Max(0, declarationIndex - 600);
        for (var i = declarationIndex - 1; i >= start; i--)
        {
            if (mask.IsCode(i) && code[i] is ';' or '}' or '{')
            {
                start = i + 1;
                break;
            }
        }

        var headerSpan = code.AsSpan(start, declarationIndex - start);
        if (!headerSpan.Contains('['))
            return false;
        if (isNonControllerCheck)
        {
            if (!headerSpan.Contains("NonController", StringComparison.Ordinal))
                return false;
        }
        else if (!headerSpan.Contains("Controller", StringComparison.Ordinal) &&
                 !headerSpan.Contains("Route", StringComparison.Ordinal))
        {
            return false;
        }

        for (var i = start; i < declarationIndex; i++)
        {
            if (!mask.IsCode(i) || code[i] != '[') continue;
            var close = FindMatchingBracket(code, mask, i, declarationIndex);
            if (close < 0) continue;

            var inner = code[(i + 1)..close];
            if (inner.Contains(':', StringComparison.Ordinal) &&
                inner.TrimStart().StartsWith("assembly", StringComparison.Ordinal))
            {
                i = close;
                continue;
            }

            foreach (var (segStart, segEnd) in SplitCodeSegments(code, mask, i + 1, close))
            {
                var segment = code[segStart..segEnd];
                var match = AttributeNamePattern().Match(segment);
                if (!match.Success) continue;

                var absNameIndex = segStart + match.Groups["name"].Index;
                if (!mask.IsCode(absNameIndex)) continue;

                var simple = CSharpSyntax.SimpleName(match.Groups["name"].Value);
                if (isNonControllerCheck)
                {
                    if (simple == "NonController") return true;
                }
                else
                {
                    if (simple is "ApiController" or "Controller" or "Route")
                        return true;
                }
            }

            i = close;
        }

        return false;
    }

    private static List<(int Start, int End)> SplitCodeSegments(
        string code,
        SourceMask mask,
        int start,
        int end)
    {
        var segments = new List<(int Start, int End)>();
        var depth = 0;
        var segStart = start;
        for (var i = start; i < end; i++)
        {
            if (!mask.IsCode(i)) continue;
            var c = code[i];
            if (c is '<' or '(' or '[') depth++;
            else if (c is '>' or ')' or ']') depth--;
            else if (c == ',' && depth == 0)
            {
                segments.Add((segStart, i));
                segStart = i + 1;
            }
        }
        segments.Add((segStart, end));
        return segments;
    }

    private static bool HasBodyActionAttribute(
        CSharpScanner.ClassDecl declaration,
        SourceFile file,
        SourceMask mask,
        ScannedTypeCatalog catalog)
    {
        var code = file.Code;
        if (declaration.BodySpan.End <= declaration.BodySpan.Start)
            return false;

        var bodyEnd = Math.Min(code.Length, declaration.BodySpan.End);
        var bodyLen = bodyEnd - declaration.BodySpan.Start;
        var span = code.AsSpan(declaration.BodySpan.Start, bodyLen);
        if (!span.Contains("[Http", StringComparison.Ordinal) &&
            !span.Contains("[Route", StringComparison.Ordinal) &&
            !span.Contains("[AcceptVerbs", StringComparison.Ordinal))
        {
            return false;
        }

        var scanClasses = catalog.GetScanClasses(file.Path);
        var bodySlice = code[declaration.BodySpan.Start..bodyEnd];
        foreach (Match match in ActionAttributePattern().Matches(bodySlice))
        {
            var absIndex = declaration.BodySpan.Start + match.Index;
            if (!mask.IsCode(absIndex)) continue;

            var innermost = DeepSliceSyntax.InnermostClass(scanClasses, absIndex);
            if (innermost is not null &&
                ReferenceEquals(catalog.GetByDeclaration(innermost), catalog.GetByDeclaration(declaration)))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindMatchingBracket(string code, SourceMask mask, int openIndex, int limit)
    {
        var depth = 0;
        for (var i = openIndex; i < limit; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '[') depth++;
            else if (code[i] == ']')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }
}
