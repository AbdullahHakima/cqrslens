using System.Xml.Linq;

namespace CqrsLens;

/// <summary>
/// Parses Visual Studio TRX test results safely using <see cref="SafeXmlLoader"/>
/// (DTD/XXE-prohibited and size-bounded).
/// </summary>
public static class TrxTestResultsParser
{
    private const int MaxErrorMessageLength = 500;

    /// <summary>Loads and parses a TRX file from disk using <see cref="SafeXmlLoader.LoadDocument"/>.</summary>
    public static TrxTestSummary ParseFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var document = SafeXmlLoader.LoadDocument(path);
        return ParseDocument(document, path);
    }

    /// <summary>Parses a TRX stream from <paramref name="reader"/> using <see cref="SafeXmlLoader.ParseDocument"/>.</summary>
    public static TrxTestSummary Parse(TextReader reader, string displayPath)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(displayPath);
        var document = SafeXmlLoader.ParseDocument(reader);
        return ParseDocument(document, displayPath);
    }

    private static TrxTestSummary ParseDocument(XDocument document, string sourceFile)
    {
        if (document.Root is null ||
            !string.Equals(document.Root.Name.LocalName, "TestRun", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Expected a TRX <TestRun> root element.");
        }

        var results = new List<TrxUnitTestResult>();
        var passed = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var resultsNode in document.Root.Elements())
        {
            if (!string.Equals(resultsNode.Name.LocalName, "Results", StringComparison.Ordinal))
                continue;

            foreach (var unit in resultsNode.Elements())
            {
                if (!string.Equals(unit.Name.LocalName, "UnitTestResult", StringComparison.Ordinal))
                    continue;

                var testName = unit.Attribute("testName")?.Value?.Trim();
                if (string.IsNullOrEmpty(testName))
                    testName = "<unknown>";

                var outcome = NormalizeOutcome(unit.Attribute("outcome")?.Value);
                switch (outcome)
                {
                    case "passed":
                        passed++;
                        break;
                    case "failed":
                        failed++;
                        break;
                    case "skipped":
                        skipped++;
                        break;
                }

                var rawDuration = unit.Attribute("duration")?.Value?.Trim();
                var duration = string.IsNullOrEmpty(rawDuration) ? null : rawDuration;
                var errorMessage = ExtractErrorMessage(unit);

                results.Add(new TrxUnitTestResult(testName, outcome, duration, errorMessage));
            }
        }

        return new TrxTestSummary(sourceFile, results.Count, passed, failed, skipped, results);
    }

    private static string NormalizeOutcome(string? rawOutcome)
    {
        var trimmed = rawOutcome?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "other";

        if (trimmed.Equals("Passed", StringComparison.OrdinalIgnoreCase))
            return "passed";
        if (trimmed.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            return "failed";
        if (trimmed.Equals("NotExecuted", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("Inconclusive", StringComparison.OrdinalIgnoreCase))
            return "skipped";

        return "other";
    }

    private static string? ExtractErrorMessage(XElement unitResult)
    {
        string? rawMessage = null;

        foreach (var output in unitResult.Elements())
        {
            if (!string.Equals(output.Name.LocalName, "Output", StringComparison.Ordinal))
                continue;

            foreach (var errorInfo in output.Elements())
            {
                if (!string.Equals(errorInfo.Name.LocalName, "ErrorInfo", StringComparison.Ordinal))
                    continue;

                foreach (var child in errorInfo.Elements())
                {
                    if (string.Equals(child.Name.LocalName, "Message", StringComparison.Ordinal))
                    {
                        rawMessage = child.Value;
                        break;
                    }
                }
                if (rawMessage is not null)
                    break;
            }
            if (rawMessage is not null)
                break;
        }

        if (rawMessage is null)
        {
            rawMessage = unitResult
                .Descendants()
                .Where(e => string.Equals(e.Name.LocalName, "ErrorInfo", StringComparison.Ordinal))
                .SelectMany(e => e.Elements())
                .FirstOrDefault(e => string.Equals(e.Name.LocalName, "Message", StringComparison.Ordinal))
                ?.Value;
        }

        if (string.IsNullOrWhiteSpace(rawMessage))
            return null;

        var trimmed = rawMessage.Trim();
        return trimmed.Length > MaxErrorMessageLength
            ? trimmed[..MaxErrorMessageLength]
            : trimmed;
    }
}
