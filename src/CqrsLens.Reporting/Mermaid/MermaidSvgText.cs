using System.Globalization;
using System.Net;

namespace CqrsLens;

/// <summary>Low-level SVG text encoding and deterministic numeric formatting.</summary>
internal static class MermaidSvgText
{
    internal static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");

    internal static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
