using AlgoTrading.Backtest.Runtime;

namespace AlgoTrading.Backtest.Metrics;

internal static class MetricsCalculator
{
    public static BacktestMetrics Compute(
        IReadOnlyList<Trade> trades,
        IReadOnlyList<EquityPoint> equity,
        decimal initialCapital,
        decimal riskFreeRatePct)
    {
        // ---------- returns ----------
        decimal gross = 0m, net = 0m, costs = 0m;
        foreach (var t in trades)
        {
            gross += t.GrossPnL;
            net += t.NetPnL;
            costs += t.TotalCosts;
        }

        decimal costPctGross = gross != 0m ? costs / Math.Abs(gross) * 100m : 0m;
        decimal netProfitPct = initialCapital != 0m ? net / initialCapital * 100m : 0m;

        decimal finalEquity = equity.Count > 0 ? equity[^1].Equity : initialCapital;
        decimal cagr = 0m;
        if (equity.Count >= 2 && initialCapital > 0m && finalEquity > 0m)
        {
            var years = (equity[^1].DateTime - equity[0].DateTime).TotalDays / 365.25;
            if (years > 0.01)
            {
                double ratio = (double)(finalEquity / initialCapital);
                cagr = (decimal)(Math.Pow(ratio, 1.0 / years) - 1.0) * 100m;
            }
        }

        // ---------- trade stats ----------
        int wins = 0, losses = 0, breakeven = 0;
        decimal sumWins = 0m, sumLosses = 0m;
        decimal largestWin = 0m, largestLoss = 0m;
        decimal sumBars = 0m, sumDays = 0m, sumMae = 0m, sumMfe = 0m;
        bool firstLargestSet = false;

        foreach (var t in trades)
        {
            if (t.NetPnL > 0m) { wins++; sumWins += t.NetPnL; if (!firstLargestSet || t.NetPnL > largestWin) largestWin = t.NetPnL; }
            else if (t.NetPnL < 0m) { losses++; sumLosses += t.NetPnL; if (!firstLargestSet || t.NetPnL < largestLoss) largestLoss = t.NetPnL; }
            else breakeven++;
            firstLargestSet = true;

            sumBars += t.BarsHeld;
            sumDays += t.DaysHeld;
            sumMae += t.MAE;
            sumMfe += t.MFE;
        }

        int total = trades.Count;
        decimal winRate = total > 0 ? (decimal)wins / total * 100m : 0m;
        decimal avgWin = wins > 0 ? sumWins / wins : 0m;
        decimal avgLoss = losses > 0 ? sumLosses / losses : 0m;
        decimal profitFactor = sumLosses != 0m ? sumWins / Math.Abs(sumLosses) : 0m;
        decimal expectancy = total > 0
            ? (winRate / 100m) * avgWin + (1m - winRate / 100m) * avgLoss
            : 0m;

        decimal avgBars = total > 0 ? sumBars / total : 0m;
        decimal avgDays = total > 0 ? sumDays / total : 0m;

        decimal months = equity.Count >= 2
            ? (decimal)((equity[^1].DateTime - equity[0].DateTime).TotalDays / 30.4375)
            : 0m;
        decimal tradesPerMonth = months > 0.01m ? total / months : 0m;

        // ---------- drawdown ----------
        decimal peak = initialCapital;
        decimal maxDd = 0m;
        decimal maxDdPct = 0m;
        int peakBar = 0;
        int ddDuration = 0;
        for (int i = 0; i < equity.Count; i++)
        {
            var e = equity[i].Equity;
            if (e > peak)
            {
                peak = e;
                peakBar = i;
            }
            else
            {
                var dd = peak - e;
                if (dd > maxDd)
                {
                    maxDd = dd;
                    maxDdPct = peak > 0m ? dd / peak * 100m : 0m;
                }
                var dur = i - peakBar;
                if (dur > ddDuration) ddDuration = dur;
            }
        }

        // ---------- monthly returns for Sharpe/Sortino ----------
        var monthlyReturns = ComputeMonthlyReturns(equity);
        decimal sharpe = Sharpe(monthlyReturns, riskFreeRatePct);
        decimal sortino = Sortino(monthlyReturns, riskFreeRatePct);

        decimal calmar = maxDdPct != 0m ? cagr / maxDdPct : 0m;
        decimal recovery = maxDd != 0m ? net / maxDd : 0m;

        // ---------- excursions ----------
        decimal avgMae = total > 0 ? sumMae / total : 0m;
        decimal avgMfe = total > 0 ? sumMfe / total : 0m;
        decimal maeMfeRatio = avgMfe != 0m ? avgMae / avgMfe : 0m;

        return new BacktestMetrics
        {
            TotalNetPnL = Round2(net),
            GrossPnL = Round2(gross),
            TotalCosts = Round2(costs),
            CostAsPctOfGross = Round2(costPctGross),
            NetProfitPct = Round2(netProfitPct),
            Cagr = Round2(cagr),

            TotalTrades = total,
            WinningTrades = wins,
            LosingTrades = losses,
            BreakevenTrades = breakeven,
            WinRatePct = Round2(winRate),
            AverageWin = Round2(avgWin),
            AverageLoss = Round2(avgLoss),
            LargestWin = Round2(largestWin),
            LargestLoss = Round2(largestLoss),
            ProfitFactor = Round2(profitFactor),
            Expectancy = Round2(expectancy),
            AverageHoldingBars = Round2(avgBars),
            AverageHoldingDays = Round2(avgDays),
            TradesPerMonth = Round2(tradesPerMonth),

            MaxDrawdownAbsolute = Round2(maxDd),
            MaxDrawdownPct = Round2(maxDdPct),
            DrawdownDurationBars = ddDuration,
            Sharpe = Round4(sharpe),
            Sortino = Round4(sortino),
            Calmar = Round4(calmar),
            RecoveryFactor = Round4(recovery),

            AverageMAE = Round2(avgMae),
            AverageMFE = Round2(avgMfe),
            MAEMFERatio = Round4(maeMfeRatio)
        };
    }

    private static List<decimal> ComputeMonthlyReturns(IReadOnlyList<EquityPoint> equity)
    {
        var result = new List<decimal>();
        if (equity.Count < 2) return result;

        int startIdx = 0;
        decimal startEquity = equity[0].Equity;
        var startMonth = new DateTime(equity[0].DateTime.Year, equity[0].DateTime.Month, 1);

        for (int i = 1; i < equity.Count; i++)
        {
            var m = new DateTime(equity[i].DateTime.Year, equity[i].DateTime.Month, 1);
            if (m != startMonth)
            {
                var endEquity = equity[i - 1].Equity;
                if (startEquity != 0m)
                    result.Add((endEquity - startEquity) / startEquity);

                startIdx = i;
                startEquity = equity[i].Equity;
                startMonth = m;
            }
        }
        // final partial month
        if (startIdx < equity.Count - 1 && startEquity != 0m)
            result.Add((equity[^1].Equity - startEquity) / startEquity);

        return result;
    }

    private static decimal Sharpe(List<decimal> monthlyReturns, decimal riskFreePct)
    {
        if (monthlyReturns.Count < 2) return 0m;
        var mean = Mean(monthlyReturns);
        var sd = StdDev(monthlyReturns, mean);
        if (sd == 0m) return 0m;
        var rfMonthly = riskFreePct / 100m / 12m;
        var adj = mean - rfMonthly;
        return adj / sd * (decimal)Math.Sqrt(12.0);
    }

    private static decimal Sortino(List<decimal> monthlyReturns, decimal riskFreePct)
    {
        if (monthlyReturns.Count < 2) return 0m;
        var mean = Mean(monthlyReturns);
        var rfMonthly = riskFreePct / 100m / 12m;
        var adj = mean - rfMonthly;

        decimal sqSum = 0m;
        int n = 0;
        foreach (var r in monthlyReturns)
        {
            if (r < 0m) { sqSum += r * r; n++; }
        }
        if (n == 0) return 0m;
        var downside = (decimal)Math.Sqrt((double)(sqSum / n));
        if (downside == 0m) return 0m;
        return adj / downside * (decimal)Math.Sqrt(12.0);
    }

    private static decimal Mean(List<decimal> values)
    {
        if (values.Count == 0) return 0m;
        decimal sum = 0m;
        foreach (var v in values) sum += v;
        return sum / values.Count;
    }

    private static decimal StdDev(List<decimal> values, decimal mean)
    {
        if (values.Count < 2) return 0m;
        decimal sq = 0m;
        foreach (var v in values) sq += (v - mean) * (v - mean);
        return (decimal)Math.Sqrt((double)(sq / (values.Count - 1)));
    }

    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);
}