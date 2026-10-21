namespace CqrsLens;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return CliRunner.Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitCodes.Unexpected;
        }
    }
}
