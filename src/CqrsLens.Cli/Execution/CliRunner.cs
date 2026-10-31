namespace CqrsLens;

/// <summary>Dispatches parsed commands and translates failures into process exit codes.</summary>
public static class CliRunner
{
    public static int Run(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        var command = CliParser.Parse(args);
        switch (command.Kind)
        {
            case CliCommandKind.Help:
                UsageWriter.Write(stdout);
                return ExitCodes.Success;
            case CliCommandKind.Version:
                stdout.WriteLine($"CqrsLens {ToolInfo.Version}");
                return ExitCodes.Success;
            case CliCommandKind.Error:
                stderr.WriteLine(command.Error);
                if (command.ShowUsage) UsageWriter.Write(stderr);
                return ExitCodes.Usage;
        }

        try
        {
            if (command.Options!.Mcp)
                return McpServer.Run(command.Options, Console.In, stdout, stderr);

            return ScanCommand.Run(command.Options!, stdout, stderr);
        }
        catch (CliFailureException ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return ex.ExitCode;
        }
        catch (Exception ex) when (ex is ConfigurationException or BaselineException or DiffException)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Usage;
        }
        catch (Exception ex)
        {
            // Keep the CLI's failure output concise; never dump a stack trace on users.
            stderr.WriteLine($"error: {ex.Message}");
            return ExitCodes.Unexpected;
        }
    }
}
