using Xunit;

namespace AlgoTrading.Backtest.Cli.Tests;

public class LockFileTests
{
    [Fact]
    public void Acquire_Then_Second_Acquire_Fails()
    {
        var dir = TempFolder.Create();
        try
        {
            var lockPath = Path.Combine(dir, "daemon.lock");
            using var first = LockFile.Acquire(lockPath);
            Assert.Throws<InvalidOperationException>(() => LockFile.Acquire(lockPath));
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void Dispose_Releases_Lock()
    {
        var dir = TempFolder.Create();
        try
        {
            var lockPath = Path.Combine(dir, "daemon.lock");
            {
                using var first = LockFile.Acquire(lockPath);
            }
            using var second = LockFile.Acquire(lockPath);
            Assert.True(File.Exists(lockPath));
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void Corrupt_Lock_File_Recovered()
    {
        var dir = TempFolder.Create();
        try
        {
            var lockPath = Path.Combine(dir, "daemon.lock");
            File.WriteAllText(lockPath, "not-json-at-all");
            using var l = LockFile.Acquire(lockPath);
            Assert.True(File.Exists(lockPath));
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void Stale_Pid_Recovered()
    {
        var dir = TempFolder.Create();
        try
        {
            var lockPath = Path.Combine(dir, "daemon.lock");
            // PID 1 is never the C# host on Windows; on Unix it might be "init" which is alive.
            // Use an absurdly high PID that almost certainly doesn't exist.
            File.WriteAllText(lockPath, "{\"pid\": 2000000000, \"startedAtUtc\": \"2000-01-01T00:00:00Z\"}");
            using var l = LockFile.Acquire(lockPath);
            Assert.True(File.Exists(lockPath));
        }
        finally { TempFolder.Delete(dir); }
    }

    [Fact]
    public void Creates_Parent_Directory()
    {
        var dir = TempFolder.Create();
        try
        {
            var lockPath = Path.Combine(dir, "nested", "sub", "daemon.lock");
            using var l = LockFile.Acquire(lockPath);
            Assert.True(File.Exists(lockPath));
        }
        finally { TempFolder.Delete(dir); }
    }
}