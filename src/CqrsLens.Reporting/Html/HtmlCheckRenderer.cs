using System.Text;
using static CqrsLens.HtmlReportText;

namespace CqrsLens;

/// <summary>Renders a single architecture-check finding as an HTML list item.</summary>
internal static class HtmlCheckRenderer
{
    internal static void Append(StringBuilder html, CheckFinding check, bool known)
        => Append(html, check, known, check.Message, repeatCount: 1);

    internal static void Append(
        StringBuilder html,
        CheckFinding check,
        bool known,
        string displayMessage,
        int repeatCount)
    {
        var cls = known
            ? "info"
            : check.Severity == CheckSeverity.Error ? "error" : "warning";
        var badge = known
            ? "ℹ️ Known"
            : check.Severity == CheckSeverity.Error ? "❌ Error" : "⚠️ Warning";
        var repeat = repeatCount > 1 ? $" <strong>(×{repeatCount})</strong>" : "";
        var locality = check.Location is null
            ? ""
            : $" <code>{Encode($"{check.Location.File}:{check.Location.Line}:{check.Location.Column}")}</code>";
        html.AppendLine(
            $"<li class=\"{cls}\">{badge} <strong>{Encode(check.Code)}</strong> — {Encode(displayMessage)}{repeat}{locality}</li>");
    }
}
