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
                    services.AddSingleton(provider => new SqlHistoricalIngestionRepository(connectionString, provider.GetRequiredService<IInstrumentRegistry>()));

                    IConfigurationSection calendar = configuration.GetSection("TradingCalendar");
                    services.AddSingleton(new TradingCalendarSelection(
                        calendar["ExchangeCode"] ?? "NSE",
                        calendar["SegmentCode"] ?? "CM",
                        calendar["Revision"] ?? throw new InvalidOperationException("TradingCalendar:Revision is required.")));

                    services.AddScoped<IMarketWatchRepository, MarketWatchRepository>();
                    services.AddScoped<IHistoricalDataRepository, HistoricalDataRepository>();

                    // --- ETL & Flat-File Registrations (NEW) ---
                    services.AddSingleton<IWarmupRepository>(provider => new WarmupRepository(connectionString));
                    services.AddSingleton(new WarmupEtlRepository(connectionString));
                    services.AddSingleton<WarmupEtlService>();
                    services.AddSingleton(new SnapshotLoader(resourcesFolder));

                    // Registry & Background Services
                    services.AddSingleton<IInstrumentRegistry, InstrumentRegistry>();
                    services.AddSingleton<ILiveMarketDataProvider, MockLiveMarketDataProvider>();
                    services.AddHostedService<LivePollingService>();

                    services.AddScoped<IInstrumentImportService, InstrumentImportService>();
                })
                .Build();

            _serviceProvider = _host.Services;

            // Construct the subscriber before WebView2 navigation
            _serviceProvider.GetRequiredService<MarketWatchViewModel>();

            // --- 3. Startup Data Initialization (Flat File / ETL) ---
            try
            {
                var loader = _serviceProvider.GetRequiredService<SnapshotLoader>();
                var snapshot = await loader.LoadLatestSnapshotAsync();

                if (snapshot == null || snapshot.Instruments.Count == 0)
                {
                    // ETL FALLBACK
                    MessageBox.Show("No warmup JSON found. Running ETL for the first time. This may take a minute...", "First Time Setup");

                    var etlService = _serviceProvider.GetRequiredService<WarmupEtlService>();
                    await etlService.RunWarmupEtlAsync(scannerFolder, resourcesFolder);

                    snapshot = await loader.LoadLatestSnapshotAsync();
                    if (snapshot == null) throw new Exception("ETL failed to generate snapshot.");
                }

                // Initialize Singleton Registry
                var registry = _serviceProvider.GetRequiredService<IInstrumentRegistry>();
                registry.Initialize(snapshot);

                // Start Background Host (Live Polling)
                await _host.StartAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Critical error during startup data ingestion: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            // --- 4. Show UI ---
            var main = _serviceProvider.GetRequiredService<MainWindow>();
            main.Show();

            // --- 5. Existing Background Initialization ---
            ViewModels.WebViewViewModel webViewViewModel = _serviceProvider.GetRequiredService<ViewModels.WebViewViewModel>();
            await webViewViewModel.CalendarLoadTask;

            try
            {
                using (IServiceScope scope = _serviceProvider.CreateScope())
                {
                    IPlatformConfigurationProvider provider = scope.ServiceProvider.GetRequiredService<IPlatformConfigurationProvider>();
                    IPlatformConfigurationRuntime runtime = _serviceProvider.GetRequiredService<IPlatformConfigurationRuntime>();
                    await runtime.InitializeAsync(provider);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show("Background market-data configuration is invalid or unavailable. "
                    + "Processing was not started.\n\n" + exception.Message,
                    "Market-data startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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