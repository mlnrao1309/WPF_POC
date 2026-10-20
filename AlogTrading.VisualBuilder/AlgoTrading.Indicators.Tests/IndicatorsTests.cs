using System;
using System.Collections.Generic;
using AlgoTrading.Indicators;
using AlgoTrading.Models;
using Xunit;

namespace AlgoTrading.Indicators.Tests
{
    internal static class H
    {
        public static readonly decimal NaN = IndicatorValues.NaN;

        public static decimal[] Lin(int n, decimal start, decimal step)
        {
            var a = new decimal[n];
            decimal v = start;
            for (int i = 0; i < n; i++) { a[i] = v; v += step; }
            return a;
        }

        public static decimal[] Flat(int n, decimal v)
        {
            var a = new decimal[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }

        public static decimal[] Rand(int n, int seed = 42)
        {
            var rnd = new Random(seed);
            var a = new decimal[n];
            for (int i = 0; i < n; i++) a[i] = Math.Round((decimal)(100.0 + rnd.NextDouble() * 10.0), 4);
            return a;
        }

        public static void IsNaN(decimal v)
            => Assert.Equal(decimal.MinValue, v);

        public static void NotNaN(decimal v)
            => Assert.NotEqual(decimal.MinValue, v);

        public static void Close(decimal expected, decimal actual, decimal tol)
            => Assert.True(Math.Abs(expected - actual) <= tol,
                $"expected {expected}, actual {actual}, tol {tol}");

        public static void NaNPrefix(ReadOnlySpan<decimal> span, int warmup)
        {
            for (int i = 0; i < warmup && i < span.Length; i++) IsNaN(span[i]);
        }

        public static readonly DateTime IstStart = new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc);
        public static DateTime[] BarTimes(int n) => BarTimes(n, IstStart);
        public static DateTime[] BarTimes(int n, DateTime start)
        {
            var a = new DateTime[n];
            for (int i = 0; i < n; i++) a[i] = start.AddMinutes(i);
            return a;
        }
    }

    public class SmaTests
    {
        [Fact]
        public void Basic()
        {
            var src = new[] { 1m, 2m, 3m, 4m, 5m };
            var dst = new decimal[5];
            Sma.Calculate(src, 3, dst);
            H.IsNaN(dst[0]); H.IsNaN(dst[1]);
            Assert.Equal(2m, dst[2]);
            Assert.Equal(3m, dst[3]);
            Assert.Equal(4m, dst[4]);
        }

        [Fact]
        public void PeriodOneEqualsSource()
        {
            var src = H.Rand(64);
            var dst = new decimal[64];
            Sma.Calculate(src, 1, dst);
            for (int i = 0; i < 64; i++) Assert.Equal(src[i], dst[i]);
        }

        [Fact]
        public void WarmupMatchesFormula()
        {
            var src = H.Rand(50);
            var dst = new decimal[50];
            Sma.Calculate(src, 20, dst);
            H.NaNPrefix(dst, 19);
            H.NotNaN(dst[19]);
        }
    }

    public class EmaTests
    {
        [Fact]
        public void Basic()
        {
            var src = new[] { 1m, 2m, 3m, 4m, 5m };
            var dst = new decimal[5];
            Ema.Calculate(src, 3, dst);
            H.IsNaN(dst[0]); H.IsNaN(dst[1]);
            Assert.Equal(2m, dst[2]);
            Assert.Equal(3m, dst[3]);
            Assert.Equal(4m, dst[4]);
        }

        [Fact]
        public void WarmupMatchesFormula()
        {
            var src = H.Rand(50);
            var dst = new decimal[50];
            Ema.Calculate(src, 20, dst);
            H.NaNPrefix(dst, 19);
            H.NotNaN(dst[19]);
        }
    }

    public class DemaTests
    {
        [Fact]
        public void WarmupIsTwoPeriodsMinusTwo()
        {
            var src = H.Rand(80);
            var dst = new decimal[80];
            Dema.Calculate(src, 10, dst);
            H.NaNPrefix(dst, 18);
            H.NotNaN(dst[18]);
        }

        [Fact]
        public void EqualToFormula()
        {
            var src = H.Rand(60);
            var dst = new decimal[60];
            Dema.Calculate(src, 5, dst);

            var e1 = new decimal[60];
            var e2 = new decimal[60];
            Ema.Calculate(src, 5, e1);
            var e1Tail = new decimal[60 - 4];
            Array.Copy(e1, 4, e1Tail, 0, 60 - 4);
            var e2Tail = new decimal[60 - 4];
            Ema.Calculate(e1Tail, 5, e2Tail);

            for (int i = 8; i < 60; i++)
                H.Close(2m * e1[i] - e2Tail[i - 4], dst[i], 1e-9m);
        }
    }

    public class TemaTests
    {
        [Fact]
        public void Warmup()
        {
            var src = H.Rand(120);
            var dst = new decimal[120];
            Tema.Calculate(src, 10, dst);
            H.NaNPrefix(dst, 27);
            H.NotNaN(dst[27]);
        }
    }

    public class WmaTests
    {
        [Fact]
        public void Basic()
        {
            var src = new[] { 1m, 2m, 3m };
            var dst = new decimal[3];
            Wma.Calculate(src, 3, dst);
            H.IsNaN(dst[0]); H.IsNaN(dst[1]);
            H.Close(14m / 6m, dst[2], 1e-12m);
        }

        [Fact]
        public void Warmup()
        {
            var src = H.Rand(50);
            var dst = new decimal[50];
            Wma.Calculate(src, 20, dst);
            H.NaNPrefix(dst, 19);
            H.NotNaN(dst[19]);
        }
    }

    public class HmaTests
    {
        [Fact]
        public void WarmupFormula()
        {
            var src = H.Rand(80);
            var dst = new decimal[80];
            Hma.Calculate(src, 20, dst);
            H.NaNPrefix(dst, 22);
            H.NotNaN(dst[22]);
        }
    }

    public class RsiTests
    {
        [Fact]
        public void MonotoneIncreaseEquals100()
        {
            var src = H.Lin(30, 10m, 1m);
            var dst = new decimal[30];
            Rsi.Calculate(src, 14, dst);
            H.NaNPrefix(dst, 14);
            for (int i = 14; i < 30; i++) Assert.Equal(100m, dst[i]);
        }

        [Fact]
        public void RangeIn0To100()
        {
            var src = H.Rand(200, 7);
            var dst = new decimal[200];
            Rsi.Calculate(src, 14, dst);
            for (int i = 14; i < 200; i++)
            {
                H.NotNaN(dst[i]);
                Assert.InRange(dst[i], 0m, 100m);
            }
        }

        [Fact]
        public void Warmup()
        {
            var src = H.Rand(30);
            var dst = new decimal[30];
            Rsi.Calculate(src, 14, dst);
            H.NaNPrefix(dst, 14);
        }
    }

    public class MacdTests
    {
        [Fact]
        public void HistogramEqualsMacdMinusSignal()
        {
            var src = H.Rand(120);
            var macd = new decimal[120];
            var sig = new decimal[120];
            var hist = new decimal[120];
            Macd.Calculate(src, 12, 26, 9, macd, sig, hist);
            for (int i = 0; i < 120; i++)
            {
                if (IndicatorValues.IsNaN(macd[i]))
                {
                    H.IsNaN(sig[i]); H.IsNaN(hist[i]);
                }
                else if (!IndicatorValues.IsNaN(sig[i]))
                {
                    H.Close(macd[i] - sig[i], hist[i], 1e-9m);
                }
            }
        }

        [Fact]
        public void WarmupMatchesSpec()
        {
            var src = H.Rand(120);
            var macd = new decimal[120];
            var sig = new decimal[120];
            var hist = new decimal[120];
            Macd.Calculate(src, 12, 26, 9, macd, sig, hist);
            H.NaNPrefix(macd, 35);
            H.NaNPrefix(sig, 35);
            H.NaNPrefix(hist, 35);
            H.NotNaN(macd[35]);
        }
    }

    public class BollingerBandsTests
    {
        [Fact]
        public void Basic()
        {
            var src = new[] { 1m, 2m, 3m, 4m, 5m };
            var up = new decimal[5];
            var mid = new decimal[5];
            var lo = new decimal[5];
            BollingerBands.Calculate(src, 5, 2m, up, mid, lo);
            for (int i = 0; i < 4; i++) { H.IsNaN(up[i]); H.IsNaN(mid[i]); H.IsNaN(lo[i]); }
            Assert.Equal(3m, mid[4]);
            decimal sd = (decimal)Math.Sqrt(2.0);
            H.Close(3m + 2m * sd, up[4], 1e-9m);
            H.Close(3m - 2m * sd, lo[4], 1e-9m);
        }

        [Fact]
        public void UpperGreaterThanMiddleGreaterThanLower()
        {
            var src = H.Rand(80, 11);
            var up = new decimal[80];
            var mid = new decimal[80];
            var lo = new decimal[80];
            BollingerBands.Calculate(src, 20, 2m, up, mid, lo);
            for (int i = 19; i < 80; i++)
            {
                Assert.True(up[i] >= mid[i]);
                Assert.True(mid[i] >= lo[i]);
            }
        }
    }

    public class AtrTests
    {
        [Fact]
        public void WarmupAndValue()
        {
            var hi = new[] { 10m, 11m, 12m, 13m, 14m, 15m, 16m };
            var lo = new[] { 9m, 10m, 11m, 12m, 13m, 14m, 15m };
            var cl = new[] { 9.5m, 10.5m, 11.5m, 12.5m, 13.5m, 14.5m, 15.5m };
            var dst = new decimal[7];
            Atr.Calculate(hi, lo, cl, 3, dst);
            H.NaNPrefix(dst, 3);
            H.NotNaN(dst[3]);
            H.Close(8m / 9m + 1.5m / 3m, dst[3], 1e-9m);
        }
    }

    public class AdxTests
    {
        [Fact]
        public void WarmupAndOutputs()
        {
            var hi = H.Rand(120);
            var lo = new decimal[120];
            var cl = new decimal[120];
            for (int i = 0; i < 120; i++) { lo[i] = hi[i] - 1m; cl[i] = hi[i] - 0.5m; }
            var adx = new decimal[120];
            var pdi = new decimal[120];
            var mdi = new decimal[120];
            Adx.Calculate(hi, lo, cl, 14, adx, pdi, mdi);
            H.NaNPrefix(adx, 28);
            H.NotNaN(adx[28]);
            for (int i = 28; i < 120; i++)
            {
                H.NotNaN(pdi[i]);
                H.NotNaN(mdi[i]);
                Assert.InRange(adx[i], 0m, 100m);
                Assert.InRange(pdi[i], 0m, 100m);
                Assert.InRange(mdi[i], 0m, 100m);
            }
        }
    }

    public class SupertrendTests
    {
        [Fact]
        public void ProducesDirectionAfterWarmup()
        {
            var n = 200;
            var hi = new decimal[n];
            var lo = new decimal[n];
            var cl = new decimal[n];
            for (int i = 0; i < n; i++)
            {
                decimal baseP = i < 100 ? 100m + i * 0.5m : 150m - (i - 100) * 0.5m;
                hi[i] = baseP + 0.5m;
                lo[i] = baseP - 0.5m;
                cl[i] = baseP;
            }
            var line = new decimal[n];
            var dir = new decimal[n];
            Supertrend.Calculate(hi, lo, cl, 10, 3m, line, dir);

            H.NaNPrefix(line, 10);
            H.NaNPrefix(dir, 10);

            bool sawUp = false, sawDown = false;
            for (int i = 10; i < n; i++)
            {
                H.NotNaN(line[i]);
                H.NotNaN(dir[i]);
                if (dir[i] == 1m) sawUp = true;
                if (dir[i] == -1m) sawDown = true;
            }
            Assert.True(sawUp, "expected at least one +1 direction");
            Assert.True(sawDown, "expected at least one -1 direction (flip on downtrend)");
        }
    }

    public class VwapTests
    {
        [Fact]
        public void FirstBarIsNaN()
        {
            var n = 5;
            var hi = new[] { 10m, 11m, 12m, 13m, 14m };
            var lo = new[] { 9m, 10m, 11m, 12m, 13m };
            var cl = new[] { 9.5m, 10.5m, 11.5m, 12.5m, 13.5m };
            var vol = new[] { 100m, 200m, 300m, 400m, 500m };
            var dt = H.BarTimes(n);
            var dst = new decimal[n];
            Vwap.Calculate(hi, lo, cl, vol, dt, dst);
            H.IsNaN(dst[0]);
            H.NotNaN(dst[1]);
        }

        [Fact]
        public void ResetsAtSessionBoundary()
        {
            var n = 3;
            var hi = new[] { 10m, 11m, 20m };
            var lo = new[] { 9m, 10m, 19m };
            var cl = new[] { 9.5m, 10.5m, 19.5m };
            var vol = new[] { 100m, 100m, 100m };
            var dt = new[]
            {
                new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 1, 3, 46, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 2, 3, 45, 0, DateTimeKind.Utc)
            };
            var dst = new decimal[3];
            Vwap.Calculate(hi, lo, cl, vol, dt, dst);
            H.IsNaN(dst[0]);
            H.NotNaN(dst[1]);
            H.IsNaN(dst[2]);
        }
    }

    public class ObvTests
    {
        [Fact]
        public void Basic()
        {
            var cl = new[] { 10m, 11m, 10m, 12m, 11m };
            var vol = new[] { 100m, 200m, 300m, 400m, 500m };
            var dst = new decimal[5];
            Obv.Calculate(cl, vol, dst);
            H.IsNaN(dst[0]);
            Assert.Equal(200m, dst[1]);
            Assert.Equal(-100m, dst[2]);
            Assert.Equal(300m, dst[3]);
            Assert.Equal(-200m, dst[4]);
        }
    }

    public class AdlTests
    {
        [Fact]
        public void Basic()
        {
            var hi = new[] { 10m, 12m, 11m };
            var lo = new[] { 9m, 10m, 10m };
            var cl = new[] { 11.5m, 11.8m, 10.9m };
            var vol = new[] { 100m, 200m, 300m };
            var dst = new decimal[3];
            Adl.Calculate(hi, lo, cl, vol, dst);
            H.IsNaN(dst[0]);
            H.Close(160m, dst[1], 1e-9m);
            H.Close(400m, dst[2], 1e-9m);
        }
    }

    public class StochasticTests
    {
        [Fact]
        public void Basic()
        {
            var n = 7;
            var hi = new[] { 10m, 11m, 12m, 13m, 14m, 15m, 16m };
            var lo = new[] { 9m, 10m, 11m, 12m, 13m, 14m, 15m };
            var cl = new[] { 9.5m, 10.5m, 11.5m, 12.5m, 13.5m, 14.5m, 15.5m };
            var k = new decimal[n];
            var d = new decimal[n];
            Stochastic.Calculate(hi, lo, cl, 3, 2, k, d);
            H.NaNPrefix(k, 5);
            H.NaNPrefix(d, 5);
            H.NotNaN(k[5]);
            H.NotNaN(d[5]);
            for (int i = 5; i < n; i++)
            {
                Assert.InRange(k[i], 0m, 100m);
                Assert.InRange(d[i], 0m, 100m);
            }
        }
    }

    public class CciTests
    {
        [Fact]
        public void BasicLinearInput()
        {
            var n = 8;
            var src = H.Lin(n, 1m, 1m);
            var dst = new decimal[n];
            Cci.Calculate(src, src, src, 5, dst);
            H.NaNPrefix(dst, 5);
            H.NotNaN(dst[5]);
            H.Close(200m / 3m, dst[5], 1e-9m);
        }
    }

    public class PivotPointsClassicTests
    {
        [Fact]
        public void UsesPreviousDay()
        {
            var n = 4;
            var hi = new[] { 10m, 11m, 20m, 21m };
            var lo = new[] { 9m, 10m, 19m, 20m };
            var cl = new[] { 9.5m, 10.5m, 19.5m, 20.5m };
            var dt = new[]
            {
                new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 1, 3, 46, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 2, 3, 45, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 2, 3, 46, 0, DateTimeKind.Utc)
            };
            var pp = new decimal[n];
            var r1 = new decimal[n]; var r2 = new decimal[n]; var r3 = new decimal[n];
            var s1 = new decimal[n]; var s2 = new decimal[n]; var s3 = new decimal[n];
            PivotPointsClassic.Calculate(hi, lo, cl, dt, pp, r1, r2, r3, s1, s2, s3);

            H.IsNaN(pp[0]); H.IsNaN(pp[1]);
            H.Close(30.5m / 3m, pp[2], 1e-9m);
            H.Close(2m * (30.5m / 3m) - 9m, r1[2], 1e-9m);
            H.Close(2m * (30.5m / 3m) - 11m, s1[2], 1e-9m);
            Assert.Equal(pp[2], pp[3]);
        }
    }

    public class PivotPointsCamarillaTests
    {
        [Fact]
        public void UsesPreviousDay()
        {
            var n = 3;
            var hi = new[] { 10m, 11m, 20m };
            var lo = new[] { 9m, 10m, 19m };
            var cl = new[] { 9.5m, 10.5m, 19.5m };
            var dt = new[]
            {
                new DateTime(2024, 1, 1, 3, 45, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 1, 3, 46, 0, DateTimeKind.Utc),
                new DateTime(2024, 1, 2, 3, 45, 0, DateTimeKind.Utc)
            };
            var r4 = new decimal[n]; var r3 = new decimal[n]; var r2 = new decimal[n]; var r1 = new decimal[n];
            var s1 = new decimal[n]; var s2 = new decimal[n]; var s3 = new decimal[n]; var s4 = new decimal[n];
            PivotPointsCamarilla.Calculate(hi, lo, cl, dt, r4, r3, r2, r1, s1, s2, s3, s4);
            H.IsNaN(r4[0]); H.IsNaN(r4[1]);
            H.Close(10.5m + 2.2m / 2m, r4[2], 1e-9m);
            H.Close(10.5m - 2.2m / 2m, s4[2], 1e-9m);
            H.Close(10.5m + 2.2m / 12m, r1[2], 1e-9m);
        }
    }

    public class VolumeSmaTests
    {
        [Fact]
        public void Basic()
        {
            var vol = new[] { 100m, 200m, 300m, 400m, 500m };
            var dst = new decimal[5];
            VolumeSma.Calculate(vol, 3, dst);
            H.IsNaN(dst[0]); H.IsNaN(dst[1]);
            Assert.Equal(200m, dst[2]);
            Assert.Equal(300m, dst[3]);
            Assert.Equal(400m, dst[4]);
        }
    }

    public class RocTests
    {
        [Fact]
        public void Basic()
        {
            var src = new[] { 10m, 12m, 15m, 14m };
            var dst = new decimal[4];
            Roc.Calculate(src, 2, dst);
            H.IsNaN(dst[0]); H.IsNaN(dst[1]);
            Assert.Equal(50m, dst[2]);
            H.Close(100m * (14m - 12m) / 12m, dst[3], 1e-9m);
        }
    }

    public class MomentumTests
    {
        [Fact]
        public void Basic()
        {
            var src = new[] { 10m, 12m, 15m, 14m };
            var dst = new decimal[4];
            Momentum.Calculate(src, 2, dst);
            H.IsNaN(dst[0]); H.IsNaN(dst[1]);
            Assert.Equal(5m, dst[2]);
            Assert.Equal(2m, dst[3]);
        }
    }

    public class TrixTests
    {
        [Fact]
        public void WarmupMatchesSpec()
        {
            var src = H.Lin(80, 100m, 0.3m);
            var dst = new decimal[80];
            Trix.Calculate(src, 5, 4, dst);
            H.NaNPrefix(dst, 19);
            H.NotNaN(dst[19]);
        }
    }

    public class KeltnerChannelTests
    {
        [Fact]
        public void WarmupAndOrdering()
        {
            var n = 100;
            var hi = new decimal[n];
            var lo = new decimal[n];
            var cl = new decimal[n];
            for (int i = 0; i < n; i++)
            {
                cl[i] = 100m + i * 0.1m;
                hi[i] = cl[i] + 1m;
                lo[i] = cl[i] - 1m;
            }
            var up = new decimal[n];
            var mid = new decimal[n];
            var low = new decimal[n];
            KeltnerChannel.Calculate(hi, lo, cl, 20, 10, 2m, up, mid, low);

            H.NaNPrefix(up, 30);
            H.NaNPrefix(mid, 30);
            H.NaNPrefix(low, 30);

            for (int i = 30; i < n; i++)
            {
                Assert.True(up[i] > mid[i]);
                Assert.True(mid[i] > low[i]);
            }
        }
    }

    public class DonchianChannelTests
    {
        [Fact]
        public void Basic()
        {
            var hi = new[] { 1m, 2m, 3m, 4m, 5m };
            var lo = new[] { 0m, 0m, 0m, 0m, 0m };
            var up = new decimal[5];
            var mid = new decimal[5];
            var low = new decimal[5];
            DonchianChannel.Calculate(hi, lo, 3, up, mid, low);
            H.IsNaN(up[0]); H.IsNaN(up[1]);
            Assert.Equal(3m, up[2]);
            Assert.Equal(4m, up[3]);
            Assert.Equal(5m, up[4]);
            Assert.Equal(1.5m, mid[2]);
            Assert.Equal(2m, mid[3]);
            Assert.Equal(2.5m, mid[4]);
        }
    }

    public class MfiTests
    {
        [Fact]
        public void AllUpTrendIsHundred()
        {
            var n = 6;
            var cl = new[] { 10m, 11m, 12m, 13m, 14m, 15m };
            var vol = new[] { 100m, 100m, 100m, 100m, 100m, 100m };
            var dst = new decimal[n];
            Mfi.Calculate(cl, cl, cl, vol, 3, dst);
            H.NaNPrefix(dst, 3);
            for (int i = 3; i < n; i++) Assert.Equal(100m, dst[i]);
        }
    }

    public class HighLow52WTests
    {
        [Fact]
        public void Basic()
        {
            var hi = new[] { 1m, 2m, 3m, 4m, 5m };
            var lo = new[] { -1m, -2m, -3m, -4m, -5m };
            var h = new decimal[5];
            var l = new decimal[5];
            HighLow52W.Calculate(hi, lo, 3, h, l);
            H.IsNaN(h[0]); H.IsNaN(h[1]);
            Assert.Equal(3m, h[2]);
            Assert.Equal(4m, h[3]);
            Assert.Equal(5m, h[4]);
            Assert.Equal(-1m, l[2]);
            Assert.Equal(-2m, l[3]);
            Assert.Equal(-3m, l[4]);
        }
    }

    public class RegistryTests
    {
        [Fact]
        public void HasExactly27Entries()
        {
            Assert.Equal(27, IndicatorRegistry.All.Count);
        }

        [Fact]
        public void AllKindsPresent()
        {
            foreach (IndicatorKind k in Enum.GetValues(typeof(IndicatorKind)))
                Assert.True(IndicatorRegistry.TryGet(k, out _), $"missing {k}");
        }

        [Fact]
        public void DescriptorsHaveOutputs()
        {
            foreach (var d in IndicatorRegistry.All)
            {
                Assert.NotNull(d.Outputs);
                Assert.True(d.Outputs.Count >= 1);
                Assert.True(d.WarmupBarsAtDefault >= 0);
                Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            }
        }

        [Fact]
        public void NoDuplicateKind()
        {
            var seen = new HashSet<IndicatorKind>();
            foreach (var d in IndicatorRegistry.All)
                Assert.True(seen.Add(d.Kind), $"duplicate descriptor for {d.Kind}");
        }
    }

    public class DispatcherTests
    {
        private static void Run(IndicatorKind kind, decimal[] paramsArr, out int count)
        {
            int n = 300;
            var hi = H.Rand(n, 1);
            var lo = new decimal[n];
            var cl = new decimal[n];
            for (int i = 0; i < n; i++) { lo[i] = hi[i] - 1m; cl[i] = hi[i] - 0.5m; }
            var vol = H.Rand(n, 2);
            var dt = H.BarTimes(n);
            var o1 = new decimal[n]; var o2 = new decimal[n]; var o3 = new decimal[n]; var o4 = new decimal[n];
            var o5 = new decimal[n]; var o6 = new decimal[n]; var o7 = new decimal[n]; var o8 = new decimal[n];
            IndicatorDispatcher.Evaluate(kind, cl, hi, lo, cl, vol, dt, paramsArr,
                o1, o2, o3, o4, o5, o6, o7, o8, out count);
            Assert.Equal(n, o1.Length);
        }

        [Fact]
        public void EveryKindDispatches()
        {
            foreach (var d in IndicatorRegistry.All)
            {
                var ps = new decimal[d.Parameters.Count];
                for (int i = 0; i < ps.Length; i++) ps[i] = d.Parameters[i].Default;

                Run(d.Kind, ps, out int count);
                Assert.Equal(d.Outputs.Count, count);
            }
        }

        [Fact]
        public void NoOutputsOverlapInLength()
        {
            int n = 50;
            var src = H.Lin(n, 1m, 1m);
            var o1 = new decimal[n];
            var o2 = new decimal[n];
            var o3 = new decimal[n];
            var o4 = new decimal[n];
            var o5 = new decimal[n];
            var o6 = new decimal[n];
            var o7 = new decimal[n];
            var o8 = new decimal[n];
            var ps = new decimal[] { 20m };
            IndicatorDispatcher.Evaluate(IndicatorKind.Sma, src, src, src, src, src, H.BarTimes(n), ps,
                o1, o2, o3, o4, o5, o6, o7, o8, out int count);
            Assert.Equal(1, count);
        }
    }

    public class CacheTests
    {
        [Fact]
        public void ReturnsSameInstanceOnRepeat()
        {
            var cache = new IndicatorCache();
            var key = new IndicatorCacheKey(2885, "15m", IndicatorKind.Sma, "", "20", "Close");
            int calls = 0;
            var a = cache.GetOrAdd(key, _ => { calls++; return new decimal[10]; });
            var b = cache.GetOrAdd(key, _ => { calls++; return new decimal[10]; });
            Assert.Same(a, b);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void DistinctKeysDistinctBuffers()
        {
            var cache = new IndicatorCache();
            var k1 = new IndicatorCacheKey(1, "15m", IndicatorKind.Sma, "", "20", "Close");
            var k2 = new IndicatorCacheKey(1, "15m", IndicatorKind.Sma, "", "50", "Close");
            var a = cache.GetOrAdd(k1, _ => new decimal[10]);
            var b = cache.GetOrAdd(k2, _ => new decimal[10]);
            Assert.NotSame(a, b);
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void KeyEqualityAndHash()
        {
            var a = new IndicatorCacheKey(1, "15m", IndicatorKind.Ema, "Value", "20", "Close");
            var b = new IndicatorCacheKey(1, "15m", IndicatorKind.Ema, "Value", "20", "Close");
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }
    }

    public class WarmupContractTests
    {
        [Fact]
        public void EveryIndicatorRespectsDescriptorWarmup()
        {
            foreach (var d in IndicatorRegistry.All)
            {
                int n = d.WarmupBarsAtDefault + 40;
                if (n < 60) n = 60;

                var hi = H.Rand(n, 21);
                var lo = new decimal[n];
                var cl = new decimal[n];
                for (int i = 0; i < n; i++) { lo[i] = hi[i] - 1m; cl[i] = hi[i] - 0.5m; }
                var vol = H.Rand(n, 22);
                var dt = H.BarTimes(n);

                var ps = new decimal[d.Parameters.Count];
                for (int i = 0; i < ps.Length; i++) ps[i] = d.Parameters[i].Default;

                var buffers = new decimal[8][];
                for (int i = 0; i < 8; i++) buffers[i] = new decimal[n];

                IndicatorDispatcher.Evaluate(d.Kind, cl, hi, lo, cl, vol, dt, ps,
                    buffers[0], buffers[1], buffers[2], buffers[3],
                    buffers[4], buffers[5], buffers[6], buffers[7], out int oc);

                for (int o = 0; o < oc; o++)
                {
                    var buf = buffers[o];
                    for (int i = 0; i < d.WarmupBarsAtDefault && i < n; i++)
                        Assert.Equal(decimal.MinValue, buf[i]);
                }
            }
        }
    }

    public class NumericStabilityTests
    {
        [Fact]
        public void SmaOverFlatSeriesIsConstant()
        {
            var src = H.Flat(100, 42m);
            var dst = new decimal[100];
            Sma.Calculate(src, 20, dst);
            for (int i = 19; i < 100; i++) Assert.Equal(42m, dst[i]);
        }

        [Fact]
        public void BollingerOverFlatSeriesIsZeroWidth()
        {
            var src = H.Flat(50, 42m);
            var up = new decimal[50]; var mid = new decimal[50]; var lo = new decimal[50];
            BollingerBands.Calculate(src, 20, 2m, up, mid, lo);
            for (int i = 19; i < 50; i++)
            {
                Assert.Equal(42m, mid[i]);
                H.Close(42m, up[i], 1e-9m);
                H.Close(42m, lo[i], 1e-9m);
            }
        }

        [Fact]
        public void RsiIsBoundedOnRealisticData()
        {
            var src = H.Rand(500, 99);
            var dst = new decimal[500];
            Rsi.Calculate(src, 14, dst);
            for (int i = 14; i < 500; i++) Assert.InRange(dst[i], 0m, 100m);
        }

        [Fact]
        public void IndicatorsHandleSingleBar()
        {
            var one = new[] { 42m };
            var dst = new decimal[1];
            Sma.Calculate(one, 5, dst);
            H.IsNaN(dst[0]);
            Ema.Calculate(one, 5, dst);
            H.IsNaN(dst[0]);
            Rsi.Calculate(one, 14, dst);
            H.IsNaN(dst[0]);
        }

        [Fact]
        public void IndicatorsHandleEmptyInput()
        {
            var empty = Array.Empty<decimal>();
            var dst = Array.Empty<decimal>();
            Sma.Calculate(empty, 5, dst);
            Ema.Calculate(empty, 5, dst);
            Rsi.Calculate(empty, 14, dst);
        }
    }
}