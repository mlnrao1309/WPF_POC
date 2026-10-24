using AlgoTrading.Models;

namespace AlgoTrading.Backtest.Results;

/// <summary>Assessment only. This never changes orders, positions or strategy approval.</summary>
public static class RiskAssessmentCalculator
{
    public static RiskAssessment Compute(BacktestResult result, BacktestEngineOptions options)
    {
        var capital = new List<CapitalPoint>(result.EquityCurve.Count);
        var periods = new List<RiskPeriodSummary>();
        var breaches = new List<RiskLimitBreach>();
        var trades = result.Trades.OrderBy(t => t.ExitTime).ToArray();
        decimal realized = options.InitialCapital;
        decimal previousRealized = realized, previousEquity = realized;
        int tradeIndex = 0;
        RiskPeriodSummary? day = null, month = null;

        foreach (var point in result.EquityCurve)
        {
            var date = DateOnly.FromDateTime(DateTime.SpecifyKind(point.DateTime, DateTimeKind.Utc).AddMinutes(330));
            var monthDate = new DateOnly(date.Year, date.Month, 1);
            if (day is null || day.StartDateIst != date)
            {
                day = NewPeriod(RiskPeriod.Daily, date, previousRealized, previousEquity,
                    options.InitialCapital * options.DailyLossLimitPercent / 100m);
                periods.Add(day);
            }
            if (month is null || month.StartDateIst != monthDate)
            {
                month = NewPeriod(RiskPeriod.Monthly, monthDate, previousRealized, previousEquity,
                    options.InitialCapital * options.MonthlyLossLimitPercent / 100m);
                periods.Add(month);
            }

            // Realized losses count on exit, including full round-trip charges.
            // Evaluate each exit so a later recovery in the same bar cannot erase a breach.
            while (tradeIndex < trades.Length && trades[tradeIndex].ExitTime <= point.DateTime)
            {
                realized += trades[tradeIndex++].NetPnL;
                if (options.LossBasis == RiskLossBasis.RealizedOnly)
                {
                    CheckBreach(day, realized, options.LossBasis, point.DateTime, breaches);
                    CheckBreach(month, realized, options.LossBasis, point.DateTime, breaches);
                }
            }
            day.ClosingRealizedCapital = month.ClosingRealizedCapital = realized;
            day.ClosingEquity = month.ClosingEquity = point.Equity;
            decimal assessmentCapital = options.LossBasis == RiskLossBasis.RealizedOnly ? realized : point.Equity;
            CheckBreach(day, assessmentCapital, options.LossBasis, point.DateTime, breaches);
            CheckBreach(month, assessmentCapital, options.LossBasis, point.DateTime, breaches);
            capital.Add(new CapitalPoint { DateTime = point.DateTime, RealizedCapital = realized, Equity = point.Equity });
            previousRealized = realized;
            previousEquity = point.Equity;
        }

        var reason = result.EquityCurve.Count < 2 ? RiskAssessmentReason.InsufficientData
            : trades.Length == 0 ? RiskAssessmentReason.NoClosedTrades : RiskAssessmentReason.None;
        return new RiskAssessment
        {
            Status = breaches.Count > 0 ? RiskAssessmentStatus.Rejected
                : reason == RiskAssessmentReason.None ? RiskAssessmentStatus.Approved : RiskAssessmentStatus.NotAssessed,
            Reason = breaches.Count > 0 ? RiskAssessmentReason.LossLimitBreached : reason,
            LossBasis = options.LossBasis, TradedCapital = options.InitialCapital,
            DailyLossLimit = options.InitialCapital * options.DailyLossLimitPercent / 100m,
            MonthlyLossLimit = options.InitialCapital * options.MonthlyLossLimitPercent / 100m,
            CapitalCurve = capital, Periods = periods, Breaches = breaches
        };
    }

    private static RiskPeriodSummary NewPeriod(RiskPeriod period, DateOnly date,
        decimal realized, decimal equity, decimal limit) => new()
    {
        Period = period, StartDateIst = date, Limit = limit,
        OpeningRealizedCapital = realized, ClosingRealizedCapital = realized,
        OpeningEquity = equity, ClosingEquity = equity
    };

    private static void CheckBreach(RiskPeriodSummary period, decimal capital, RiskLossBasis basis,
        DateTime atUtc, List<RiskLimitBreach> breaches)
    {
        decimal opening = basis == RiskLossBasis.RealizedOnly ? period.OpeningRealizedCapital : period.OpeningEquity;
        decimal loss = opening - capital;
        if (period.FirstBreachAtUtc.HasValue || loss < period.Limit) return;
        period.FirstBreachAtUtc = atUtc;
        breaches.Add(new RiskLimitBreach
        {
            Period = period.Period, StartDateIst = period.StartDateIst,
            AtUtc = atUtc, Loss = loss, Limit = period.Limit
        });
    }
}
