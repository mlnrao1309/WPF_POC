using System;
using Xunit.Sdk;

namespace AlgoTrading.Indicators.Tests;

internal static class H
{
    public static decimal?[] Out(int n) => new decimal?[n];

    public static void Approx(decimal expected, decimal? actual, decimal tol = 0.0001m)
    {
        if (!actual.HasValue)
            throw new XunitException($"Expected {expected} but got null.");
        decimal d = expected - actual.Value;
        if (d < 0m) d = -d;
        if (d > tol)
            throw new XunitException($"Expected {expected} Â± {tol}, got {actual.Value} (diff {d}).");
    }

    public static void Null(decimal? actual, int index)
    {
        if (actual.HasValue)
            throw new XunitException($"Expected null at index {index}, got {actual.Value}.");
    }

    public static void AllNull(System.ReadOnlySpan<decimal?> span)
    {
        for (int i = 0; i < span.Length; i++)
            if (span[i].HasValue)
                throw new XunitException($"Expected null at index {i}, got {span[i]!.Value}.");
    }

    public static decimal[] Lin(int n, decimal start = 1m, decimal step = 1m)
    {
        var a = new decimal[n];
        for (int i = 0; i < n; i++) a[i] = start + step * i;
        return a;
    }

    public static decimal[] Const(int n, decimal v)
    {
        var a = new decimal[n];
        for (int i = 0; i < n; i++) a[i] = v;
        return a;
    }

    public static decimal[] Sinusoid(int n, decimal baseV, decimal amp)
    {
        var a = new decimal[n];
        for (int i = 0; i < n; i++)
            a[i] = baseV + amp * (decimal)System.Math.Sin(i * 0.2);
        return a;
    }
}
