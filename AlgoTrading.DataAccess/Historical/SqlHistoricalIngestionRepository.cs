using System.Data;
using System.Globalization;
using System.Text.Json;
using AlgoTrading.Models;
using AlgoTrading.Models.MarketData.Ingestion;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.DataAccess.Historical
{
    /// <summary>
    /// Writes a provider chunk through a transaction-local staging table, upserts candle identities, and advances
    /// the durable checkpoint before committing. Existing candle identities are updated so explicit correction
    /// refreshes cannot be hidden by an older value.
    /// </summary>
    public sealed class SqlHistoricalIngestionRepository : IHistoricalIngestionRepository
    {
        private readonly string connectionString;

        public SqlHistoricalIngestionRepository(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A SQL connection string is required.", nameof(connectionString));
            }

            this.connectionString = connectionString;
        }

        public async Task<DateTime?> GetLatestCandleTimestampAsync(long instrumentToken, string databaseTimeframe,
            CancellationToken cancellationToken)
        {
            if (instrumentToken <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instrumentToken));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseTimeframe);
            const string sql = "SELECT MAX(vw.[TimeStamp]) FROM dbo.vw_InstrumentBars vw "
                + "WHERE vw.[InstrumentToken] = @InstrumentToken AND vw.[TimeFrame] = @TimeFrame;";

            using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            using SqlCommand command = new SqlCommand(sql, connection);
            command.Parameters.Add("@InstrumentToken", SqlDbType.BigInt).Value = instrumentToken;
            command.Parameters.Add("@TimeFrame", SqlDbType.VarChar, 10).Value = databaseTimeframe;
            object? value = await command.ExecuteScalarAsync(cancellationToken);
            return value == null || value == DBNull.Value
                ? null
                : DateTime.SpecifyKind((DateTime)value, DateTimeKind.Unspecified);
        }

        public async Task<(TimeSpan ClosesAt, TimeSpan DailyCandleReadyAt)?> GetCasSessionOverrideAsync(
            long instrumentToken, CancellationToken cancellationToken)
        {
            if (instrumentToken <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(instrumentToken));
            }
            const string sql = "SELECT TOP (1) vw.ClosesAt, vw.DailyCandleReadyAt "
                + "FROM dbo.vw_CAS_Instruments vw WHERE vw.InstrumentToken = @InstrumentToken "
                + "AND EXISTS (SELECT 1 FROM dbo.CAS_Instruments cas "
                + "WHERE cas.TradingSymbol = vw.TradingSymbol);";

            using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            using SqlCommand command = new SqlCommand(sql, connection);
            command.Parameters.Add("@InstrumentToken", SqlDbType.BigInt).Value = instrumentToken;
            using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }
            return (reader.GetTimeSpan(0), reader.GetTimeSpan(1));
        }

        public async Task<HistoricalIngestionCheckpoint?> GetCheckpointAsync(long instrumentToken, string streamType,
            CancellationToken cancellationToken)
        {
            HistoricalStreamTypes.Validate(streamType);
            const string sql = "SELECT LastCompletedChunkStartDate, LastCompletedChunkEndDate "
                + "FROM dbo.IngestionSyncState WHERE InstrumentToken = @InstrumentToken AND StreamType = @StreamType;";

            using (SqlConnection connection = new SqlConnection(this.connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (SqlCommand command = new SqlCommand(sql, connection))
                {
                    command.Parameters.Add("@InstrumentToken", SqlDbType.BigInt).Value = instrumentToken;
                    command.Parameters.Add("@StreamType", SqlDbType.VarChar, 20).Value = streamType;
                    using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        if (!await reader.ReadAsync(cancellationToken))
                        {
                            return null;
                        }

                        DateTime chunkStart = reader.GetDateTime(0);
                        DateTime chunkEnd = reader.GetDateTime(1);
                        return new HistoricalIngestionCheckpoint(instrumentToken, streamType, chunkStart, chunkEnd);
                    }
                }
            }
        }

        public async Task PersistChunkAndCheckpointAsync(HistoricalChunkRequest request, string providerPayload,
            CancellationToken cancellationToken)
        {
            await this.PersistAsync(request, providerPayload, true, cancellationToken);
        }

        public async Task PersistChunkAsync(HistoricalChunkRequest request, string providerPayload,
            CancellationToken cancellationToken)
        {
            await this.PersistAsync(request, providerPayload, false, cancellationToken);
        }

        private async Task PersistAsync(HistoricalChunkRequest request, string providerPayload, bool advanceCheckpoint,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            DataTable candles = BuildCandleTable(request, providerPayload);
            using (candles)
            using (SqlConnection connection = new SqlConnection(this.connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using (SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
                {
                    try
                    {
                        await CreateStagingTableAsync(connection, transaction, cancellationToken);
                        if (candles.Rows.Count > 0)
                        {
                            await BulkStageAsync(connection, transaction, candles, cancellationToken);
                            await MergeCandlesAsync(connection, transaction, cancellationToken);
                        }

                        if (advanceCheckpoint)
                        {
                            await AdvanceCheckpointAsync(connection, transaction, request, cancellationToken);
                        }
                        await transaction.CommitAsync(cancellationToken);
                    }
                    catch
                    {
                        await transaction.RollbackAsync(CancellationToken.None);
                        throw;
                    }
                }
            }
        }

        private static DataTable BuildCandleTable(HistoricalChunkRequest request, string providerPayload)
        {
            if (string.IsNullOrWhiteSpace(providerPayload))
            {
                throw new InvalidOperationException("Historical provider returned an empty payload.");
            }

            DataTable table = new DataTable();
            table.Locale = CultureInfo.InvariantCulture;
            table.Columns.Add("InstrumentToken", typeof(long));
            table.Columns.Add("TimeStamp", typeof(DateTime));
            table.Columns.Add("TimeFrame", typeof(string));
            table.Columns.Add("Open", typeof(decimal));
            table.Columns.Add("High", typeof(decimal));
            table.Columns.Add("Low", typeof(decimal));
            table.Columns.Add("Close", typeof(decimal));
            table.Columns.Add("Volume", typeof(long));
            table.Columns.Add("OI", typeof(long));

            HashSet<DateTime> identities = new HashSet<DateTime>();
            using (JsonDocument document = JsonDocument.Parse(providerPayload))
            {
                JsonElement root = document.RootElement;
                JsonElement dataElement;
                JsonElement candlesElement;
                if (!root.TryGetProperty("data", out dataElement)
                    || !dataElement.TryGetProperty("candles", out candlesElement)
                    || candlesElement.ValueKind != JsonValueKind.Array)
                {
                    table.Dispose();
                    throw new InvalidOperationException("Historical provider payload does not contain data.candles.");
                }

                foreach (JsonElement candle in candlesElement.EnumerateArray())
                {
                    if (candle.ValueKind != JsonValueKind.Array || candle.GetArrayLength() < 6)
                    {
                        table.Dispose();
                        throw new InvalidOperationException("Historical provider returned a malformed candle row.");
                    }

                    string? timestampText = candle[0].GetString();
                    if (string.IsNullOrWhiteSpace(timestampText))
                    {
                        table.Dispose();
                        throw new InvalidOperationException("Historical provider returned a candle without a timestamp.");
                    }

                    DateTime timestamp = ToMarketWallTime(timestampText);
                    if (timestamp.Date < request.RangeStart.Date || timestamp.Date > request.RangeEnd.Date)
                    {
                        table.Dispose();
                        throw new InvalidOperationException("Historical provider returned a candle outside the requested chunk.");
                    }

                    if (request.CandleStart.HasValue
                        && (timestamp < request.CandleStart.Value || timestamp >= request.CandleEnd!.Value))
                    {
                        continue;
                    }

                    if (!identities.Add(timestamp))
                    {
                        table.Dispose();
                        throw new InvalidOperationException("Historical provider returned a duplicate candle identity.");
                    }

                    decimal open = candle[1].GetDecimal();
                    decimal high = candle[2].GetDecimal();
                    decimal low = candle[3].GetDecimal();
                    decimal close = candle[4].GetDecimal();
                    long volume = candle[5].GetInt64();
                    long openInterest = 0;
                    if (candle.GetArrayLength() > 6 && candle[6].ValueKind != JsonValueKind.Null)
                    {
                        openInterest = candle[6].GetInt64();
                    }

                    ValidateCandle(open, high, low, close, volume, openInterest);
                    table.Rows.Add(request.InstrumentToken, timestamp,
                        HistoricalStreamTypes.GetDatabaseTimeframe(request.StreamType), open, high, low, close, volume,
                        openInterest);
                }
            }

            return table;
        }

        private static void ValidateCandle(decimal open, decimal high, decimal low, decimal close, long volume,
            long openInterest)
        {
            if (high < low || high < open || high < close || low > open || low > close)
            {
                throw new InvalidOperationException("Historical provider returned invalid OHLC values.");
            }

            if (volume < 0 || openInterest < 0)
            {
                throw new InvalidOperationException("Historical provider returned negative volume or open interest.");
            }
        }

        private static DateTime ToMarketWallTime(string timestampText)
        {
            DateTimeOffset instant = DateTimeOffset.Parse(timestampText, CultureInfo.InvariantCulture,
                DateTimeStyles.None);
            return MarketTimestamp.ToDatabase(instant);
        }

        private static async Task CreateStagingTableAsync(SqlConnection connection, SqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            const string sql = "CREATE TABLE #HistoricalChunk ("
                + "InstrumentToken bigint NOT NULL, TimeStamp datetime NOT NULL, TimeFrame varchar(10) NOT NULL, "
                + "[Open] decimal(18,8) NOT NULL, High decimal(18,8) NOT NULL, Low decimal(18,8) NOT NULL, "
                + "[Close] decimal(18,8) NOT NULL, Volume bigint NOT NULL, OI bigint NOT NULL, "
                + "PRIMARY KEY (InstrumentToken, TimeFrame, TimeStamp));";
            using (SqlCommand command = new SqlCommand(sql, connection, transaction))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private static async Task BulkStageAsync(SqlConnection connection, SqlTransaction transaction, DataTable candles,
            CancellationToken cancellationToken)
        {
            using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, transaction))
            {
                bulkCopy.DestinationTableName = "#HistoricalChunk";
                bulkCopy.BatchSize = 10000;
                bulkCopy.BulkCopyTimeout = 60;
                AddColumnMappings(bulkCopy);
                await bulkCopy.WriteToServerAsync(candles, cancellationToken);
            }
        }

        private static void AddColumnMappings(SqlBulkCopy bulkCopy)
        {
            bulkCopy.ColumnMappings.Add("InstrumentToken", "InstrumentToken");
            bulkCopy.ColumnMappings.Add("TimeStamp", "TimeStamp");
            bulkCopy.ColumnMappings.Add("TimeFrame", "TimeFrame");
            bulkCopy.ColumnMappings.Add("Open", "Open");
            bulkCopy.ColumnMappings.Add("High", "High");
            bulkCopy.ColumnMappings.Add("Low", "Low");
            bulkCopy.ColumnMappings.Add("Close", "Close");
            bulkCopy.ColumnMappings.Add("Volume", "Volume");
            bulkCopy.ColumnMappings.Add("OI", "OI");
        }

        private static async Task MergeCandlesAsync(SqlConnection connection, SqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            const string sql = "MERGE dbo.Instruments_OHLC WITH (HOLDLOCK) AS target "
                + "USING #HistoricalChunk AS source ON target.InstrumentToken = source.InstrumentToken "
                + "AND target.TimeFrame = source.TimeFrame AND target.TimeStamp = source.TimeStamp "
                + "WHEN MATCHED THEN UPDATE SET target.[Open] = source.[Open], target.High = source.High, "
                + "target.Low = source.Low, target.[Close] = source.[Close], target.Volume = source.Volume, "
                + "target.OI = source.OI "
                + "WHEN NOT MATCHED THEN INSERT (InstrumentToken, TimeStamp, TimeFrame, [Open], High, Low, [Close], Volume, OI) "
                + "VALUES (source.InstrumentToken, source.TimeStamp, source.TimeFrame, source.[Open], source.High, "
                + "source.Low, source.[Close], source.Volume, source.OI);";
            using (SqlCommand command = new SqlCommand(sql, connection, transaction))
            {
                command.CommandTimeout = 60;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private static async Task AdvanceCheckpointAsync(SqlConnection connection, SqlTransaction transaction,
            HistoricalChunkRequest request, CancellationToken cancellationToken)
        {
            const string sql = "UPDATE dbo.IngestionSyncState WITH (UPDLOCK, HOLDLOCK) "
                + "SET LastCompletedChunkStartDate = @ChunkStart, LastCompletedChunkEndDate = @ChunkEnd, "
                + "UpdatedUtc = GETUTCDATE() WHERE InstrumentToken = @InstrumentToken AND StreamType = @StreamType "
                + "AND LastCompletedChunkEndDate < @ChunkEnd; "
                + "IF @@ROWCOUNT = 0 AND NOT EXISTS (SELECT 1 FROM dbo.IngestionSyncState WITH (UPDLOCK, HOLDLOCK) "
                + "WHERE InstrumentToken = @InstrumentToken AND StreamType = @StreamType) "
                + "INSERT dbo.IngestionSyncState (InstrumentToken, StreamType, LastCompletedChunkStartDate, "
                + "LastCompletedChunkEndDate, UpdatedUtc) VALUES (@InstrumentToken, @StreamType, @ChunkStart, "
                + "@ChunkEnd, GETUTCDATE());";
            using (SqlCommand command = new SqlCommand(sql, connection, transaction))
            {
                command.Parameters.Add("@InstrumentToken", SqlDbType.BigInt).Value = request.InstrumentToken;
                command.Parameters.Add("@StreamType", SqlDbType.VarChar, 20).Value = request.StreamType;
                command.Parameters.Add("@ChunkStart", SqlDbType.DateTime).Value = request.RangeStart;
                command.Parameters.Add("@ChunkEnd", SqlDbType.DateTime).Value = request.RangeEnd;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
