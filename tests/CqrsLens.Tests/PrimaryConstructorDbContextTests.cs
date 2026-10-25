using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Regression coverage for DbContext classes declared with a C# primary
/// constructor. El-Hakim's AppDbContext uses that shape; text mode reported
/// zero DbContexts for it while the Roslyn backend found the context and all
/// of its DbSets.
/// </summary>
public class PrimaryConstructorDbContextTests
{
    private static AnalysisResult Scan(string code, string project = "App")
    {
        var result = new AnalysisResult { RootPath = "/tmp" };
        CSharpScanner.ScanText(code, project, result);
        return result;
    }

    [Fact]
    public void DetectsPrimaryConstructorDbContextWithBaseCallArguments()
    {
        // The El-Hakim shape, verbatim: primary constructor, base call with
        // arguments, and an interface after the base type (no space after
        // the comma in the original source).
        var result = Scan("""
            public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options),IAppDbContext
            {
                public DbSet<Post> Posts { get; set; }
                public DbSet<Tag> Tags { get; set; }
                public DbSet<MediaFile> MediaFiles { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("AppDbContext", ctx.Name);
        Assert.Equal(3, ctx.DbSets.Count);
        Assert.Contains(ctx.DbSets, s => s.Entity == "Post" && s.Property == "Posts");
        Assert.Contains(ctx.DbSets, s => s.Entity == "Tag" && s.Property == "Tags");
        Assert.Contains(ctx.DbSets, s => s.Entity == "MediaFile" && s.Property == "MediaFiles");
    }

    [Fact]
    public void DetectsPrimaryConstructorDbContextWithoutInterface()
    {
        var result = Scan("""
            public sealed class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
            {
                public DbSet<Order> Orders { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("ShopDbContext", ctx.Name);
        Assert.Single(ctx.DbSets);
    }

    [Fact]
    public void DetectsPrimaryConstructorDbContextWithoutBaseCallArguments()
    {
        var result = Scan("""
            public class ReportDbContext(string connectionString) : DbContext, IReportDbContext
            {
                public DbSet<Report> Reports { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("ReportDbContext", ctx.Name);
        Assert.Single(ctx.DbSets);
    }

    [Fact]
    public void DetectsPrimaryConstructorIdentityDbContextWithGenericBaseAndArguments()
    {
        var result = Scan("""
            public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options)
                : IdentityDbContext<AppUser>(options)
            {
                public DbSet<AppUser> AppUsers { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("AppIdentityDbContext", ctx.Name);
        Assert.Single(ctx.DbSets);
    }

    [Fact]
    public void DetectsPrimaryConstructorDbContextWithFullyQualifiedBase()
    {
        var result = Scan("""
            public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
                : Microsoft.EntityFrameworkCore.DbContext(options)
            {
                public DbSet<Product> Products { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("CatalogDbContext", ctx.Name);
        Assert.Single(ctx.DbSets);
    }

    [Fact]
    public void DetectsDbContextWhenBaseCallArgumentsContainCommas()
    {
        var result = Scan("""
            public class MultiDbContext(DbContextOptions<MultiDbContext> options) : DbContext(options, true)
            {
                public DbSet<Order> Orders { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Single(ctx.DbSets);
    }

    [Fact]
    public void DetectsDbContextDerivedFromCustomContextWithPrimaryConstructor()
    {
        var result = Scan("""
            public class TenantDbContext(DbContextOptions<TenantDbContext> options) : ShopDbContext(options)
            {
                public DbSet<Tenant> Tenants { get; set; }
            }
            """);

        var ctx = Assert.Single(result.DbContexts);
        Assert.Equal("TenantDbContext", ctx.Name);
        Assert.Single(ctx.DbSets);
    }

    [Fact]
    public void InterfaceOnlyImplementationIsNotADbContext()
    {
        // Implementing the context interface without a DbContext base type is
        // not a context, even when the primary constructor takes options.
        var result = Scan("""
            public class FakeContext(DbContextOptions<FakeContext> options) : IAppDbContext
            {
                public DbSet<Post> Posts { get; set; }
            }
            """);

        Assert.Empty(result.DbContexts);
    }

    [Fact]
    public void UnrelatedContextBaseIsNotADbContext()
    {
        var result = Scan("""
            public class CacheContext(string key) : MemoryCacheContext(key)
            {
                public DbSet<CacheEntry> Entries { get; set; }
            }
            """);

        Assert.Empty(result.DbContexts);
    }

    [Fact]
    public void InterfaceLikeDbContextBaseIsNotADbContext()
    {
        var result = Scan("""
            public class LegacyContext : IDbContext
            {
                public DbSet<Order> Orders { get; set; }
            }
            """);

        Assert.Empty(result.DbContexts);
    }

    [Fact]
    public void DbSetsOutsideADbContextClassAreNotAttributed()
    {
        var result = Scan("""
            public class PlainHolder
            {
                public DbSet<Order> Orders { get; set; }
            }
            """);

        Assert.Empty(result.DbContexts);
    }

    [Fact]
    public void IsDbContextBase_RecognizesBaseCallArguments()
    {
        Assert.True(CSharpScanner.IsDbContextBase("DbContext(options), IAppDbContext"));
        Assert.True(CSharpScanner.IsDbContextBase("IdentityDbContext<AppUser>(options)"));
        Assert.False(CSharpScanner.IsDbContextBase("IAppDbContext"));
        Assert.False(CSharpScanner.IsDbContextBase("DbContextOptions<AppDbContext>"));
        Assert.False(CSharpScanner.IsDbContextBase("MemoryCacheContext(key)"));
    }

    [Fact]
    public void ParseTypeReference_StripsPrimaryConstructorBaseCallArguments()
    {
        var (name, args) = CSharpScanner.ParseTypeReference("IdentityDbContext<AppUser>(options)");

        Assert.Equal("IdentityDbContext", name);
        Assert.Equal(new[] { "AppUser" }, args);
    }

    [Fact]
    public void ParseTypeReference_LeavesTupleTypesAlone()
    {
        var (name, _) = CSharpScanner.ParseTypeReference("(int Id, string Name)");

        Assert.Equal("(int Id, string Name)", name);
    }
}
