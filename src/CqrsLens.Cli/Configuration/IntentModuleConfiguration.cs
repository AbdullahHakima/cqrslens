namespace CqrsLens;

/// <summary>On-disk module selector group under <c>intent.modules</c>.</summary>
internal sealed class IntentModuleConfiguration
{
    public List<string>? Projects { get; set; }
    public List<string>? Components { get; set; }
}
