using System.Globalization;
using System.Text.RegularExpressions;
using CqrsLens;
using Xunit;

namespace CqrsLens.Tests;

/// <summary>
/// Guards checkpoint 5 defect #17: the self-referential ER relationship loop and its
/// label must render fully inside the viewBox. Markup presence alone is not enough —
/// the previous fix had the label in the markup but clipped off the top of the canvas.
/// These assertions therefore check rendered coordinates against the canvas bounds.
/// </summary>
public sealed class ErSelfLoopBoundsTests
{
    private static DbContextInfo SelfLoopContext()
    {
        var context = new DbContextInfo
        {
            Name = "NorthwindDbContext",
            Namespace = "Northwind.Infrastructure",
            ProjectName = "Northwind.Infrastructure",
        };

        var employee = new EntityInfo { Name = "Employee", Namespace = "Northwind.Domain" };
        employee.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        employee.Properties.Add(new EntityPropertyInfo { Name = "Name", Type = "string" });
        context.Entities.Add(employee);

        context.Relationships.Add(new EntityRelationshipInfo(
            "Northwind.Domain.Employee",
            "Northwind.Domain.Employee",
            PrincipalNavigation: "DirectReports",
            DependentNavigation: "Manager",
            ForeignKey: "ReportsTo",
            IsRequired: false,
            Kind: EntityRelationshipKind.OneToMany));

        return context;
    }

    private static double ViewBoxHeight(string svg)
    {
        var match = Regex.Match(svg, "viewBox=\"0 0 (?<w>[0-9.]+) (?<h>[0-9.]+)\"");
        Assert.True(match.Success, "SVG is missing a viewBox.");
        return double.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void SelfLoopLabelAndLoop_RenderInsideTheCanvasBounds()
    {
        var source = DiagramBuilder.ErDiagram(SelfLoopContext());
        Assert.NotNull(source);
        var svg = MermaidSvgRenderer.Render(source!);
        var height = ViewBoxHeight(svg);

        // The label text must be present and must stay exactly this.
        Assert.Contains("DirectReports / Manager (0..1..*)", svg);

        // Every relationship label pill and its text must sit inside the canvas.
        var labelGroups = Regex.Matches(
            svg,
            "<g class=\"er-relationship-label\"[^>]*>.*?</g>",
            RegexOptions.Singleline);
        Assert.NotEmpty(labelGroups);
        foreach (Match group in labelGroups)
        {
            var rectY = ExtractY(group.Value, "<rect");
            var textY = ExtractY(group.Value, "<text");
            Assert.True(rectY >= 0, $"Label pill is clipped above the canvas: rect y={rectY}.");
            Assert.True(rectY <= height, $"Label pill is clipped below the canvas: rect y={rectY} > {height}.");
            Assert.True(textY >= 0, $"Label text is clipped above the canvas: text y={textY}.");
            Assert.True(textY <= height, $"Label text is clipped below the canvas: text y={textY} > {height}.");
        }

        // The self-loop path (principal id == dependent id) must also stay inside.
        var relationshipGroups = Regex.Matches(
            svg,
            "<g class=\"er-relationship\"[^>]*>.*?</g>",
            RegexOptions.Singleline);
        var selfLoops = relationshipGroups
            .Where(m => SelfLoop(m.Value))
            .ToList();
        Assert.NotEmpty(selfLoops);
        foreach (var loop in selfLoops)
        {
            var path = Regex.Match(loop.Value, "d=\"(?<d>[^\"]+)\"").Groups["d"].Value;
            var ys = YCoordinates(path).ToList();
            Assert.NotEmpty(ys);
            Assert.True(ys.Min() >= 0, $"Self-loop is clipped above the canvas: min y={ys.Min()}.");
            Assert.True(ys.Max() <= height, $"Self-loop is clipped below the canvas: max y={ys.Max()} > {height}.");
        }
    }

    [Fact]
    public void DiagramWithoutSelfLoop_KeepsTheOriginalTopMargin()
    {
        var context = new DbContextInfo
        {
            Name = "ShopDbContext",
            Namespace = "Shop.Infrastructure",
            ProjectName = "Shop.Infrastructure",
        };
        var customer = new EntityInfo { Name = "Customer", Namespace = "Shop.Domain" };
        customer.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        var order = new EntityInfo { Name = "Order", Namespace = "Shop.Domain" };
        order.Properties.Add(new EntityPropertyInfo { Name = "Id", Type = "int", IsKey = true });
        context.Entities.Add(customer);
        context.Entities.Add(order);
        context.Relationships.Add(new EntityRelationshipInfo(
            "Shop.Domain.Customer",
            "Shop.Domain.Order",
            PrincipalNavigation: "Orders",
            DependentNavigation: "Customer",
            ForeignKey: "CustomerId",
            IsRequired: true,
            Kind: EntityRelationshipKind.OneToMany));

        var source = DiagramBuilder.ErDiagram(context);
        var svg = MermaidSvgRenderer.Render(source!);

        // No self-loop ⇒ no reserved top padding: the first boxes start at the 24px margin.
        var firstEntityY = Regex.Matches(svg, "<g class=\"er-entity\"[^>]*><rect[^>]*\\by=\"(?<y>[0-9.]+)\"")
            .Select(m => double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture))
            .Min();
        Assert.Equal(24.0, firstEntityY);
    }

    private static double ExtractY(string group, string tag)
    {
        var match = Regex.Match(group, Regex.Escape(tag) + "[^>]*\\by=\"(?<y>[0-9.]+)\"");
        Assert.True(match.Success, $"Could not find a y coordinate on {tag}.");
        return double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
    }

    private static bool SelfLoop(string group)
    {
        var principal = Regex.Match(group, "data-principal=\"(?<id>[^\"]+)\"").Groups["id"].Value;
        var dependent = Regex.Match(group, "data-dependent=\"(?<id>[^\"]+)\"").Groups["id"].Value;
        return principal.Length > 0 && string.Equals(principal, dependent, StringComparison.Ordinal);
    }

    private static IEnumerable<double> YCoordinates(string path)
    {
        foreach (Match match in Regex.Matches(path, "[ML]\\s*[-\\.0-9]+\\s+(?<y>[-\\.0-9]+)"))
            yield return double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture);
    }
}
