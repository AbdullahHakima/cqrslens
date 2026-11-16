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
    private static readonly string[] MsDiExplicitMethods = { "AddScoped", "AddTransient", "AddSingleton", "AddHttpClient" };

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
                code.IndexOf("AddHttpClient", StringComparison.Ordinal) < 0 &&
                code.IndexOf("RegisterType", StringComparison.Ordinal) < 0)
                continue;

            ParseMsDi(file, mask, results);
            ParseAutofac(file, mask, results);
        }
        return results;
    }

    /// <summary>
    /// Extracts DI registration sites that name an implementation type (including
    /// 1-argument concrete registrations, <c>AddHttpClient</c>, factory lambdas,
    /// and Autofac <c>RegisterType</c>) for <see cref="ComponentRole.Service"/> discovery.
    /// </summary>
    internal static List<ExplicitServiceRegistration> ParseExplicitRegistrations(
        IReadOnlyList<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)> parsedFiles)
    {
        var results = new List<ExplicitServiceRegistration>();
        foreach (var (file, _, mask) in parsedFiles)
        {
            var code = file.Code;
            if (code.IndexOf("AddScoped", StringComparison.Ordinal) < 0 &&
                code.IndexOf("AddTransient", StringComparison.Ordinal) < 0 &&
                code.IndexOf("AddSingleton", StringComparison.Ordinal) < 0 &&
                code.IndexOf("AddHttpClient", StringComparison.Ordinal) < 0 &&
                code.IndexOf("RegisterType", StringComparison.Ordinal) < 0)
                continue;

            ParseMsDiExplicit(file, mask, results);
            ParseAutofacExplicit(file, mask, results);
        }
        return results;
    }

    internal static bool IsRepositoryContractSimpleName(string simpleName)
        => simpleName.Length >= "IRepository".Length &&
           simpleName[0] == 'I' &&
           char.IsUpper(simpleName[1]) &&
           simpleName.EndsWith("Repository", StringComparison.Ordinal);

    private static void ScanMsDiInvocations(
        SourceFile file,
        SourceMask mask,
        string[] methods,
        Action<string, int, List<string>?, int, int> onInvocation)
    {
        var code = file.Code;
        foreach (var method in methods)
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

                onInvocation(method, index, genericArguments, cursor + 1, closeParen);
            }
        }
    }

    private static void ParseMsDi(SourceFile file, SourceMask mask, List<ServiceRegistration> results)
        => ScanMsDiInvocations(file, mask, MsDiExplicitMethods, (method, index, genericArguments, bodyStart, bodyEnd) =>
            AddMsDiRegistration(file, mask, method, index, genericArguments, bodyStart, bodyEnd, results));

    private static void ParseMsDiExplicit(SourceFile file, SourceMask mask, List<ExplicitServiceRegistration> results)
        => ScanMsDiInvocations(file, mask, MsDiExplicitMethods, (method, index, genericArguments, bodyStart, bodyEnd) =>
            AddMsDiExplicitRegistration(file, mask, method, index, genericArguments, bodyStart, bodyEnd, results));

    private static void AddMsDiExplicitRegistration(
        SourceFile file,
        SourceMask mask,
        string method,
        int index,
        List<string>? genericArguments,
        int bodyStart,
        int bodyEnd,
        List<ExplicitServiceRegistration> results)
    {
        var isHttpClient = string.Equals(method, "AddHttpClient", StringComparison.Ordinal);
        if (genericArguments is { Count: 2 })
        {
            // Regular Add*(I, Impl) pairs resolve via ServiceResolutions; only AddHttpClient<I, Client>
            // is absent from the F1 index and needs direct service attribution here.
            if (!isHttpClient) return;

            var isRepo = IsRepositoryContractWritten(genericArguments[0]);
            AddExplicitType(file, index, genericArguments[1], isRepo, results);
            return;
        }

        if (genericArguments is { Count: 1 })
        {
            var isRepo = IsRepositoryContractWritten(genericArguments[0]);
            AddExplicitType(file, index, genericArguments[0], isRepo, results);
            if (!isHttpClient && HasLambdaArrow(file.Code, mask, bodyStart, bodyEnd))
                AddExplicitFactoryTargets(file, mask, index, bodyStart, bodyEnd, isRepo, includeGetService: false, results);
            return;
        }

        var typeofs = FindTypeofArguments(file, mask, bodyStart, bodyEnd);
        if (typeofs.Count >= 2)
        {
            if (!isHttpClient) return;
            var isRepo = IsRepositoryContractWritten(typeofs[0]);
            AddExplicitType(file, index, typeofs[1], isRepo, results);
            return;
        }

        if (typeofs.Count == 1)
        {
            var isRepo = IsRepositoryContractWritten(typeofs[0]);
            AddExplicitType(file, index, typeofs[0], isRepo, results);
            if (!isHttpClient && HasLambdaArrow(file.Code, mask, bodyStart, bodyEnd))
                AddExplicitFactoryTargets(file, mask, index, bodyStart, bodyEnd, isRepo, includeGetService: false, results);
            return;
        }

        if (!isHttpClient && HasLambdaArrow(file.Code, mask, bodyStart, bodyEnd))
            AddExplicitFactoryTargets(file, mask, index, bodyStart, bodyEnd, isRepo: false, includeGetService: true, results);
    }

    private static void AddExplicitFactoryTargets(
        SourceFile file,
        SourceMask mask,
        int index,
        int bodyStart,
        int bodyEnd,
        bool isRepo,
        bool includeGetService,
        List<ExplicitServiceRegistration> results)
    {
        foreach (var (target, _) in FindFactoryReturnTargets(file, mask, bodyStart, bodyEnd, includeGetService, includeNew: true))
            AddExplicitType(file, index, target, isRepo, results);
    }

    private static void AddExplicitType(
        SourceFile file,
        int index,
        string written,
        bool isRepo,
        List<ExplicitServiceRegistration> results)
    {
        var (name, _, _, arity) = ParseType(written);
        if (name.Length > 0)
            results.Add(new ExplicitServiceRegistration(file, index, name, arity, isRepo));
    }

    private static bool IsRepositoryContractWritten(string written)
    {
        var (name, _, _, _) = ParseType(written);
        return name.Length > 0 && IsRepositoryContractSimpleName(CSharpScanner.SimpleName(name));
    }

    private static void AddMsDiRegistration(
        SourceFile file,
        SourceMask mask,
        string method,
        int index,
        List<string>? genericArguments,
        int bodyStart,
        int bodyEnd,
        List<ServiceRegistration> results)
    {
        var isHttpClient = string.Equals(method, "AddHttpClient", StringComparison.Ordinal);

        if (genericArguments is { Count: 2 })
        {
            // AddScoped<I, Impl>() / AddHttpClient<I, Client>() — interface first, implementation second.
            Add(file, index, genericArguments[0], genericArguments[1],
                ServiceRegistrationKind.MsDi, ServiceRegistrationForm.GenericPair, results);
            return;
        }

        if (!isHttpClient && genericArguments is { Count: 1 })
        {
            // AddScoped<I>(sp => ...)
            foreach (var (factory, isNew) in FindFactoryReturnTargets(file, mask, bodyStart, bodyEnd, includeGetService: true, includeNew: true))
            {
                Add(file, index, genericArguments[0], factory,
                    ServiceRegistrationKind.MsDi, ServiceRegistrationForm.Factory, results,
                    isFactoryNew: isNew);
            }
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

        if (!isHttpClient && typeofs.Count == 1)
        {
            foreach (var (factory, isNew) in FindFactoryReturnTargets(file, mask, bodyStart, bodyEnd, includeGetService: true, includeNew: true))
            {
                Add(file, index, typeofs[0], factory,
                    ServiceRegistrationKind.MsDi, ServiceRegistrationForm.Factory, results,
                    isFactoryNew: isNew);
            }
        }
    }

    private static void ScanAutofacRegisterType(
        SourceFile file,
        SourceMask mask,
        Action<int, string, string, int, bool, int, int> onRegister)
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
            string? rawImplementation = null;
            string? implementation = null;
            var implementationArity = 0;
            var implementationOpen = false;

            if (cursor < code.Length && code[cursor] == '<')
            {
                var close = FindMatchingAngle(code, cursor);
                if (close < 0) continue;
                var inner = CSharpScanner.SplitTopLevel(code[(cursor + 1)..close]);
                if (inner.Count == 0) continue;
                rawImplementation = inner[0];
                (implementation, _, implementationOpen, implementationArity) = ParseType(inner[0]);
                cursor = SkipWhitespace(code, close + 1);
            }
            else if (cursor < code.Length && code[cursor] == '(')
            {
                var close = DeepSliceSyntax.FindMatchingParen(code, mask, cursor);
                if (close < 0) continue;
                var typeofs = FindTypeofArguments(file, mask, cursor + 1, close);
                if (typeofs.Count == 0) continue;
                rawImplementation = typeofs[0];
                (implementation, _, implementationOpen, implementationArity) = ParseType(typeofs[0]);
                cursor = SkipWhitespace(code, close + 1);
            }

            if (rawImplementation is null || implementation is null) continue;

            var chainEnd = FindStatementEnd(code, mask, cursor);
            onRegister(index, rawImplementation, implementation, implementationArity, implementationOpen, cursor, chainEnd);
        }
    }

    private static void ParseAutofac(SourceFile file, SourceMask mask, List<ServiceRegistration> results)
    {
        ScanAutofacRegisterType(file, mask, (index, _, implementation, implementationArity, implementationOpen, cursor, chainEnd) =>
        {
            foreach (var service in EnumerateAutofacAsServices(file, mask, cursor, chainEnd))
            {
                Add(file, index, service, implementation,
                    ServiceRegistrationKind.Autofac, ServiceRegistrationForm.AutofacAs, results,
                    implementationOpen, implementationArity);
            }
        });
    }

    private static void ParseAutofacExplicit(SourceFile file, SourceMask mask, List<ExplicitServiceRegistration> results)
    {
        var code = file.Code;
        ScanAutofacRegisterType(file, mask, (index, rawImplementation, _, _, _, cursor, chainEnd) =>
        {
            var services = EnumerateAutofacAsServices(file, mask, cursor, chainEnd);
            var hasAsSelf = HasAutofacAsSelf(code, mask, cursor, chainEnd);
            if (services.Count == 0 || hasAsSelf)
            {
                AddExplicitType(file, index, rawImplementation, isRepo: false, results);
                return;
            }

            foreach (var service in services)
            {
                if (!IsRepositoryContractWritten(service))
                {
                    AddExplicitType(file, index, rawImplementation, isRepo: false, results);
                    break;
                }
            }
        });
    }

    private static List<string> EnumerateAutofacAsServices(
        SourceFile file,
        SourceMask mask,
        int cursor,
        int chainEnd)
    {
        var code = file.Code;
        var services = new List<string>();
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
                continue;

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

            if (service is not null)
                services.Add(service);
        }
        return services;
    }

    private static bool HasAutofacAsSelf(string code, SourceMask mask, int cursor, int chainEnd)
    {
        var search = cursor;
        while (search < chainEnd)
        {
            var idx = code.IndexOf(".AsSelf", search, StringComparison.Ordinal);
            if (idx < 0 || idx >= chainEnd) break;
            search = idx + ".AsSelf".Length;
            if (mask.IsCode(idx) && IsIdentifierBoundary(code, idx + ".AsSelf".Length))
                return true;
        }
        return false;
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
        int implementationArityOverride = 0,
        bool isFactoryNew = false)
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
            form,
            isFactoryNew));
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

    private static List<(string TypeName, bool IsNew)> FindFactoryReturnTargets(
        SourceFile file,
        SourceMask mask,
        int bodyStart,
        int bodyEnd,
        bool includeGetService,
        bool includeNew)
    {
        var results = new List<(string TypeName, bool IsNew)>();
        var arrowIndex = FindTopLevelLambdaArrow(file.Code, mask, bodyStart, bodyEnd);
        if (arrowIndex < 0)
            return results;

        var code = file.Code;
        var lambdaStart = SkipWhitespace(code, arrowIndex + 2);
        if (lambdaStart >= bodyEnd)
            return results;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (code[lambdaStart] == '{')
        {
            var closeBrace = FindMatchingBrace(code, mask, lambdaStart, bodyEnd);
            if (closeBrace < 0)
                return results;

            ExtractBlockLambdaReturns(
                code, mask, lambdaStart + 1, closeBrace, includeGetService, includeNew, results, seen);
        }
        else
        {
            ExtractReturnedCandidates(
                code, mask, lambdaStart, bodyEnd, includeGetService, includeNew, results, seen);
        }

        return results;
    }

    private static void ExtractBlockLambdaReturns(
        string code,
        SourceMask mask,
        int start,
        int end,
        bool includeGetService,
        bool includeNew,
        List<(string TypeName, bool IsNew)> results,
        HashSet<string> seen)
    {
        Dictionary<string, (int Start, int End)>? locals = null;
        var pos = start;
        while (pos < end)
        {
            if (!mask.IsCode(pos))
            {
                pos++;
                continue;
            }

            // Skip nested block lambdas so inner helper returns do not leak into the factory target set.
            if (pos + 1 < end && code[pos] == '=' && code[pos + 1] == '>')
            {
                var afterArrow = SkipWhitespace(code, pos + 2);
                if (afterArrow < end && code[afterArrow] == '{')
                {
                    var nestedEnd = FindMatchingBrace(code, mask, afterArrow, end);
                    if (nestedEnd > afterArrow)
                    {
                        pos = nestedEnd + 1;
                        continue;
                    }
                }
                pos += 2;
                continue;
            }

            if (MatchesKeywordAt(code, mask, pos, end, "var"))
            {
                var identStart = SkipWhitespace(code, pos + 3);
                if (identStart < end && IsIdentifierStart(code[identStart]))
                {
                    var identEnd = identStart + 1;
                    while (identEnd < end && IsIdentifierPart(code[identEnd])) identEnd++;
                    var eq = SkipWhitespace(code, identEnd);
                    if (eq < end && code[eq] == '=' && (eq + 1 >= end || code[eq + 1] != '='))
                    {
                        var rhsStart = SkipWhitespace(code, eq + 1);
                        var rhsEnd = FindReturnSemicolon(code, mask, rhsStart, end);
                        locals ??= new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);
                        locals[code[identStart..identEnd]] = (rhsStart, rhsEnd);
                        pos = rhsEnd + 1;
                        continue;
                    }
                }
            }

            if (MatchesKeywordAt(code, mask, pos, end, "return"))
            {
                var exprStart = SkipWhitespace(code, pos + 6);
                var exprEnd = FindReturnSemicolon(code, mask, exprStart, end);
                if (exprEnd > exprStart)
                {
                    var returnedVar = TrySingleIdentifier(code, exprStart, exprEnd);
                    if (returnedVar is not null && locals is not null && locals.TryGetValue(returnedVar, out var rhs))
                    {
                        ExtractReturnedCandidates(
                            code, mask, rhs.Start, rhs.End, includeGetService, includeNew, results, seen);
                    }
                    else
                    {
                        ExtractReturnedCandidates(
                            code, mask, exprStart, exprEnd, includeGetService, includeNew, results, seen);
                    }
                }

                pos = exprEnd + 1;
                continue;
            }

            pos++;
        }
    }

    private static void ExtractReturnedCandidates(
        string code,
        SourceMask mask,
        int exprStart,
        int exprEnd,
        bool includeGetService,
        bool includeNew,
        List<(string TypeName, bool IsNew)> results,
        HashSet<string> seen)
    {
        if (includeGetService)
        {
            foreach (var method in new[] { "GetService", "GetRequiredService" })
            {
                var search = exprStart;
                while (search < exprEnd)
                {
                    var index = code.IndexOf(method, search, StringComparison.Ordinal);
                    if (index < 0 || index >= exprEnd) break;
                    search = index + method.Length;

                    if (!mask.IsCode(index) || !IsIdentifierBoundary(code, index - 1) ||
                        !IsIdentifierBoundary(code, index + method.Length))
                        continue;

                    var cursor = SkipWhitespace(code, index + method.Length);
                    if (cursor >= exprEnd || code[cursor] != '<') continue;
                    var closeAngle = FindMatchingAngle(code, cursor);
                    if (closeAngle < 0 || closeAngle >= exprEnd) continue;

                    var afterAngle = SkipWhitespace(code, closeAngle + 1);
                    if (afterAngle >= exprEnd || code[afterAngle] != '(') continue;
                    var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, afterAngle);
                    if (closeParen < 0 || closeParen >= exprEnd) continue;

                    // Ignore chained member access like sp.GetRequiredService<IDataProviderManager>().DataProvider.
                    var afterCall = SkipWhitespace(code, closeParen + 1);
                    if (afterCall < exprEnd && code[afterCall] == '.')
                        continue;

                    var written = code[(cursor + 1)..closeAngle];
                    if (seen.Add(written))
                        results.Add((written, false));
                }
            }
        }

        if (includeNew)
        {
            var search = exprStart;
            while (search < exprEnd)
            {
                var index = code.IndexOf("new", search, StringComparison.Ordinal);
                if (index < 0 || index >= exprEnd) break;
                search = index + 3;

                if (!mask.IsCode(index) || !IsIdentifierBoundary(code, index - 1) ||
                    !IsIdentifierBoundary(code, index + 3))
                    continue;

                if (IsPrecededByThrow(code, mask, exprStart, index))
                    continue;

                var cursor = SkipWhitespace(code, index + 3);
                if (cursor >= exprEnd || !IsIdentifierStart(code[cursor])) continue;

                var nameStart = cursor;
                while (cursor < exprEnd && (IsIdentifierPart(code[cursor]) || code[cursor] == '.'))
                    cursor++;

                var afterIdent = SkipWhitespace(code, cursor);
                var typeEnd = cursor;
                if (afterIdent < exprEnd && code[afterIdent] == '<')
                {
                    var close = FindMatchingAngle(code, afterIdent);
                    if (close < 0 || close >= exprEnd) continue;
                    typeEnd = close + 1;
                    afterIdent = SkipWhitespace(code, typeEnd);
                }

                var objectEnd = -1;
                if (afterIdent < exprEnd && code[afterIdent] == '(')
                {
                    var closeParen = DeepSliceSyntax.FindMatchingParen(code, mask, afterIdent);
                    if (closeParen < 0 || closeParen >= exprEnd) continue;
                    objectEnd = closeParen + 1;
                    var afterParen = SkipWhitespace(code, objectEnd);
                    if (afterParen < exprEnd && code[afterParen] == '{')
                    {
                        var closeInit = FindMatchingBrace(code, mask, afterParen, exprEnd);
                        if (closeInit > afterParen)
                            objectEnd = closeInit + 1;
                    }
                }
                else if (afterIdent < exprEnd && code[afterIdent] == '{')
                {
                    var closeInit = FindMatchingBrace(code, mask, afterIdent, exprEnd);
                    if (closeInit < 0 || closeInit >= exprEnd) continue;
                    objectEnd = closeInit + 1;
                }

                if (objectEnd < 0)
                    continue;

                // Advance past nested constructor arguments so `new Outer(new Inner())` only yields `Outer`.
                search = objectEnd;
                var afterObject = SkipWhitespace(code, objectEnd);
                if (afterObject < exprEnd && code[afterObject] == '.')
                    continue;

                var written = code[nameStart..typeEnd];
                if (seen.Add(written))
                    results.Add((written, true));
            }
        }
    }

    private static int FindTopLevelLambdaArrow(string code, SourceMask mask, int bodyStart, int bodyEnd)
    {
        var parenDepth = 0;
        var braceDepth = 0;
        for (var i = bodyStart; i < bodyEnd - 1; i++)
        {
            if (!mask.IsCode(i)) continue;
            var ch = code[i];
            if (ch == '(') parenDepth++;
            else if (ch == ')') { if (parenDepth > 0) parenDepth--; }
            else if (ch == '{') braceDepth++;
            else if (ch == '}') { if (braceDepth > 0) braceDepth--; }
            else if (ch == '=' && code[i + 1] == '>' && parenDepth == 0 && braceDepth == 0)
                return i;
        }
        return -1;
    }

    private static int FindMatchingBrace(string code, SourceMask mask, int openBrace, int limit)
    {
        var depth = 0;
        for (var i = openBrace; i < limit; i++)
        {
            if (!mask.IsCode(i)) continue;
            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static int FindReturnSemicolon(string code, SourceMask mask, int start, int limit)
    {
        var paren = 0;
        var bracket = 0;
        var brace = 0;
        for (var i = start; i < limit; i++)
        {
            if (!mask.IsCode(i)) continue;
            switch (code[i])
            {
                case '(': paren++; break;
                case ')': if (paren > 0) paren--; break;
                case '[': bracket++; break;
                case ']': if (bracket > 0) bracket--; break;
                case '{': brace++; break;
                case '}': if (brace > 0) brace--; break;
                case ';' when paren == 0 && bracket == 0 && brace == 0:
                    return i;
            }
        }
        return limit;
    }

    private static bool MatchesKeywordAt(string code, SourceMask mask, int pos, int limit, string keyword)
    {
        if (pos + keyword.Length > limit) return false;
        if (!mask.IsCode(pos)) return false;
        if (string.CompareOrdinal(code, pos, keyword, 0, keyword.Length) != 0) return false;
        return IsIdentifierBoundary(code, pos - 1) && IsIdentifierBoundary(code, pos + keyword.Length);
    }

    private static string? TrySingleIdentifier(string code, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(code[start])) start++;
        while (end > start && char.IsWhiteSpace(code[end - 1])) end--;
        if (start >= end || !IsIdentifierStart(code[start])) return null;
        for (var i = start + 1; i < end; i++)
        {
            if (!IsIdentifierPart(code[i])) return null;
        }
        return code[start..end];
    }

    private static bool IsPrecededByThrow(string code, SourceMask mask, int exprStart, int newIndex)
    {
        var cursor = newIndex - 1;
        while (cursor >= exprStart && char.IsWhiteSpace(code[cursor])) cursor--;
        var throwEnd = cursor + 1;
        var throwStart = throwEnd - "throw".Length;
        return throwStart >= exprStart &&
               mask.IsCode(throwStart) &&
               string.CompareOrdinal(code, throwStart, "throw", 0, "throw".Length) == 0 &&
               IsIdentifierBoundary(code, throwStart - 1);
    }

    private static bool HasLambdaArrow(string code, SourceMask mask, int bodyStart, int bodyEnd)
    {
        var search = bodyStart;
        while (search < bodyEnd - 1)
        {
            var index = code.IndexOf("=>", search, StringComparison.Ordinal);
            if (index < 0 || index >= bodyEnd) return false;
            if (mask.IsCode(index)) return true;
            search = index + 2;
        }
        return false;
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
