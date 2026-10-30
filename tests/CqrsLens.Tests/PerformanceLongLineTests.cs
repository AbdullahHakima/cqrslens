using System.Diagnostics;
using System.Text;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Permanent guard for the "Parse sources" performance pathology (Problem 1):
/// the text scan of a data file built from oversized single-line string
/// literals (NorthwindTraders' <c>SampleDataSeeder</c> shape) was superlinear in
/// the length of a word run, taking minutes instead of milliseconds. The file
/// must still be parsed in full - this test asserts both the wall-clock bound and
/// that the surrounding CQRS shapes are unaffected.
/// </summary>
public class PerformanceLongLineTests : IDisposable
{
    // Long enough that the old quadratic scan took far longer than the bound,
    // short enough that the fixed scan stays in the low milliseconds.
    private const int LongLineLength = 128 * 1024;

    private readonly string _root;

    public PerformanceLongLineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-perf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "Perf.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string name, string code)
        => File.WriteAllText(Path.Combine(_root, name), code);

    private static string OversizedDataFile(int literalLength)
    {
        const string digits = "FABCDEF0123456789";
        var literal = new StringBuilder(literalLength);
        for (var i = 0; i < literalLength; i++)
            literal.Append(digits[i % digits.Length]);

        var builder = new StringBuilder(literalLength + 512);
        builder.AppendLine("namespace Perf.Data;");
        builder.AppendLine("public static class SeedData");
        builder.AppendLine("{");
        builder.AppendLine("    public static byte[] Load()");
        builder.AppendLine("    {");
        builder.AppendLine("        var chunks = new List<byte[]>();");
        builder.Append("        chunks.Add(StringToByteArray(\"");
        builder.Append(literal);
        builder.AppendLine("\"));");
        builder.AppendLine("        return chunks[0];");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private static byte[] StringToByteArray(string hex)");
        builder.AppendLine("        => new byte[hex.Length / 2];");
        builder.AppendLine("}");
        return builder.ToString();
    }

    [Fact]
    public void Text_scan_of_oversized_long_line_file_completes_quickly_and_fully()
    {
        // The generated file must contain a run of word characters this long.
        WriteFile("SeedData.cs", OversizedDataFile(LongLineLength));
        WriteFile("Contracts.cs", """
            using System.Threading;
            using System.Threading.Tasks;

            namespace MediatR
            {
                public interface IRequest<out TResponse> { }
                public interface IRequestHandler<in TRequest, TResponse> where TRequest : IRequest<TResponse>
                {
                    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
                }
            }

            namespace Perf
            {
                using MediatR;

                public sealed record GetOrdersQuery : IRequest<int>;

                public sealed class GetOrdersQueryHandler : IRequestHandler<GetOrdersQuery, int>
                {
                    public Task<int> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
                        => Task.FromResult(0);
                }
            }
            """);

        var stopwatch = Stopwatch.StartNew();
        var result = Analyzer.Analyze(_root);
        stopwatch.Stop();

        // Findings are unaffected by the oversized data file.
        var request = Assert.Single(result.Requests);
        Assert.Equal("GetOrdersQuery", request.Name);
        Assert.Single(result.Handlers);
        Assert.Empty(result.SkippedFiles);

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"Text scan took {stopwatch.ElapsedMilliseconds} ms for one {LongLineLength}-char line; " +
            "the property scan is superlinear in word-run length again.");
    }
}
