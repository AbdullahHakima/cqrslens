namespace CqrsLens;

internal sealed class JsonServiceCandidate
{
    public string Type { get; set; } = "";
    public bool DeclaredInScan { get; set; }
    public bool FromRegistration { get; set; }
    public JsonLocation? Location { get; set; }
    public JsonLocation? RegistrationLocation { get; set; }
}
