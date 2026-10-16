namespace AlgoTrading.Models
{
    using AlgoTrading.Dtos;
    using AlgoTrading.Services;
    using AlgoTrading.ViewModels;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Web.WebView2.Core;
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Windows.Controls;
    using System.Xml.Linq;

    /// <summary>
    /// Owns the instrument universe observed from the embedded Kite market-watch payload.
    /// Instrument-master persistence is delegated to IInstrumentImportService so exactly one code path writes
    /// Instruments_EQ/Instruments_FO, and every write is awaited: nothing is fired and forgotten.
    /// </summary>
    internal sealed class MarketWatchModel
    {
        private const string MarketWatchUriMarker = "items?uid=marketwatch";
        private readonly IServiceScopeFactory scopeFactory;
        private readonly Dictionary<string, int> subscriptions = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly object subscriptionLock = new object();

        public MarketWatchModel(IServiceScopeFactory scopeFactory, string connectionString)
        {
            if (scopeFactory == null)
            {
                throw new ArgumentNullException(nameof(scopeFactory));
            }

            this.scopeFactory = scopeFactory;
            this.ConnectionString = connectionString ?? string.Empty;
        }

        public MarketWatchModel(string connectionString)
        {
            this.ConnectionString = connectionString ?? string.Empty;
        }

        public event EventHandler? MarketWatchDataUpdated;

        public string ConnectionString { get; set; }

        public string BaseDirectory
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "MarketWatch");
            }
        }

        public string[] MarketWatchFiles
        {
            get
            {
                string directory = this.BaseDirectory;
                if (!Directory.Exists(directory))
                {
                    return new string[0];
                }

                return Directory.GetFiles(directory, "*.json");
            }
        }

        /// <summary>Returns an immutable snapshot so callers cannot mutate shared state.</summary>
        public IReadOnlyDictionary<string, int> ActiveSubscriptions
        {
            get
            {
                lock (this.subscriptionLock)
                {
                    return new Dictionary<string, int>(this.subscriptions, StringComparer.Ordinal);
                }
            }
        }

        public int ActiveSubscriptionCount
        {
            get
            {
                lock (this.subscriptionLock)
                {
                    return this.subscriptions.Count;
                }
            }
        }

        public void ClearSubscriptions()
        {
            lock (this.subscriptionLock)
            {
                this.subscriptions.Clear();
            }
        }

        /// <summary>Persists one observed market-watch payload and records its subscriptions.</summary>
        internal async Task<StatusMessage> ProcessWebResourceResponseReceivedAsync(CoreWebView2WebResourceRequest request,
            CoreWebView2WebResourceResponseView response, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (!request.Uri.Contains(MarketWatchUriMarker, StringComparison.OrdinalIgnoreCase))
            {
                return new StatusMessage("Market Watch", "Ignored resource " + request.Uri, StatusMessageType.Warning);
            }

            using (Stream content = await response.GetContentAsync())
            {
                if (content == null)
                {
                    return new StatusMessage("Market Watch", "The market-watch response had no readable content.", StatusMessageType.Warning);
                }

                int watchListNumber = -1;
                string watchListName = "";
                JsonElement[] jsonElements = [];
                using (StreamReader reader = new StreamReader(content))
                {
                    string json = await reader.ReadToEndAsync();
                    var jsonDocument = JsonDocument.Parse(json); // validate JSON
                    var dataElement = jsonDocument.RootElement.GetProperty("data");
                    watchListNumber = dataElement.GetProperty("id").GetInt32();
                    watchListName = dataElement.GetProperty("name").GetString();
                    jsonElements = dataElement.GetProperty("groups")[0].GetProperty("items").EnumerateArray().ToArray();
                }
                await Helpers.SqlCandleBulkUploader.BulkUploadMarketWatch(ConnectionString, 
                    watchListNumber, watchListName, jsonElements, cancellationToken);
                foreach (var jsonEle in jsonElements)
                {
                    subscriptions.TryAdd(jsonEle.GetProperty("TradingSymbol").GetString(), jsonEle.GetProperty("instrument_token").GetInt32());
                }

                return new StatusMessage("Watch list " + watchListNumber + ": ", "Successfully exported market watch data.");
            }
        }


        /// <summary>Loads every saved market-watch file through the same import path as the live payload.</summary>
        public async Task<StatusMessage> LoadOfflineMarketWatchAsync(int watchListNumber = -1, CancellationToken cancellationToken = default)
        {
            string[] files = this.MarketWatchFiles;
            if (files.Length == 0)
            {
                return new StatusMessage("Market Watch", "No market-watch files were found in " + this.BaseDirectory + ".", StatusMessageType.Warning);
            }

            List<string> failures = new List<string>();
            int importedFiles = 0;
            foreach (string file in files)
            {
                try
                {
                    string content = await File.ReadAllTextAsync(file, cancellationToken);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        continue;
                    }

                    await this.ImportAsync(content, watchListNumber, cancellationToken);
                    importedFiles++;
                }
                catch (Exception exception)
                {
                    failures.Add(Path.GetFileName(file) + ": " + exception.Message);
                }
            }

            string details = "Watch list " + watchListNumber.ToString(CultureInfo.InvariantCulture) + ": imported "
                + importedFiles.ToString(CultureInfo.InvariantCulture) + " of " + files.Length.ToString(CultureInfo.InvariantCulture)
                + " market-watch file(s); " + this.ActiveSubscriptionCount.ToString(CultureInfo.InvariantCulture) + " subscription(s) known.";
            if (failures.Count != 0)
            {
                return new StatusMessage("Market Watch", details + " Failures: " + string.Join(" | ", failures), StatusMessageType.Error);
            }

            return new StatusMessage("Market Watch", details, StatusMessageType.Info);
        }

        private async Task<StatusMessage> ImportAsync(string json, int requestedWatchId, CancellationToken cancellationToken)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                MarketWatchApiData marketWatch = ReadMarketWatch(document.RootElement, requestedWatchId);
                int recorded = 0;
                foreach (MarketWatchApiItem item in marketWatch.Items)
                {
                    if (item.InstrumentToken is > 0 and <= int.MaxValue && item.TradingSymbol.Length != 0)
                    {
                        this.SetSubscription(item.TradingSymbol, (int)item.InstrumentToken);
                        recorded++;
                    }
                }

                using (IServiceScope scope = this.scopeFactory.CreateScope())
                {
                    IMarketWatchSyncService service = scope.ServiceProvider.GetRequiredService<IMarketWatchSyncService>();
                    await service.SynchronizeAsync(marketWatch, cancellationToken);
                }

                this.RaiseUpdated();
                return new StatusMessage("Market Watch", "MarketWatch persisted "
                    + marketWatch.Items.Count.ToString(CultureInfo.InvariantCulture) + " row(s); "
                    + recorded.ToString(CultureInfo.InvariantCulture) + " subscription(s) recorded.", StatusMessageType.Info);
            }
        }

        private static MarketWatchApiData ReadMarketWatch(JsonElement root, int requestedWatchId)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out JsonElement data))
                throw new InvalidDataException("The MarketWatch response does not contain data.");

            JsonElement source = data;
            if (data.TryGetProperty("groups", out JsonElement groups) && groups.ValueKind == JsonValueKind.Array)
            {
                JsonElement? match = null;
                foreach (JsonElement group in groups.EnumerateArray())
                {
                    int groupId = ReadInt32(group, "id");
                    if (match is null || groupId == requestedWatchId) match = group;
                    if (groupId == requestedWatchId) break;
                }
                source = match ?? throw new InvalidDataException("The MarketWatch response contains no groups.");
            }

            int watchId = root.GetProperty("data").GetProperty("id").GetInt32();
            if (watchId <= 0) throw new InvalidDataException("The MarketWatch response contains no valid id.");

            string name = root.GetProperty("data").GetProperty("name").GetString() ?? string.Empty;
            if (name.Length == 0) throw new InvalidDataException("The MarketWatch response contains no name.");

            JsonElement items;
            if (!source.TryGetProperty("items", out items) || items.ValueKind != JsonValueKind.Array)
                items = default;

            var result = new MarketWatchApiData { Id = watchId, Name = name };
            if (items.ValueKind != JsonValueKind.Array) return result;
            HashSet<long> tokens = new HashSet<long>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("instrument_token", out JsonElement tokenValue)
                    || tokenValue.ValueKind != JsonValueKind.Number)
                    continue;
                long token = tokenValue.GetInt64();
                if (token <= 0 || !tokens.Add(token)) continue;
                result.Items.Add(new MarketWatchApiItem
                {
                    InstrumentToken = token,
                    TradingSymbol = ReadString(item, "tradingsymbol"),
                    Exchange = ReadString(item, "exchange")
                });
            }
            return result;
        }

        private static int ReadInt32(JsonElement source, string propertyName)
        {
            return source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out int result) ? result : 0;
        }

        private static string ReadString(JsonElement source, string propertyName)
        {
            return source.ValueKind == JsonValueKind.Object
                && source.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        }

        /// <summary>Accepts both shapes seen in practice: a bare item array and Kite's data.groups[].items envelope.</summary>
        private static List<string> ReadItemTexts(JsonElement root)
        {
            List<string> itemTexts = new List<string>();
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in root.EnumerateArray())
                {
                    itemTexts.Add(item.GetRawText());
                }

                return itemTexts;
            }

            JsonElement data;
            JsonElement groups;
            if (!root.TryGetProperty("data", out data) || !data.TryGetProperty("groups", out groups)
                || groups.ValueKind != JsonValueKind.Array)
            {
                return itemTexts;
            }

            foreach (JsonElement group in groups.EnumerateArray())
            {
                JsonElement items;
                if (!group.TryGetProperty("items", out items) || items.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement item in items.EnumerateArray())
                {
                    itemTexts.Add(item.GetRawText());
                }
            }

            return itemTexts;
        }

        private bool TryReadSubscription(JsonElement instrument, out string symbol, out int token)
        {
            symbol = string.Empty;
            token = 0;
            JsonElement symbolProperty;
            JsonElement tokenProperty;
            if (!instrument.TryGetProperty("tradingsymbol", out symbolProperty) || symbolProperty.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            if (!instrument.TryGetProperty("instrument_token", out tokenProperty) || tokenProperty.ValueKind != JsonValueKind.Number)
            {
                return false;
            }

            long value = tokenProperty.GetInt64();
            if (value <= 0 || value > int.MaxValue)
            {
                return false;
            }

            symbol = symbolProperty.GetString() ?? string.Empty;
            token = (int)value;
            return symbol.Length != 0;
        }

        private static int ReadWatchListNumber(string uri)
        {
            Match match = Regex.Match(uri, @"\d+");
            if (!match.Success)
            {
                return 0;
            }

            int number;
            if (!int.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out number))
            {
                return 0;
            }

            return number;
        }

        private void SetSubscription(string symbol, int instrumentToken)
        {
            lock (this.subscriptionLock)
            {
                this.subscriptions[symbol] = instrumentToken;
            }
        }

        private void RaiseUpdated()
        {
            EventHandler? handler = this.MarketWatchDataUpdated;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }
    }
}
