using System.Collections.Concurrent;
using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Ingestion;
using AlgoTrading.Models.MarketData.Pipeline;
using AlgoTrading.Models.MarketData.Processing;
using AlgoTrading.Models.Rules;

internal sealed class BoundedMarketDataPipelineTests
{
    private int assertions;

    public async Task RunAsync()
    {
        await this.VerifySyntheticUniverseBurstAsync();
        await this.VerifySingleInstrumentOrderingAndDeduplicationAsync();
        await this.VerifyBoundedCapacityAsync();
        await this.VerifyGracefulAndImmediateShutdownAsync();
        await this.VerifyFailureIsolationAndRetryMetricsAsync();
        await this.VerifyDeduplicationHistoryIsBoundedAsync();
        Console.WriteLine("Passed " + this.assertions + " bounded market-data pipeline assertions.");
    }

    private async Task VerifySyntheticUniverseBurstAsync()
    {
        RecordingProcessorFactory factory = new RecordingProcessorFactory();
        CountingSink sink = new CountingSink();
        MarketDataPipelineOptions options = new MarketDataPipelineOptions(412, 8, 2, TimeSpan.Zero);
        await using (BoundedMarketDataPipeline pipeline = new BoundedMarketDataPipeline(options, factory, sink))
        {
            await pipeline.StartAsync();
            DateTimeOffset origin = new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero);
            for (int index = 0; index < 206; index++)
            {
                FinalizedSourceCandleEvent sourceEvent = CreateEvent(1000 + index, origin, index);
                MarketDataEnqueueStatus status = await pipeline.EnqueueAsync(sourceEvent, CancellationToken.None);
                this.Assert(status == MarketDataEnqueueStatus.Accepted,
                    "Every synthetic-universe event is accepted under configured capacity.");
            }

            await pipeline.StopAsync(true);
            MarketDataPipelineMetricsSnapshot metrics = pipeline.GetMetrics();
            this.Assert(metrics.ProcessedEvents == 206 && metrics.QueueDepth == 0,
                "A 206-instrument burst drains completely.");
            this.Assert(factory.InstrumentCount == 206,
                "Independent instruments receive independent processors.");
        }
    }

    private async Task VerifySingleInstrumentOrderingAndDeduplicationAsync()
    {
        RecordingProcessorFactory factory = new RecordingProcessorFactory();
        CountingSink sink = new CountingSink();
        MarketDataPipelineOptions options = new MarketDataPipelineOptions(32, 4, 1, TimeSpan.Zero);
        await using (BoundedMarketDataPipeline pipeline = new BoundedMarketDataPipeline(options, factory, sink))
        {
            await pipeline.StartAsync();
            DateTimeOffset origin = new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero);
            FinalizedSourceCandleEvent? first = null;
            for (int index = 0; index < 20; index++)
            {
                FinalizedSourceCandleEvent sourceEvent = CreateEvent(265, origin.AddMinutes(index * 5), index);
                if (index == 0)
                {
                    first = sourceEvent;
                }

                MarketDataEnqueueStatus status = await pipeline.EnqueueAsync(sourceEvent, CancellationToken.None);
                this.Assert(status == MarketDataEnqueueStatus.Accepted,
                    "Ordered source event is accepted.");
            }

            if (first == null)
            {
                throw new InvalidOperationException("Synthetic ordering fixture produced no first event.");
            }

            this.Assert(pipeline.TryEnqueue(first) == MarketDataEnqueueStatus.Duplicate,
                "A repeated provider identity is deduplicated at ingress.");
            await pipeline.StopAsync(true);
            IReadOnlyList<DateTimeOffset> observed = factory.GetObserved(265);
            this.Assert(observed.Count == 20, "The duplicate event did not reach the processor.");
            for (int index = 1; index < observed.Count; index++)
            {
                this.Assert(observed[index] > observed[index - 1],
                    "A single instrument retains source order.");
            }
        }
    }

    private async Task VerifyBoundedCapacityAsync()
    {
        RecordingProcessorFactory factory = new RecordingProcessorFactory();
        BlockingSink sink = new BlockingSink();
        MarketDataPipelineOptions options = new MarketDataPipelineOptions(1, 1, 1, TimeSpan.Zero);
        await using (BoundedMarketDataPipeline pipeline = new BoundedMarketDataPipeline(options, factory, sink))
        {
            await pipeline.StartAsync();
            DateTimeOffset origin = new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero);
            this.Assert(pipeline.TryEnqueue(CreateEvent(265, origin, 1)) == MarketDataEnqueueStatus.Accepted,
                "The first bounded event is accepted.");
            await sink.Entered;
            this.Assert(pipeline.TryEnqueue(CreateEvent(265, origin.AddMinutes(5), 2))
                == MarketDataEnqueueStatus.Accepted, "The shard queue accepts work up to capacity.");
            this.Assert(pipeline.TryEnqueue(CreateEvent(265, origin.AddMinutes(10), 3))
                == MarketDataEnqueueStatus.QueueFull,
                "TryEnqueue reports full capacity without evicting accepted work.");
            sink.Release();
            await pipeline.StopAsync(true);
            this.Assert(pipeline.GetMetrics().ProcessedEvents == 2,
                "Work accepted before back-pressure drains safely.");
        }
    }

    private async Task VerifyGracefulAndImmediateShutdownAsync()
    {
        RecordingProcessorFactory gracefulFactory = new RecordingProcessorFactory();
        CountingSink gracefulSink = new CountingSink();
        MarketDataPipelineOptions options = new MarketDataPipelineOptions(16, 2, 1, TimeSpan.Zero);
        await using (BoundedMarketDataPipeline graceful = new BoundedMarketDataPipeline(options, gracefulFactory,
            gracefulSink))
        {
            await graceful.StartAsync();
            DateTimeOffset origin = new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero);
            for (int index = 0; index < 10; index++)
            {
                graceful.TryEnqueue(CreateEvent(265 + index, origin, index));
            }

            await graceful.StopAsync(true);
            MarketDataPipelineMetricsSnapshot metrics = graceful.GetMetrics();
            this.Assert(metrics.AcceptedEvents == metrics.ProcessedEvents && metrics.QueueDepth == 0,
                "Graceful shutdown drains every accepted event.");
        }

        RecordingProcessorFactory immediateFactory = new RecordingProcessorFactory();
        BlockingSink immediateSink = new BlockingSink();
        await using (BoundedMarketDataPipeline immediate = new BoundedMarketDataPipeline(options, immediateFactory,
            immediateSink))
        {
            await immediate.StartAsync();
            DateTimeOffset origin = new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero);
            for (int index = 0; index < 8; index++)
            {
                immediate.TryEnqueue(CreateEvent(265, origin.AddMinutes(index * 5), index));
            }

            await immediateSink.Entered;
            await immediate.StopAsync(false);
            MarketDataPipelineMetricsSnapshot metrics = immediate.GetMetrics();
            this.Assert(metrics.UnprocessedOnShutdown > 0 && metrics.DroppedEvents > 0,
                "Immediate shutdown reports every safely unprocessed accepted item.");
            immediateSink.Release();
        }
    }

    private async Task VerifyFailureIsolationAndRetryMetricsAsync()
    {
        RecordingProcessorFactory factory = new RecordingProcessorFactory();
        RetryThenCountSink retrySink = new RetryThenCountSink();
        MarketDataPipelineOptions retryOptions = new MarketDataPipelineOptions(4, 1, 2, TimeSpan.Zero);
        await using (BoundedMarketDataPipeline retryPipeline = new BoundedMarketDataPipeline(retryOptions, factory,
            retrySink))
        {
            await retryPipeline.StartAsync();
            retryPipeline.TryEnqueue(CreateEvent(265,
                new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero), 1));
            await retryPipeline.StopAsync(true);
            MarketDataPipelineMetricsSnapshot metrics = retryPipeline.GetMetrics();
            this.Assert(metrics.RetryCount == 1 && metrics.ProcessedEvents == 1 && metrics.FailureCount == 0,
                "A transient sink failure is retried and measured.");
        }

        RecordingProcessorFactory failureFactory = new RecordingProcessorFactory();
        AlwaysFailSink failureSink = new AlwaysFailSink();
        MarketDataPipelineOptions failureOptions = new MarketDataPipelineOptions(4, 1, 1, TimeSpan.Zero);
        await using (BoundedMarketDataPipeline failurePipeline = new BoundedMarketDataPipeline(failureOptions,
            failureFactory, failureSink))
        {
            await failurePipeline.StartAsync();
            DateTimeOffset origin = new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero);
            failurePipeline.TryEnqueue(CreateEvent(265, origin, 1));
            failurePipeline.TryEnqueue(CreateEvent(265, origin.AddMinutes(5), 2));
            await failurePipeline.StopAsync(true);
            MarketDataPipelineMetricsSnapshot metrics = failurePipeline.GetMetrics();
            this.Assert(metrics.FailureCount == 2 && failurePipeline.GetFailures().Count == 2,
                "One consumer failure is observable and does not kill the worker before later work.");
        }
    }

    private async Task VerifyDeduplicationHistoryIsBoundedAsync()
    {
        RecordingProcessorFactory factory = new RecordingProcessorFactory();
        CountingSink sink = new CountingSink();
        MarketDataPipelineOptions options = new MarketDataPipelineOptions(4, 1, 1, TimeSpan.Zero, 2);
        await using BoundedMarketDataPipeline pipeline = new BoundedMarketDataPipeline(options, factory, sink);
        await pipeline.StartAsync();
        DateTimeOffset origin = new DateTimeOffset(2026, 9, 28, 3, 45, 0, TimeSpan.Zero);
        FinalizedSourceCandleEvent first = CreateEvent(265, origin, 1);
        for (int index = 0; index < 3; index++)
        {
            this.Assert(pipeline.TryEnqueue(CreateEvent(265, origin.AddMinutes(index * 5), index + 1))
                == MarketDataEnqueueStatus.Accepted, "A unique identity is accepted.");
            await WaitForProcessedAsync(pipeline, index + 1);
        }

        this.Assert(pipeline.TryEnqueue(first) == MarketDataEnqueueStatus.Accepted,
            "An identity evicted from bounded deduplication history can be replayed.");
        await pipeline.StopAsync(true);
        this.Assert(pipeline.GetMetrics().ProcessedEvents == 4,
            "The replay accepted after bounded-history eviction is processed.");
    }

    private static async Task WaitForProcessedAsync(BoundedMarketDataPipeline pipeline, long expected)
    {
        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (pipeline.GetMetrics().ProcessedEvents < expected)
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private static FinalizedSourceCandleEvent CreateEvent(int instrumentToken, DateTimeOffset openedAt,
        int sequence)
    {
        DateTimeOffset closedAt = openedAt.AddMinutes(5);
        decimal price = 100m + sequence;
        Candle candle = new Candle(instrumentToken, "5minute", MarketTimestamp.ToDatabase(openedAt), price,
            price + 1m, price - 1m, price + 0.5m, 100);
        return new FinalizedSourceCandleEvent(HistoricalStreamTypes.IntradayFiveMinute,
            new CompletedCandle(candle, closedAt), closedAt, true);
    }

    private void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        this.assertions++;
    }

    private sealed class RecordingProcessorFactory : IInstrumentCandleProcessorFactory
    {
        private readonly ConcurrentDictionary<int, List<DateTimeOffset>> observed;

        public RecordingProcessorFactory()
        {
            this.observed = new ConcurrentDictionary<int, List<DateTimeOffset>>();
        }

        public int InstrumentCount
        {
            get
            {
                return this.observed.Count;
            }
        }

        public IClosedCandleEventProcessor Create(int instrumentToken)
        {
            List<DateTimeOffset> values = new List<DateTimeOffset>();
            if (!this.observed.TryAdd(instrumentToken, values))
            {
                values = this.observed[instrumentToken];
            }

            return new RecordingProcessor(values);
        }

        public IReadOnlyList<DateTimeOffset> GetObserved(int instrumentToken)
        {
            List<DateTimeOffset> values = this.observed[instrumentToken];
            lock (values)
            {
                return new List<DateTimeOffset>(values).AsReadOnly();
            }
        }
    }

    private sealed class RecordingProcessor : IClosedCandleEventProcessor
    {
        private readonly List<DateTimeOffset> observed;

        public RecordingProcessor(List<DateTimeOffset> observed)
        {
            this.observed = observed;
        }

        public ClosedCandleProcessingResult Process(FinalizedSourceCandleEvent sourceEvent)
        {
            lock (this.observed)
            {
                this.observed.Add(sourceEvent.Candle.Candle.OpenedAt);
            }

            IReadOnlyList<CompletedTargetCandle> outputs = new List<CompletedTargetCandle>().AsReadOnly();
            return new ClosedCandleProcessingResult(ClosedCandleProcessingStatus.Accepted, "synthetic", "test-r1",
                outputs);
        }
    }

    private sealed class CountingSink : IClosedCandleProcessingSink
    {
        private int count;

        public int Count
        {
            get
            {
                return Volatile.Read(ref this.count);
            }
        }

        public Task HandleAsync(ClosedCandleProcessingResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref this.count);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSink : IClosedCandleProcessingSink
    {
        private readonly TaskCompletionSource<bool> entered;
        private readonly TaskCompletionSource<bool> released;

        public BlockingSink()
        {
            this.entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            this.released = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task Entered
        {
            get
            {
                return this.entered.Task;
            }
        }

        public async Task HandleAsync(ClosedCandleProcessingResult result, CancellationToken cancellationToken)
        {
            this.entered.TrySetResult(true);
            await this.released.Task.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            this.released.TrySetResult(true);
        }
    }

    private sealed class RetryThenCountSink : IClosedCandleProcessingSink
    {
        private int attempts;

        public Task HandleAsync(ClosedCandleProcessingResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int attempt = Interlocked.Increment(ref this.attempts);
            if (attempt == 1)
            {
                throw new InvalidOperationException("Synthetic transient sink failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailSink : IClosedCandleProcessingSink
    {
        public Task HandleAsync(ClosedCandleProcessingResult result, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Synthetic terminal sink failure.");
        }
    }
}
