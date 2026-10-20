using System;
using AlgoTrading.Indicators.Internal;

namespace AlgoTrading.Indicators;

public static class Trix
{
    public static void Calculate(
        ReadOnlySpan<decimal> source, int period, int signal, Span<decimal?> output)
    {
        if (period < 1 || signal < 1)
            throw new ArgumentOutOfRangeException(nameof(period));
        int n = source.Length;
        if (output.Length != n)
            throw new ArgumentException("Output length must equal source length.");

        output.Clear();
        int w = period - 1;
        int w3 = 3 * w;
        if (n <= w3 + 1) return;

        var e1 = Scratch.RentNullable(n);
        var e2 = Scratch.RentNullable(n);
        var e3 = Scratch.RentNullable(n);
        var rocArr = Scratch.RentNullable(n);
        try
        {
            var e1s = e1.AsSpan(0, n);
            var e2s = e2.AsSpan(0, n - w);
            var e3s = e3.AsSpan(0, n - 2 * w);

            Ema.Calculate(source, period, e1s);
            Ema.Calculate(e1s.Slice(w), period, e2s);
            Ema.Calculate(e2s.Slice(w), period, e3s);

            // e3s first valid index = w. ROC needs two consecutive TEMA values,
            // so it is valid from index w + 1.
            int e3Len = e3s.Length;
            var rocSpan = rocArr.AsSpan(0, e3Len);
            for (int i = w + 1; i < e3Len; i++)
            {
                decimal prev = e3s[i - 1]!.Value;
                decimal cur = e3s[i]!.Value;
                // Write 0 instead of null for a zero denominator to keep the
                // internal ROC series fully non-null. The user-visible Roc
                // indicator still emits null for this case; Trix's ROC is a
                // private intermediate and is never surfaced.
                rocSpan[i] = prev == 0m ? 0m : (cur - prev) / prev * 100m;
            }

            var rocTrimmed = rocSpan.Slice(w + 1);
            int rocLen = rocTrimmed.Length;
            var trixArr = Scratch.RentNullable(rocLen);
            try
            {
                var trixSpan = trixArr.AsSpan(0, rocLen);
                Ema.Calculate(rocTrimmed, signal, trixSpan);

                int baseIdx = w3 + 1;
                for (int k = 0; k < rocLen; k++)
                {
                    if (trixSpan[k].HasValue)
                        output[baseIdx + k] = trixSpan[k];
                }
            }
            finally { Scratch.Return(trixArr); }
        }
        finally
        {
            Scratch.Return(e1);
            Scratch.Return(e2);
            Scratch.Return(e3);
            Scratch.Return(rocArr);
        }
    }
}
