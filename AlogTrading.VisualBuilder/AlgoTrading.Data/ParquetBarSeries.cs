using AlgoTrading.Models;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using SysDateTime = System.DateTime;
using SysDateTimeKind = System.DateTimeKind;
using SysDateTimeOffset = System.DateTimeOffset;
namespace AlgoTrading.Data;

public sealed class ParquetBarSeries : IBarSeries
{
    public uint InstrumentToken { get; }
    public string Symbol { get; }
    public Timeframe Timeframe { get; }
    public SysDateTime From { get; }
    public SysDateTime To { get; }
    public int Count { get; }

    public ReadOnlyMemory<decimal> Open { get; }
    public ReadOnlyMemory<decimal> High { get; }
    public ReadOnlyMemory<decimal> Low { get; }
    public ReadOnlyMemory<decimal> Close { get; }
    public ReadOnlyMemory<decimal> Volume { get; }
    public ReadOnlyMemory<decimal> OI { get; }
    public ReadOnlyMemory<SysDateTime> DateTime { get; }

    private ParquetBarSeries(
        uint instrumentToken, string symbol, Timeframe timeframe,
        SysDateTime[] dateTime, decimal[] open, decimal[] high, decimal[] low,
        decimal[] close, decimal[] volume, decimal[] oi)
    {
        InstrumentToken = instrumentToken;
        Symbol = symbol;
        Timeframe = timeframe;
        DateTime = dateTime;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        OI = oi;
        Count = dateTime.Length;
        From = dateTime.Length > 0 ? dateTime[0] : default;
        To = dateTime.Length > 0 ? dateTime[^1] : default;
    }

    public static ParquetBarSeries Load(string filePath)
        => Load(filePath, 0u, null, null);

    public static ParquetBarSeries Load(string filePath, uint instrumentToken, SysDateTime? fromUtc, SysDateTime? toUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Parquet bar file not found: {filePath}", filePath);

        (string symbol, Timeframe timeframe) = ParseFileName(filePath);

        using Stream stream = File.OpenRead(filePath);
        using ParquetReader reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();

        DataField[] fields = reader.Schema.DataFields;
        DataField dtField = FindField(fields, "DateTime");
        DataField oField  = FindField(fields, "Open");
        DataField hField  = FindField(fields, "High");
        DataField lField  = FindField(fields, "Low");
        DataField cField  = FindField(fields, "Close");
        DataField? vField = FindFieldOrNull(fields, "Volume");
        DataField? oiField = FindFieldOrNull(fields, "OI");

        int totalRows = 0;
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using ParquetRowGroupReader rg = reader.OpenRowGroupReader(i);
            totalRows += (int)rg.RowCount;
        }

        var dtArr = new SysDateTime[totalRows];
        var oArr = new decimal[totalRows];
        var hArr = new decimal[totalRows];
        var lArr = new decimal[totalRows];
        var cArr = new decimal[totalRows];
        var vArr = new decimal[totalRows];
        var oiArr = new decimal[totalRows];

        int offset = 0;
        for (int i = 0; i < reader.RowGroupCount; i++)
        {
            using ParquetRowGroupReader rg = reader.OpenRowGroupReader(i);
            int rows = (int)rg.RowCount;
            CopyDateTime(rg.ReadColumnAsync(dtField).GetAwaiter().GetResult(), dtArr, offset, rows);
            CopyDecimal(rg.ReadColumnAsync(oField).GetAwaiter().GetResult(), oArr, offset, rows);
            CopyDecimal(rg.ReadColumnAsync(hField).GetAwaiter().GetResult(), hArr, offset, rows);
            CopyDecimal(rg.ReadColumnAsync(lField).GetAwaiter().GetResult(), lArr, offset, rows);
            CopyDecimal(rg.ReadColumnAsync(cField).GetAwaiter().GetResult(), cArr, offset, rows);
            if (vField is not null) CopyDecimal(rg.ReadColumnAsync(vField).GetAwaiter().GetResult(), vArr, offset, rows);
            if (oiField is not null) CopyDecimal(rg.ReadColumnAsync(oiField).GetAwaiter().GetResult(), oiArr, offset, rows);
            offset += rows;
        }

        return new ParquetBarSeries(instrumentToken, symbol, timeframe, dtArr, oArr, hArr, lArr, cArr, vArr, oiArr);
    }

    private static (string Symbol, Timeframe Timeframe) ParseFileName(string filePath)
    {
        string stem = Path.GetFileNameWithoutExtension(filePath);
        int lastUs = stem.LastIndexOf('_');
        if (lastUs <= 0 || lastUs == stem.Length - 1)
            throw new ArgumentException($"Parquet filename must be '{{symbol}}_{{timeframe}}.parquet'. Got: {stem}");
        string sym = stem.Substring(0, lastUs);
        string tfRaw = stem.Substring(lastUs + 1);
        return (sym, Timeframe.Parse(tfRaw));
    }

    private static DataField FindField(DataField[] fields, string name)
        => FindFieldOrNull(fields, name) ?? throw new InvalidOperationException($"Parquet schema missing column '{name}'.");

    private static DataField? FindFieldOrNull(DataField[] fields, string name)
    {
        for (int i = 0; i < fields.Length; i++)
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal)) return fields[i];
        return null;
    }

    private static void CopyDecimal(DataColumn col, decimal[] dest, int offset, int rows)
    {
        Array data = col.Data;
        switch (data)
        {
            case decimal[] d: Array.Copy(d, 0, dest, offset, rows); return;
            case double[] db: for (int i = 0; i < rows; i++) dest[offset + i] = (decimal)db[i]; return;
            case float[] f:   for (int i = 0; i < rows; i++) dest[offset + i] = (decimal)f[i];  return;
            case long[] l:    for (int i = 0; i < rows; i++) dest[offset + i] = l[i];            return;
            case int[] ii:    for (int i = 0; i < rows; i++) dest[offset + i] = ii[i];           return;
            case short[] sh:  for (int i = 0; i < rows; i++) dest[offset + i] = sh[i];           return;
            default: throw new InvalidOperationException($"Unsupported Parquet column type: {data.GetType().FullName}");
        }
    }

    private static void CopyDateTime(DataColumn col, SysDateTime[] dest, int offset, int rows)
    {
        Array data = col.Data;
        switch (data)
        {
            case SysDateTime[] dt: Array.Copy(dt, 0, dest, offset, rows); return;
            case SysDateTimeOffset[] dto: for (int i = 0; i < rows; i++) dest[offset + i] = dto[i].UtcDateTime; return;
            case long[] ticks: for (int i = 0; i < rows; i++) dest[offset + i] = new SysDateTime(ticks[i], SysDateTimeKind.Utc); return;
            default: throw new InvalidOperationException($"Unsupported Parquet DateTime column type: {data.GetType().FullName}");
        }
    }
}