using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public class TextScannerCorrectnessTests : IDisposable
{
    private readonly string _root;

    public TextScannerCorrectnessTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cqrslens-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteProject(string folder, string name)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    private void WriteFile(string relative, string code)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, code);
    }

    private static AnalysisResult Scan(string code)
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        CSharpScanner.ScanText(code, "App", result);
        return result;
    }

    [Fact]
    public void MultipleBlockNamespaces_GetPerDeclarationNamespaces()
    {
        var result = Scan("""
            namespace Alpha
            {
                public sealed record FirstCommand : IRequest<int>;
                public sealed class FirstCommandHandler : IRequestHandler<FirstCommand, int> { }
            }

            namespace Beta
            {
                public sealed record SecondCommand : IRequest<string>;
                public sealed class SecondCommandHandler : IRequestHandler<SecondCommand, string> { }
            }
            """);

        Assert.Equal(2, result.Requests.Count);
        Assert.Equal("Alpha", result.Requests.Single(r => r.Name == "FirstCommand").Namespace);
        Assert.Equal("Beta", result.Requests.Single(r => r.Name == "SecondCommand").Namespace);
        Assert.Equal("Alpha", result.Handlers.Single(h => h.Name == "FirstCommandHandler").Namespace);
        Assert.Equal("Beta", result.Handlers.Single(h => h.Name == "SecondCommandHandler").Namespace);
    }

    [Fact]
    public void NestedBlockNamespaces_ComposeTheFullNamespace()
    {
        var result = Scan("""
            namespace Outer
            {
                namespace Inner
                {
                    public sealed record NestedCommand : IRequest<int>;
                }
            }
            """);

        Assert.Equal("Outer.Inner", Assert.Single(result.Requests).Namespace);
    }

    [Fact]
    public void MultipleNamespaces_LinkHandlersToTheirOwnRequests()
    {
        WriteProject("App", "App");
        WriteFile("App/Features.cs", """
            namespace Alpha
            {
                public sealed record FirstCommand : IRequest<int>;
                public sealed class FirstCommandHandler : IRequestHandler<FirstCommand, int> { }
            }
            namespace Beta
            {
                public sealed record SecondCommand : IRequest<string>;
                public sealed class SecondCommandHandler : IRequestHandler<SecondCommand, string> { }
            }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Equal(new[] { "Alpha.FirstCommandHandler" },
            result.Requests.Single(r => r.Name == "FirstCommand").Handlers);
        Assert.Equal(new[] { "Beta.SecondCommandHandler" },
            result.Requests.Single(r => r.Name == "SecondCommand").Handlers);
    }

    [Fact]
    public void BracesInsideStringsAndComments_DoNotEndTheTypeBody()
    {
        var result = Scan("""
            namespace App;
            public sealed class ShopDbContext : DbContext
            {
                private const string Closing = "}";
                private const string Opening = "{";
                // }
                /* { */
                public DbSet<Order> Orders { get; set; }
            }
            """);

        var context = Assert.Single(result.DbContexts);
        Assert.Contains(context.DbSets, s => s.Entity == "Order" && s.Property == "Orders");
    }

    [Fact]
    public void DbSetsAfterABraceLiteral_AreNotDropped()
    {
        var result = Scan("""
            public sealed class FirstDbContext : DbContext
            {
                public string Marker { get; set; } = "}";
                public DbSet<FirstEntity> Firsts { get; set; }
            }

            public sealed class SecondDbContext : DbContext
            {
                public string Marker { get; set; } = "{";
                public DbSet<SecondEntity> Seconds { get; set; }
            }
            """);

        Assert.Equal(2, result.DbContexts.Count);
        Assert.Contains(result.DbContexts.Single(c => c.Name == "FirstDbContext").DbSets,
            s => s.Entity == "FirstEntity" && s.Property == "Firsts");
        Assert.Contains(result.DbContexts.Single(c => c.Name == "SecondDbContext").DbSets,
            s => s.Entity == "SecondEntity" && s.Property == "Seconds");
    }

    [Fact]
    public void DbSet_DoesNotLeakAcrossContexts()
    {
        var result = Scan("""
            public sealed class FirstDbContext : DbContext
            {
                public string Marker { get; set; } = "{";
            }
            public sealed class SecondDbContext : DbContext
            {
                public DbSet<SecondEntity> Seconds { get; set; }
            }
            """);

        Assert.Empty(result.DbContexts.Single(c => c.Name == "FirstDbContext").DbSets);
        Assert.Single(result.DbContexts.Single(c => c.Name == "SecondDbContext").DbSets);
    }

    [Fact]
    public void DbContext_RecognizesFullyQualifiedAndCustomBases_NotInterfaces()
    {
        var result = Scan("""
            public sealed class EfContext : Microsoft.EntityFrameworkCore.DbContext { }
            public sealed class IdentityContext : IdentityDbContext<User> { }
            public sealed class InterfaceOnly : IDbContext { }
            """);

        Assert.Equal(2, result.DbContexts.Count);
        Assert.Contains(result.DbContexts, c => c.Name == "EfContext");
        Assert.Contains(result.DbContexts, c => c.Name == "IdentityContext");
        Assert.DoesNotContain(result.DbContexts, c => c.Name == "InterfaceOnly");
    }

    [Fact]
    public void PrimaryConstructorWithNestedParentheses_IsParsed()
    {
        var result = Scan("""
            public sealed class GetDefault(int a, int b = Math.Max(1, 2)) : IRequest<int>;
            """);

        var request = Assert.Single(result.Requests);
        Assert.Equal("GetDefault", request.Name);
        Assert.Equal("int", request.ResponseType);
    }

    [Fact]
    public void DbSet_HandlesQualifiedAndNestedGenericEntityTypes()
    {
        var result = Scan("""
            public sealed class ShopDbContext : DbContext
            {
                public DbSet<App.Order> Orders { get; set; }
                public DbSet<Dictionary<string, int>> Lookups { get; set; }
            }
            """);

        var context = Assert.Single(result.DbContexts);
        Assert.Contains(context.DbSets, s => s.Entity == "App.Order" && s.Property == "Orders");
        Assert.Contains(context.DbSets, s => s.Entity == "Dictionary<string, int>" && s.Property == "Lookups");
    }

    [Fact]
    public void AllStringFormsAndCharLiterals_AreIgnoredWhenMatchingTypeBodies()
    {
        var code = """"
            public sealed class ShopDbContext : DbContext
            {
                private const string Normal = "}";
                private const string Verbatim = @"{ ""x"": ""}"" }";
                private const string Interpolated = $"{1} }}";
                private const string Raw = """{ "x": "}" }""";
                private const char Closing = '}';
                // }
                /* { */
                public DbSet<Order> Orders { get; set; }
            }
            """";

        var result = Scan(code);

        var context = Assert.Single(result.DbContexts);
        Assert.Contains(context.DbSets, s => s.Entity == "Order" && s.Property == "Orders");
    }

    [Fact]
    public void GenericConstraints_DoNotBecomeBaseLists()
    {
        var result = Scan("""
            namespace App;
            public sealed class GetThing<T> where T : class, IRequest<int> { }
            public sealed class Handler<T> where T : IRequestHandler<GetThing<int>, int> { }
            """);

        Assert.Empty(result.Requests);
        Assert.Empty(result.Handlers);
    }

    [Fact]
    public void NestedTypes_GetContainingTypeIdentityAndLinkByQualifiedName()
    {
        WriteProject("App", "App");
        WriteFile("App/Nested.cs", """
            namespace App;

            public class Outer
            {
                public sealed record Inner : IRequest<int>;
                public sealed class InnerHandler : IRequestHandler<Outer.Inner, int> { }
            }
            """);

        var result = Analyzer.Analyze(_root);

        var request = Assert.Single(result.Requests);
        Assert.Equal("App.Outer.Inner", request.FullName);
        Assert.Equal(new[] { "App.Outer.InnerHandler" }, request.Handlers);
    }

    [Fact]
    public void SameNestedNameInTwoOuters_DoesNotCollide()
    {
        WriteProject("App", "App");
        WriteFile("App/Nested.cs", """
            namespace App;

            public class OuterOne
            {
                public sealed record Inner : IRequest<int>;
                public sealed class InnerHandler : IRequestHandler<OuterOne.Inner, int> { }
            }

            public class OuterTwo
            {
                public sealed record Inner : IRequest<string>;
                public sealed class InnerHandler : IRequestHandler<OuterTwo.Inner, string> { }
            }
            """);

        var result = Analyzer.Analyze(_root);

        Assert.Equal(2, result.Requests.Count);
        Assert.Equal(2, result.Requests.Select(r => r.FullName).Distinct().Count());
        Assert.Equal(new[] { "App.OuterOne.InnerHandler" },
            result.Requests.Single(r => r.FullName == "App.OuterOne.Inner").Handlers);
        Assert.Equal(new[] { "App.OuterTwo.InnerHandler" },
            result.Requests.Single(r => r.FullName == "App.OuterTwo.Inner").Handlers);
    }
}
