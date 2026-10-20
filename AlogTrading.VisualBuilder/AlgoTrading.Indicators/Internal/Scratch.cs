using System;
using System.Buffers;

namespace AlgoTrading.Indicators.Internal;

/// <summary>
/// Pooled scratch buffers for multi-stage indicators.
/// Rented arrays are cleared on return to prevent stale state leakage.
/// </summary>
internal static class Scratch
{
    public static decimal?[] RentNullable(int minLength)
        => ArrayPool<decimal?>.Shared.Rent(minLength);

    public static void Return(decimal?[] array)
    {
        Array.Clear(array, 0, array.Length);
        ArrayPool<decimal?>.Shared.Return(array);
    }

    public static decimal[] RentDecimal(int minLength)
        => ArrayPool<decimal>.Shared.Rent(minLength);

    public static void Return(decimal[] array)
    {
        Array.Clear(array, 0, array.Length);
        ArrayPool<decimal>.Shared.Return(array);
    }

    public static int[] RentInt(int minLength)
        => ArrayPool<int>.Shared.Rent(minLength);

    public static void Return(int[] array)
    {
        Array.Clear(array, 0, array.Length);
        ArrayPool<int>.Shared.Return(array);
    }
}