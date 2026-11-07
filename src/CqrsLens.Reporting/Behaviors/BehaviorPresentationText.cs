namespace CqrsLens;

/// <summary>
/// The words that carry the registered-order view's honesty label. Shared so the
/// Markdown note, the HTML note and the diagram caption say the same thing.
/// </summary>
internal static class BehaviorPresentationText
{
    /// <summary>
    /// The label that must appear in the view: this is registered order, and execution
    /// order is not claimed by the scan.
    /// </summary>
    internal const string RegisteredOrderNote =
        "Behaviors are listed in registered order — the order of their DI registration " +
        "expressions in the scanned source. Execution order is not guaranteed and is not " +
        "proven by the scan.";

    /// <summary>Heading for behaviors whose registration site could not be resolved.</summary>
    internal const string UnregisteredHeading = "Registration site not found — order not determinable";

    /// <summary>
    /// The diagram caption. States registered order and disclaims execution order; when
    /// some behaviors have no resolvable site it says so rather than implying their order.
    /// </summary>
    internal static string ChainCaption(IReadOnlyList<BehaviorInfo> behaviors)
    {
        var unregistered = behaviors.Count(b => b.RegistrationSite is null);
        var caption =
            $"{MermaidDiagramText.CountText(behaviors.Count, "behavior")} in registered order. " +
            "Execution order is not guaranteed and is not proven by the scan.";
        if (unregistered > 0)
        {
            caption += unregistered == 1
                ? " 1 behavior has no resolvable registration site and is appended after the registered ones."
                : $" {unregistered} behaviors have no resolvable registration site and are appended after the registered ones.";
            caption += " Their order is not determinable.";
        }
        return caption;
    }
}
