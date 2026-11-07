using System.Xml.Linq;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Pre-1.0.0 export additions: sequence diagrams join the SVG export, and the new
/// --export-mermaid export writes the same diagram set as .mmd sources.
/// </summary>
public sealed class DiagramExportAdditionsTests : IDisposable
{
    private readonly string _root;

    public DiagramExportAdditionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-exports-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static AnalysisResult ResultWithRequests(params (string Name, string Namespace, bool Handled)[] requests)
    {
        var result = new AnalysisResult { RootPath = "/tmp/shop" };
        result.Projects.Add(new ProjectInfo
        {
            Name = "Shop.Application",
            FilePath = "/tmp/Shop.Application/Shop.Application.csproj",
            DirectoryPath = "/tmp/Shop.Application",
            Layer = "Application",
        });

        foreach (var (name, ns, handled) in requests)
        {
            var request = new CqrsRequest
            {
                Name = name,
                Namespace = ns,
                ProjectName = "Shop.Application",
            };
            if (handled)
            {
                request.Handlers.Add($"{ns}.{name}Handler");
                result.Handlers.Add(new HandlerInfo
                {
                    Name = $"{name}Handler",
                    Namespace = ns,
                    RequestName = name,
                    ProjectName = "Shop.Application",
                });
                result.FlowEdges.Add(new FlowEdge
                {
                    Kind = FlowEdgeKind.RequestToHandler,
                    From = request.FullName,
                    To = $"{ns}.{name}Handler",
                    ProjectName = "Shop.Application",
                });
            }

            result.Requests.Add(request);
        }

        return result;
    }

    [Fact]
    public void SvgExport_EmitsOneSequencePerHandledRequest()
    {
        var result = ResultWithRequests(
            ("CreateOrder", "Shop.Application.Orders", true),
            ("GetOrder", "Shop.Application.Orders", true),
            ("DeleteOrder", "Shop.Application.Orders", false));

        var directory = Path.Combine(_root, "svg");
        var names = SvgExporter.Export(result, directory).Select(Path.GetFileName).ToList();

        Assert.Contains("sequence-CreateOrder.svg", names);
        Assert.Contains("sequence-GetOrder.svg", names);
        Assert.DoesNotContain("sequence-DeleteOrder.svg", names);

        var sequence = XDocument.Load(Path.Combine(directory, "sequence-CreateOrder.svg"));
        Assert.Equal("svg", sequence.Root!.Name.LocalName);
        Assert.Contains("CreateOrderHandler", File.ReadAllText(Path.Combine(directory, "sequence-CreateOrder.svg")));
    }

    [Fact]
    public void SvgExport_DuplicateRequestNames_GetDeterministicSuffixes()
    {
        var result = ResultWithRequests(
            ("DoThing", "Shop.Application.Alpha", true),
            ("DoThing", "Shop.Application.Beta", true));

        var directory = Path.Combine(_root, "svg-dupes");
        var names = SvgExporter.Export(result, directory).Select(Path.GetFileName).ToList();

        Assert.Contains("sequence-DoThing.svg", names);
        Assert.Contains("sequence-DoThing-2.svg", names);
    }

    [Fact]
    public void MermaidExport_FileSetMatchesSvgExport_AndContentIsTheDiagramSource()
    {
        var result = ResultWithRequests(("CreateOrder", "Shop.Application.Orders", true));

        var svgNames = SvgExporter.Export(result, Path.Combine(_root, "svg"))
            .Select(p => Path.GetFileNameWithoutExtension(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var mmdDirectory = Path.Combine(_root, "mmd");
        var mmdNames = MermaidExporter.Export(result, mmdDirectory)
            .Select(p => Path.GetFileNameWithoutExtension(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(svgNames, mmdNames);

        var sequence = File.ReadAllText(Path.Combine(mmdDirectory, "sequence-CreateOrder.mmd"));
        Assert.StartsWith("sequenceDiagram", sequence);

        var expected = SvgExporter.BuildDiagrams(result).Single(d => d.Name == "project-graph.svg").Source;
        var actual = File.ReadAllText(Path.Combine(mmdDirectory, "project-graph.mmd"));
        Assert.Equal(expected.EndsWith("\n", StringComparison.Ordinal) ? expected : expected + "\n", actual);
    }

    [Fact]
    public void MermaidExport_TwiceProducesIdenticalBytes()
    {
        var result = ResultWithRequests(("CreateOrder", "Shop.Application.Orders", true));

        var first = MermaidExporter.Export(result, Path.Combine(_root, "one"));
        var firstBytes = first.ToDictionary(Path.GetFileName, File.ReadAllBytes)!;

        var second = MermaidExporter.Export(result, Path.Combine(_root, "two"));
        Assert.Equal(
            firstBytes.Keys.OrderBy(n => n, StringComparer.Ordinal),
            second.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var path in second)
            Assert.Equal(firstBytes[Path.GetFileName(path)!], File.ReadAllBytes(path));
    }

    [Fact]
    public void MermaidExport_ToAPathThatIsAFile_FailsWithNonZeroExitCode()
    {
        var filePath = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(filePath, "x");

        var ex = Assert.Throws<CliFailureException>(
            () => MermaidExporter.Export(ResultWithRequests(("CreateOrder", "Shop.Application.Orders", true)), filePath));
        Assert.NotEqual(ExitCodes.Success, ex.ExitCode);
        Assert.Contains("--export-mermaid", ex.Message);
    }

    [Fact]
    public void CliParser_ReadsExportMermaidDirectory()
    {
        var command = CliParser.Parse(new[] { "src", "--export-mermaid", "diagrams" });

        Assert.Equal(CliCommandKind.Run, command.Kind);
        var options = Assert.IsType<CliOptions>(command.Options);
        Assert.Equal("diagrams", options.ExportMermaidDirectory);
    }
}
