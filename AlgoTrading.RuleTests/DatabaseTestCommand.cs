using System.IO;

internal static class DatabaseTestCommand
{
    internal static async Task<bool> TryRunAsync(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            return false;
        }

        if (arguments[0] != "--database-crossovers" || arguments.Length > 2)
        {
            throw new ArgumentException("Usage: --database-crossovers [output-directory]");
        }

        string outputDirectory;
        if (arguments.Length == 2)
        {
            outputDirectory = Path.GetFullPath(arguments[1]);
        }
        else
        {
            outputDirectory = Path.Combine(AppContext.BaseDirectory, "logs", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        }

        try
        {
            await new DatabaseCrossoverTest().RunAsync(outputDirectory);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("Database crossover test failed: " + exception.Message);
            Environment.ExitCode = 1;
        }

        return true;
    }
}

