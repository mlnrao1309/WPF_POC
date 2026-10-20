using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class FileWatcherTests
{
    private static async Task<string?> ReadWithTimeout(FileWatcher w, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try { return await w.Reader.ReadAsync(cts.Token); }
        catch (OperationCanceledException) { return null; }
        catch (System.Threading.Channels.ChannelClosedException) { return null; }
    }

    [Fact]
    public async Task Watches_New_File()
    {
        var dir = TempFolder.Create();
        try
        {
            using var w = new FileWatcher(dir, stabilityMs: 200);
            w.Start();

            var path = Path.Combine(dir, "job-A.job.json");
            File.WriteAllText(path, "{}");

            var got = await ReadWithTimeout(w, 5000);
            Assert.NotNull(got);
            Assert.Equal(path, got);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public async Task Initial_Scan_Picks_Up_Existing_Files()
    {
        var dir = TempFolder.Create();
        try
        {
            var path = Path.Combine(dir, "pre.job.json");
            File.WriteAllText(path, "{}");

            using var w = new FileWatcher(dir, stabilityMs: 200);
            w.Start();

            var got = await ReadWithTimeout(w, 5000);
            Assert.Equal(path, got);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public async Task Ignores_Non_Job_Files()
    {
        var dir = TempFolder.Create();
        try
        {
            using var w = new FileWatcher(dir, stabilityMs: 200);
            w.Start();

            File.WriteAllText(Path.Combine(dir, "strategy.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "random.txt"), "x");

            var got = await ReadWithTimeout(w, 1500);
            Assert.Null(got);
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public async Task Unstable_File_Not_Enqueued_Until_Stable()
    {
        var dir = TempFolder.Create();
        try
        {
            using var w = new FileWatcher(dir, stabilityMs: 600);
            w.Start();

            var path = Path.Combine(dir, "slow.job.json");
            File.WriteAllText(path, "{}");

            // Update the file quickly, repeatedly.
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(100);
                File.AppendAllText(path, " ");
            }

            var got = await ReadWithTimeout(w, 5000);
            Assert.Equal(path, got);
        }
        finally { TempFolder.Delete(dir); }
    }
}