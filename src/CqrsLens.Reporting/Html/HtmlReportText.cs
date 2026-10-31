using System.Net;

namespace CqrsLens;

/// <summary>HTML text encoding shared by report section renderers.</summary>
internal static class HtmlReportText
{
    internal static string Encode(string? value) => WebUtility.HtmlEncode(value ?? "");
}
