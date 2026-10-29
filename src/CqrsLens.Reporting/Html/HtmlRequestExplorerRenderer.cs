using System.Text;

namespace CqrsLens;

/// <summary>Renders the requests table and the interactive CQRS explorer.</summary>
internal static class HtmlRequestExplorerRenderer
{
    internal static void Render(
        StringBuilder html,
        AnalysisResult result,
        DiagramRenderMode renderMode)
    {
        html.AppendLine("<h2>Requests</h2>");
        if (result.Requests.Count == 0)
        {
            html.AppendLine("<p><em>No MediatR requests found.</em></p>");
            return;
        }

        var index = RequestExplorerIndex.Create(result);
        RequestTableRenderer.Render(html, result, index);
        RequestExplorerRenderer.Render(html, result, renderMode, index);
    }
}
