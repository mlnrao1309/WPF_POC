using System;
using System.Collections.Generic;

namespace AlgoTrading.Backtest;

public sealed class BacktestMetrics
{
    public decimal TotalNetPnL { get; init; }
    public decimal GrossPnL { get; init; }
    public decimal TotalCosts { get; init; }
    public decimal CostAsPercentOfGross { get; init; }
    public decimal NetProfitPct { get; init; }
    public decimal CAGR { get; init; }

    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public int BreakevenTrades { get; init; }
    public decimal WinRate { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public decimal LargestWin { get; init; }
    public decimal LargestLoss { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal Expectancy { get; init; }
    public decimal AverageHoldingBars { get; init; }
    public decimal AverageHoldingDays { get; init; }
    public decimal TradesPerMonth { get; init; }

    public decimal MaxDrawdown { get; init; }
    public decimal MaxDrawdownPct { get; init; }
    public int MaxDrawdownDurationBars { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal SortinoRatio { get; init; }
    public decimal CalmarRatio { get; init; }
    public decimal RecoveryFactor { get; init; }

    public decimal AverageMAE { get; init; }
    public decimal AverageMFE { get; init; }
    public decimal MAEToMFERatio { get; init; }

    public static BacktestMetrics Compute(EquityPoint[] equity, IReadOnlyList<Trade> trades, BacktestConfig cfg)
    {
        int n = equity.Length;

        decimal totalNet = 0m, totalGross = 0m, totalCosts = 0m;
        for (int i = 0; i < trades.Count; i++)
        {
            totalNet   += trades[i].NetPnL;
            totalGross += trades[i].GrossPnL;
            totalCosts += trades[i].TotalCosts;
        }

        decimal costPct = Math.Abs(totalGross) == 0m ? 0m : totalCosts / Math.Abs(totalGross) * 100m;
        decimal netPct  = cfg.InitialCapital == 0m ? 0m : totalNet / cfg.InitialCapital * 100m;

        decimal cagr = 0m;
        if (n >= 2 && cfg.InitialCapital > 0m)
        {
            double days = (equity[n - 1].DateTime - equity[0].DateTime).TotalDays;
            if (days > 0.0)
            {
                decimal final = cfg.InitialCapital + totalNet;
                if (final > 0m)
                {
                    double years = days / 365.25;
                    cagr = (decimal)(Math.Pow((double)(final / cfg.InitialCapital), 1.0 / years) - 1.0);
                }
            }
        }

        int wins = 0, losses = 0, be = 0;
        decimal sumW = 0m, sumL = 0m, largW = 0m, largL = 0m, sumBars = 0m, sumDays = 0m, sumMae = 0m, sumMfe = 0m;
        for (int i = 0; i < trades.Count; i++)
        {
            var t = trades[i];
            if (t.NetPnL > 0m) { wins++; sumW += t.NetPnL; if (t.NetPnL > largW) largW = t.NetPnL; }
            else if (t.NetPnL < 0m) { losses++; sumL += t.NetPnL; if (t.NetPnL < largL) largL = t.NetPnL; }
            else be++;
            sumBars += t.BarsHeld; sumDays += t.DaysHeld; sumMae += t.MAE; sumMfe += t.MFE;
        }

        int total = trades.Count;
        decimal avgW = wins == 0 ? 0m : sumW / wins;
        decimal avgL = losses == 0 ? 0m : sumL / losses;
        decimal winRate = total == 0 ? 0m : (decimal)wins / total * 100m;
        decimal profitFactor = Math.Abs(sumL) == 0m ? (sumW > 0m ? 999999m : 0m) : sumW / Math.Abs(sumL);
        decimal expectancy = (winRate / 100m) * avgW - (1m - winRate / 100m) * Math.Abs(avgL);

        decimal tradesPerMonth = 0m;
        if (n >= 2)
        {
            double td = (equity[n - 1].DateTime - equity[0].DateTime).TotalDays;
            if (td > 0.0) tradesPerMonth = (decimal)(total / (td / 30.4375));
        }

        decimal peak = equity[0].Equity, maxDD = 0m, maxDDPct = 0m;
        int ddStart = 0, maxDur = 0;
        for (int i = 0; i < n; i++)
        {
            decimal eq = equity[i].Equity;
            if (eq > peak) { peak = eq; ddStart = i; }
            decimal dd = peak - eq;
            if (dd > maxDD) maxDD = dd;
            if (peak > 0m) { decimal ddp = dd / peak * 100m; if (ddp > maxDDPct) maxDDPct = ddp; }
            int dur = i - ddStart; if (dur > maxDur) maxDur = dur;
        }

        var monthly = BuildMonthlyReturns(equity);
        decimal rf = cfg.RiskFreeRatePercent / 100m / 12m;
        ComputeSharpeSortino(monthly, rf, out decimal sharpe, out decimal sortino);

        decimal calmar = maxDDPct == 0m ? 0m : cagr / (maxDDPct / 100m);
        decimal recov  = maxDD == 0m ? 0m : totalNet / maxDD;
        decimal avgMae = total == 0 ? 0m : sumMae / total;
        decimal avgMfe = total == 0 ? 0m : sumMfe / total;
        decimal maeMfe = avgMfe == 0m ? 0m : avgMae / avgMfe;

        return new BacktestMetrics
        {
            TotalNetPnL = totalNet, GrossPnL = totalGross, TotalCosts = totalCosts,
            CostAsPercentOfGross = costPct, NetProfitPct = netPct, CAGR = cagr,
            TotalTrades = total, WinningTrades = wins, LosingTrades = losses, BreakevenTrades = be,
            WinRate = winRate, AverageWin = avgW, AverageLoss = avgL, LargestWin = largW, LargestLoss = largL,
            ProfitFactor = profitFactor, Expectancy = expectancy,
            AverageHoldingBars = total == 0 ? 0m : sumBars / total,
            AverageHoldingDays = total == 0 ? 0m : sumDays / total,
            TradesPerMonth = tradesPerMonth,
            MaxDrawdown = maxDD, MaxDrawdownPct = maxDDPct, MaxDrawdownDurationBars = maxDur,
            SharpeRatio = sharpe, SortinoRatio = sortino, CalmarRatio = calmar, RecoveryFactor = recov,
            AverageMAE = avgMae, AverageMFE = avgMfe, MAEToMFERatio = maeMfe,
        };
    }

    private static List<decimal> BuildMonthlyReturns(EquityPoint[] equity)
    {
        var ret = new List<decimal>();
        if (equity.Length < 2) return ret;

        int curYear = equity[0].DateTime.Year;
        int curMonth = equity[0].DateTime.Month;
        decimal monthClose = equity[0].Equity;
        var closings = new List<decimal>();

        for (int i = 1; i < equity.Length; i++)
        {
            var dt = equity[i].DateTime;
            if (dt.Year != curYear || dt.Month != curMonth)
            {
                closings.Add(monthClose);
                curYear = dt.Year;
                curMonth = dt.Month;
            }
            monthClose = equity[i].Equity;
        }
        closings.Add(monthClose);

        for (int i = 1; i < closings.Count; i++)
        {
            decimal prev = closings[i - 1];
            if (prev > 0m) ret.Add(closings[i] / prev - 1m);
        }
        return ret;
    }

    private static void ComputeSharpeSortino(List<decimal> m, decimal rf, out decimal sharpe, out decimal sortino)
    {
        sharpe = 0m; sortino = 0m;
        int n = m.Count; if (n < 2) return;
        decimal mean = 0m; for (int i = 0; i < n; i++) mean += m[i]; mean /= n;
        decimal s = 0m; for (int i = 0; i < n; i++) { decimal d = m[i] - mean; s += d * d; }
        decimal std = Sqrt(s / (n - 1));
        decimal ex = mean - rf;
        const decimal ann = 3.46410161513775m;
        if (std > 0m) sharpe = ex / std * ann;
        decimal ds = 0m; int dc = 0;
        for (int i = 0; i < n; i++) if (m[i] < rf) { decimal d = m[i] - rf; ds += d * d; dc++; }
        if (dc > 0) { decimal dd = Sqrt(ds / dc); if (dd > 0m) sortino = ex / dd * ann; }
    }

    private static decimal Sqrt(decimal v) => v <= 0m ? 0m : (decimal)Math.Sqrt((double)v);
}