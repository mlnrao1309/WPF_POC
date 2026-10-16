using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AlgoTrading.Models
{
    /// <summary>
    /// Client-side pacing that keeps Kite historical requests inside the documented
    /// three-requests-per-second quota. One instance guards one sequential request pipeline,
    /// so no external rate-limiting package is required.
    /// </summary>
    internal sealed class KiteRequestPacer
    {
        private readonly int maximumRequestsPerSecond;
        private readonly Queue<long> requestTicks = new Queue<long>();
        private readonly Stopwatch clock;
        private readonly SemaphoreSlim gate;

        internal KiteRequestPacer(int maximumRequestsPerSecond)
        {
            if (maximumRequestsPerSecond <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRequestsPerSecond), "A positive request quota is required.");
            }

            this.maximumRequestsPerSecond = maximumRequestsPerSecond;
            this.clock = Stopwatch.StartNew();
            this.gate = new SemaphoreSlim(1, 1);
        }

        /// <summary>Blocks the caller until the next request is inside the configured quota.</summary>
        internal async Task WaitForTurnAsync(CancellationToken cancellationToken)
        {
            await this.gate.WaitAsync(cancellationToken);
            try
            {
                while (true)
                {
                    long nowTicks = this.clock.ElapsedTicks;
                    ForgetExpiredRequests(nowTicks);
                    if (this.requestTicks.Count < this.maximumRequestsPerSecond)
                    {
                        this.requestTicks.Enqueue(nowTicks);
                        return;
                    }

                    long oldestTicks = this.requestTicks.Peek();
                    long remainingTicks = Stopwatch.Frequency - (nowTicks - oldestTicks);
                    if (remainingTicks <= 0)
                    {
                        continue;
                    }

                    TimeSpan wait = TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
                    await Task.Delay(wait, cancellationToken);
                }
            }
            finally
            {
                this.gate.Release();
            }
        }

        private void ForgetExpiredRequests(long nowTicks)
        {
            while (this.requestTicks.Count > 0)
            {
                long oldestTicks = this.requestTicks.Peek();
                if (nowTicks - oldestTicks < Stopwatch.Frequency)
                {
                    return;
                }

                this.requestTicks.Dequeue();
            }
        }
    }
}
