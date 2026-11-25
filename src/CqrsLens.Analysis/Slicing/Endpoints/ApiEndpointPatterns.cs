using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>Regular expressions shared by the endpoint discovery strategies.</summary>
internal static partial class ApiEndpointPatterns
{
    [GeneratedRegex(@"\[Route\s*\(\s*@?""(?<route>[^""]*)""\s*\)\]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    public static partial Regex RouteAttribute();

    [GeneratedRegex(@"\[Http(?<verb>Get|Post|Put|Delete|Patch)(?:\s*\(\s*@?""(?<route>[^""]*)""\s*\))?\s*\]", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 2000)]
    public static partial Regex HttpVerbAttribute();

    [GeneratedRegex(@"(?<attrs>(?:\[\s*[^\[\]]*\]\s*)+)(?:(?:public|protected|internal|virtual|override|async|sealed|new)\s+)+[^\(\{;=]+?\s+(?<method>[A-Za-z_]\w*)\s*\(", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    public static partial Regex ControllerAction();

    [GeneratedRegex(@"\b(?<var>[A-Za-z_]\w*)\s*=\s*[^;=]{1,400}?\.MapGroup\s*\(\s*@?""(?<prefix>[^""]*)""\s*\)", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    public static partial Regex MapGroup();

    [GeneratedRegex(@"(?<target>[A-Za-z_]\w*)\s*\.\s*Map(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*@?""(?<route>[^""]*)""", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    public static partial Regex MinimalApiMap();

    // Matches Jason Taylor CleanArchitecture style: groupBuilder.MapPost(CreateTodoItem) or groupBuilder.MapPut(UpdateTodoItem, "{id}")
    [GeneratedRegex(@"(?<target>[A-Za-z_]\w*)\s*\.\s*Map(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*(?<handler>[A-Za-z_]\w*)\s*(?:,\s*@?""(?<route>[^""]*)"")?\s*\)", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    public static partial Regex MethodGroupMinimalApi();

    [GeneratedRegex(@"(?:(?:public|private|protected|internal|static|async)\s+)+(?<ret>[^\(\{;=]+?)\s+(?<method>[A-Za-z_]\w*)\s*\(", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
    public static partial Regex LocalMethodDeclaration();

    // Matches FastEndpoints route calls: Post("/route") OR Post(CreateContributorRequest.Route)
    [GeneratedRegex(@"\b(?<verb>Get|Post|Put|Delete|Patch)\s*\(\s*(?:@?""(?<route>[^""]*)""|(?<constRef>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+))\s*\)", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    public static partial Regex FastEndpointVerb();

    [GeneratedRegex(@"\bconst\s+string\s+(?<name>[A-Za-z_]\w*)\s*=\s*@?""(?<value>[^""]*)""", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    public static partial Regex ConstString();
}
