namespace CqrsLens;

/// <summary>Embedded CSS and Mermaid bootstrap assets for the portable HTML report.</summary>
internal static class HtmlReportAssets
{
    internal const string MermaidVersion = "12.2.0";
    internal const string MermaidIntegrity =
        "sha384-8xUhuLNMjW/cI2Dacg3DR4WliYznvIg0LVG1stctThKD/YVrJFI9TNCFxft5NYgp";

    internal const string Css = HtmlReportStyles.Css;
    internal const string MermaidBootstrapScript = HtmlReportScripts.MermaidBootstrapScript;
}
