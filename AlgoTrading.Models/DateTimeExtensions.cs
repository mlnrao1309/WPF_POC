using System;

namespace AlgoTrading.Models
{
    public static class DateTimeExtensions
    {
        public static long ToUnixMilliseconds(this DateTime dt)
        {
            var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return (long)(utc - DateTime.UnixEpoch).TotalMilliseconds;
        }

        public static DateTime FromUnixMilliseconds(long ms)
        {
            return DateTime.UnixEpoch.AddMilliseconds(ms);
        }
    }
}
