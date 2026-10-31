using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Regex patterns shared by more than one deep-slice responsibility. Keeping them in one
/// place prevents the request, index-building, handler and endpoint readers from drifting.
/// </summary>
internal static partial class DeepSlicePatterns
{
    [GeneratedRegex(@"\bnew\s+(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[\(\{]")]
    internal static partial Regex ObjectCreationRegex();

    [GeneratedRegex(@"\.\s*(?:Publish|Send|InvokeAsync)\s*<\s*(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[,>]")]
    internal static partial Regex GenericDispatchRegex();

    [GeneratedRegex(@"\b(?<type>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s+@?[A-Za-z_]\w*\s*[,)=]")]
    internal static partial Regex ParameterTypeRegex();

    [GeneratedRegex(@"\.\s*(?<prop>[A-Za-z_]\w*)\b")]
    internal static partial Regex MemberAccessRegex();

    [GeneratedRegex(@"\.\s*Set\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>\s*\(")]
    internal static partial Regex DbContextSetMethodRegex();

    [GeneratedRegex(@"\b(?:I\w*Repository|\w*Repository|DbSet)\s*<\s*(?<entity>(?:global::)?[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*[,>]")]
    internal static partial Regex GenericRepositoryRegex();

    [GeneratedRegex(@"^I?(?<entity>[A-Z]\w+?)Repository$")]
    internal static partial Regex NamedRepositoryRegex();

    [GeneratedRegex(@"\b(?<type>[A-Z]\w*)\s*\.\s*(?<method>[A-Z]\w*)\s*\(")]
    internal static partial Regex StaticMethodCallRegex();
}
