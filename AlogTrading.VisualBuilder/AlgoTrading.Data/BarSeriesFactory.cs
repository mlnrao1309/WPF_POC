using AlgoTrading.Models;
using SysDateTime = System.DateTime;

namespace AlgoTrading.Data;

public sealed class BarSeriesFactory
{
    private readonly DataOptions _options;
    private readonly IInstrumentLookup? _lookup;

    public BarSeriesFactory(DataOptions options, IInstrumentLookup? lookup = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lookup = lookup;
    }

    public IBarSeries Load(uint instrumentToken, Timeframe timeframe, SysDateTime fromUtc, SysDateTime toUtc)
    {
        return _options.Source switch
        {
            "Sql"     => SqlBarSeries.Load(_options.Sql.ConnectionString, instrumentToken, timeframe, fromUtc, toUtc),
            "Csv"     => LoadCsv(instrumentToken, timeframe, fromUtc, toUtc),
            "Parquet" => LoadParquet(instrumentToken, timeframe, fromUtc, toUtc),
            _ => throw new InvalidOperationException($"Unknown Data.Source: '{_options.Source}'. Expected Sql, Csv, or Parquet.")
        };
    }

    private IBarSeries LoadCsv(uint instrumentToken, Timeframe timeframe, SysDateTime fromUtc, SysDateTime toUtc)
    {
        string symbol = ResolveSymbol(instrumentToken);
        string path = Path.Combine(_options.Csv.FolderPath, $"{symbol}_{timeframe}.csv");
        return CsvBarSeries.Load(path, instrumentToken, fromUtc, toUtc);
    }

    private IBarSeries LoadParquet(uint instrumentToken, Timeframe timeframe, SysDateTime fromUtc, SysDateTime toUtc)
    {
        string symbol = ResolveSymbol(instrumentToken);
        string path = Path.Combine(_options.Parquet.FolderPath, $"{symbol}_{timeframe}.parquet");
        return ParquetBarSeries.Load(path, instrumentToken, fromUtc, toUtc);
    }

    private string ResolveSymbol(uint instrumentToken)
    {
        if (_lookup is null)
            throw new InvalidOperationException(
                "File-based data sources require an IInstrumentLookup to resolve instrument token to symbol.");
        if (!_lookup.TryGet(instrumentToken, out var info))
            throw new InvalidOperationException($"Instrument token {instrumentToken} not found.");
        return info.Symbol;
    }
}