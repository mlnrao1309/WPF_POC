using System;

namespace AlgoTrading.Backtest;

public readonly record struct EquityPoint(DateTime DateTime, decimal Equity);