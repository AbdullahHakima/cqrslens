namespace CqrsLens;

/// <summary>
/// Recovers explicit DI registrations from source text. This is deliberate,
/// narrow parsing of the two forms the resolution index consumes:
///
/// <list type="bullet">
/// <item>MS-DI: <c>AddScoped/AddTransient/AddSingleton&lt;I, Impl&gt;()</c>,
/// <c>AddX(typeof(I), typeof(Impl))</c> and the factory form
/// <c>AddX&lt;I&gt;(sp =&gt; sp.GetService&lt;Impl&gt;())</c> (also
/// <c>GetRequiredService</c>).</item>
/// <item>Autofac: <c>RegisterType&lt;Impl&gt;().As&lt;I&gt;()</c>,
/// <c>RegisterType(typeof(Impl)).As(typeof(I))</c>. <c>.AsSelf()</c> is a no-op for
/// the index.</item>
/// </list>
///
/// Convention-based scanning forms (<c>RegisterAssemblyTypes</c>, Scrutor/ABP
/// assembly scanning) are out of scope and ignored silently. Comments, strings and
/// interpolations are skipped through the shared <see cref="SourceMask"/>.
/// </summary>
internal static class ServiceRegistrationParser
{
    private static readonly string[] MsDiMethods = { "AddScoped", "AddTransient", "AddSingleton" };

    public static List<ServiceRegistration> Parse(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles)
    {
        var results = new List<ServiceRegistration>();
        foreach (var (file, _, mask) in parsedFiles)
        {
            var code = file.Code;
            if (code.IndexOf("AddScoped", StringComparison.Ordinal) < 0 &&
                code.IndexOf("AddTransient", StringComparison.Ordinal) < 0 &&
                code.IndexOf("AddSingleton", StringComparison.Ordinal) < 0 &&
                code.IndexOf("RegisterType", StringComparison.Ordinal) < 0)
                continue;

            ParseMsDi(file, mask, results);
            ParseAutofac(file, mask, results);
        }
        return results;
    }

    private static void ParseMsDi(SourceFile file, SourceMask mask, List<ServiceRegistration> results)
    {
        var code = file.Code;
        foreach (var method in MsDiMethods)
        {
            var search = 0;
            while (search < code.Length)
            {
                var index = code.IndexOf(method, search, StringComparison.Ordinal);
                if (index < 0) break;
                search = index + method.Length;

                if (!mask.IsCode(index) || !IsIdentifierBoundary(code, index - 1) ||
                    !IsIdentifierBoundary(code, index + method.Length))
                    continue;

                var cursor = SkipWhitespace(code, index + method.Length);
                List<string>? genericArguments = null;
                if (cursor < code.Length && code[cursor] == '<')
                {
                    var close = FindMatchingAngle(code, cursor);
                    if (close < 0) continue;
                    genericArguments = CSharpScanner.SplitTopLevel(code[(cursor + 1)..close]);
                    cursor = SkipWhitespace(code, close + 1);
                }

                if (cursor >= code.Length || code[cursor] != '(') continue;
                var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, cursor);
                if (closeParen < 0) continue;

                var bodyStart = cursor + 1;
                var bodyEnd = closeParen;
                AddMsDiRegistration(
                    file, mask, index, genericArguments, bodyStart, bodyEnd, results);
            }
        }
    }

    private static void AddMsDiRegistration(
        SourceFile file,
        SourceMask mask,
        int index,
        List<string>? genericArguments,
        int bodyStart,
        int bodyEnd,
        List<ServiceRegistration> results)
    {
        if (genericArguments is { Count: 2 })
        {
            // AddScoped<I, Impl>() — the interface first, implementation second.
            Add(file, index, genericArguments[0], genericArguments[1],
                ServiceRegistrationKind.MsDi, ServiceRegistrationForm.GenericPair, results);
            return;
        }

        if (genericArguments is { Count: 1 })
        {
            // AddScoped<I>(sp => sp.GetService<Impl>())
            var factory = FindFactoryImplementation(file, mask, bodyStart, bodyEnd);
            if (factory is not null)
                Add(file, index, genericArguments[0], factory,
                    ServiceRegistrationKind.MsDi, ServiceRegistrationForm.Factory, results);
            return;
        }

        // No generic arguments: AddScoped(typeof(I), typeof(Impl)) or a typeof factory.
        var typeofs = FindTypeofArguments(file, mask, bodyStart, bodyEnd);
        if (typeofs.Count >= 2)
        {
            Add(file, index, typeofs[0], typeofs[1],
                ServiceRegistrationKind.MsDi, ServiceRegistrationForm.TypeOfPair, results);
            return;
        }

        if (typeofs.Count == 1)
        {
            var factory = FindFactoryImplementation(file, mask, bodyStart, bodyEnd);
            if (factory is not null)
                Add(file, index, typeofs[0], factory,
                    ServiceRegistrationKind.MsDi, ServiceRegistrationForm.Factory, results);
        }
    }

    private static void ParseAutofac(SourceFile file, SourceMask mask, List<ServiceRegistration> results)
    {
        var code = file.Code;
        var search = 0;
        while (search < code.Length)
        {
            var index = code.IndexOf("RegisterType", search, StringComparison.Ordinal);
            if (index < 0) break;
            search = index + "RegisterType".Length;

            if (!mask.IsCode(index) || !IsIdentifierBoundary(code, index - 1) ||
                !IsIdentifierBoundary(code, index + "RegisterType".Length))
                continue;

            var cursor = SkipWhitespace(code, index + "RegisterType".Length);
            string? implementation = null;
            var implementationArity = 0;
            var implementationOpen = false;

            if (cursor < code.Length && code[cursor] == '<')
            {
                var close = FindMatchingAngle(code, cursor);
                if (close < 0) continue;
                var inner = CSharpScanner.SplitTopLevel(code[(cursor + 1)..close]);
                if (inner.Count == 0) continue;
                (implementation, _, implementationOpen, implementationArity) = ParseType(inner[0]);
                cursor = SkipWhitespace(code, close + 1);
            }
            else if (cursor < code.Length && code[cursor] == '(')
            {
                var close = DeepSliceSyntax.FindMatchingParen(code, mask, cursor);
                if (close < 0) continue;
                var typeofs = FindTypeofArguments(file, mask, cursor + 1, close);
                if (typeofs.Count == 0) continue;
                (implementation, _, implementationOpen, implementationArity) = ParseType(typeofs[0]);
                cursor = SkipWhitespace(code, close + 1);
            }

            if (implementation is null) continue;

            // Walk the call chain to its statement end collecting .As<...>/.As(typeof(...)).
            var chainEnd = FindStatementEnd(code, mask, cursor);
            var asSearch = cursor;
            while (asSearch < chainEnd)
            {
                var asIndex = code.IndexOf(".As", asSearch, StringComparison.Ordinal);
                if (asIndex < 0 || asIndex >= chainEnd) break;
                asSearch = asIndex + 3;

                if (!mask.IsCode(asIndex)) continue;
                if (!IsIdentifierBoundary(code, asIndex - 1)) continue;

                var asCursor = asIndex + 3;
                if (asCursor < code.Length && IsIdentifierStart(code[asCursor]))
                {
                    // .AsSelf() and friends: a named no-op, not an interface mapping.
                    continue;
                }

                asCursor = SkipWhitespace(code, asCursor);
                string? service = null;
                if (asCursor < code.Length && code[asCursor] == '<')
                {
                    var close = FindMatchingAngle(code, asCursor);
                    if (close < 0) continue;
                    service = code[(asCursor + 1)..close];
                    asSearch = close + 1;
                }
                else if (asCursor < code.Length && code[asCursor] == '(')
                {
                    var close = DeepSliceSyntax.FindMatchingParen(code, mask, asCursor);
                    if (close < 0) continue;
                    var typeofs = FindTypeofArguments(file, mask, asCursor + 1, close);
                    if (typeofs.Count == 0) continue;
                    service = typeofs[0];
                    asSearch = close + 1;
                }

                if (service is null) continue;
                Add(file, index, service, implementation,
                    ServiceRegistrationKind.Autofac, ServiceRegistrationForm.AutofacAs, results,
                    implementationOpen, implementationArity);
            }
        }
    }

    private static void Add(
        SourceFile file,
        int index,
        string serviceWritten,
        string implementationWritten,
        ServiceRegistrationKind kind,
        ServiceRegistrationForm form,
        List<ServiceRegistration> results,
        bool implementationOpenOverride = false,
        int implementationArityOverride = 0)
    {
        var (serviceName, serviceArgs, serviceOpen, serviceArity) = ParseType(serviceWritten);
        if (serviceName.Length == 0) return;

        var (implementationName, implementationArgs, implementationOpen, implementationArity) =
            ParseType(implementationWritten);
        if (implementationName.Length == 0) return;

        if (implementationOpenOverride) implementationOpen = true;
        if (implementationArityOverride > implementationArity)
            implementationArity = implementationArityOverride;

        results.Add(new ServiceRegistration(
            file,
            index,
            serviceName,
            serviceArgs,
            serviceOpen,
            serviceArity,
            implementationName,
            implementationArgs,
            implementationOpen,
            implementationArity,
            kind,
            form));
    }

    /// <summary>
    /// Parses a written type reference into its base name, top-level generic arguments
    /// and open/closed state. Empty arguments (the <c>&lt;,&gt;</c> convention) mark an
    /// open generic reference and are preserved so the arity stays correct.
    /// </summary>
    private static (string Name, List<string> Arguments, bool IsOpen, int Arity) ParseType(string written)
    {
        var text = written.Trim();
        if (text.StartsWith("global::", StringComparison.Ordinal))
            text = text[8..];

        var angle = text.IndexOf('<');
        if (angle < 0)
            return (text.Trim(), new List<string>(), false, 0);

        var name = text[..angle].Trim();
        var close = FindMatchingAngle(text, angle);
        if (close < 0)
            return (name, new List<string>(), false, 0);

        var inner = text.Substring(angle + 1, close - angle - 1);
        var arguments = SplitPreservingEmpty(inner);
        var isOpen = arguments.Any(argument => argument.Length == 0);
        return (name, arguments, isOpen, arguments.Count);
    }

    private static string? FindFactoryImplementation(
        SourceFile file,
        SourceMask mask,
        int bodyStart,
        int bodyEnd)
    {
        var code = file.Code;
        foreach (var method in new[] { "GetService", "GetRequiredService" })
        {
            var search = bodyStart;
            while (search < bodyEnd)
            {
                var index = code.IndexOf(method, search, StringComparison.Ordinal);
                if (index < 0 || index >= bodyEnd) break;
                search = index + method.Length;

                if (!mask.IsCode(index) || !IsIdentifierBoundary(code, index - 1) ||
                    !IsIdentifierBoundary(code, index + method.Length))
                    continue;

                var cursor = SkipWhitespace(code, index + method.Length);
                if (cursor >= bodyEnd || code[cursor] != '<') continue;
                var close = FindMatchingAngle(code, cursor);
                if (close < 0 || close > bodyEnd) continue;
                return code[(cursor + 1)..close];
            }
        }
        return null;
    }

    private static List<string> FindTypeofArguments(
        SourceFile file,
        SourceMask mask,
        int start,
        int end)
    {
        var code = file.Code;
        var results = new List<string>();
        var search = start;
        while (search < end)
        {
            var index = code.IndexOf("typeof", search, StringComparison.Ordinal);
            if (index < 0 || index >= end) break;
            search = index + "typeof".Length;

            if (!mask.IsCode(index) || !IsIdentifierBoundary(code, index - 1) ||
                !IsIdentifierBoundary(code, index + "typeof".Length))
                continue;

            var cursor = SkipWhitespace(code, index + "typeof".Length);
            if (cursor >= end || code[cursor] != '(') continue;
            var close = DeepSliceSyntax.FindMatchingParen(code, mask, cursor);
            if (close < 0 || close > end) continue;
            results.Add(code[(cursor + 1)..close]);
            search = close + 1;
        }
        return results;
    }

    private static int FindStatementEnd(string code, SourceMask mask, int start)
    {
        for (var i = start; i < code.Length; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] is ';' or '{' or '}') return i;
        }
        return code.Length;
    }

    private static int FindMatchingAngle(string text, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            if (text[i] == '<') depth++;
            else if (text[i] == '>')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static List<string> SplitPreservingEmpty(string inner)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < inner.Length; i++)
        {
            var c = inner[i];
            if (c is '<' or '(' or '[') depth++;
            else if (c is '>' or ')' or ']') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(inner[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(inner[start..].Trim());
        return parts;
    }

    private static int SkipWhitespace(string code, int index)
    {
        while (index < code.Length && char.IsWhiteSpace(code[index])) index++;
        return index;
    }

    private static bool IsIdentifierBoundary(string code, int index)
        => index < 0 || index >= code.Length || !IsIdentifierPart(code[index]);

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
}
