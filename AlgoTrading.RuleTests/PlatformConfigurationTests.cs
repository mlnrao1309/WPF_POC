using AlgoTrading.DataAccess.Infrastructure;
using AlgoTrading.Models;
using AlgoTrading.Models.Configuration;
using AlgoTrading.Models.MarketData.Entities;

internal sealed class PlatformConfigurationTests
{
    private int assertions;

    public async Task RunAsync()
    {
        await this.ActiveConfigurationLoadsIntoRuntimeAsync();
        this.DisabledTimeframeSuppressesDependentRule();
        this.DisabledCalculatorProducesNoActiveRule();
        this.InvalidActiveConfigurationFailsBeforeActivation();
        this.EmptyConfigurationFailsDeterministically();
        this.InvalidSourceAndJsonFailClearly();
        Console.WriteLine("Passed " + this.assertions + " platform configuration assertions.");
    }

    private async Task ActiveConfigurationLoadsIntoRuntimeAsync()
    {
        PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
        PlatformConfigurationSnapshot snapshot = validator.Activate(CreateTimeframes(true), CreateRules(true));
        FakePlatformConfigurationProvider provider = new FakePlatformConfigurationProvider(snapshot);
        PlatformConfigurationRuntime runtime = new PlatformConfigurationRuntime();

        await runtime.InitializeAsync(provider, CancellationToken.None);

        this.Assert(runtime.IsInitialized, "Validated configuration is loaded into the runtime boundary.");
        this.Assert(runtime.Current.Timeframes.ActiveCodes.Contains("15m"),
            "An active timeframe is available to downstream aggregation.");
        this.Assert(runtime.Current.CriticalLevelRules.Count == 2,
            "Active calculator rules are exposed to downstream calculation.");
        this.Assert(runtime.Current.ConfigurationRevision.Length == 64,
            "The activated configuration has a stable SHA-256 revision.");
    }

    private void DisabledTimeframeSuppressesDependentRule()
    {
        PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
        PlatformConfigurationSnapshot snapshot = validator.Activate(CreateTimeframes(false), CreateRules(true));

        this.Assert(!snapshot.Timeframes.ActiveCodes.Contains("15m"),
            "A disabled timeframe is absent from active aggregation targets.");
        this.Assert(snapshot.CriticalLevelRules.Count == 1
            && snapshot.CriticalLevelRules[0].MethodCode == CalculatorMethodCodes.StandardPivot,
            "A rule using a disabled applied timeframe is not activated.");
    }

    private void DisabledCalculatorProducesNoActiveRule()
    {
        List<ConfigCriticalLevel> rules = CreateRules(false);
        PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
        PlatformConfigurationSnapshot snapshot = validator.Activate(CreateTimeframes(true), rules);

        this.Assert(snapshot.CriticalLevelRules.Count == 1,
            "A disabled calculator rule produces no active output instruction.");
    }

    private void InvalidActiveConfigurationFailsBeforeActivation()
    {
        List<ConfigCriticalLevel> rules = CreateRules(true);
        rules[1].ParametersJson = "{\"fastLength\":5,\"slowLength\":5,\"function\":\"EMA\"}";
        bool failed = false;
        try
        {
            PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
            validator.Activate(CreateTimeframes(true), rules);
        }
        catch (PlatformConfigurationException exception)
        {
            failed = exception.Message.Contains("fastLength", StringComparison.Ordinal);
        }

        this.Assert(failed, "Invalid active EMA parameters fail before runtime activation.");
    }

    private void EmptyConfigurationFailsDeterministically()
    {
        bool failed = false;
        try
        {
            PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
            validator.Activate(new List<ConfigTimeframe>(), new List<ConfigCriticalLevel>());
        }
        catch (PlatformConfigurationException exception)
        {
            failed = exception.Message.Contains("Config_Timeframes is empty", StringComparison.Ordinal);
        }

        this.Assert(failed, "An empty timeframe table never creates an accidental live default pipeline.");
    }

    private void InvalidSourceAndJsonFailClearly()
    {
        List<ConfigTimeframe> invalidTimeframes = CreateTimeframes(true);
        invalidTimeframes[0].SourceStream = "UNKNOWN";
        bool sourceFailed = false;
        try
        {
            PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
            validator.Activate(invalidTimeframes, CreateRules(true));
        }
        catch (PlatformConfigurationException exception)
        {
            sourceFailed = exception.Message.Contains("unsupported source stream", StringComparison.Ordinal);
        }

        this.Assert(sourceFailed, "An unknown source stream is rejected instead of being treated as intraday.");

        List<ConfigCriticalLevel> invalidRules = CreateRules(true);
        invalidRules[0].ParametersJson = "not-json";
        bool jsonFailed = false;
        try
        {
            PlatformConfigurationValidator validator = new PlatformConfigurationValidator();
            validator.Activate(CreateTimeframes(true), invalidRules);
        }
        catch (PlatformConfigurationException exception)
        {
            jsonFailed = exception.Message.Contains("invalid ParametersJson", StringComparison.Ordinal);
        }

        this.Assert(jsonFailed, "Invalid calculator JSON fails before runtime activation.");
    }

    private static List<ConfigTimeframe> CreateTimeframes(bool fifteenMinuteActive)
    {
        List<ConfigTimeframe> rows = new List<ConfigTimeframe>();
        rows.Add(CreateTimeframe(1, "5m", 5, "INTRADAY_5M", true));
        rows.Add(CreateTimeframe(2, "15m", 15, "INTRADAY_5M", fifteenMinuteActive));
        rows.Add(CreateTimeframe(3, "1D", 0, "DAILY_1D", true));
        return rows;
    }

    private static ConfigTimeframe CreateTimeframe(int id, string code, int minutes, string sourceStream,
        bool isActive)
    {
        ConfigTimeframe row = new ConfigTimeframe();
        row.Id = id;
        row.TimeframeCode = code;
        row.MinutesMultiplier = minutes;
        row.SourceStream = sourceStream;
        row.IsActive = isActive;
        return row;
    }

    private static List<ConfigCriticalLevel> CreateRules(bool emaActive)
    {
        List<ConfigCriticalLevel> rows = new List<ConfigCriticalLevel>();
        ConfigCriticalLevel pivot = new ConfigCriticalLevel();
        pivot.Id = 1;
        pivot.MethodCode = CalculatorMethodCodes.StandardPivot;
        pivot.AppliedTimeframe = "1D";
        pivot.ReferenceTimeframe = "1D";
        pivot.ParametersJson = "{\"type\":\"SQL_RANGE_EXTENSION_V1\"}";
        pivot.MinimumBarsRequired = 2;
        pivot.IsActive = true;
        rows.Add(pivot);

        ConfigCriticalLevel ema = new ConfigCriticalLevel();
        ema.Id = 2;
        ema.MethodCode = CalculatorMethodCodes.EmaCrossover;
        ema.AppliedTimeframe = "15m";
        ema.ReferenceTimeframe = null;
        ema.ParametersJson = "{\"fastLength\":5,\"slowLength\":20,\"function\":\"EMA\"}";
        ema.MinimumBarsRequired = 21;
        ema.IsActive = emaActive;
        rows.Add(ema);
        return rows;
    }

    private void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        this.assertions++;
    }

    private sealed class FakePlatformConfigurationProvider : IPlatformConfigurationProvider
    {
        private readonly PlatformConfigurationSnapshot snapshot;

        public FakePlatformConfigurationProvider(PlatformConfigurationSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public Task<TimeframeCatalog> GetTimeframeCatalogAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.snapshot.Timeframes);
        }

        public Task<IReadOnlyList<ConfigCriticalLevel>> GetCriticalLevelRulesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ConfigCriticalLevel> empty = new List<ConfigCriticalLevel>().AsReadOnly();
            return Task.FromResult(empty);
        }

        public Task<PlatformConfigurationSnapshot> LoadActiveConfigurationAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(this.snapshot);
        }
    }
}
