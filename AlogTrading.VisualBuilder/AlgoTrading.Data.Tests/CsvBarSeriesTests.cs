using AlgoTrading.Data;
using FluentAssertions;
using Xunit;

namespace AlgoTrading.Data.Tests;

public sealed class CsvBarSeriesTests : IDisposable
{
    private readonly string _tempDir;

    public CsvBarSeriesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AlgoTradingCsv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_ParsesBasicCsv()
    {
        string path = Path.Combine(_tempDir, "RELIANCE_1m.csv");
        File.WriteAllText(path,
            "DateTime,Open,High,Low,Close,Volume,OI\n" +
            "2024-01-01T09:15:00Z,2500.00,2505.50,2498.20,2503.10,125000,0\n" +
            "2024-01-01T09:16:00Z,2503.10,2506.00,2501.50,2505.40,98000,0\n" +
            "2024-01-01T09:17:00Z,2505.40,2510.00,2504.00,2509.20,110000,0\n");

        var series = CsvBarSeries.Load(path);

        series.Count.Should().Be(3);
        series.Symbol.Should().Be("RELIANCE");
        series.Timeframe.ToString().Should().Be("1m");
        series.DateTime.Span[0].Should().Be(new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc));
        series.Open.Span[0].Should().Be(2500.00m);
        series.High.Span[0].Should().Be(2505.50m);
        series.Low.Span[0].Should().Be(2498.20m);
        series.Close.Span[0].Should().Be(2503.10m);
        series.Volume.Span[0].Should().Be(125000m);
        series.Close.Span[2].Should().Be(2509.20m);
    }

    [Fact]
    public void Load_HandlesMissingVolumeAndOi()
    {
        string path = Path.Combine(_tempDir, "TCS_1m.csv");
        File.WriteAllText(path,
            "DateTime,Open,High,Low,Close\n" +
            "2024-01-01T09:15:00Z,3500.00,3505.50,3498.20,3503.10\n");

        var series = CsvBarSeries.Load(path);

        series.Count.Should().Be(1);
        series.Volume.Span[0].Should().Be(0m);
        series.OI.Span[0].Should().Be(0m);
    }

    [Fact]
    public void Load_HandlesCrLfLineEndings()
    {
        string path = Path.Combine(_tempDir, "INFY_1m.csv");
        File.WriteAllText(path,
            "DateTime,Open,High,Low,Close,Volume,OI\r\n" +
            "2024-01-01T09:15:00Z,1500.00,1505.50,1498.20,1503.10,125000,0\r\n" +
            "2024-01-01T09:16:00Z,1503.10,1506.00,1501.50,1505.40,98000,0\r\n");

        var series = CsvBarSeries.Load(path);

        series.Count.Should().Be(2);
        series.Close.Span[1].Should().Be(1505.40m);
    }

    [Fact]
    public void Load_AppliesDateRangeFilter()
    {
        string path = Path.Combine(_tempDir, "HDFCBANK_1m.csv");
        File.WriteAllText(path,
            "DateTime,Open,High,Low,Close,Volume,OI\n" +
            "2024-01-01T09:15:00Z,1500,1501,1499,1500,100,0\n" +
            "2024-01-01T09:16:00Z,1500,1501,1499,1500,100,0\n" +
            "2024-01-01T09:17:00Z,1500,1501,1499,1500,100,0\n" +
            "2024-01-01T09:18:00Z,1500,1501,1499,1500,100,0\n" +
            "2024-01-01T09:19:00Z,1500,1501,1499,1500,100,0\n");

        var from = new DateTime(2024, 1, 1, 9, 16, 0, DateTimeKind.Utc);
        var to   = new DateTime(2024, 1, 1, 9, 18, 0, DateTimeKind.Utc);
        var series = CsvBarSeries.Load(path, 0u, from, to);

        series.Count.Should().Be(3);
        series.DateTime.Span[0].Should().Be(from);
        series.DateTime.Span[2].Should().Be(to);
    }

    [Fact]
    public void Load_ThrowsOnMissingFile()
    {
        Action act = () => CsvBarSeries.Load(Path.Combine(_tempDir, "missing_1m.csv"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_ThrowsOnMalformedFilename()
    {
        string path = Path.Combine(_tempDir, "nounderscore.csv");
        File.WriteAllText(path, "DateTime,Open,High,Low,Close,Volume,OI\n");
        Action act = () => CsvBarSeries.Load(path);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Load_ThrowsOnMalformedRow()
    {
        string path = Path.Combine(_tempDir, "SBIN_1m.csv");
        File.WriteAllText(path,
            "DateTime,Open,High,Low,Close,Volume,OI\n" +
            "not-a-date,2500,2505,2498,2503,125,0\n");
        Action act = () => CsvBarSeries.Load(path);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Load_ParallelPathIsConsistentForLargeInput()
    {
        string path = Path.Combine(_tempDir, "ICICIBANK_1m.csv");
        var sb = new System.Text.StringBuilder(1_000_000);
        sb.Append("DateTime,Open,High,Low,Close,Volume,OI\n");
        var t0 = new DateTime(2024, 1, 1, 9, 15, 0, DateTimeKind.Utc);
        const int n = 20_000;
        for (int i = 0; i < n; i++)
        {
            var dt = t0.AddMinutes(i);
            sb.Append(dt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            sb.Append(',').Append(100m + i).Append(",101,99,").Append(100m + i).Append(",10,0\n");
        }
        File.WriteAllText(path, sb.ToString());

        var series = CsvBarSeries.Load(path);

        series.Count.Should().Be(n);
        series.Open.Span[0].Should().Be(100m);
        series.Open.Span[n - 1].Should().Be(100m + (n - 1));
        series.DateTime.Span[n - 1].Should().Be(t0.AddMinutes(n - 1));
    }
}