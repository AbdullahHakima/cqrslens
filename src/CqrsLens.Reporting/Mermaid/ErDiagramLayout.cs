namespace CqrsLens;

/// <summary>Assigns deterministic box geometry to ER entities in a grid.</summary>
internal static class ErDiagramLayout
{
    internal const double HeaderHeight = 32.0;
    internal const double RowHeight = 24.0;

    private const double OuterMargin = 24.0;
    private const double ColumnGap = 90.0;
    private const double RowGap = 64.0;

    internal static (double Width, double Height) Apply(ErDiagramModel model)
    {
        foreach (var entity in model.Entities)
        {
            var headerW = entity.Label.Length * 7.8 + 28.0;
            var maxAttrW = 0.0;
            foreach (var attr in entity.Attributes)
            {
                var w = (attr.Type.Length + attr.Name.Length + attr.Keys.Length) * 7.0 + 48.0;
                if (w > maxAttrW) maxAttrW = w;
            }

            entity.Width = Math.Max(160.0, Math.Ceiling(Math.Max(headerW, maxAttrW)));
            entity.Height = HeaderHeight + Math.Max(1, entity.Attributes.Count) * RowHeight;
            if (entity.Attributes.Count == 0)
                entity.Height = HeaderHeight;
        }

        var columns = Math.Max(1, Math.Min(3, model.Entities.Count));
        var colWidths = new double[columns];
        var rowCount = (model.Entities.Count + columns - 1) / columns;
        var rowHeights = new double[Math.Max(1, rowCount)];

        for (var i = 0; i < model.Entities.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            if (model.Entities[i].Width > colWidths[col])
                colWidths[col] = model.Entities[i].Width;
            if (model.Entities[i].Height > rowHeights[row])
                rowHeights[row] = model.Entities[i].Height;
        }

        for (var i = 0; i < model.Entities.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            var x = OuterMargin;
            for (var c = 0; c < col; c++)
                x += colWidths[c] + ColumnGap;
            var y = OuterMargin;
            for (var r = 0; r < row; r++)
                y += rowHeights[r] + RowGap;

            model.Entities[i].X = x + (colWidths[col] - model.Entities[i].Width) / 2.0;
            model.Entities[i].Y = y;
        }

        var canvasWidth = Math.Max(
            240.0,
            OuterMargin * 2 + colWidths.Sum() + Math.Max(0, columns - 1) * ColumnGap);
        var canvasHeight = Math.Max(
            100.0,
            OuterMargin * 2 + rowHeights.Sum() + Math.Max(0, rowCount - 1) * RowGap);
        return (canvasWidth, canvasHeight);
    }
}
