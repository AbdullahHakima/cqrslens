namespace CqrsLens;

internal sealed class JsonAmbiguousResolution
{
    public string InterfaceSignature { get; set; } = "";
    public string InterfaceName { get; set; } = "";
    public JsonLocation? InterfaceLocation { get; set; }
    public List<JsonServiceCandidate> Candidates { get; set; } = new();
}
