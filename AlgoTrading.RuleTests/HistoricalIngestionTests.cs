using AlgoTrading.Models.MarketData.Ingestion;

internal sealed class HistoricalIngestionTests
{
    private int assertions;

    public async Task RunAsync()
    {
        await this.FreshRunStartsAtRequestedRangeStartAsync();
        await this.RestartResumesAtFirstUncompletedChunkAsync();
        await this.FailedUploadDoesNotAdvanceAsync();
        await this.FailedDownloadDoesNotAdvanceAsync();
        await this.CompletedReplayIsIdempotentAsync();
        await this.BudgetExpirationPreservesResumableStateAsync();
        await this.InstrumentsAndStreamsRemainIndependentAsync();
        await this.PrefixAndFullReplayRemainEquivalentAsync();
        await this.CorrectionRefreshBypassesCheckpointAsync();
        Console.WriteLine("Passed " + this.assertions + " historical ingestion checkpoint assertions.");
    }

    private async Task FreshRunStartsAtRequestedRangeStartAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest request = CreateRequest(new DateTime(2025, 1, 10), new DateTime(2025, 1, 20),
            new string[] { HistoricalStreamTypes.IntradayFiveMinute }, TimeSpan.FromMinutes(10), false);

        await coordinator.RunAsync(new int[] { 101 }, request, CancellationToken.None);

        this.Assert(source.Requests.Count == 1, "A short fresh range is downloaded once.");
        this.Assert(source.Requests[0].RangeStart == new DateTime(2025, 1, 10),
            "A fresh run starts at the requested range start.");
    }

    private async Task RestartResumesAtFirstUncompletedChunkAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        repository.SeedCheckpoint(new HistoricalIngestionCheckpoint(101, HistoricalStreamTypes.IntradayFiveMinute,
            new DateTime(2025, 1, 1), new DateTime(2025, 3, 21)));
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest request = CreateRequest(new DateTime(2025, 1, 1), new DateTime(2025, 4, 1),
            new string[] { HistoricalStreamTypes.IntradayFiveMinute }, TimeSpan.FromMinutes(10), false);

        await coordinator.RunAsync(new int[] { 101 }, request, CancellationToken.None);

        this.Assert(source.Requests.Count == 1, "A resumed run downloads only the remaining chunk.");
        this.Assert(source.Requests[0].RangeStart == new DateTime(2025, 3, 22),
            "Restart resumes on the day after the last completed chunk.");
    }

    private async Task FailedUploadDoesNotAdvanceAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(DateTime.UtcNow);
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        repository.FailNextPersistence = true;
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest request = CreateRequest(new DateTime(2025, 1, 1), new DateTime(2025, 1, 2),
            new string[] { HistoricalStreamTypes.IntradayFiveMinute }, TimeSpan.FromMinutes(10), false);

        bool failed = false;
        try
        {
            await coordinator.RunAsync(new int[] { 101 }, request, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }

        this.Assert(failed, "A persistence failure is reported.");
        this.Assert(repository.GetSeededCheckpoint(101, HistoricalStreamTypes.IntradayFiveMinute) == null,
            "A failed candle upload does not advance durable state.");
    }

    private async Task FailedDownloadDoesNotAdvanceAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(DateTime.UtcNow);
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        source.FailNextDownload = true;
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest request = CreateRequest(new DateTime(2025, 1, 1), new DateTime(2025, 1, 2),
            new string[] { HistoricalStreamTypes.IntradayFiveMinute }, TimeSpan.FromMinutes(10), false);

        bool failed = false;
        try
        {
            await coordinator.RunAsync(new int[] { 101 }, request, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            failed = true;
        }

        this.Assert(failed, "A download failure is reported.");
        this.Assert(repository.PersistAttempts == 0, "A failed download never calls persistence.");
        this.Assert(repository.GetSeededCheckpoint(101, HistoricalStreamTypes.IntradayFiveMinute) == null,
            "A failed download does not advance durable state.");
    }

    private async Task CompletedReplayIsIdempotentAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(DateTime.UtcNow);
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest request = CreateRequest(new DateTime(2025, 1, 1), new DateTime(2025, 1, 5),
            new string[] { HistoricalStreamTypes.IntradayFiveMinute }, TimeSpan.FromMinutes(10), false);

        await coordinator.RunAsync(new int[] { 101 }, request, CancellationToken.None);
        int persistedIdentityCount = repository.PersistedIdentities.Count;
        await coordinator.RunAsync(new int[] { 101 }, request, CancellationToken.None);

        this.Assert(source.Requests.Count == 1, "An ordinary retry does not download a completed range again.");
        this.Assert(repository.PersistedIdentities.Count == persistedIdentityCount,
            "A retry creates no duplicate persisted candle identities.");
    }

    private async Task BudgetExpirationPreservesResumableStateAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        source.AdvancePerDownload = TimeSpan.FromSeconds(40);
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest request = CreateRequest(new DateTime(2025, 1, 1), new DateTime(2025, 7, 19),
            new string[] { HistoricalStreamTypes.IntradayFiveMinute }, TimeSpan.FromMinutes(1), false);

        HistoricalBackfillReport report = await coordinator.RunAsync(new int[] { 101 }, request, CancellationToken.None);
        HistoricalIngestionCheckpoint? checkpoint = repository.GetSeededCheckpoint(101,
            HistoricalStreamTypes.IntradayFiveMinute);

        this.Assert(report.BudgetExhausted, "Budget expiration is explicit in the report.");
        this.Assert(checkpoint != null && checkpoint.ChunkEnd == new DateTime(2025, 6, 9),
            "Budget expiration retains the last fully persisted chunk.");
    }

    private async Task InstrumentsAndStreamsRemainIndependentAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(DateTime.UtcNow);
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest request = CreateRequest(new DateTime(2025, 1, 1), new DateTime(2025, 1, 2),
            new string[] { HistoricalStreamTypes.IntradayFiveMinute, HistoricalStreamTypes.DailyOneDay },
            TimeSpan.FromMinutes(10), false);

        await coordinator.RunAsync(new int[] { 101, 202 }, request, CancellationToken.None);

        this.Assert(repository.CheckpointCount == 4,
            "Two instruments and both source streams maintain four independent checkpoints.");
    }

    private async Task PrefixAndFullReplayRemainEquivalentAsync()
    {
        DateTime start = new DateTime(2025, 1, 1);
        DateTime prefixEnd = new DateTime(2025, 3, 21);
        DateTime fullEnd = new DateTime(2025, 6, 9);
        string[] streams = new string[] { HistoricalStreamTypes.IntradayFiveMinute };

        FakeHistoricalClock resumedClock = new FakeHistoricalClock(DateTime.UtcNow);
        FakeHistoricalSource resumedSource = new FakeHistoricalSource(resumedClock);
        FakeHistoricalRepository resumedRepository = new FakeHistoricalRepository();
        HistoricalBackfillCoordinator resumed = new HistoricalBackfillCoordinator(resumedSource, resumedRepository,
            resumedClock);
        await resumed.RunAsync(new int[] { 101 }, CreateRequest(start, prefixEnd, streams, TimeSpan.FromMinutes(10),
            false), CancellationToken.None);
        await resumed.RunAsync(new int[] { 101 }, CreateRequest(start, fullEnd, streams, TimeSpan.FromMinutes(10),
            false), CancellationToken.None);

        FakeHistoricalClock fullClock = new FakeHistoricalClock(DateTime.UtcNow);
        FakeHistoricalSource fullSource = new FakeHistoricalSource(fullClock);
        FakeHistoricalRepository fullRepository = new FakeHistoricalRepository();
        HistoricalBackfillCoordinator full = new HistoricalBackfillCoordinator(fullSource, fullRepository, fullClock);
        await full.RunAsync(new int[] { 101 }, CreateRequest(start, fullEnd, streams, TimeSpan.FromMinutes(10), false),
            CancellationToken.None);

        this.Assert(resumedRepository.PersistedIdentities.SetEquals(fullRepository.PersistedIdentities),
            "Prefix ingestion followed by resume has the same persisted identities as full-history ingestion.");
    }

    private async Task CorrectionRefreshBypassesCheckpointAsync()
    {
        FakeHistoricalClock clock = new FakeHistoricalClock(DateTime.UtcNow);
        FakeHistoricalSource source = new FakeHistoricalSource(clock);
        FakeHistoricalRepository repository = new FakeHistoricalRepository();
        repository.SeedCheckpoint(new HistoricalIngestionCheckpoint(101, HistoricalStreamTypes.DailyOneDay,
            new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)));
        HistoricalBackfillCoordinator coordinator = new HistoricalBackfillCoordinator(source, repository, clock);
        HistoricalBackfillRequest correction = CreateRequest(new DateTime(2025, 5, 1), new DateTime(2025, 5, 2),
            new string[] { HistoricalStreamTypes.DailyOneDay }, TimeSpan.FromMinutes(10), true);

        await coordinator.RunAsync(new int[] { 101 }, correction, CancellationToken.None);

        HistoricalIngestionCheckpoint? checkpoint = repository.GetSeededCheckpoint(101,
            HistoricalStreamTypes.DailyOneDay);
        this.Assert(source.Requests.Count == 1 && source.Requests[0].RangeStart == new DateTime(2025, 5, 1),
            "An explicit correction refresh bypasses an old checkpoint.");
        this.Assert(checkpoint != null && checkpoint.ChunkEnd == new DateTime(2025, 12, 31),
            "A correction refresh never regresses the normal continuation checkpoint.");
    }

    private static HistoricalBackfillRequest CreateRequest(DateTime start, DateTime end, string[] streams,
        TimeSpan budget, bool refreshCorrections)
    {
        return new HistoricalBackfillRequest(start, end, start, end, streams, budget, refreshCorrections);
    }

    private void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        this.assertions++;
    }

    private sealed class FakeHistoricalClock : IHistoricalBackfillClock
    {
        private DateTime utcNow;

        public FakeHistoricalClock(DateTime utcNow)
        {
            this.utcNow = utcNow;
        }

        public DateTime UtcNow
        {
            get
            {
                return this.utcNow;
            }
        }

        public void Advance(TimeSpan duration)
        {
            this.utcNow = this.utcNow.Add(duration);
        }
    }

    private sealed class FakeHistoricalSource : IHistoricalChunkSource
    {
        private readonly FakeHistoricalClock clock;

        public FakeHistoricalSource(FakeHistoricalClock clock)
        {
            this.clock = clock;
            this.Requests = new List<HistoricalChunkRequest>();
            this.AdvancePerDownload = TimeSpan.Zero;
        }

        public List<HistoricalChunkRequest> Requests { get; }

        public bool FailNextDownload { get; set; }

        public TimeSpan AdvancePerDownload { get; set; }

        public Task<string> DownloadChunkAsync(HistoricalChunkRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.Requests.Add(request);
            this.clock.Advance(this.AdvancePerDownload);
            if (this.FailNextDownload)
            {
                this.FailNextDownload = false;
                throw new HttpRequestException("Synthetic download failure.");
            }

            return Task.FromResult("synthetic-provider-payload");
        }
    }

    private sealed class FakeHistoricalRepository : IHistoricalIngestionRepository
    {
        private readonly Dictionary<string, HistoricalIngestionCheckpoint> checkpoints;

        public FakeHistoricalRepository()
        {
            this.checkpoints = new Dictionary<string, HistoricalIngestionCheckpoint>(StringComparer.Ordinal);
            this.PersistedIdentities = new HashSet<string>(StringComparer.Ordinal);
        }

        public bool FailNextPersistence { get; set; }

        public int PersistAttempts { get; private set; }

        public int CheckpointCount
        {
            get
            {
                return this.checkpoints.Count;
            }
        }

        public HashSet<string> PersistedIdentities { get; }

        public Task<HistoricalIngestionCheckpoint?> GetCheckpointAsync(long instrumentToken, string streamType,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HistoricalIngestionCheckpoint? checkpoint = this.GetSeededCheckpoint(instrumentToken, streamType);
            return Task.FromResult(checkpoint);
        }

        public Task PersistChunkAndCheckpointAsync(HistoricalChunkRequest request, string providerPayload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            this.PersistAttempts++;
            if (this.FailNextPersistence)
            {
                this.FailNextPersistence = false;
                throw new InvalidOperationException("Synthetic persistence failure.");
            }

            DateTime date = request.RangeStart.Date;
            while (date <= request.RangeEnd.Date)
            {
                this.PersistedIdentities.Add(Key(request.InstrumentToken, request.StreamType) + "/"
                    + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                date = date.AddDays(1.0);
            }

            string key = Key(request.InstrumentToken, request.StreamType);
            HistoricalIngestionCheckpoint? current = this.GetSeededCheckpoint(request.InstrumentToken,
                request.StreamType);
            if (current == null || current.ChunkEnd < request.RangeEnd)
            {
                this.checkpoints[key] = new HistoricalIngestionCheckpoint(request.InstrumentToken,
                    request.StreamType, request.RangeStart, request.RangeEnd);
            }

            return Task.CompletedTask;
        }

        public void SeedCheckpoint(HistoricalIngestionCheckpoint checkpoint)
        {
            this.checkpoints[Key(checkpoint.InstrumentToken, checkpoint.StreamType)] = checkpoint;
        }

        public HistoricalIngestionCheckpoint? GetSeededCheckpoint(long instrumentToken, string streamType)
        {
            HistoricalIngestionCheckpoint? checkpoint;
            if (this.checkpoints.TryGetValue(Key(instrumentToken, streamType), out checkpoint))
            {
                return checkpoint;
            }

            return null;
        }

        private static string Key(long instrumentToken, string streamType)
        {
            return instrumentToken.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + streamType;
        }
    }
}
