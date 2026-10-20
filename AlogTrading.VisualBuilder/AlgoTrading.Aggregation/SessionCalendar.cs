using System.Globalization;
using System.Runtime.CompilerServices;

namespace AlgoTrading.Aggregation;

/// <summary>
/// Centralises NSE session logic. Session window is 09:15â€“15:30 IST
/// (03:45â€“10:00 UTC). Boundaries align to session start.
/// </summary>
public static class SessionCalendar
{
    public const int SessionStartMinutesIst = 9 * 60 + 15;      // 555
    public const int SessionEndMinutesIst   = 15 * 60 + 30;     // 930
    public const int SessionLengthMinutes   = SessionEndMinutesIst - SessionStartMinutesIst; // 375
    public const int IstOffsetMinutes       = 330;              // +05:30

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime ToIst(DateTime utc) => utc.AddMinutes(IstOffsetMinutes);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime ToUtc(DateTime ist) => ist.AddMinutes(-IstOffsetMinutes);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWeekend(DayOfWeek dow) =>
        dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsTradingDay(DateTime istDate) => !IsWeekend(istDate.DayOfWeek);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MinutesSinceSessionStartIst(DateTime ist) =>
        ist.Hour * 60 + ist.Minute - SessionStartMinutesIst;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWithinSessionIst(DateTime ist)
    {
        int m = ist.Hour * 60 + ist.Minute;
        return m >= SessionStartMinutesIst && m < SessionEndMinutesIst;
    }

    public static (int Year, int Week) GetIsoWeek(DateTime date) =>
        (ISOWeek.GetYear(date), ISOWeek.GetWeekOfYear(date));
}
