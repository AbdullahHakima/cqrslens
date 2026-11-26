using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>Builds normalized route templates from controller, group and endpoint parts.</summary>
internal static partial class EndpointRoutes
{
    [GeneratedRegex(@"\[controller\]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ControllerTokenRegex();

    [GeneratedRegex(@"\[action\]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex ActionTokenRegex();

    [GeneratedRegex(@"\[area\]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    private static partial Regex AreaTokenRegex();

    public static string ForController(
        string classRoute,
        string methodRoute,
        string controllerStem,
        string actionName)
    {
        string ReplaceTokens(string template)
        {
            var replaced = ControllerTokenRegex().Replace(template, controllerStem);
            return ActionTokenRegex().Replace(replaced, actionName.ToLowerInvariant());
        }

        var prefix = ReplaceTokens(classRoute.Trim());
        var sub = ReplaceTokens(methodRoute.Trim());

        if (sub.StartsWith('/') || sub.StartsWith("~/"))
            return Normalize(sub.TrimStart('~'));

        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(sub))
            return $"/{controllerStem}";

        return Combine(prefix, sub);
    }

    /// <summary>
    /// Resolves ASP.NET Core controller and MVC action routes for system entrypoints,
    /// supporting <c>[area]</c> / <c>[controller]</c> / <c>[action]</c> tokens and
    /// conventional <c>/{area}/{controller}/{action}</c> fallback for non-<c>[ApiController]</c> MVC actions.
    /// </summary>
    public static string ForSystemController(
        string classRoute,
        string methodRoute,
        string controllerStem,
        string actionName,
        string? areaName = null,
        bool isApiController = false)
    {
        var effectiveAction = StripAsyncSuffix(actionName).ToLowerInvariant();
        var areaSegment = (areaName ?? string.Empty).Trim().Trim('/').ToLowerInvariant();

        string ReplaceTokens(string template)
        {
            var replaced = AreaTokenRegex().Replace(template, areaSegment);
            replaced = ControllerTokenRegex().Replace(replaced, controllerStem);
            return ActionTokenRegex().Replace(replaced, effectiveAction);
        }

        var prefix = ReplaceTokens(classRoute.Trim());
        var sub = ReplaceTokens(methodRoute.Trim());

        if (sub.StartsWith('/') || sub.StartsWith("~/"))
            return Normalize(sub.TrimStart('~'));

        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(sub))
        {
            if (isApiController || string.IsNullOrEmpty(effectiveAction))
                return $"/{controllerStem}";

            return !string.IsNullOrEmpty(areaSegment)
                ? $"/{areaSegment}/{controllerStem}/{effectiveAction}"
                : $"/{controllerStem}/{effectiveAction}";
        }

        return Combine(prefix, sub);
    }

    private static string StripAsyncSuffix(string actionName)
        => actionName.Length > 5 && actionName.EndsWith("Async", StringComparison.OrdinalIgnoreCase)
            ? actionName[..^5]
            : actionName;

    public static string Combine(string? prefix, string? route)
    {
        var left = (prefix ?? "").Trim().Trim('/');
        var right = (route ?? "").Trim().Trim('/');
        if (string.IsNullOrEmpty(left) && string.IsNullOrEmpty(right))
            return "/";
        if (string.IsNullOrEmpty(left))
            return "/" + right;
        if (string.IsNullOrEmpty(right))
            return "/" + left;
        return $"/{left}/{right}";
    }

    public static string Normalize(string route)
    {
        var trimmed = route.Trim();
        if (string.IsNullOrEmpty(trimmed)) return "/";
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
