using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>Builds normalized route templates from controller, group and endpoint parts.</summary>
internal static class EndpointRoutes
{
    public static string ForController(
        string classRoute,
        string methodRoute,
        string controllerStem,
        string actionName)
    {
        string ReplaceTokens(string template)
        {
            var replaced = Regex.Replace(
                template, @"\[controller\]", controllerStem, RegexOptions.IgnoreCase);
            return Regex.Replace(
                replaced, @"\[action\]", actionName.ToLowerInvariant(), RegexOptions.IgnoreCase);
        }

        var prefix = ReplaceTokens(classRoute.Trim());
        var sub = ReplaceTokens(methodRoute.Trim());

        if (sub.StartsWith('/') || sub.StartsWith("~/"))
            return Normalize(sub.TrimStart('~'));

        if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(sub))
            return $"/{controllerStem}";

        return Combine(prefix, sub);
    }

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
