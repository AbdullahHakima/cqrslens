using System.Text;

namespace CqrsLens;

/// <summary>Writes the validation-coverage figure: per project and solution-wide.</summary>
internal static class MarkdownValidationCoverageWriter
{
    internal static void Write(StringBuilder md, AnalysisResult result)
    {
        var view = ValidationCoverageView.Build(result);

        md.AppendLine("## Validation Coverage");
        md.AppendLine();
        md.AppendLine("_The share of requests with a linked validator (the model's request→validator linkage). " +
                      "A request with no linked validator is also reported individually by the " +
                      "command-without-validator check._");
        md.AppendLine();
        md.AppendLine("| Project | Requests | With linked validator | Coverage | Validators |");
        md.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var row in view.Projects)
        {
            md.AppendLine($"| {MarkdownEscaping.TableCell(row.Project)} | {row.Requests} | {row.Covered} | " +
                          $"{MarkdownEscaping.TableCell(ValidationCoverageView.CoverageText(row))} | {row.Validators} |");
        }
        var solution = view.Solution;
        md.AppendLine($"| **{MarkdownEscaping.TableCell(solution.Project)}** | **{solution.Requests}** | " +
                      $"**{solution.Covered}** | **{MarkdownEscaping.TableCell(ValidationCoverageView.CoverageText(solution))}** | " +
                      $"**{solution.Validators}** |");
        md.AppendLine();
    }
}
