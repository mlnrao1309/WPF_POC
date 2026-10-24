using AlgoTrading.DataAccess.Calendar;
using AlgoTrading.DataAccess.Etl;
using AlgoTrading.DataAccess.Historical;
using AlgoTrading.DataAccess.Infrastructure;
using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
using AlgoTrading.DataAccess.Repositories;
using AlgoTrading.DataAccess.Runtime;
using AlgoTrading.Models;
using AlgoTrading.Repositories;
using AlgoTrading.Screener.Dsl;
using AlgoTrading.Screener.Repositories;
using AlgoTrading.Screener.Storage;
using AlgoTrading.Screener.Wpf;
using AlgoTrading.Services;
using AlgoTrading.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace AlgoTrading
{
    public partial class App : Application
    {
        public static IServiceProvider? _serviceProvider;
        public static IServiceProvider ServiceProvider { get => _serviceProvider ?? throw new InvalidOperationException("Service provider is not initialized."); private set => _serviceProvider = value; }

        private IHost? _host;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // --- 1. Configuration & Paths ---
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            IConfiguration configuration = builder.Build();

            var appSettings = new AppSettings();
            try
            {
                var cs = configuration.GetSection("ConnectionStrings");
                if (cs.Exists())
                {
                    var val = Environment.GetEnvironmentVariable("ALGOTRADING_SQL_CONNECTION_STRING") ?? cs["MsSqlDatabase"];
                    if (!string.IsNullOrEmpty(val)) appSettings.MsSqlDatabase = val;
                }
            }
            catch { }

            string? connectionString = Environment.GetEnvironmentVariable("ALGOTRADING_SQL_CONNECTION_STRING")
                ?? configuration.GetConnectionString("MsSqlDatabase");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                MessageBox.Show("A SQL connection string is required.", "Startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            // Declare folders at the method scope so startup logic can use them
            string resourcesFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlgoTrading", "Resources");
            string scannerFolder = Path.Combine(resourcesFolder, "Screener");

            // --- 2. Build the Host & DI Container ---
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(configuration);
                    services.AddSingleton(appSettings);

                    // --- Existing View & ViewModel Registrations ---
                    services.AddSingleton<AccessTokenStore>();
                    services.AddSingleton<IAccessTokenProvider>(provider => provider.GetRequiredService<AccessTokenStore>());
                    services.AddSingleton<ViewModels.WebViewViewModel>();
                    services.AddSingleton<IMarketWatchDataStream>(provider => provider.GetRequiredService<ViewModels.WebViewViewModel>());
                    services.AddSingleton(new HttpClient());

                    services.AddSingleton<HistoricalDataModel>(provider =>
                        new HistoricalDataModel(
                            provider.GetRequiredService<HttpClient>(),
                            string.Empty,
                            provider.GetRequiredService<SqlHistoricalIngestionRepository>()));

                    services.AddSingleton<IInstrumentRepository, InstrumentRepository>();
                    services.AddSingleton<IMarketWatchRepository, MarketWatchRepository>();
                    services.AddSingleton<IMarketWatchApiService, MarketWatchApiService>();
                    services.AddSingleton<IHistoricalDataWorkflow, HistoricalDataWorkflow>();
                    services.AddSingleton<IHistoricalPollingCoordinator, HistoricalPollingCoordinator>();
                    services.AddTransient<MainWindow>();
                    services.AddSingleton<MarketWatchViewModel>();
                    services.AddTransient<MarketWatchWindow>();

                    // --- Screener Registrations ---
                    services.AddSingleton<IQueryStore, FileQueryStore>();
                    services.AddSingleton<QueryCache>();
                    services.AddSingleton<ISettingsStore, FileSettingsStore>();
                    services.AddSingleton<ScreenerExecutionPlanBuilder>();
                    services.AddSingleton<ScreenerPlanStore>();
                    services.AddTransient<ScreenerWindow>();
                    services.AddScoped<IScreenerRepository, ScreenerRepository>();
                    services.AddScoped<ScreenerRunner>();
                    services.AddScoped<IScreenerRunner>(sp => sp.GetRequiredService<ScreenerRunner>());

                    // --- Data Access Registrations ---
                    services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
                    services.AddScoped<DataAccess.Infrastructure.Repositories.IInstrumentEqRepository, DataAccess.Infrastructure.Repositories.InstrumentEqRepository>();
                    services.AddScoped<DataAccess.Infrastructure.Repositories.IInstrumentFoRepository, DataAccess.Infrastructure.Repositories.InstrumentFoRepository>();
                    services.AddScoped<DataAccess.Infrastructure.Repositories.IUnitOfWork, DataAccess.Infrastructure.Repositories.UnitOfWork>();
                    services.AddScoped<IPlatformConfigurationProvider, PlatformConfigurationProvider>();
                    services.AddSingleton<IPlatformConfigurationRuntime, PlatformConfigurationRuntime>();

                    services.AddSingleton<ITradingCalendarRepository>(new SqlTradingCalendarRepository(connectionString, commandTimeoutSeconds: 30));
                    services.AddSingleton<ITradingCalendarProvider, RepositoryTradingCalendarProvider>();
                    services.AddSingleton(provider => new SqlHistoricalIngestionRepository(connectionString,
                        provider.GetRequiredService<IInstrumentRegistry>(), provider.GetRequiredService<ITradingCalendarRepository>(),
                        provider.GetRequiredService<TradingCalendarSelection>()));

                    IConfigurationSection calendar = configuration.GetSection("TradingCalendar");
                    services.AddSingleton(new TradingCalendarSelection(
                        calendar["ExchangeCode"] ?? "NSE",
                        calendar["SegmentCode"] ?? "CM",
                        calendar["Revision"] ?? throw new InvalidOperationException("TradingCalendar:Revision is required."),
                        DateOnly.ParseExact(calendar["SessionBoundsFrom"] ?? "2026-08-03", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));

                    services.AddScoped<IMarketWatchRepository, MarketWatchRepository>();
                    services.AddScoped<IHistoricalDataRepository, HistoricalDataRepository>();

                    // --- ETL & Flat-File Registrations (NEW) ---
                    services.AddSingleton<IWarmupRepository>(provider => new WarmupRepository(connectionString));
                    services.AddSingleton(new WarmupEtlRepository(connectionString));
                    services.AddSingleton<WarmupEtlService>();
                    services.AddSingleton(new SnapshotLoader(resourcesFolder));

                    // Registry & Background Services
                    services.AddSingleton<IInstrumentRegistry, InstrumentRegistry>();
                    // Real data continues through the existing historical polling workflow.
                    // The mock stream is enabled only when explicitly selected for development.
                    string liveMode = configuration["MarketData:LiveMode"] ?? "HistoricalPolling";
                    if (liveMode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
                    {
                        services.AddSingleton<ILiveMarketDataProvider, MockLiveMarketDataProvider>();
                        services.AddHostedService<LivePollingService>();
                    }
                    else if (!liveMode.Equals("HistoricalPolling", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("MarketData:LiveMode must be HistoricalPolling or Mock.");

                    services.AddScoped<IInstrumentImportService, InstrumentImportService>();
                })
                .Build();

            _serviceProvider = _host.Services;

            // Construct the subscriber before WebView2 navigation
            _serviceProvider.GetRequiredService<MarketWatchViewModel>();

            // --- 3. Startup Data Initialization (Flat File / ETL) ---
            try
            {
                // Validate prerequisites before starting any dependent hosted service.
                using (IServiceScope scope = _serviceProvider.CreateScope())
                {
                    await _serviceProvider.GetRequiredService<IPlatformConfigurationRuntime>()
                        .InitializeAsync(scope.ServiceProvider.GetRequiredService<IPlatformConfigurationProvider>());
                }
                var dependencies = new ScannerDependencyRegistry();
                dependencies.LoadFromDirectory(scannerFolder);
                var selection = _serviceProvider.GetRequiredService<TradingCalendarSelection>();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                var calendar = await _serviceProvider.GetRequiredService<ITradingCalendarRepository>()
                    .LoadAsync(selection.ExchangeCode, selection.SegmentCode, selection.Revision, now);
                DateTime marketNow = MarketTimestamp.ToDatabase(now);
                if (calendar == null || DateOnly.FromDateTime(marketNow) < calendar.FirstDate
                    || DateOnly.FromDateTime(marketNow) > calendar.LastDate)
                    throw new InvalidOperationException("The selected trading calendar does not cover the current market date.");
                DateTime? expectedSession = calendar.Days.Where(day => day.ClosesAt.HasValue
                        && day.Date.ToDateTime(day.ClosesAt.Value) <= marketNow)
                    .Select(day => (DateTime?)day.Date.ToDateTime(TimeOnly.MinValue)).Max();
                if (!expectedSession.HasValue)
                    throw new InvalidOperationException("The trading calendar has no completed session for warmup validation.");
                var loader = _serviceProvider.GetRequiredService<SnapshotLoader>();
                var snapshot = await loader.LoadLatestSnapshotAsync(expectedFingerprint: dependencies.Fingerprint);
                Guid calculationGeneration = snapshot.Generation;

                if (snapshot.Instruments.Count == 0 || snapshot.SnapshotDate.Date < expectedSession.Value.Date)
                {
                    // ETL FALLBACK
                    MessageBox.Show("No current warmup snapshot found. Building one from SQL history. This may take a minute...", "First Time Setup");

                    var etlService = _serviceProvider.GetRequiredService<WarmupEtlService>();
                    await etlService.RunWarmupEtlAsync(scannerFolder, resourcesFolder, calendar: calendar);

                    snapshot = await loader.LoadLatestSnapshotAsync(expectedFingerprint: dependencies.Fingerprint);
                    if (snapshot.Instruments.Count == 0)
                        throw new InvalidOperationException("ETL did not generate a complete, compatible snapshot. Check source history and scanner requirements.");
                }

                // Historical data must be loadable before login so the UI can refresh SQL history.
                if (snapshot.SnapshotDate.Date < expectedSession.Value.Date)
                {
                    string message = $"History is available through {snapshot.SnapshotDate:yyyy-MM-dd} for the least-current instrument. "
                        + $"The latest completed trading session is {expectedSession:yyyy-MM-dd}. "
                        + "Sign in, refresh historical data, and restart the app before using screening results as current.";
                    Trace.TraceWarning(message);
                    MessageBox.Show(message, "Historical data needs refresh", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                // Initialize Singleton Registry
                var registry = _serviceProvider.GetRequiredService<IInstrumentRegistry>();
                registry.Initialize(snapshot);
                if (calculationGeneration == Guid.Empty) calculationGeneration = snapshot.Generation;
                string? calculationsFile = Directory.EnumerateFiles(resourcesFolder,
                    $"ScreenerCalculations_*_{calculationGeneration:N}.json").FirstOrDefault();
                if (calculationsFile != null)
                    await Task.Run(() => _serviceProvider.GetRequiredService<QueryCache>()
                        .LoadCalculationsAsync(calculationsFile, calculationGeneration));

                // Start Background Host (Live Polling)
                await _host.StartAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Critical error during startup data ingestion: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                MessageBox.Show($"Critical error during startup data ingestion: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // --- 4. Show UI ---
            var main = _serviceProvider.GetRequiredService<MainWindow>();
            bool closeSaved = false;
            bool closeSaving = false;
            main.Closing += async (_, closing) =>
            {
                if (closeSaved) return;
                closing.Cancel = true;
                if (closeSaving) return;
                closeSaving = true;
                main.IsEnabled = false;
                try
                {
                    await _serviceProvider.GetRequiredService<MarketWatchViewModel>().StopPollingForShutdownAsync();
                    await _host.StopAsync();
                    await Task.Run(async () =>
                    {
                        var registry = _serviceProvider.GetRequiredService<IInstrumentRegistry>();
                        var loader = _serviceProvider.GetRequiredService<SnapshotLoader>();
                        Guid generation = await loader.SaveLatestSnapshotAsync(registry);
                        DateTime marketDate = MarketTimestamp.ToDatabase(DateTimeOffset.UtcNow).Date;
                        string path = Path.Combine(resourcesFolder, $"ScreenerCalculations_{marketDate:yyyyMMdd}_{generation:N}.json");
                        await _serviceProvider.GetRequiredService<QueryCache>().SaveCalculationsAsync(path, registry, generation);
                    });
                    closeSaved = true;
                    main.Close();
                }
                catch (Exception exception)
                {
                    Trace.TraceError("Could not save closing snapshot: {0}", exception);
                    MessageBox.Show(main, $"Could not save history and calculations: {exception.Message}. Close the window again to retry.",
                        "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    closeSaving = false;
                    main.IsEnabled = true;
                }
            };

            main.Show();

            // --- 5. Existing Background Initialization ---
            ViewModels.WebViewViewModel webViewViewModel = _serviceProvider.GetRequiredService<ViewModels.WebViewViewModel>();
            await webViewViewModel.CalendarLoadTask;


        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
            base.OnExit(e);
        }
    }
}
