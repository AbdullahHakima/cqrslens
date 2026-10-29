using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders a single architecture-check finding as an HTML list item.</summary>
internal static class HtmlCheckRenderer
{
    internal static void Append(StringBuilder html, CheckFinding check, bool known)
    {
        var cls = known
            ? "info"
            : check.Severity == CheckSeverity.Error ? "error" : "warning";
        var badge = known
            ? "ℹ️ Known"
            : check.Severity == CheckSeverity.Error ? "❌ Error" : "⚠️ Warning";
        var locality = check.Location is null
            ? ""
            : $" <code>{Encode($"{check.Location.File}:{check.Location.Line}:{check.Location.Column}")}</code>";
        html.AppendLine(
            $"<li class=\"{cls}\">{badge} <strong>{Encode(check.Code)}</strong> — {Encode(check.Message)}{locality}</li>");
    }
}