namespace CqrsLens;

/// <summary>Links discovered shapes to the requests and notifications they target.</summary>
internal static class ShapeLinker
{
    public static void Link(AnalysisResult result)
    {
        RequestLinker.Link(result);
        NotificationLinker.Link(result);
    }
}
