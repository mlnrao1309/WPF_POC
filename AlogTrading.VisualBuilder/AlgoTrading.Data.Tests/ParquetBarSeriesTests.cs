using AlgoTrading.Data;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Data.Tests;

public sealed class ParquetBarSeriesTests
{
    [Fact]
    public void Load_ThrowsOnMissingFile()
    {
        Action act = () => ParquetBarSeries.Load(@"C:\does\not\exist_RELIANCE_1m.parquet");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_ThrowsOnMalformedFilename()
    {
        string dir = Path.Combine(Path.GetTempPath(), "AlgoTradingParquet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "nounderscore.parquet");
            File.WriteAllBytes(path, Array.Empty<byte>());
            Action act = () => ParquetBarSeries.Load(path);
            act.Should().Throw<ArgumentException>();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}