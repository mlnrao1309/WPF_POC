using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using AlgoTrading.Models.MarketData.Processing;

namespace AlgoTrading.Models.MarketData.Pipeline
{
    /// <summary>
    /// Bounded in-process live pipeline. Instrument hashing assigns every instrument to exactly one single-reader
    /// shard, preserving its order while independent shards process concurrently.
    /// </summary>
    public sealed class BoundedMarketDataPipeline : IAsyncDisposable
    {
        private readonly MarketDataPipelineOptions options;
        private readonly IInstrumentCandleProcessorFactory processorFactory;
        private readonly IClosedCandleProcessingSink sink;
        private readonly Channel<QueuedFinalizedCandle>[] shards;
        private readonly Task[] workers;
        private readonly CancellationTokenSource shutdownCancellation;
        private readonly ConcurrentDictionary<string, byte> acceptedIdentities;
        private readonly ConcurrentQueue<string> completedIdentities;
        private readonly ConcurrentQueue<MarketDataPipelineFailure> failures;
        private readonly MarketDataPipelineMetrics metrics;
        private int started;
        private int stopped;
        private int completedIdentityCount;

        public BoundedMarketDataPipeline(MarketDataPipelineOptions options,
            IInstrumentCandleProcessorFactory processorFactory, IClosedCandleProcessingSink sink)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (processorFactory == null)
            {
                throw new ArgumentNullException(nameof(processorFactory));
            }

            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            if (options.Capacity < options.ShardCount)
            {
                throw new ArgumentException("Pipeline capacity must be at least the shard count.", nameof(options));
            }

            this.options = options;
            this.processorFactory = processorFactory;
            this.sink = sink;
            this.shards = new Channel<QueuedFinalizedCandle>[options.ShardCount];
            this.workers = new Task[options.ShardCount];
            this.shutdownCancellation = new CancellationTokenSource();
            this.acceptedIdentities = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            this.completedIdentities = new ConcurrentQueue<string>();
            this.failures = new ConcurrentQueue<MarketDataPipelineFailure>();
            this.metrics = new MarketDataPipelineMetrics();

            int baseCapacity = options.Capacity / options.ShardCount;
            int remainder = options.Capacity % options.ShardCount;
            for (int index = 0; index < options.ShardCount; index++)
            {
                int shardCapacity = baseCapacity;
                if (index < remainder)
                {
                    shardCapacity++;
                }

                BoundedChannelOptions channelOptions = new BoundedChannelOptions(shardCapacity);
                channelOptions.FullMode = BoundedChannelFullMode.Wait;
                channelOptions.SingleReader = true;
                channelOptions.SingleWriter = false;
                channelOptions.AllowSynchronousContinuations = false;
                this.shards[index] = Channel.CreateBounded<QueuedFinalizedCandle>(channelOptions);
                this.workers[index] = Task.CompletedTask;
            }
        }

        public Task StartAsync()
        {
            if (Interlocked.CompareExchange(ref this.started, 1, 0) != 0)
            {
                return Task.CompletedTask;
            }

            if (Volatile.Read(ref this.stopped) != 0)
            {
                throw new InvalidOperationException("A stopped market-data pipeline cannot be restarted.");
            }

            for (int index = 0; index < this.workers.Length; index++)
            {
                this.workers[index] = this.RunShardAsync(index);
            }

            return Task.CompletedTask;
        }

        public MarketDataEnqueueStatus TryEnqueue(FinalizedSourceCandleEvent sourceEvent)
        {
            MarketDataEnqueueStatus validation = this.ValidateForEnqueue(sourceEvent);
            if (validation != MarketDataEnqueueStatus.Accepted)
            {
                return validation;
            }

            string identity = BuildSourceIdentity(sourceEvent);
            if (!this.acceptedIdentities.TryAdd(identity, 0))
            {
                this.metrics.Rejected();
                return MarketDataEnqueueStatus.Duplicate;
            }

            QueuedFinalizedCandle queued = new QueuedFinalizedCandle(identity, sourceEvent);
            int shardIndex = this.GetShardIndex(sourceEvent.Candle.Candle.InstrumentToken);
            if (!this.shards[shardIndex].Writer.TryWrite(queued))
            {
                byte ignored;
                this.acceptedIdentities.TryRemove(identity, out ignored);
                this.metrics.Rejected();
                return MarketDataEnqueueStatus.QueueFull;
            }

            this.metrics.Accepted();
            return MarketDataEnqueueStatus.Accepted;
        }

        public async Task<MarketDataEnqueueStatus> EnqueueAsync(FinalizedSourceCandleEvent sourceEvent,
            CancellationToken cancellationToken)
        {
            MarketDataEnqueueStatus validation = this.ValidateForEnqueue(sourceEvent);
            if (validation != MarketDataEnqueueStatus.Accepted)
            {
                return validation;
            }

            string identity = BuildSourceIdentity(sourceEvent);
            if (!this.acceptedIdentities.TryAdd(identity, 0))
            {
                this.metrics.Rejected();
                return MarketDataEnqueueStatus.Duplicate;
            }

            QueuedFinalizedCandle queued = new QueuedFinalizedCandle(identity, sourceEvent);
            int shardIndex = this.GetShardIndex(sourceEvent.Candle.Candle.InstrumentToken);
            try
            {
                await this.shards[shardIndex].Writer.WriteAsync(queued, cancellationToken);
                this.metrics.Accepted();
                return MarketDataEnqueueStatus.Accepted;
            }
            catch (ChannelClosedException)
            {
                byte ignored;
                this.acceptedIdentities.TryRemove(identity, out ignored);
                this.metrics.Rejected();
                return MarketDataEnqueueStatus.Stopped;
            }
            catch
            {
                byte ignored;
                this.acceptedIdentities.TryRemove(identity, out ignored);
                throw;
            }
        }

        public MarketDataPipelineMetricsSnapshot GetMetrics()
        {
            return this.metrics.Snapshot();
        }

        public IReadOnlyList<MarketDataPipelineFailure> GetFailures()
        {
            return this.failures.ToArray();
        }

        public async Task StopAsync(bool drainAcceptedWork)
        {
            if (Interlocked.CompareExchange(ref this.stopped, 1, 0) != 0)
            {
                await Task.WhenAll(this.workers);
                return;
            }

            for (int index = 0; index < this.shards.Length; index++)
            {
                this.shards[index].Writer.TryComplete();
            }

            if (!drainAcceptedWork)
            {
                this.shutdownCancellation.Cancel();
            }

            await Task.WhenAll(this.workers);
            if (!drainAcceptedWork)
            {
                this.DiscardRemaining();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await this.StopAsync(false);
            this.shutdownCancellation.Dispose();
        }

        private MarketDataEnqueueStatus ValidateForEnqueue(FinalizedSourceCandleEvent sourceEvent)
        {
            if (Volatile.Read(ref this.started) == 0)
            {
                this.metrics.Rejected();
                return MarketDataEnqueueStatus.NotStarted;
            }

            if (Volatile.Read(ref this.stopped) != 0)
            {
                this.metrics.Rejected();
                return MarketDataEnqueueStatus.Stopped;
            }

            if (sourceEvent == null || sourceEvent.Candle == null || !sourceEvent.ProviderFinalized
                || sourceEvent.Candle.ClosedAt > sourceEvent.ObservedAt)
            {
                this.metrics.Rejected();
                return MarketDataEnqueueStatus.RejectedNotFinalized;
            }

            return MarketDataEnqueueStatus.Accepted;
        }

        private async Task RunShardAsync(int shardIndex)
        {
            ChannelReader<QueuedFinalizedCandle> reader = this.shards[shardIndex].Reader;
            Dictionary<int, IClosedCandleEventProcessor> processors =
                new Dictionary<int, IClosedCandleEventProcessor>();
            try
            {
                while (await reader.WaitToReadAsync(this.shutdownCancellation.Token))
                {
                    QueuedFinalizedCandle? queued;
                    while (reader.TryRead(out queued))
                    {
                        if (queued != null)
                        {
                            await this.ProcessOneAsync(queued, processors, this.shutdownCancellation.Token);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task ProcessOneAsync(QueuedFinalizedCandle queued,
            Dictionary<int, IClosedCandleEventProcessor> processors, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            int instrumentToken = queued.SourceEvent.Candle.Candle.InstrumentToken;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                IClosedCandleEventProcessor? processor;
                if (!processors.TryGetValue(instrumentToken, out processor) || processor == null)
                {
                    processor = this.processorFactory.Create(instrumentToken);
                    processors.Add(instrumentToken, processor);
                }

                ClosedCandleProcessingResult result = processor.Process(queued.SourceEvent);
                await this.DeliverWithRetryAsync(queued, result, cancellationToken);
                this.RememberCompletedIdentity(queued.Identity);
                stopwatch.Stop();
                this.metrics.Processed(stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                byte ignored;
                this.acceptedIdentities.TryRemove(queued.Identity, out ignored);
                this.metrics.Unprocessed();
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                this.failures.Enqueue(new MarketDataPipelineFailure(queued.Identity, instrumentToken, 1,
                    exception.Message));
                byte ignored;
                this.acceptedIdentities.TryRemove(queued.Identity, out ignored);
                this.metrics.Failed(stopwatch.Elapsed);
            }
        }

        private async Task DeliverWithRetryAsync(QueuedFinalizedCandle queued, ClosedCandleProcessingResult result,
            CancellationToken cancellationToken)
        {
            Exception? lastFailure = null;
            for (int attempt = 1; attempt <= this.options.MaximumSinkAttempts; attempt++)
            {
                try
                {
                    await this.sink.HandleAsync(result, cancellationToken);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                    if (attempt == this.options.MaximumSinkAttempts)
                    {
                        break;
                    }

                    this.metrics.Retried();
                    await Task.Delay(this.options.SinkRetryDelay, cancellationToken);
                }
            }

            string message = "Processing sink failed after "
                + this.options.MaximumSinkAttempts.ToString(CultureInfo.InvariantCulture) + " attempt(s).";
            throw new InvalidOperationException(message, lastFailure);
        }

        private void DiscardRemaining()
        {
            for (int index = 0; index < this.shards.Length; index++)
            {
                QueuedFinalizedCandle? queued;
                while (this.shards[index].Reader.TryRead(out queued))
                {
                    if (queued != null)
                    {
                        byte ignored;
                        this.acceptedIdentities.TryRemove(queued.Identity, out ignored);
                        this.metrics.Unprocessed();
                    }
                }
            }
        }

        private void RememberCompletedIdentity(string identity)
        {
            if (!this.acceptedIdentities.TryUpdate(identity, 1, 0))
            {
                return;
            }

            this.completedIdentities.Enqueue(identity);
            int count = Interlocked.Increment(ref this.completedIdentityCount);
            while (count > this.options.DeduplicationHistoryCapacity
                && this.completedIdentities.TryDequeue(out string? expiredIdentity))
            {
                Interlocked.Decrement(ref this.completedIdentityCount);
                this.acceptedIdentities.TryRemove(expiredIdentity, out _);
                count = Volatile.Read(ref this.completedIdentityCount);
            }
        }

        private int GetShardIndex(int instrumentToken)
        {
            return (instrumentToken & int.MaxValue) % this.shards.Length;
        }

        private static string BuildSourceIdentity(FinalizedSourceCandleEvent sourceEvent)
        {
            return sourceEvent.Candle.Candle.InstrumentToken.ToString(CultureInfo.InvariantCulture) + "|"
                + sourceEvent.StreamType + "|"
                + sourceEvent.Candle.Candle.OpenedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture) + "|"
                + sourceEvent.Candle.ClosedAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture);
        }
    }
}
