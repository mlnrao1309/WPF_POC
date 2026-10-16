using AlgoTrading.DataAccess.Calendar;
using AlgoTrading.DataAccess.Historical;
using AlgoTrading.DataAccess.Infrastructure;
using AlgoTrading.DataAccess.Infrastructure.DatabaseContext;
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
using System.Net.Http;
using System.Windows;

namespace AlgoTrading
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        //protected override void OnExit(ExitEventArgs e)
        //{
        //    base.OnExit(e);
        //}

        //protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        //{
        //    base.OnSessionEnding(e);
        //}
        
        public static IServiceProvider? _serviceProvider;

        public static IServiceProvider Services { get => _serviceProvider ?? throw new InvalidOperationException("Service provider is not initialized."); private set => _serviceProvider = value; }
        //private readonly IHost _host;
        public static IServiceProvider ServiceProvider => _serviceProvider ?? throw new InvalidOperationException("Service provider is not initialized.");

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var services = new ServiceCollection();

            // Configuration
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            IConfiguration configuration = builder.Build();
            services.AddSingleton(configuration);

            // Bind settings (ConnectionStrings section)
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
            services.AddSingleton(appSettings);

            // Register ViewModels and Views
            services.AddSingleton<AccessTokenStore>();
            services.AddSingleton<IAccessTokenProvider>(provider => provider.GetRequiredService<AccessTokenStore>());
            services.AddSingleton<ViewModels.WebViewViewModel>();
            services.AddSingleton<IMarketWatchDataStream>(provider => provider.GetRequiredService<ViewModels.WebViewViewModel>());
            services.AddSingleton(new HttpClient());
            //services.AddTransient<ViewModels.MarketWatchViewModel>(provider => new ViewModels.MarketWatchViewModel(
            //    provider.GetRequiredService<IMarketWatchDataService>(),
            //    provider.GetRequiredService<IMarketWatchSyncService>(),
            //    provider.GetRequiredService<IHistoricalDataWorkflow>(),
            //    provider.GetRequiredService<IHistoricalPollingCoordinator>(),
            //    provider.GetRequiredService<HistoricalDataModel>(),
            //    action => Current.Dispatcher.InvokeAsync(action).Task));
            services.AddSingleton<HistoricalDataModel>(provider =>
                new HistoricalDataModel(
                    provider.GetRequiredService<HttpClient>(),
                    string.Empty,
                    provider.GetRequiredService<SqlHistoricalIngestionRepository>()));
            services.AddSingleton<IInstrumentRepository, InstrumentRepository>();
            services.AddSingleton<IMarketWatchRepository, MarketWatchRepository>();
            services.AddSingleton<IMarketWatchApiService, MarketWatchApiService>();
            //services.AddSingleton<IMarketWatchSyncService, MarketWatchSyncService>();
            //services.AddSingleton<IMarketWatchDataService, SqlMarketWatchDataService>();
            services.AddSingleton<IHistoricalDataWorkflow, HistoricalDataWorkflow>();
            services.AddSingleton<IHistoricalPollingCoordinator, HistoricalPollingCoordinator>();
            services.AddTransient<MainWindow>();
            //services.AddTransient<MarketWatchWindow>();
            services.AddSingleton<MarketWatchViewModel>();
            services.AddTransient<MarketWatchWindow>();   // or AddTransient — see note below
            services.AddSingleton<IQueryStore, FileQueryStore>();
            services.AddSingleton<QueryCache>();
            services.AddSingleton<IQueryStore, FileQueryStore>();
            services.AddSingleton<ISettingsStore, FileSettingsStore>();
            services.AddSingleton<ScreenerExecutionPlanBuilder>();
            services.AddSingleton<ScreenerPlanStore>();
            //services.AddTransient<ScreenerWindow>(); 
            services.AddTransient<ScreenerWindow>();
            services.AddScoped<IScreenerRepository, ScreenerRepository>();
            services.AddScoped<IScreenerRunner, ScreenerRunner>();
            services.AddScoped<ScreenerRunner>();
            services.AddScoped<IScreenerRunner>(sp => sp.GetRequiredService<ScreenerRunner>());
            string? connectionString = Environment.GetEnvironmentVariable("ALGOTRADING_SQL_CONNECTION_STRING")
                ?? configuration.GetConnectionString("MsSqlDatabase");
            //// Data access registrations

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                MessageBox.Show("A SQL connection string is required. Configure ConnectionStrings:MsSqlDatabase "
                    + "or ALGOTRADING_SQL_CONNECTION_STRING.", "Startup failed", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-1);
                return;
            }

            if (!string.IsNullOrWhiteSpace(connectionString))
            {

                services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlServer(connectionString));

                services.AddScoped<DataAccess.Infrastructure.Repositories.IInstrumentEqRepository, DataAccess.Infrastructure.Repositories.InstrumentEqRepository>();
                services.AddScoped<DataAccess.Infrastructure.Repositories.IInstrumentFoRepository, DataAccess.Infrastructure.Repositories.InstrumentFoRepository>();
                services.AddScoped<DataAccess.Infrastructure.Repositories.IUnitOfWork, DataAccess.Infrastructure.Repositories.UnitOfWork>();
                services.AddScoped<IPlatformConfigurationProvider, PlatformConfigurationProvider>();
                services.AddSingleton<IPlatformConfigurationRuntime, PlatformConfigurationRuntime>();

                services.AddSingleton<ITradingCalendarRepository>(
                    new SqlTradingCalendarRepository(connectionString, commandTimeoutSeconds: 30));
                services.AddSingleton<ITradingCalendarProvider, RepositoryTradingCalendarProvider>();
                services.AddSingleton(new SqlHistoricalIngestionRepository(connectionString));

                IConfigurationSection calendar = configuration.GetSection("TradingCalendar");
                services.AddSingleton(new TradingCalendarSelection(
                    calendar["ExchangeCode"] ?? "NSE",
                    calendar["SegmentCode"] ?? "CM",
                    calendar["Revision"] ?? throw new InvalidOperationException("TradingCalendar:Revision is required.")));
                services.AddScoped<IMarketWatchRepository, MarketWatchRepository>();
                services.AddScoped<IHistoricalDataRepository, HistoricalDataRepository>();

                // Background service (auto-expire / soft delete)
                //services.AddHoster<MarketWatchExpiryService>();
            }
            services.AddScoped<IInstrumentImportService, InstrumentImportService>();
            _serviceProvider = services.BuildServiceProvider();

            // Construct the subscriber before WebView2 navigation so MarketWatch responses cannot be missed.
            _serviceProvider.GetRequiredService<MarketWatchViewModel>();

            // Show the shell before background startup work that may wait on external services.
            var main = _serviceProvider.GetRequiredService<MainWindow>();
            main.Show();

            ViewModels.WebViewViewModel webViewViewModel = _serviceProvider
                .GetRequiredService<ViewModels.WebViewViewModel>();
            await webViewViewModel.CalendarLoadTask;

            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                try
                {
                    using (IServiceScope scope = _serviceProvider.CreateScope())
                    {
                        IPlatformConfigurationProvider provider = scope.ServiceProvider
                            .GetRequiredService<IPlatformConfigurationProvider>();
                        IPlatformConfigurationRuntime runtime = _serviceProvider
                            .GetRequiredService<IPlatformConfigurationRuntime>();
                        await runtime.InitializeAsync(provider);
                        //uncomment later when we have a proper configuration provider
                    }
                }
                catch (Exception exception)
                {
                    MessageBox.Show("Background market-data configuration is invalid or unavailable. "
                        + "Processing was not started.\n\n" + exception.Message,
                        "Market-data startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        //protected override async void OnStartup(StartupEventArgs e)
        //{
        //    await _host.StartAsync();
        //    base.OnStartup(e);

        //    // Optional: force DB to be reachable early so failures are visible.
        //    using var scope = Services.CreateScope();
        //    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        //    await db.Database.CanConnectAsync();
        //}

        //protected override async void OnExit(ExitEventArgs e)
        //{
        //    try { await _host.StopAsync(TimeSpan.FromSeconds(5)); }
        //    finally { _host.Dispose(); }

        //    base.OnExit(e);
        //}
    }

}
