using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Entities;
using AlgoTrading.Models.MarketData.Ingestion;
using AlgoTrading.Repositories;
using AlgoTrading.Services;
using Microsoft.Extensions.DependencyInjection;
using Stage1Scanner.Core.Market;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace AlgoTrading.ViewModels;

public sealed class MarketWatchViewModel : BaseViewModel
{
    private readonly Models.AppSettings _appSettings;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMarketWatchDataStream _dataStream;
    private readonly IHistoricalDataWorkflow _historicalDataWorkflow;
    private readonly IHistoricalPollingCoordinator _historicalPollingCoordinator;
    private readonly object _pollingGate = new();
    private CancellationTokenSource? _pollingCancellation;
    private Task _pollingTask = Task.CompletedTask;
    private bool _shutdownRequested;
    public MarketWatchViewModel(
        Models.AppSettings appSettings,
        IServiceScopeFactory scopeFactory,
        IAccessTokenProvider accessTokenProvider,
        IMarketWatchDataStream dataStream,
        IHistoricalDataWorkflow historicalDataWorkflow,
        IHistoricalPollingCoordinator historicalPollingCoordinator)
    : base(accessTokenProvider)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));

        _dataStream = dataStream ?? throw new ArgumentNullException(nameof(dataStream));
        _historicalDataWorkflow = historicalDataWorkflow
            ?? throw new ArgumentNullException(nameof(historicalDataWorkflow));
        _historicalPollingCoordinator = historicalPollingCoordinator
            ?? throw new ArgumentNullException(nameof(historicalPollingCoordinator));
        _dataStream.DataReceived += OnMarketWatchDataReceived;
        if (Application.Current != null)
            Application.Current.Exit += (_, _) => _pollingCancellation?.Cancel();

        MarketWatchNames = new ObservableCollection<MarketWatchNameItem>();
        Instruments = new ObservableCollection<MarketWatchInstrumentItem>();
        HistoryCandleIntervals = new ObservableCollection<HistoryCandleIntervalOption>();
        SelectedHistoryCandleIntervals = new ObservableCollection<string>();
        foreach (string interval in HistoryCandleInterval.AllIntervals)
        {
            var option = new HistoryCandleIntervalOption(interval);
            option.PropertyChanged += OnHistoryCandleIntervalChanged;
            HistoryCandleIntervals.Add(option);
        }

        DateTime marketNow = MarketTimestamp.ToDatabase(DateTimeOffset.UtcNow);
        HistoricalFromDate = marketNow.AddYears(-1).Date;
        HistoricalFromTime = "09:15";
        HistoricalToDate = marketNow.Date;
        HistoricalToTime = "15:45";

        PollCommand = new AsyncRelayCommand(ExecutePollAsync);
        StopPollingCommand = new AsyncRelayCommand(ExecuteStopPollingAsync);
        FetchHistoricalCommand = new AsyncRelayCommand(ExecuteFetchHistoricalAsync);
        ActiveInactiveCommand = new AsyncRelayCommand(ExecuteActiveInactiveAsync);
        CloseCommand = new RelayCommand(ExecuteClose);
    }

    private async void OnMarketWatchDataReceived(object? sender, MarketWatchPayload payload)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || !dispatcher.CheckAccess())
        {
            if (dispatcher is not null)
            {
                await dispatcher.InvokeAsync(() => OnMarketWatchDataReceived(sender, payload));
            }
            return;
        }

        try
        {
            using JsonDocument json = JsonDocument.Parse(payload.Body);
            JsonElement data = json.RootElement.GetProperty("data");
            int watchListNumber = data.GetProperty("id").GetInt32();
            string watchListName = data.GetProperty("name").GetString()
                ?? throw new InvalidDataException("MarketWatch name is missing.");
            List<MarketWatch> marketWatches = [];

            foreach (JsonElement item in data.GetProperty("groups")[0].GetProperty("items").EnumerateArray())
            {
                marketWatches.Add(new MarketWatch
                {
                    WatchId = watchListNumber,
                    Name = watchListName,
                    InstrumentToken = item.GetProperty("instrument_token").GetInt64(),
                    TradingSymbol = item.GetProperty("tradingsymbol").GetString(),
                    Segment = item.GetProperty("segment").GetString(),
                    Exchange = item.GetProperty("exchange").GetString(),
                    FullId = item.GetProperty("id").GetString(),
                    ExpiresOn = item.TryGetProperty("expiry", out var expiry)
    && expiry.ValueKind == JsonValueKind.String
    && expiry.TryGetDateTime(out var date)
        ? date
        : (DateTime?)null
                });
            }

            using IServiceScope scope = _scopeFactory.CreateScope();
            IMarketWatchRepository repository = scope.ServiceProvider.GetRequiredService<IMarketWatchRepository>();
            await repository.InsertBatchIfNotExistsAsync(marketWatches, default);
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
    }

    // ------------------------------------------------------------------
    // Collections
    // ------------------------------------------------------------------
    public ObservableCollection<MarketWatchNameItem> MarketWatchNames { get; }
    public ObservableCollection<MarketWatchInstrumentItem> Instruments { get; }
    public ObservableCollection<HistoryCandleIntervalOption> HistoryCandleIntervals { get; }
    public ObservableCollection<string> SelectedHistoryCandleIntervals { get; }
    public IReadOnlyList<string> HistoricalTimes { get; } = Enumerable.Range(0, 391)
        .Select(minutes => new TimeOnly(9, 15).AddMinutes(minutes).ToString("HH:mm", CultureInfo.InvariantCulture))
        .ToArray();

    private DateTime? _historicalFromDate;
    public DateTime? HistoricalFromDate
    {
        get => _historicalFromDate;
        set => SetProperty(ref _historicalFromDate, value);
    }

    private string _historicalFromTime = string.Empty;
    public string HistoricalFromTime
    {
        get => _historicalFromTime;
        set => SetProperty(ref _historicalFromTime, value);
    }

    private DateTime? _historicalToDate;
    public DateTime? HistoricalToDate
    {
        get => _historicalToDate;
        set => SetProperty(ref _historicalToDate, value);
    }

    private string _historicalToTime = string.Empty;
    public string HistoricalToTime
    {
        get => _historicalToTime;
        set => SetProperty(ref _historicalToTime, value);
    }

    private void OnHistoryCandleIntervalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HistoryCandleIntervalOption.IsSelected)
            || sender is not HistoryCandleIntervalOption option)
            return;

        if (option.IsSelected)
            SelectedHistoryCandleIntervals.Add(option.Interval);
        else
            SelectedHistoryCandleIntervals.Remove(option.Interval);
    }

    // ------------------------------------------------------------------
    // Selection
    // ------------------------------------------------------------------
    private MarketWatchNameItem? _selectedMarketWatch;
    public MarketWatchNameItem? SelectedMarketWatch
    {
        get => _selectedMarketWatch;
        set
        {
            if (!SetProperty(ref _selectedMarketWatch, value)) return;
            LoadInstrumentsFor(value);
        }
    }

    private void LoadInstrumentsFor(MarketWatchNameItem? item)
    {
        Instruments.Clear();
        if (item is null) return;
        foreach (var inst in item.Instruments)
            Instruments.Add(inst);
    }

    // ------------------------------------------------------------------
    // Status
    // ------------------------------------------------------------------
    private string _pollingStatus = string.Empty;
    public string PollingStatus
    {
        get => _pollingStatus;
        set => SetProperty(ref _pollingStatus, value);
    }

    private string _errorMessage = string.Empty;
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    private double _historicalProgress;
    public double HistoricalProgress
    {
        get => _historicalProgress;
        set => SetProperty(ref _historicalProgress, value);
    }

    private string _historicalExecutionStatus = string.Empty;
    public string HistoricalExecutionStatus
    {
        get => _historicalExecutionStatus;
        set => SetProperty(ref _historicalExecutionStatus, value);
    }

    // ------------------------------------------------------------------
    // Commands
    // ------------------------------------------------------------------
    public ICommand PollCommand { get; }
    public ICommand StopPollingCommand { get; }
    public ICommand FetchHistoricalCommand { get; }
    public ICommand ActiveInactiveCommand { get; }
    public ICommand CloseCommand { get; }

    // ------------------------------------------------------------------
    // Initial load
    // ------------------------------------------------------------------
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            ErrorMessage = string.Empty;

            // Scoped repository → fresh DbContext per call.
            using IServiceScope scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IMarketWatchRepository>();

            var groups = await repo.GetGroupedByWatchAsync(ct);

            foreach (MarketWatchNameItem item in MarketWatchNames)
                item.PropertyChanged -= OnMarketWatchNameChanged;
            MarketWatchNames.Clear();
            foreach (var g in groups.OrderBy(g => g.WatchId).ThenBy(g => g.Name))
            {
                var instruments = g.Instruments
                    .Select(m => new MarketWatchInstrumentItem(m))
                    .ToList();

                var item = new MarketWatchNameItem(g.WatchId, g.Name, instruments);
                item.PropertyChanged += OnMarketWatchNameChanged;
                MarketWatchNames.Add(item);
            }

            SelectedMarketWatch = MarketWatchNames.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static void OnMarketWatchNameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MarketWatchNameItem.IsChecked)
            || sender is not MarketWatchNameItem item)
            return;

        foreach (MarketWatchInstrumentItem instrument in item.Instruments)
            instrument.IsChecked = item.IsChecked;
    }

    // ------------------------------------------------------------------
    // Command handlers — still stubs; we'll fill one at a time.
    // ------------------------------------------------------------------
    private async Task ExecutePollAsync()
    {
        CancellationTokenSource? cancellation = null;
        HistoricalExecutionStatus = string.Empty;
        try
        {
            ErrorMessage = string.Empty;
            long[] tokens = MarketWatchNames.SelectMany(x => x.Instruments).Where(x => x.IsChecked)
                .Select(x => x.InstrumentToken).Distinct().ToArray();
            string[] intervals = SelectedHistoryCandleIntervals.Distinct().ToArray();
            if (tokens.Length == 0)
                throw new InvalidOperationException("Select at least one instrument.");
            if (intervals.Length == 0)
                throw new InvalidOperationException("Select at least one historical interval.");

            lock (_pollingGate)
            {
                if (_shutdownRequested) throw new InvalidOperationException("The application is closing.");
                if (_pollingCancellation != null)
                    throw new InvalidOperationException("Historical polling is already active.");
                cancellation = new CancellationTokenSource();
                _pollingCancellation = cancellation;
            }

            PollingStatus = "Historical polling started.";
            IProgress<string> progress = new Progress<string>(message =>
            {
                PollingStatus = message;
                AppendHistoricalStatus(message);
            });
            _pollingTask = _historicalPollingCoordinator.RunAsync(tokens, intervals, progress, cancellation.Token);
            await _pollingTask;
            PollingStatus = "Historical polling completed.";
        }
        catch (OperationCanceledException)
        {
            PollingStatus = "Historical polling stopped.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            PollingStatus = "Historical polling failed.";
        }
        finally
        {
            lock (_pollingGate)
            {
                if (cancellation != null && ReferenceEquals(_pollingCancellation, cancellation))
                {
                    cancellation.Dispose();
                    _pollingCancellation = null;
                }
            }
        }
    }

    public async Task StopPollingForShutdownAsync()
    {
        Task polling;
        lock (_pollingGate)
        {
            _shutdownRequested = true;
            _pollingCancellation?.Cancel();
            polling = _pollingTask;
        }
        try { await polling; }
        catch (OperationCanceledException) { }
        catch (Exception exception) { System.Diagnostics.Trace.TraceError("Polling stopped with an error: {0}", exception); }
    }

    private Task ExecuteStopPollingAsync()
    {
        lock (_pollingGate)
            _pollingCancellation?.Cancel();
        return Task.CompletedTask;
    }

    private async Task ExecuteFetchHistoricalAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            HistoricalProgress = 0;
            AppendHistoricalStatus("Historical data fetch started.");
            long[] tokens = MarketWatchNames.SelectMany(x => x.Instruments).Where(x => x.IsChecked)
                .Select(x => x.InstrumentToken)
                .Distinct()
                .ToArray();
            string[] intervals = SelectedHistoryCandleIntervals.Distinct().ToArray();
            if (tokens.Length == 0)
                throw new InvalidOperationException("Select at least one instrument.");
            if (intervals.Length == 0)
                throw new InvalidOperationException("Select at least one historical interval.");

            DateTime from = ReadHistoricalDateTime(HistoricalFromDate, HistoricalFromTime, "From");
            DateTime to = ReadHistoricalDateTime(HistoricalToDate, HistoricalToTime, "To");
            if (from > to)
                throw new InvalidOperationException("From must not be later than To.");

            AppendHistoricalStatus("Selected instruments: " + tokens.Length + ".");
            AppendHistoricalStatus("Selected timeframes: " + intervals.Length + ".");
            IProgress<string> statusProgress = new Progress<string>(ReportHistoricalProgress);
            IProgress<HistoricalBackfillProgress> workProgress =
                new Progress<HistoricalBackfillProgress>(ReportHistoricalWorkProgress);
            HistoricalBackfillReport report = await _historicalDataWorkflow.FetchAsync(tokens, intervals, from, to,
                statusProgress.Report, workProgress);
            if (report.IsComplete)
            {
                HistoricalProgress = 100;
                AppendHistoricalStatus("Historical data fetch completed. Stored chunks: " + report.StoredChunks + ".");
            }
            else
            {
                AppendHistoricalStatus("Historical data fetch stopped before completion. Stored chunks: "
                    + report.StoredChunks + ".");
            }
             System.Diagnostics.Trace.TraceInformation(
                "Historical data pass finished: {0} chunk(s) stored; budget exhausted: {1}.",
                report.StoredChunks, report.BudgetExhausted);
        }
        catch (OperationCanceledException exception)
        {
            ErrorMessage = exception.Message;
            AppendHistoricalStatus("Historical data fetch cancelled.");
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            AppendHistoricalStatus("Historical data fetch failed: " + exception.Message);
        }
    }

    private static DateTime ReadHistoricalDateTime(DateTime? date, string timeText, string name)
    {
        if (!date.HasValue)
            throw new InvalidOperationException(name + " date is required.");
        if (!DateTime.TryParseExact(timeText, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateTime parsedTime))
            throw new InvalidOperationException(name + " time must use HH:mm.");

        return DateTime.SpecifyKind(date.Value.Date.Add(parsedTime.TimeOfDay), DateTimeKind.Unspecified);
    }

    private void ReportHistoricalProgress(string message)
    {
        System.Diagnostics.Trace.TraceInformation("Historical data: {0}", message);
        AppendHistoricalStatus(message);
    }

    private void ReportHistoricalWorkProgress(HistoricalBackfillProgress progress)
    {
        if (!progress.TotalChunks.HasValue || progress.TotalChunks.Value <= 0)
            return;

        HistoricalProgress = Math.Min(100.0,
            progress.CompletedChunks * 100.0 / progress.TotalChunks.Value);
    }

    private void AppendHistoricalStatus(string message)
    {
        HistoricalExecutionStatus += "[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
            + "] " + message + Environment.NewLine;
    }

    private Task ExecuteActiveInactiveAsync()
    {
        // TODO
        return Task.CompletedTask;
    }

    private void ExecuteClose()
    {
        // TODO
    }
}

public sealed class HistoryCandleIntervalOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public HistoryCandleIntervalOption(string interval)
    {
        Interval = interval;
    }

    public string Interval { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
