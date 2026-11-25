using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;
using Xunit;

namespace CqrsLens.Tests;

public sealed class SecurityHardeningTests
{
    [Fact]
    public void SafeXmlLoader_LoadsAndParsesValidXml()
    {
        using var temp = new TempDir();
        var xmlPath = Path.Combine(temp.Path, "sample.xml");
        const string xml = "<TestRun><Results count=\"1\" /></TestRun>";
        File.WriteAllText(xmlPath, xml);

        var fromFile = SafeXmlLoader.LoadDocument(xmlPath);
        Assert.Equal("TestRun", fromFile.Root?.Name.LocalName);
        Assert.Equal("1", fromFile.Root?.Element("Results")?.Attribute("count")?.Value);

        using var reader = new StringReader(xml);
        var fromReader = SafeXmlLoader.ParseDocument(reader);
        Assert.Equal("TestRun", fromReader.Root?.Name.LocalName);
    }

    [Fact]
    public void SafeXmlLoader_BlocksXxeExternalEntity()
    {
        using var temp = new TempDir();
        var secretPath = Path.Combine(temp.Path, "secret.txt");
        File.WriteAllText(secretPath, "TOP_SECRET");
        var xxeXml = $"""
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "{new Uri(secretPath).AbsoluteUri}"> ]>
            <root>&xxe;</root>
            """;

        var xmlPath = Path.Combine(temp.Path, "xxe.xml");
        File.WriteAllText(xmlPath, xxeXml);

        Assert.Throws<XmlException>(() => SafeXmlLoader.LoadDocument(xmlPath));

        using var reader = new StringReader(xxeXml);
        Assert.Throws<XmlException>(() => SafeXmlLoader.ParseDocument(reader));
    }

    [Fact]
    public void SafeXmlLoader_BlocksDtdEntityExpansion()
    {
        using var temp = new TempDir();
        const string billionLaughs = """
            <?xml version="1.0"?>
            <!DOCTYPE lolz [
              <!ENTITY lol "lol">
              <!ENTITY lol1 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
              <!ENTITY lol2 "&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;">
            ]>
            <lolz>&lol2;</lolz>
            """;

        var xmlPath = Path.Combine(temp.Path, "laughs.xml");
        File.WriteAllText(xmlPath, billionLaughs);

        Assert.Throws<XmlException>(() => SafeXmlLoader.LoadDocument(xmlPath));

        using var reader = new StringReader(billionLaughs);
        Assert.Throws<XmlException>(() => SafeXmlLoader.ParseDocument(reader));
    }

    [Fact]
    public void SafeXmlLoader_RejectsOversizedFilesAndCharacters()
    {
        using var temp = new TempDir();
        var xmlPath = Path.Combine(temp.Path, "big.xml");
        var xml = $"<root>{new string('a', 256)}</root>";
        File.WriteAllText(xmlPath, xml);

        Assert.Throws<InvalidDataException>(() => SafeXmlLoader.LoadDocument(xmlPath, maxBytes: 64));

        using var reader = new StringReader(xml);
        Assert.Throws<XmlException>(() => SafeXmlLoader.ParseDocument(reader, maxCharacters: 64));
    }

    [Fact]
    public void BoundedFileReader_EnforcesByteLimitsAcrossChunks()
    {
        using var temp = new TempDir();
        var smallPath = Path.Combine(temp.Path, "exact.txt");
        var exactBytes = new string('x', 90_000); // > 81,920-byte chunk size
        File.WriteAllText(smallPath, exactBytes, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var text = BoundedFileReader.ReadAllText(smallPath, maxBytes: 90_000);
        Assert.Equal(exactBytes, text);

        using var stream = BoundedFileReader.ReadAllBytesStream(smallPath, maxBytes: 90_000);
        Assert.Equal(90_000, stream.Length);

        Assert.Throws<InvalidDataException>(() => BoundedFileReader.ReadAllText(smallPath, maxBytes: 89_999));
        Assert.Throws<InvalidDataException>(() => BoundedFileReader.ReadAllBytesStream(smallPath, maxBytes: 89_999));
    }

    [Fact]
    public void BoundedFileReader_IntegratesWithConfigDiffAndBaselineLoaders()
    {
        using var temp = new TempDir();
        File.WriteAllText(Path.Combine(temp.Path, ConfigurationLoader.FileName), """{"analysis":"text"}""");
        var settings = ConfigurationLoader.Load(temp.Path);
        Assert.Equal(AnalysisMode.Text, settings.AnalysisMode);

        var reportPath = Path.Combine(temp.Path, "report.json");
        File.WriteAllText(reportPath, JsonReportGenerator.Generate(new AnalysisResult
        {
            RootPath = temp.Path,
            ScannerName = "text",
        }));
        var report = JsonReportDocumentReader.Read(reportPath);
        Assert.Equal(JsonReportGenerator.CurrentSchemaVersion, report.SchemaVersion);

        var baselinePath = Path.Combine(temp.Path, "baseline.json");
        BaselineStore.Write(Array.Empty<CheckFinding>(), baselinePath);
        var baseline = BaselineStore.Read(baselinePath);
        Assert.Equal(BaselineStore.CurrentSchemaVersion, baseline.SchemaVersion);
    }

    [Fact]
    public void McpPathGuard_ValidatesWorkspaceTraversalSiblingControlCharsAndRoots()
    {
        using var temp = new TempDir();
        var workspace = Path.Combine(temp.Path, "repo");
        var subDir = Path.Combine(workspace, "src");
        var siblingEvil = Path.Combine(temp.Path, "repo-evil");
        var externalAllowed = Path.Combine(temp.Path, "extra-root");
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(siblingEvil);
        Directory.CreateDirectory(externalAllowed);

        Assert.True(McpPathGuard.TryValidateDirectory(
            ".", workspace, Array.Empty<string>(), allowExternalPaths: false, out var rootFull, out var rootErr));
        Assert.Null(rootErr);
        Assert.Equal(Path.GetFullPath(workspace), rootFull);

        Assert.True(McpPathGuard.TryValidateDirectory(
            "src", workspace, Array.Empty<string>(), allowExternalPaths: false, out var subFull, out var subErr));
        Assert.Null(subErr);
        Assert.Equal(Path.GetFullPath(subDir), subFull);

        Assert.False(McpPathGuard.TryValidateDirectory(
            "../repo-evil", workspace, Array.Empty<string>(), allowExternalPaths: false, out var travFull, out var travErr));
        Assert.Equal(string.Empty, travFull);
        Assert.Contains("escapes allowed MCP workspace roots", travErr, StringComparison.OrdinalIgnoreCase);

        Assert.False(McpPathGuard.TryValidateDirectory(
            siblingEvil, workspace, Array.Empty<string>(), allowExternalPaths: false, out _, out var sibErr));
        Assert.Contains("escapes allowed MCP workspace roots", sibErr, StringComparison.OrdinalIgnoreCase);

        Assert.False(McpPathGuard.TryValidatePath(
            "src\0evil", workspace, Array.Empty<string>(), allowExternalPaths: true, out _, out var nullErr));
        Assert.Contains("control characters", nullErr, StringComparison.OrdinalIgnoreCase);

        Assert.False(McpPathGuard.TryValidatePath(
            "src\nevil", workspace, Array.Empty<string>(), allowExternalPaths: true, out _, out var ctrlErr));
        Assert.Contains("control characters", ctrlErr, StringComparison.OrdinalIgnoreCase);

        Assert.True(McpPathGuard.TryValidateDirectory(
            externalAllowed, workspace, new[] { externalAllowed }, allowExternalPaths: false, out var allowedFull, out var allowedErr));
        Assert.Null(allowedErr);
        Assert.Equal(Path.GetFullPath(externalAllowed), allowedFull);

        Assert.True(McpPathGuard.TryValidateDirectory(
            siblingEvil, workspace, Array.Empty<string>(), allowExternalPaths: true, out var extFull, out var extErr));
        Assert.Null(extErr);
        Assert.Equal(Path.GetFullPath(siblingEvil), extFull);

        var nonExistentInside = Path.Combine(workspace, "future", "report.json");
        Assert.True(McpPathGuard.TryValidatePath(
            nonExistentInside, workspace, Array.Empty<string>(), allowExternalPaths: false, out var pathFull, out var pathErr));
        Assert.Null(pathErr);
        Assert.Equal(Path.GetFullPath(nonExistentInside), pathFull);

        Assert.False(McpPathGuard.TryValidateDirectory(
            nonExistentInside, workspace, Array.Empty<string>(), allowExternalPaths: false, out _, out var dirMissingErr));
        Assert.Contains("path not found", dirMissingErr, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public void McpPathGuard_BlocksSymlinkEscapesInDirectoryAndAncestorSegments()
    {
        using var temp = new TempDir();
        var workspace = Path.Combine(temp.Path, "repo");
        var outside = Path.Combine(temp.Path, "outside");
        var outsideNested = Path.Combine(outside, "nested");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outsideNested);

        var dirLink = Path.Combine(workspace, "escape-dir");
        try
        {
            Directory.CreateSymbolicLink(dirLink, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Skip.If(true, $"Symbolic link creation requires elevated permissions: {ex.Message}");
        }

        Assert.False(McpPathGuard.TryValidateDirectory(
            dirLink, workspace, Array.Empty<string>(), allowExternalPaths: false, out var full1, out var err1));
        Assert.Equal(string.Empty, full1);
        Assert.Contains("escapes allowed MCP workspace roots", err1, StringComparison.OrdinalIgnoreCase);

        // Ancestor symlink check: escape-dir/nested does not itself look like a symlink,
        // so every ancestor segment must be walked.
        Assert.False(McpPathGuard.TryValidateDirectory(
            Path.Combine(dirLink, "nested"),
            workspace,
            Array.Empty<string>(),
            allowExternalPaths: false,
            out var full2,
            out var err2));
        Assert.Equal(string.Empty, full2);
        Assert.Contains("escapes allowed MCP workspace roots", err2, StringComparison.OrdinalIgnoreCase);

        Assert.False(McpPathGuard.TryValidatePath(
            Path.Combine(dirLink, "nested", "nonexistent.cs"),
            workspace,
            Array.Empty<string>(),
            allowExternalPaths: false,
            out _,
            out var err3));
        Assert.Contains("escapes allowed MCP workspace roots", err3, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void McpMessageFraming_SupportsContentTypeBeforeContentLengthInAnyCase()
    {
        const string body = """{"jsonrpc":"2.0","id":7,"method":"ping"}""";
        var byteCount = Encoding.UTF8.GetByteCount(body);
        var framed =
            $"content-type: application/vscode-jsonrpc; charset=utf-8\r\n" +
            $"cOnTeNt-LeNgTh: {byteCount}\r\n" +
            "\r\n" +
            body;

        using var reader = new StringReader(framed);
        var message = McpMessageFraming.Read(reader);

        Assert.True(message.UseContentLength);
        Assert.Null(message.Error);
        Assert.Equal(body, message.Payload);
    }

    [Fact]
    public void McpMessageFraming_RejectsInvalidAndOversizedHeadersAndRecovers()
    {
        const string validBody = """{"jsonrpc":"2.0","id":9,"method":"ping"}""";
        var validFrame = $"Content-Length: {Encoding.UTF8.GetByteCount(validBody)}\r\n\r\n{validBody}";

        // 1. Oversized header line (> 8 KB) followed by valid frame
        var oversizedHeader = $"Content-Type: {new string('a', McpMessageFraming.MaxHeaderLineBytes)}\r\nContent-Length: 2\r\n\r\n";
        using (var reader = new StringReader(oversizedHeader + validFrame))
        {
            var errMsg = McpMessageFraming.Read(reader);
            Assert.Null(errMsg.Payload);
            Assert.True(errMsg.UseContentLength);
            Assert.NotNull(errMsg.Error);

            var okMsg = McpMessageFraming.Read(reader);
            Assert.Null(okMsg.Error);
            Assert.Equal(validBody, okMsg.Payload);
        }

        // 2. > 32 headers rejected, 32 headers accepted
        var tooManyHeaders = new StringBuilder();
        for (var i = 1; i <= McpMessageFraming.MaxHeaders; i++)
            tooManyHeaders.Append(CultureInfo.InvariantCulture, $"X-H{i}: v\r\n");
        tooManyHeaders.Append("Content-Length: 2\r\n\r\n");
        using (var reader = new StringReader(tooManyHeaders.ToString()))
        {
            var msg = McpMessageFraming.Read(reader);
            Assert.Null(msg.Payload);
            Assert.True(msg.UseContentLength);
            Assert.Contains("32", msg.Error, StringComparison.Ordinal);
        }

        var exactlyMaxHeaders = new StringBuilder();
        for (var i = 1; i < McpMessageFraming.MaxHeaders; i++)
            exactlyMaxHeaders.Append(CultureInfo.InvariantCulture, $"X-H{i}: v\r\n");
        exactlyMaxHeaders.Append(CultureInfo.InvariantCulture, $"Content-Length: {Encoding.UTF8.GetByteCount(validBody)}\r\n\r\n{validBody}");
        using (var reader = new StringReader(exactlyMaxHeaders.ToString()))
        {
            var msg = McpMessageFraming.Read(reader);
            Assert.Null(msg.Error);
            Assert.Equal(validBody, msg.Payload);
        }

        // 3. Conflicting / duplicate Content-Length
        using (var reader = new StringReader("Content-Length: 10\r\nContent-Length: 20\r\n\r\n"))
        {
            var msg = McpMessageFraming.Read(reader);
            Assert.Null(msg.Payload);
            Assert.True(msg.UseContentLength);
            Assert.NotNull(msg.Error);
        }

        // 4. Missing Content-Length after Content-Type
        using (var reader = new StringReader("Content-Type: application/json\r\n\r\n"))
        {
            var msg = McpMessageFraming.Read(reader);
            Assert.Null(msg.Payload);
            Assert.True(msg.UseContentLength);
            Assert.NotNull(msg.Error);
        }

        // 5. Negative, non-numeric, and oversized Content-Length
        foreach (var badLen in new[] { "-1", "abc", (McpMessageFraming.MaxContentLength + 1).ToString(CultureInfo.InvariantCulture), "99999999999" })
        {
            using var reader = new StringReader($"Content-Length: {badLen}\r\n\r\n");
            var msg = McpMessageFraming.Read(reader);
            Assert.Null(msg.Payload);
            Assert.True(msg.UseContentLength);
            Assert.NotNull(msg.Error);
        }
    }

    [Fact]
    public void McpMessageFraming_ReadPayload_HandlesUnpairedHighSurrogatesWithoutCorruptingNextFrame()
    {
        // Unpaired high surrogate (3 UTF-8 bytes) + "ab" (2 bytes) + valid surrogate pair 😀 (4 bytes) = 9 bytes
        const string firstPayload = "\uD800ab\uD83D\uDE00";
        var firstBytes = Encoding.UTF8.GetByteCount(firstPayload);
        Assert.Equal(9, firstBytes);

        // Unpaired high surrogate right at the end of a frame (must NOT consume '{' of the next frame)
        const string secondPayload = "x\uD800";
        var secondBytes = Encoding.UTF8.GetByteCount(secondPayload);
        Assert.Equal(4, secondBytes);

        const string thirdPayload = """{"jsonrpc":"2.0","id":3,"method":"ping"}""";
        var thirdBytes = Encoding.UTF8.GetByteCount(thirdPayload);

        var streamText =
            $"Content-Length: {firstBytes}\r\n\r\n{firstPayload}" +
            $"Content-Length: {secondBytes}\r\n\r\n{secondPayload}" +
            $"Content-Length: {thirdBytes}\r\n\r\n{thirdPayload}";

        using var reader = new StringReader(streamText);

        var msg1 = McpMessageFraming.Read(reader);
        Assert.Null(msg1.Error);
        Assert.Equal(firstPayload, msg1.Payload);

        var msg2 = McpMessageFraming.Read(reader);
        Assert.Null(msg2.Error);
        Assert.Equal(secondPayload, msg2.Payload);

        var msg3 = McpMessageFraming.Read(reader);
        Assert.Null(msg3.Error);
        Assert.Equal(thirdPayload, msg3.Payload);
    }

    [Fact]
    public void McpScanCache_EvictsLeastRecentlyUsedBeyondMaxEntriesAndIsThreadSafe()
    {
        var cache = new McpScanCache();
        Assert.Equal(8, McpScanCache.MaxEntries);

        var fingerprints = new McpScanFingerprint[9];
        for (var i = 0; i < 9; i++)
            fingerprints[i] = new McpScanFingerprint(1, 100 + i, (ulong)(i + 1));

        for (var i = 0; i < 8; i++)
        {
            cache.Set($"root-{i}|text", new AnalysisResult { RootPath = $"root-{i}", ScannerName = "text" }, fingerprints[i]);
        }

        // Touch root-0 so root-1 becomes the least recently used entry.
        Assert.True(cache.TryGet("root-0|text", fingerprints[0], out var hit0));
        Assert.Equal("root-0", hit0.RootPath);

        // Insert 9th entry -> should evict root-1, keeping root-0 and root-2..root-8.
        cache.Set("root-8|text", new AnalysisResult { RootPath = "root-8", ScannerName = "text" }, fingerprints[8]);

        Assert.False(cache.TryGet("root-1|text", fingerprints[1], out _));
        Assert.True(cache.TryGet("root-0|text", fingerprints[0], out _));
        for (var i = 2; i < 9; i++)
        {
            Assert.True(cache.TryGet($"root-{i}|text", fingerprints[i], out var item));
            Assert.Equal($"root-{i}", item.RootPath);
        }

        // Concurrent read/write stress check
        var parallelCache = new McpScanCache();
        Parallel.For(0, 200, i =>
        {
            var slot = i % 16;
            var fp = new McpScanFingerprint(1, slot, (ulong)slot);
            var key = $"concurrent-{slot}|text";
            parallelCache.Set(key, new AnalysisResult { RootPath = $"concurrent-{slot}", ScannerName = "text" }, fp);
            parallelCache.TryGet(key, fp, out _);
        });
    }

    [Fact]
    public void RequestPropertyPopulator_AdversarialArrayBrackets_CompletesQuickly()
    {
        var code = "namespace Test;\npublic class AdversarialCommand\n{\n    public int"
            + string.Concat(Enumerable.Repeat("[] ", 200))
            + "! { get; }\n    public string GoodProp { get; }\n}\n";
        var openBrace = code.IndexOf('{');
        var decl = new CSharpScanner.ClassDecl
        {
            Name = "AdversarialCommand",
            Namespace = "Test",
            BaseList = string.Empty,
            BaseTypeNames = new List<string>(),
            BaseRawNames = new List<string>(),
            BodySpan = (openBrace, code.Length),
        };
        var scan = new CSharpScanner.FileScan { Namespace = "Test" };
        scan.Classes.Add(decl);
        var mask = SourceMask.Build(code);
        var result = new AnalysisResult { RootPath = "/tmp", ScannerName = "text" };
        var request = new CqrsRequest { Name = "AdversarialCommand", Namespace = "Test" };
        result.Requests.Add(request);

        var sw = Stopwatch.StartNew();
        RequestPropertyPopulator.Populate([(new SourceFile("AdversarialCommand.cs", code, null), scan, mask)], result);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"Regex took too long: {sw.Elapsed}");
        Assert.Contains(request.Properties, p => p.Name == "GoodProp" && p.Type == "string");
    }

    [Fact]
    public void HtmlReportGenerator_SvgMode_EmitsStrictContentSecurityPolicyMetaTag()
    {
        var result = new AnalysisResult
        {
            RootPath = "/tmp/shop",
            GeneratedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ScannerName = "text",
        };

        var svgHtml = HtmlReportGenerator.Generate(result, DiagramRenderMode.Svg);
        Assert.Contains(
            "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src data:;\">",
            svgHtml,
            StringComparison.Ordinal);

        var mermaidHtml = HtmlReportGenerator.Generate(result, DiagramRenderMode.Mermaid);
        Assert.DoesNotContain("http-equiv=\"Content-Security-Policy\"", mermaidHtml, StringComparison.Ordinal);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cqrslens-security-tests-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore temp cleanup races
            }
        }
    }
}
