using System.Buffers.Text;
using System.Runtime.CompilerServices;
using AlgoTrading.Models;
using SysDateTime = System.DateTime;
using SysDateTimeKind = System.DateTimeKind;
namespace AlgoTrading.Data;

public sealed class CsvBarSeries : IBarSeries
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

    private CsvBarSeries(
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

    public static CsvBarSeries Load(string filePath)
        => Load(filePath, 0u, null, null);

    public static CsvBarSeries Load(string filePath, uint instrumentToken, SysDateTime? fromUtc, SysDateTime? toUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"CSV bar file not found: {filePath}", filePath);

        (string symbol, Timeframe timeframe) = ParseFileName(filePath);

        byte[] bytes = File.ReadAllBytes(filePath);
        ReadOnlySpan<byte> span = bytes;
        if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
            span = span.Slice(3);

        int[] lineStarts = ComputeLineStarts(span, out int totalLines);
        if (totalLines <= 1)
            throw new InvalidOperationException($"CSV file contains no data rows: {filePath}");

        int dataLines = totalLines - 1;
        var dtArr = new SysDateTime[dataLines];
        var oArr = new decimal[dataLines];
        var hArr = new decimal[dataLines];
        var lArr = new decimal[dataLines];
        var cArr = new decimal[dataLines];
        var vArr = new decimal[dataLines];
        var oiArr = new decimal[dataLines];

        int procs = Environment.ProcessorCount;
        int chunks = dataLines < 8192 ? 1 : Math.Min(procs, (dataLines + 8191) / 8192);
        if (chunks < 1) chunks = 1;
        int perChunk = (dataLines + chunks - 1) / chunks;

        if (chunks == 1)
        {
            ParseRange(bytes, lineStarts, bytes.Length, 0, dataLines, dtArr, oArr, hArr, lArr, cArr, vArr, oiArr, filePath);
        }
        else
        {
            Parallel.For(0, chunks, p =>
            {
                int s = p * perChunk;
                int e = Math.Min(s + perChunk, dataLines);
                if (s >= e) return;
                ParseRange(bytes, lineStarts, bytes.Length, s, e, dtArr, oArr, hArr, lArr, cArr, vArr, oiArr, filePath);
            });
        }

        if (fromUtc.HasValue || toUtc.HasValue)
        {
            int lo = LowerBound(dtArr, fromUtc ?? SysDateTime.MinValue);
            int hi = UpperBound(dtArr, toUtc ?? SysDateTime.MaxValue);
            if (lo > 0 || hi < dataLines)
            {
                int n = hi - lo;
                dtArr = SliceDt(dtArr, lo, n);
                oArr = SliceDec(oArr, lo, n);
                hArr = SliceDec(hArr, lo, n);
                lArr = SliceDec(lArr, lo, n);
                cArr = SliceDec(cArr, lo, n);
                vArr = SliceDec(vArr, lo, n);
                oiArr = SliceDec(oiArr, lo, n);
            }
        }

        return new CsvBarSeries(instrumentToken, symbol, timeframe, dtArr, oArr, hArr, lArr, cArr, vArr, oiArr);
    }

    private static (string Symbol, Timeframe Timeframe) ParseFileName(string filePath)
    {
        string stem = Path.GetFileNameWithoutExtension(filePath);
        int lastUs = stem.LastIndexOf('_');
        if (lastUs <= 0 || lastUs == stem.Length - 1)
            throw new ArgumentException($"CSV filename must be '{{symbol}}_{{timeframe}}.csv'. Got: {stem}");
        string sym = stem.Substring(0, lastUs);
        string tfRaw = stem.Substring(lastUs + 1);
        return (sym, Timeframe.Parse(tfRaw));
    }

    private static void ParseRange(
        byte[] bytes, int[] lineStarts, int spanLength,
        int from, int to,
        SysDateTime[] dtArr, decimal[] oArr, decimal[] hArr, decimal[] lArr,
        decimal[] cArr, decimal[] vArr, decimal[] oiArr, string filePath)
    {
        ReadOnlySpan<byte> span = bytes.AsSpan(0, spanLength);
        for (int i = from; i < to; i++)
        {
            int lineIdx = i + 1;
            int start = lineStarts[lineIdx];
            int end = (lineIdx + 1 < lineStarts.Length) ? lineStarts[lineIdx + 1] : spanLength;
            if (end > start && span[end - 1] == (byte)'\n') end--;
            if (end > start && span[end - 1] == (byte)'\r') end--;
            ReadOnlySpan<byte> line = span.Slice(start, end - start);
            if (!TryParseLine(line, out var dt, out var o, out var h, out var l, out var c, out var v, out var oi))
                throw new FormatException($"Malformed CSV row {lineIdx + 1} in {filePath}");
            dtArr[i] = dt; oArr[i] = o; hArr[i] = h; lArr[i] = l;
            cArr[i] = c; vArr[i] = v; oiArr[i] = oi;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseLine(
        ReadOnlySpan<byte> line,
        out SysDateTime dt,
        out decimal o, out decimal h, out decimal l, out decimal c, out decimal v, out decimal oi)
    {
        dt = default;
        o = h = l = c = v = oi = 0m;
        if (line.IsEmpty) return false;

        Span<int> starts = stackalloc int[8];
        starts[0] = 0;
        int fields = 1;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == (byte)',')
            {
                if (fields >= 7) return false;
                starts[fields++] = i + 1;
            }
        }
        starts[fields] = line.Length;
        if (fields < 5) return false;

        if (!TryParseIsoUtc(line.Slice(starts[0], starts[1] - starts[0]), out dt)) return false;
        if (!Utf8Parser.TryParse(line.Slice(starts[1], starts[2] - starts[1]), out o, out _)) return false;
        if (!Utf8Parser.TryParse(line.Slice(starts[2], starts[3] - starts[2]), out h, out _)) return false;
        if (!Utf8Parser.TryParse(line.Slice(starts[3], starts[4] - starts[3]), out l, out _)) return false;
        if (!Utf8Parser.TryParse(line.Slice(starts[4], starts[5] - starts[4]), out c, out _)) return false;
        if (fields >= 6 && !Utf8Parser.TryParse(line.Slice(starts[5], starts[6] - starts[5]), out v, out _)) return false;
        if (fields >= 7 && !Utf8Parser.TryParse(line.Slice(starts[6], starts[7] - starts[6]), out oi, out _)) return false;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryParseIsoUtc(ReadOnlySpan<byte> s, out SysDateTime dt)
    {
        dt = default;
        if (s.Length < 20) return false;
        if (s[4] != (byte)'-' || s[7] != (byte)'-' || s[10] != (byte)'T'
            || s[13] != (byte)':' || s[16] != (byte)':' || s[19] != (byte)'Z') return false;

        int y  = (s[0] - 48) * 1000 + (s[1] - 48) * 100 + (s[2] - 48) * 10 + (s[3] - 48);
        int mo = (s[5] - 48) * 10 + (s[6] - 48);
        int d  = (s[8] - 48) * 10 + (s[9] - 48);
        int h  = (s[11] - 48) * 10 + (s[12] - 48);
        int mi = (s[14] - 48) * 10 + (s[15] - 48);
        int ss = (s[17] - 48) * 10 + (s[18] - 48);

        if ((uint)y > 9999u || (uint)mo < 1u || mo > 12 || (uint)d < 1u || d > 31) return false;
        if ((uint)h > 23u || (uint)mi > 59u || (uint)ss > 60u) return false;

        dt = new SysDateTime(y, mo, d, h, mi, ss, SysDateTimeKind.Utc);
        return true;
    }

    private static int[] ComputeLineStarts(ReadOnlySpan<byte> span, out int totalLines)
    {
        int count = 1;
        int pos = 0;
        while (pos < span.Length)
        {
            int nl = span.Slice(pos).IndexOf((byte)'\n');
            if (nl < 0) break;
            int next = pos + nl + 1;
            if (next < span.Length) count++;
            pos = next;
        }

        var starts = new int[count];
        starts[0] = 0;
        int idx = 1;
        pos = 0;
        while (pos < span.Length && idx < count)
        {
            int nl = span.Slice(pos).IndexOf((byte)'\n');
            if (nl < 0) break;
            int next = pos + nl + 1;
            if (next < span.Length) starts[idx++] = next;
            pos = next;
        }

        totalLines = count;
        return starts;
    }

    private static int LowerBound(SysDateTime[] arr, SysDateTime key)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid] < key) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private static int UpperBound(SysDateTime[] arr, SysDateTime key)
    {
        int lo = 0, hi = arr.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (arr[mid] <= key) lo = mid + 1; else hi = mid;
        }
        return lo;
    }

    private static SysDateTime[] SliceDt(SysDateTime[] src, int start, int len)
    {
        var dst = new SysDateTime[len];
        Array.Copy(src, start, dst, 0, len);
        return dst;
    }

    private static decimal[] SliceDec(decimal[] src, int start, int len)
    {
        var dst = new decimal[len];
        Array.Copy(src, start, dst, 0, len);
        return dst;
    }
}