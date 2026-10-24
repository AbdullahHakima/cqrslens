using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

public sealed class EfCoreErDiagramTests : IDisposable
{
    private readonly string _root;

    public EfCoreErDiagramTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "cqrslens-ef-er-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(
            Path.Combine(_root, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void WriteFile(string name, string code)
        => File.WriteAllText(Path.Combine(_root, name), code);

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void DiscoversTransitiveEntitiesWithKeysAndExcludesValueObjects(AnalysisMode mode)
    {
        WriteFile("Shop.cs", """
            using System;
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using Microsoft.EntityFrameworkCore;

            namespace Shop;

            public sealed class Order
            {
                public Guid Id { get; set; }
                public int CustomerId { get; set; }
                public Customer? Customer { get; set; }
                public List<OrderLine> Lines { get; set; } = new();
                public ShippingAddress Address { get; set; } = new();
                [NotMapped]
                public string IgnoredNote { get; set; } = "";
                public byte[] RowVersion { get; set; } = Array.Empty<byte>();
            }

            public sealed class OrderLine
            {
                public int OrderLineId { get; set; }
                public Guid OrderId { get; set; }
                public Order? Order { get; set; }
                public decimal UnitPrice { get; set; }
            }

            public sealed class Customer
            {
                public int Id { get; set; }
                [Required]
                public string Name { get; set; } = "";
                public List<Order> Orders { get; set; } = new();
            }

            // Value object: no Id, [Key] or Fluent HasKey -> excluded from ER model.
            public sealed class ShippingAddress
            {
                public string Street { get; set; } = "";
                public string City { get; set; } = "";
            }

            public sealed class ShopDbContext : DbContext
            {
                public DbSet<Order> Orders { get; set; } = null!;
            }
            """);

        var result = Analyzer.Analyze(_root, mode);
        var context = Assert.Single(result.DbContexts);

        Assert.Equal(
            new[] { "Shop.Customer", "Shop.Order", "Shop.OrderLine" },
            context.Entities.Select(e => e.FullName).ToArray());

        var order = context.Entities.Single(e => e.Name == "Order");
        Assert.DoesNotContain(order.Properties, p => p.Name == "IgnoredNote");
        var rowVersion = Assert.Single(order.Properties, p => p.Name == "RowVersion");
        Assert.False(rowVersion.IsNavigation);
        Assert.True( Assert.Single(order.Properties, p => p.Name == "Id").IsKey);
        Assert.True(Assert.Single(order.Properties, p => p.Name == "CustomerId").IsForeignKey);

        var line = context.Entities.Single(e => e.Name == "OrderLine");
        Assert.True(Assert.Single(line.Properties, p => p.Name == "OrderLineId").IsKey);
        Assert.True(Assert.Single(line.Properties, p => p.Name == "OrderId").IsForeignKey);

        Assert.Equal(2, context.Relationships.Count);
        Assert.Contains(context.Relationships, r =>
            r.PrincipalEntity == "Shop.Customer" &&
            r.DependentEntity == "Shop.Order" &&
            r.ForeignKey == "CustomerId" &&
            r.IsRequired &&
            r.Kind == EntityRelationshipKind.OneToMany);
        Assert.Contains(context.Relationships, r =>
            r.PrincipalEntity == "Shop.Order" &&
            r.DependentEntity == "Shop.OrderLine" &&
            r.ForeignKey == "OrderId" &&
            r.IsRequired &&
            r.Kind == EntityRelationshipKind.OneToMany);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SupportsAnnotationsInversePropertyAndForeignKey(AnalysisMode mode)
    {
        WriteFile("Annotated.cs", """
            using System.Collections.Generic;
            using System.ComponentModel.DataAnnotations;
            using System.ComponentModel.DataAnnotations.Schema;
            using Microsoft.EntityFrameworkCore;

            namespace Annotated;

            public sealed class User
            {
                [Key]
                public string UserCode { get; set; } = "";

                [InverseProperty(nameof(Ticket.CreatedBy))]
                public List<Ticket> AuthoredTickets { get; set; } = new();

                [InverseProperty("AssignedTo")]
                public List<Ticket> AssignedTickets { get; set; } = new();
            }

            public sealed class Ticket
            {
                public int Id { get; set; }

                [Required]
                public string AuthorCode { get; set; } = "";

                [ForeignKey(nameof(AuthorCode))]
                public User? CreatedBy { get; set; }

                public string? AssigneeCode { get; set; }

                [ForeignKey("AssigneeCode")]
                public User? AssignedTo { get; set; }
            }

            public sealed class HelpdeskDbContext : DbContext
            {
                public DbSet<User> Users { get; set; } = null!;
                public DbSet<Ticket> Tickets { get; set; } = null!;
            }
            """);

        var result = Analyzer.Analyze(_root, mode);
        Assert.Empty(result.Diagnostics);

        var context = Assert.Single(result.DbContexts);
        Assert.Equal(2, context.Relationships.Count);

        var authored = Assert.Single(context.Relationships, r => r.PrincipalNavigation == "AuthoredTickets");
        Assert.Equal("CreatedBy", authored.DependentNavigation);
        Assert.Equal("AuthorCode", authored.ForeignKey);
        Assert.True(authored.IsRequired);

        var assigned = Assert.Single(context.Relationships, r => r.PrincipalNavigation == "AssignedTickets");
        Assert.Equal("AssignedTo", assigned.DependentNavigation);
        Assert.Equal("AssigneeCode", assigned.ForeignKey);
        Assert.False(assigned.IsRequired);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void SupportsFluentApiInOnModelCreatingAndEntityTypeConfiguration(AnalysisMode mode)
    {
        WriteFile("Fluent.cs", """
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;

            namespace FluentDemo;

            public abstract class EntityBase
            {
                public int Id { get; set; }
            }

            public sealed class Course : EntityBase
            {
                public string Title { get; set; } = "";
                public CourseDetail? Detail { get; set; }
                public List<Tag> Tags { get; set; } = new();
                public List<Enrollment> Enrollments { get; set; } = new();
            }

            public sealed class CourseDetail
            {
                public int CourseKey { get; set; }
                public string Syllabus { get; set; } = "";
                public Course? Course { get; set; }
            }

            public sealed class Enrollment
            {
                public int CourseId { get; set; }
                public int StudentId { get; set; }
                public Course? Course { get; set; }
            }

            public sealed record Tag(int Id, string Label)
            {
                public List<Course> Courses { get; set; } = new();
            }

            public sealed class EnrollmentConfiguration : IEntityTypeConfiguration<Enrollment>
            {
                public void Configure(EntityTypeBuilder<Enrollment> builder)
                {
                    builder.HasKey(x => new { x.CourseId, x.StudentId });
                    builder.HasOne(x => x.Course)
                        .WithMany(c => c.Enrollments)
                        .HasForeignKey(x => x.CourseId)
                        .IsRequired();
                }
            }

            public sealed class SchoolDbContext : DbContext
            {
                public DbSet<Course> Courses { get; set; } = null!;

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.Entity<CourseDetail>().HasKey(d => d.CourseKey);
                    modelBuilder.Entity<Course>()
                        .HasOne(c => c.Detail)
                        .WithOne(d => d.Course)
                        .HasForeignKey<CourseDetail>(d => d.CourseKey)
                        .IsRequired();

                    modelBuilder.Entity<Course>()
                        .HasMany(c => c.Tags)
                        .WithMany(t => t.Courses);
                }
            }
            """);

        var result = Analyzer.Analyze(_root, mode);
        var context = Assert.Single(result.DbContexts);

        Assert.Equal(
            new[]
            {
                "FluentDemo.Course",
                "FluentDemo.CourseDetail",
                "FluentDemo.Enrollment",
                "FluentDemo.Tag",
            },
            context.Entities.Select(e => e.FullName).ToArray());

        var enrollment = context.Entities.Single(e => e.Name == "Enrollment");
        var courseIdProp = enrollment.Properties.Single(p => p.Name == "CourseId");
        var studentIdProp = enrollment.Properties.Single(p => p.Name == "StudentId");
        Assert.True(courseIdProp.IsKey);
        Assert.True(courseIdProp.IsForeignKey);
        Assert.True(studentIdProp.IsKey);

        Assert.Contains(context.Relationships, r =>
            r.Kind == EntityRelationshipKind.OneToOne &&
            r.PrincipalEntity == "FluentDemo.Course" &&
            r.DependentEntity == "FluentDemo.CourseDetail" &&
            r.ForeignKey == "CourseKey" &&
            r.IsRequired);

        Assert.Contains(context.Relationships, r =>
            r.Kind == EntityRelationshipKind.ManyToMany &&
            r.PrincipalEntity == "FluentDemo.Course" &&
            r.DependentEntity == "FluentDemo.Tag");

        Assert.Contains(context.Relationships, r =>
            r.Kind == EntityRelationshipKind.OneToMany &&
            r.PrincipalEntity == "FluentDemo.Course" &&
            r.DependentEntity == "FluentDemo.Enrollment" &&
            r.ForeignKey == "CourseId" &&
            r.IsRequired);

        var er = DiagramBuilder.ErDiagram(context);
        Assert.NotNull(er);
        Assert.Contains("erDiagram", er);
        Assert.Contains("int CourseId PK, FK", er);
        Assert.Contains("int StudentId PK", er);
        Assert.Contains("ent_Course ||--o| ent_CourseDetail : \"Detail / Course\"", er);
        Assert.Contains("ent_Course ||--o{ ent_Enrollment : \"Enrollments / Course\"", er);
        Assert.Contains("ent_Course }o--o{ ent_Tag : \"Tags / Courses\"", er);
    }

    [Theory]
    [InlineData(AnalysisMode.Text)]
    [InlineData(AnalysisMode.Roslyn)]
    public void ReportsAmbiguousInverseNavigationsWithoutBreakingStrictChecks(AnalysisMode mode)
    {
        WriteFile("Ambiguous.cs", """
            using System.Collections.Generic;
            using Microsoft.EntityFrameworkCore;

            namespace AmbiguousDemo;

            public sealed class User
            {
                public int Id { get; set; }
                public List<Order> CreatedOrders { get; set; } = new();
                public List<Order> ApprovedOrders { get; set; } = new();
            }

            public sealed class Order
            {
                public int Id { get; set; }
                public User? CreatedBy { get; set; }
                public User? ApprovedBy { get; set; }
            }

            public sealed class AppDbContext : DbContext
            {
                public DbSet<User> Users { get; set; } = null!;
                public DbSet<Order> Orders { get; set; } = null!;
            }
            """);

        var result = Analyzer.Analyze(_root, mode);
        var context = Assert.Single(result.DbContexts);

        Assert.Empty(context.Relationships);
        Assert.True(result.CanRunStrictChecks);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == AnalysisResult.EfNavigationUnresolvedCode &&
            d.Severity == AnalysisDiagnosticSeverity.Info);
    }

    [Fact]
    public void ErDiagram_RendersInMarkdownAndHtmlReportsAndSanitizesGenericAttributeTypes()
    {
        WriteFile("Report.cs", """
            using System;
            using Microsoft.EntityFrameworkCore;

            namespace ReportDemo;

            public sealed class Invoice
            {
                public Guid Id { get; set; }
                public Nullable<int> DiscountPercent { get; set; }
            }

            public sealed class BillingDbContext : DbContext
            {
                public DbSet<Invoice> Invoices { get; set; } = null!;
            }
            """);

        var result = Analyzer.Analyze(_root);
        var md = ReportGenerator.Generate(result);
        var html = HtmlReportGenerator.Generate(result);

        Assert.Contains("### BillingDbContext ER Diagram", md);
        Assert.Contains("erDiagram", md);
        Assert.Contains("accTitle: BillingDbContext ER diagram", md);
        Assert.Contains("Nullable~int~ DiscountPercent", md);
        Assert.Contains("Guid Id PK", md);

        Assert.Contains("<h3>BillingDbContext ER Diagram</h3>", html);
        Assert.Contains("erDiagram", html);
        Assert.Contains("Nullable~int~ DiscountPercent", html);
    }
}
