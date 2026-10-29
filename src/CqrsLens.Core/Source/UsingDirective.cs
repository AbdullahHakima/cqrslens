namespace CqrsLens;

/// <summary>
/// A <c>using</c> directive captured from one source file. Text mode uses these
/// to choose between same-named contracts in different namespaces, which is
/// common in modular solutions.
/// </summary>
public sealed record UsingDirective(
    string Target,
    string? Alias = null,
    bool IsStatic = false,
    bool IsGlobal = false,
    string? ContainerNamespace = null)
{
    /// <summary>
    /// Namespace imports implied by this directive, relative namespace first
    /// (C# looks there first) and the written/global name second.
    /// </summary>
    public IEnumerable<string> NamespaceCandidates()
    {
        if (IsStatic || Alias is not null || Target.Length == 0 || Target.Contains('<'))
            yield break;

        if (!string.IsNullOrEmpty(ContainerNamespace))
            yield return $"{ContainerNamespace}.{Target}";
        yield return Target;
    }

    /// <summary>Renders this directive as a file-scoped C# <c>using</c> statement.</summary>
    public string ToUsingDeclaration()
    {
        if (IsStatic)
            return $"using static {Target};";
        if (!string.IsNullOrEmpty(Alias))
            return $"using {Alias} = {Target};";
        return $"using {Target};";
    }
}
