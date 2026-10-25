using System.Text;

namespace CqrsLens;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Some hosts disallow changing OutputEncoding; continue with the current encoding.
            }

            return CliRunner.Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return ExitCodes.Unexpected;
        }
    }
}
