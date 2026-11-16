using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// F2 R-ENDPOINT (severable): discovers conventional and non-CQRS MVC/WebAPI controller
/// actions on <see cref="ComponentRole.Controller"/> components directly into
/// <see cref="ArchitectureSpine.Endpoints"/> and <see cref="RelationKind.Exposes"/> without
/// mutating the legacy <see cref="AnalysisResult.Endpoints"/> list.
/// </summary>
internal static partial class ConventionalControllerEndpointDiscovery
{
    private static readonly HashSet<string> ActionResultSimpleNames = new(StringComparer.Ordinal)
    {
        "IActionResult",
        "ActionResult",
        "IResult",
        "ViewResult",
        "PartialViewResult",
        "JsonResult",
        "ContentResult",
        "RedirectResult",
        "RedirectToActionResult",
        "RedirectToRouteResult",
        "LocalRedirectResult",
        "FileResult",
        "FileContentResult",
        "FileStreamResult",
        "PhysicalFileResult",
        "VirtualFileResult",
        "EmptyResult",
        "StatusCodeResult",
        "ObjectResult",
        "OkResult",
        "OkObjectResult",
        "CreatedResult",
        "CreatedAtActionResult",
        "CreatedAtRouteResult",
        "AcceptedResult",
        "NoContentResult",
        "BadRequestResult",
        "BadRequestObjectResult",
        "UnauthorizedResult",
        "ForbidResult",
        "ChallengeResult",
        "NotFoundResult",
        "NotFoundObjectResult",
        "ConflictResult",
        "ViewComponentResult",
    };

    [GeneratedRegex(@"(?:^|[\[,]\s*)NonAction(?:Attribute)?\s*(?:\(\s*\))?\s*(?=[,\]])", RegexOptions.IgnoreCase)]
    private static partial Regex NonActionAttributeRegex();

    [GeneratedRegex(@"(?:^|[\[,]\s*)Area(?:Attribute)?\s*\(\s*@?""(?<area>[^""]*)""\s*\)\s*(?=[,\]])", RegexOptions.IgnoreCase)]
    private static partial Regex AreaAttributeRegex();

    [GeneratedRegex(@"(?:^|[\[,]\s*)ActionName(?:Attribute)?\s*\(\s*@?""(?<name>[^""]*)""\s*\)\s*(?=[,\]])", RegexOptions.IgnoreCase)]
    private static partial Regex ActionNameAttributeRegex();

    [GeneratedRegex(@"(?:^|[\[,]\s*)AcceptVerbs(?:Attribute)?\s*\(\s*@?""(?<verb>[A-Za-z]+)""[^\)]*\)\s*(?=[,\]])", RegexOptions.IgnoreCase)]
    private static partial Regex AcceptVerbsAttributeRegex();

    [GeneratedRegex(@"(?:^|[\[,]\s*)Http(?<verb>Get|Post|Put|Delete|Patch)(?:Attribute)?\s*(?:\(\s*(?:@?""(?<route>[^""]*)"")?[^\)]*\))?\s*(?=[,\]])", RegexOptions.IgnoreCase)]
    private static partial Regex HttpVerbAttributeRegex();

    [GeneratedRegex(@"(?:^|[\[,]\s*)Route(?:Attribute)?\s*\(\s*@?""(?<route>[^""]*)""[^\)]*\)\s*(?=[,\]])", RegexOptions.IgnoreCase)]
    private static partial Regex RouteAttributeRegex();

    public static void Analyze(
        ScannedTypeCatalog catalog,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result)
    {
        if (catalog.DiscoveredControllerCount == 0 || result.Spine.ComponentsCount == 0)
            return;

        AnalyzeCore(catalog, lineIndices, result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AnalyzeCore(
        ScannedTypeCatalog catalog,
        IReadOnlyDictionary<string, SourceLineIndex> lineIndices,
        AnalysisResult result)
    {

        var existingSites = new HashSet<(string File, int Line)>();
        var existingByName = new HashSet<(string File, string Name)>();
        var existingIds = new HashSet<EndpointId>();
        foreach (var existing in result.Spine.Endpoints)
        {
            existingIds.Add(existing.Id);
            if (existing.Location is not null)
            {
                existingSites.Add((existing.Location.File, existing.Location.Line));
                existingByName.Add((existing.Location.File, existing.Name));
            }
        }

        // Snapshot controllers because we only add endpoints/relations, not components.
        var controllers = new List<ComponentNode>();
        foreach (var comp in result.Spine.Components)
        {
            if (comp.HasRole(ComponentRole.Controller))
                controllers.Add(comp);
        }

        foreach (var controller in controllers)
        {
            var entry = catalog.TryGetById(controller.Id);
            if (entry is null || !entry.IsConcrete || entry.IsTestProject)
                continue;

            foreach (var part in entry.Declarations)
            {
                if (catalog.IsTestProject(part.File.ProjectName))
                    continue;
                if (!lineIndices.TryGetValue(part.File.Path, out var lineIndex))
                    continue;

                ScanControllerPart(
                    controller,
                    part.Declaration,
                    part.File,
                    part.Mask,
                    lineIndex,
                    existingSites,
                    existingByName,
                    existingIds,
                    result);
            }
        }
    }

    private static void ScanControllerPart(
        ComponentNode controller,
        CSharpScanner.ClassDecl declaration,
        SourceFile file,
        SourceMask mask,
        SourceLineIndex lineIndex,
        HashSet<(string File, int Line)> existingSites,
        HashSet<(string File, string Name)> existingByName,
        HashSet<EndpointId> existingIds,
        AnalysisResult result)
    {
        var code = file.Code;
        if (declaration.BodySpan.Start < 0 || declaration.BodySpan.End <= declaration.BodySpan.Start)
            return;

        var openBrace = declaration.BodySpan.Start;
        while (openBrace < declaration.BodySpan.End && openBrace < code.Length &&
               !(mask.IsCode(openBrace) && code[openBrace] == '{'))
        {
            openBrace++;
        }

        if (openBrace >= declaration.BodySpan.End || openBrace >= code.Length)
            return;

        var bodyStart = openBrace + 1;
        var bodyEnd = Math.Min(code.Length, declaration.BodySpan.End - 1);
        if (bodyStart >= bodyEnd)
            return;

        var headerAttrs = ExtractHeaderAttributes(code, declaration.DeclarationIndex);
        var classRouteMatch = RouteAttributeRegex().Match(headerAttrs);
        var hasExplicitClassRoute = classRouteMatch.Success;
        var classRoute = hasExplicitClassRoute ? classRouteMatch.Groups["route"].Value : "";
        var areaMatch = AreaAttributeRegex().Match(headerAttrs);
        var classArea = areaMatch.Success ? areaMatch.Groups["area"].Value.Trim() : "";
        if (string.IsNullOrEmpty(classRoute) && !string.IsNullOrEmpty(classArea))
            classRoute = classArea + "/[controller]";

        var controllerStem = ControllerStem(declaration.Name);
        var depth = 0;
        var bracketDepth = 0;
        var inInitializer = false;
        var memberStart = bodyStart;

        for (var i = bodyStart; i < bodyEnd; i++)
        {
            if (!mask.IsCode(i))
                continue;

            var ch = code[i];
            if (bracketDepth > 0)
            {
                if (ch == '[') bracketDepth++;
                else if (ch == ']') bracketDepth--;
                continue;
            }

            if (depth == 0 && ch == '[')
            {
                bracketDepth = 1;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                if (depth > 0) depth--;
                if (depth == 0)
                {
                    memberStart = i + 1;
                    inInitializer = false;
                }
                continue;
            }

            if (depth != 0)
                continue;

            if (ch == '#')
            {
                while (i < bodyEnd && code[i] != '\n')
                    i++;
                memberStart = i + 1;
                inInitializer = false;
                continue;
            }

            if (ch == ';')
            {
                memberStart = i + 1;
                inInitializer = false;
                continue;
            }

            if (ch == '=' && !inInitializer)
            {
                inInitializer = true;
                continue;
            }

            if (inInitializer || ch != '(')
                continue;

            var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, i);
            if (closeParen < 0 || closeParen >= bodyEnd)
                break;

            // Skip past member body/initializer so top-level scanning stays at class-member depth 0.
            var afterMember = SkipMethodSignatureTailAndBody(code, mask, closeParen + 1, bodyEnd, out var isMethodBody);
            if (isMethodBody)
            {
                TryRecordAction(
                    controller,
                    declaration,
                    file,
                    code,
                    mask,
                    lineIndex,
                    memberStart,
                    i,
                    classRoute,
                    hasExplicitClassRoute,
                    controllerStem,
                    existingSites,
                    existingByName,
                    existingIds,
                    result);
            }

            memberStart = afterMember;
            inInitializer = false;
            i = afterMember - 1;
        }
    }

    private static void TryRecordAction(
        ComponentNode controller,
        CSharpScanner.ClassDecl declaration,
        SourceFile file,
        string code,
        SourceMask mask,
        SourceLineIndex lineIndex,
        int memberStart,
        int openParen,
        string classRoute,
        bool hasExplicitClassRoute,
        string controllerStem,
        HashSet<(string File, int Line)> existingSites,
        HashSet<(string File, string Name)> existingByName,
        HashSet<EndpointId> existingIds,
        AnalysisResult result)
    {
        var signatureSlice = code[memberStart..openParen];
        if (string.IsNullOrWhiteSpace(signatureSlice))
            return;

        if (!ContainsWord(signatureSlice, "public") ||
            ContainsWord(signatureSlice, "static") ||
            ContainsWord(signatureSlice, "class") ||
            ContainsWord(signatureSlice, "struct") ||
            ContainsWord(signatureSlice, "record") ||
            ContainsWord(signatureSlice, "interface") ||
            ContainsWord(signatureSlice, "enum") ||
            ContainsWord(signatureSlice, "delegate"))
        {
            return;
        }

        var (attrs, modifierStartOffset) = SplitLeadingAttributes(code, mask, memberStart, openParen);
        if (attrs.Length > 0 && NonActionAttributeRegex().IsMatch(attrs))
            return;

        var preParen = code[modifierStartOffset..openParen].TrimEnd();
        if (!TryExtractReturnTypeAndMethodName(preParen, out var returnType, out var methodName))
            return;

        if (string.Equals(methodName, declaration.Name, StringComparison.Ordinal))
            return;

        var verbMatch = attrs.Length > 0 ? HttpVerbAttributeRegex().Match(attrs) : Match.Empty;
        var acceptVerbsMatch = !verbMatch.Success && attrs.Length > 0
            ? AcceptVerbsAttributeRegex().Match(attrs)
            : Match.Empty;
        var routeMatch = attrs.Length > 0 ? RouteAttributeRegex().Match(attrs) : Match.Empty;

        var hasExplicitEndpointAttr = verbMatch.Success || acceptVerbsMatch.Success || routeMatch.Success;
        if (!hasExplicitEndpointAttr && !IsConventionalActionReturnType(returnType))
            return;

        var endpointName = $"{declaration.Name}.{methodName}";
        var siteIndex = FindMethodLineAnchor(code, mask, memberStart, openParen);
        var location = lineIndex.Create(siteIndex) ?? controller.Location;
        if (location is null)
            return;

        if (existingSites.Contains((location.File, location.Line)) ||
            existingByName.Contains((location.File, endpointName)))
        {
            return;
        }

        var actionNameMatch = attrs.Length > 0 ? ActionNameAttributeRegex().Match(attrs) : Match.Empty;
        var effectiveActionName = actionNameMatch.Success ? actionNameMatch.Groups["name"].Value.Trim() : methodName;
        if (effectiveActionName.Length == 0)
            effectiveActionName = methodName;

        string httpMethod;
        EvidenceStrength verbStrength;
        if (verbMatch.Success)
        {
            httpMethod = verbMatch.Groups["verb"].Value.ToUpperInvariant();
            verbStrength = EvidenceStrength.Proven;
        }
        else if (acceptVerbsMatch.Success)
        {
            httpMethod = acceptVerbsMatch.Groups["verb"].Value.ToUpperInvariant();
            verbStrength = EvidenceStrength.Proven;
        }
        else
        {
            httpMethod = "UNKNOWN";
            verbStrength = EvidenceStrength.Inferred;
        }

        var methodRoute = verbMatch.Success && verbMatch.Groups["route"].Success
            ? verbMatch.Groups["route"].Value
            : routeMatch.Success
                ? routeMatch.Groups["route"].Value
                : "";

        var fullRoute = string.IsNullOrEmpty(methodRoute) && string.IsNullOrEmpty(classRoute)
            ? $"/{controllerStem}/{effectiveActionName}"
            : EndpointRoutes.ForController(
                classRoute,
                methodRoute,
                controllerStem,
                effectiveActionName);
        if (string.IsNullOrEmpty(methodRoute) &&
            !hasExplicitClassRoute &&
            !string.IsNullOrEmpty(classRoute))
        {
            fullRoute = EndpointRoutes.Combine(
                EndpointRoutes.ForController(classRoute, "", controllerStem, effectiveActionName),
                effectiveActionName);
        }

        var hasExplicitMethodRoute = !string.IsNullOrEmpty(methodRoute);
        var routeStrength = hasExplicitMethodRoute || hasExplicitClassRoute
            ? EvidenceStrength.Proven
            : EvidenceStrength.Inferred;

        var ordinal = 0;
        var epId = EndpointId.Create(controller.Project, location, ordinal);
        while (!existingIds.Add(epId))
        {
            ordinal++;
            epId = EndpointId.Create(controller.Project, location, ordinal);
        }

        var origin = hasExplicitEndpointAttr ? EvidenceOrigin.MappingOrAttribute : EvidenceOrigin.Declaration;
        var evidenceStrength = verbStrength == EvidenceStrength.Inferred || routeStrength == EvidenceStrength.Inferred
            ? EvidenceStrength.Inferred
            : EvidenceStrength.Proven;

        var occurrence = new EvidenceOccurrence(
            EvidenceRule.ControllerEndpointExposure,
            origin,
            location,
            evidenceStrength,
            scopeProjectName: file.ProjectName,
            isTestProject: false);

        var endpointNode = new EndpointNode(
            epId,
            endpointName,
            httpMethod,
            fullRoute,
            verbStrength,
            routeStrength,
            occurrence);

        result.Spine.AddEndpoint(endpointNode);
        result.Spine.AddOrMergeRelation(
            RelationKind.Exposes,
            NodeReference.ForComponent(controller.Id),
            NodeReference.ForEndpoint(epId),
            occurrence);

        existingSites.Add((location.File, location.Line));
        existingByName.Add((location.File, endpointName));
    }

    private static int SkipMethodSignatureTailAndBody(
        string code,
        SourceMask mask,
        int afterCloseParen,
        int limit,
        out bool isMethodBody)
    {
        isMethodBody = false;
        var i = afterCloseParen;
        while (i < limit)
        {
            if (!mask.IsCode(i))
            {
                i++;
                continue;
            }

            var ch = code[i];
            if (ch == '{')
            {
                isMethodBody = true;
                var depth = 0;
                for (; i < limit; i++)
                {
                    if (!mask.IsCode(i)) continue;
                    if (code[i] == '{') depth++;
                    else if (code[i] == '}')
                    {
                        depth--;
                        if (depth == 0) return i + 1;
                    }
                }
                return limit;
            }

            if (ch == ';')
                return i + 1;

            if (ch == '=' && i + 1 < limit && code[i + 1] == '>')
            {
                isMethodBody = true;
                var nestedBrace = 0;
                var nestedParen = 0;
                for (i += 2; i < limit; i++)
                {
                    if (!mask.IsCode(i)) continue;
                    switch (code[i])
                    {
                        case '{': nestedBrace++; break;
                        case '}': if (nestedBrace > 0) nestedBrace--; break;
                        case '(': nestedParen++; break;
                        case ')': if (nestedParen > 0) nestedParen--; break;
                        case ';' when nestedBrace == 0 && nestedParen == 0:
                            return i + 1;
                    }
                }
                return limit;
            }

            i++;
        }

        return limit;
    }

    private static (string Attributes, int ModifierStart) SplitLeadingAttributes(
        string code,
        SourceMask mask,
        int start,
        int end)
    {
        var cursor = start;
        var firstAttr = -1;
        var lastAttrEnd = start;

        while (cursor < end)
        {
            while (cursor < end && (char.IsWhiteSpace(code[cursor]) || !mask.IsCode(cursor)))
                cursor++;

            if (cursor < end && code[cursor] == '#')
            {
                while (cursor < end && code[cursor] != '\n')
                    cursor++;
                continue;
            }

            if (cursor < end && code[cursor] == '[')
            {
                firstAttr = firstAttr < 0 ? cursor : firstAttr;
                var depth = 0;
                while (cursor < end)
                {
                    if (mask.IsCode(cursor))
                    {
                        if (code[cursor] == '[') depth++;
                        else if (code[cursor] == ']')
                        {
                            depth--;
                            if (depth == 0)
                            {
                                cursor++;
                                lastAttrEnd = cursor;
                                break;
                            }
                        }
                    }
                    cursor++;
                }
                continue;
            }

            break;
        }

        if (firstAttr < 0)
            return ("", cursor);

        return (code[firstAttr..lastAttrEnd], lastAttrEnd);
    }

    private static bool TryExtractReturnTypeAndMethodName(
        string preParen,
        out string returnType,
        out string methodName)
    {
        returnType = "";
        methodName = "";
        if (string.IsNullOrWhiteSpace(preParen))
            return false;

        var end = preParen.Length;
        while (end > 0 && char.IsWhiteSpace(preParen[end - 1]))
            end--;

        if (end > 0 && preParen[end - 1] == '>')
        {
            var depth = 0;
            var pos = end - 1;
            for (; pos >= 0; pos--)
            {
                if (preParen[pos] == '>') depth++;
                else if (preParen[pos] == '<')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = pos;
                        break;
                    }
                }
            }
            while (end > 0 && char.IsWhiteSpace(preParen[end - 1]))
                end--;
        }

        var nameEnd = end;
        var nameStart = nameEnd;
        while (nameStart > 0 && (char.IsLetterOrDigit(preParen[nameStart - 1]) || preParen[nameStart - 1] == '_'))
            nameStart--;

        if (nameStart == nameEnd)
            return false;

        methodName = preParen[nameStart..nameEnd];
        var beforeName = preParen[..nameStart].TrimEnd();
        if (beforeName.Length == 0 || beforeName.EndsWith("public", StringComparison.Ordinal))
            return false;

        // Extract last type token (supporting generic return types like Task<IActionResult>).
        var angleDepth = 0;
        var typeStart = beforeName.Length;
        for (var i = beforeName.Length - 1; i >= 0; i--)
        {
            var c = beforeName[i];
            if (c == '>') angleDepth++;
            else if (c == '<') { if (angleDepth > 0) angleDepth--; }
            else if (angleDepth == 0 && char.IsWhiteSpace(c))
            {
                typeStart = i + 1;
                break;
            }
            if (i == 0) typeStart = 0;
        }

        returnType = beforeName[typeStart..].Trim();
        return returnType.Length > 0;
    }

    private static bool IsConventionalActionReturnType(string returnType)
    {
        var normalized = returnType.TrimEnd('?', ' ');
        var simple = UnwrapAsyncWrapper(normalized);
        var lt = simple.IndexOf('<');
        if (lt > 0)
            simple = simple[..lt];
        var dot = simple.LastIndexOf('.');
        if (dot >= 0)
            simple = simple[(dot + 1)..];

        return ActionResultSimpleNames.Contains(simple);
    }

    private static string UnwrapAsyncWrapper(string typeName)
    {
        var core = typeName;
        var dot = core.IndexOf('<');
        if (dot < 0)
            return core;

        var outer = core[..dot].Trim();
        var outerDot = outer.LastIndexOf('.');
        var outerSimple = outerDot >= 0 ? outer[(outerDot + 1)..] : outer;
        if (outerSimple is "Task" or "ValueTask" && core.EndsWith('>'))
            return core[(dot + 1)..^1].Trim().TrimEnd('?');

        return core;
    }

    private static string ExtractHeaderAttributes(string code, int declarationIndex)
    {
        var windowStart = Math.Max(0, declarationIndex - 800);
        return code[windowStart..declarationIndex];
    }

    private static string ControllerStem(string className)
        => className.EndsWith("Controller", StringComparison.Ordinal) && className.Length > "Controller".Length
            ? className[..^"Controller".Length]
            : className;

    private static int FindMethodLineAnchor(string code, SourceMask mask, int memberStart, int openParen)
    {
        for (var i = memberStart; i < openParen; i++)
        {
            if (!mask.IsCode(i) || char.IsWhiteSpace(code[i]))
                continue;
            if (code[i] == '#')
            {
                while (i < openParen && code[i] != '\n')
                    i++;
                continue;
            }
            return i;
        }
        return memberStart;
    }

    private static bool ContainsWord(string text, string word)
    {
        var pos = 0;
        while (pos < text.Length)
        {
            var idx = text.IndexOf(word, pos, StringComparison.Ordinal);
            if (idx < 0) return false;
            var leftOk = idx == 0 || (!char.IsLetterOrDigit(text[idx - 1]) && text[idx - 1] != '_');
            var rightIdx = idx + word.Length;
            var rightOk = rightIdx >= text.Length || (!char.IsLetterOrDigit(text[rightIdx]) && text[rightIdx] != '_');
            if (leftOk && rightOk) return true;
            pos = idx + word.Length;
        }
        return false;
    }
}
