namespace AlgoTrading.Helpers
{
    using AlgoTrading.Models;
    using Microsoft.Data.SqlClient;
    using System;
    using System.Data;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// High-speed ingestion writers. The legacy Instruments_OHLC table stores TimeStamp as an IST wall time,
    /// TimeFrame as the short code returned by GetTimeFrame, OHLC as decimal(18,8) and Volume/OI as bigint.
    /// </summary>
    public static class SqlCandleBulkUploader
    {
        private static string GetTimeFrame(string interval) =>
interval switch
{
    HistoryCandleInterval.OneMinute => "1",
    HistoryCandleInterval.ThreeMinutes => "3",
    HistoryCandleInterval.FiveMinutes => "5",
    HistoryCandleInterval.FifteenMinutes => "15",
    HistoryCandleInterval.Hour => "60",
    _ => "D"
};
        /// <summary>Zerodha sends an offset timestamp; persistence uses the application's IST wall-time convention.</summary>
        private static DateTime ToIstWallTime(string timestamp)
        {
            DateTimeOffset instant = DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None);
            return MarketTimestamp.ToDatabase(instant);
        }

        public static async Task BulkUploadCandleJsonAsync(
            string connectionString,
            long instrumentToken,
            string timeframe,
            string jsonResponse,
            CancellationToken cancellationToken = default)
        {

            // 1. Prepare an In-Memory DataTable matching the Target SQL Schema
            using var table = new DataTable();
            table.Columns.Add("InstrumentToken", typeof(long));
            table.Columns.Add("TimeStamp", typeof(DateTime));
            table.Columns.Add("TimeFrame", typeof(string));
            table.Columns.Add("Open", typeof(double));
            table.Columns.Add("High", typeof(double));
            table.Columns.Add("Low", typeof(double));
            table.Columns.Add("Close", typeof(double));
            table.Columns.Add("Volume", typeof(long));
            table.Columns.Add("OI", typeof(long)).AllowDBNull = true;

            // 2. Fast Parse JSON using System.Text.Json Document
            using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
            {
                JsonElement root = doc.RootElement;

                // Navigate to data.candles array in Zerodha payload
                if (root.TryGetProperty("data", out JsonElement dataElement) &&
                    dataElement.TryGetProperty("candles", out JsonElement candlesElement))
                {
                    foreach (JsonElement candle in candlesElement.EnumerateArray())
                    {
                        // Zerodha Candle Format: 
                        // [ Timestamp (string), Open, High, Low, Close, Volume, OpenInterest (optional) ]
                        string? timeStr = candle[0].GetString();
                        if (string.IsNullOrWhiteSpace(timeStr))
                        {
                            throw new InvalidOperationException("A Zerodha candle row did not carry a timestamp.");
                        }

                        DateTime timestamp = ToIstWallTime(timeStr);
                        double open = candle[1].GetDouble();
                        double high = candle[2].GetDouble();
                        double low = candle[3].GetDouble();
                        double close = candle[4].GetDouble();
                        long volume = candle[5].GetInt64();

                        // Handle OI if present in array (Options), otherwise default to 0
                        long openInterest = candle.GetArrayLength() > 6 ? candle[6].GetInt64() : 0;

                        // Append row to in-memory DataTable
                        table.Rows.Add(instrumentToken, timestamp, GetTimeFrame(timeframe), open, high, low, close, volume, openInterest);
                    }
                }
            }

            if (table.Rows.Count == 0) return;
            foreach (DataRow item in table.Rows)
            {
                if (item.IsNull("OI"))
                    item["OI"] = 0;
            }

            // 3. Perform High-Speed SqlBulkCopy
            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.UseInternalTransaction, null))
                {
                    bulkCopy.DestinationTableName = "dbo.Instruments_OHLC";
                    bulkCopy.BatchSize = 10000;
                    bulkCopy.BulkCopyTimeout = 60; // 60 seconds

                    // Map C# DataTable columns to SQL Server Table columns
                    bulkCopy.ColumnMappings.Add("InstrumentToken", "InstrumentToken");
                    bulkCopy.ColumnMappings.Add("TimeStamp", "TimeStamp");
                    bulkCopy.ColumnMappings.Add("TimeFrame", "TimeFrame");
                    bulkCopy.ColumnMappings.Add("Open", "Open");
                    bulkCopy.ColumnMappings.Add("High", "High");
                    bulkCopy.ColumnMappings.Add("Low", "Low");
                    bulkCopy.ColumnMappings.Add("Close", "Close");
                    bulkCopy.ColumnMappings.Add("Volume", "Volume");
                    bulkCopy.ColumnMappings.Add("OI", "OI");
                    // Execute the stream directly to the database engine; failures must surface, never be swallowed.
                    await bulkCopy.WriteToServerAsync(table, cancellationToken);
                }
            }
        }

        public static async Task BulkUploadMarketWatch(
    string connectionString,
    int watchListNumber,
    string watchListName,
    JsonElement[] jsonElements,
    CancellationToken cancellationToken = default)
        {
            DataTable dataTable = new DataTable();
            dataTable.Columns.Add("WatchId", typeof(int));
            dataTable.Columns.Add("Name", typeof(string));
            dataTable.Columns.Add("FullId", typeof(string));
            dataTable.Columns.Add("TradingSymbol", typeof(string));
            dataTable.Columns.Add("InstrumentToken", typeof(int));
            dataTable.Columns.Add("Segment", typeof(string));
            dataTable.Columns.Add("exchange", typeof(string));
            dataTable.Columns.Add("ExpiresOn", typeof(DateTime));

            foreach (var item in jsonElements)
            {
                DateTime dt = DateTime.MinValue;
                item.GetProperty("expiry").TryGetDateTime(out dt);
                dataTable.Rows.Add(

                        watchListNumber
                        , watchListName
                        , item.GetProperty("id").GetString()
                , item.GetProperty("tradingsymbol").GetString()
                , item.GetProperty("instrument_token").GetInt32()

                , item.GetProperty("segment").GetString()

                , item.GetProperty("exchange").GetString()

                , dt);


            }
            if (dataTable.Rows.Count == 0)
            {
                return;
            }

            using (var connection = new SqlConnection(connectionString))
            {
                await connection.OpenAsync(cancellationToken);

                // 1. Explicitly create the temporary table first
                string createTempTableSql = @"
        CREATE TABLE #TempMarketWatch1 (
            WatchId INT,
            Name NVARCHAR(255),
            InstrumentToken BIGINT,
            TradingSymbol NVARCHAR(255),
            Segment NVARCHAR(50),
            Exchange NVARCHAR(50),
            ExpiresOn DATETIME,
            FullId NVARCHAR(255)
        );";

                using (var createCmd = new SqlCommand(createTempTableSql, connection))
                {
                    await createCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                // 2. Perform the Bulk Copy
                using (var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null))
                {
                    bulkCopy.DestinationTableName = "#TempMarketWatch1";
                    bulkCopy.BatchSize = 10000;
                    bulkCopy.BulkCopyTimeout = 60;

                    bulkCopy.ColumnMappings.Add("WatchId", "WatchId");
                    bulkCopy.ColumnMappings.Add("Name", "Name");
                    bulkCopy.ColumnMappings.Add("InstrumentToken", "InstrumentToken");
                    bulkCopy.ColumnMappings.Add("TradingSymbol", "TradingSymbol");
                    bulkCopy.ColumnMappings.Add("Segment", "Segment");
                    bulkCopy.ColumnMappings.Add("Exchange", "Exchange");
                    bulkCopy.ColumnMappings.Add("ExpiresOn", "ExpiresOn");
                    bulkCopy.ColumnMappings.Add("FullId", "FullId");

                    await bulkCopy.WriteToServerAsync(dataTable, cancellationToken);
                }

                // 3. Execute the MERGE statement (Removed 'GO' and separated commands with semicolons)
                string mergeSql = @"
        MERGE INTO dbo.MarketWatch AS target
        USING #TempMarketWatch1 AS source 
        ON target.InstrumentToken = source.InstrumentToken 
           AND target.WatchId = source.WatchId 
           AND target.Name = source.Name
        WHEN NOT MATCHED THEN
            INSERT (WatchId, Name, InstrumentToken, TradingSymbol, Segment, Exchange, ExpiresOn, FullId)
            VALUES (source.WatchId, source.Name, source.InstrumentToken, source.TradingSymbol, source.Segment, source.Exchange, source.ExpiresOn, source.FullId);
        
        DROP TABLE #TempMarketWatch1;";

                using (SqlCommand cmd = new SqlCommand(mergeSql, connection))
                {
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }



        }

    }
}
