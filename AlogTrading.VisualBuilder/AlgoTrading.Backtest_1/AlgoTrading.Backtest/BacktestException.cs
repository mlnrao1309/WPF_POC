using System;

namespace AlgoTrading.Backtest;

public sealed class BacktestException : Exception
{
    public BacktestException(string msg) : base(msg) { }
    public BacktestException(string msg, Exception inner) : base(msg, inner) { }
}