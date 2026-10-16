using AlgoTrading.Models;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Reflection;

namespace AlgoTrading.DataAccess.Calendar
{
    public sealed class SqlTradingCalendarRepository : ITradingCalendarRepository
    {
        private readonly string connectionString;
        private readonly int commandTimeoutSeconds;

        public SqlTradingCalendarRepository(string connectionString, int commandTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(connectionString) || commandTimeoutSeconds <= 0)
            {
                throw new ArgumentException("A connection string and positive command timeout are required.");
            }
            this.connectionString = connectionString;
            this.commandTimeoutSeconds = commandTimeoutSeconds;
        }

        private SqlCommand CreateCommand(SqlConnection connection, string text)
        {
            SqlCommand command = connection.CreateCommand();
            command.CommandText = text;
            command.CommandTimeout = this.commandTimeoutSeconds;
            return command;
        }

        private void AddParameter(SqlCommand command, string name, SqlDbType type, object value, int size = 0)
        {
            SqlParameter parameter = command.Parameters.Add(name, type);
            if (size != 0)
            {
                parameter.Size = size;
            }
            parameter.Value = value;
        }

        /// <summary>Explicit additive schema installation, never called implicitly by reads or constructors.</summary>
        public async Task CreateSchemaAsync(CancellationToken cancellationToken = default)
        {
            Assembly assembly = typeof(SqlTradingCalendarRepository).Assembly;
            using Stream? stream = assembly.GetManifestResourceStream("AlgoTrading.DataAccess.Sql.001_TradingCalendar.sql");
            if (stream == null)
            {
                throw new InvalidOperationException("Calendar schema resource is missing.");
            }
            using StreamReader reader = new StreamReader(stream);
            string script = await reader.ReadToEndAsync(cancellationToken);
            await using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = CreateCommand(connection, script);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task SaveSourceAsync(CalendarSourceDocument source, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            await using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = CreateCommand(connection,
                "INSERT dbo.TradingCalendarSource (SourceId, ExchangeCode, SourceUri, RetrievedAt, ContentType, ContentSha256, Content) " +
                "VALUES (@Id, @Exchange, @Uri, @Retrieved, @Media, @Hash, @Content);");
            AddParameter(command, "@Id", SqlDbType.UniqueIdentifier, source.SourceId);
            AddParameter(command, "@Exchange", SqlDbType.NVarChar, source.ExchangeCode, 16);
            AddParameter(command, "@Uri", SqlDbType.NVarChar, source.SourceUri.AbsoluteUri, 2048);
            AddParameter(command, "@Retrieved", SqlDbType.DateTimeOffset, source.RetrievedAt);
            AddParameter(command, "@Media", SqlDbType.NVarChar, source.ContentType, 128);
            AddParameter(command, "@Hash", SqlDbType.Char, source.GetContentSha256(), 64);
            AddParameter(command, "@Content", SqlDbType.NVarChar, source.Content, -1);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<bool> HasCalendarDaysAsync(CancellationToken cancellationToken = default)
        {
            await using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = CreateCommand(connection,
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.TradingCalendarDay) THEN 1 ELSE 0 END;");
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture) == 1;
        }

        /// <summary>Publishes all dates atomically. Existing revisions are immutable; use a new revision for corrections.</summary>
        public async Task PublishAsync(ExchangeCalendarSnapshot calendar, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(calendar);
            await using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            await using SqlCommand header = CreateCommand(connection,
                "IF NOT EXISTS (SELECT 1 FROM dbo.TradingCalendarSource WHERE SourceId=@Source AND ExchangeCode=@Exchange) " +
                "THROW 51001, 'Calendar source is absent or belongs to another exchange.', 1; " +
                "INSERT dbo.TradingCalendarRevision (CalendarId, ExchangeCode, SegmentCode, Revision, FirstDate, LastDate, SourceId) " +
                "VALUES (@Id, @Exchange, @Segment, @Revision, @First, @Last, @Source);");
            header.Transaction = transaction;
            AddParameter(header, "@Id", SqlDbType.UniqueIdentifier, calendar.CalendarId);
            AddParameter(header, "@Exchange", SqlDbType.NVarChar, calendar.ExchangeCode, 16);
            AddParameter(header, "@Segment", SqlDbType.NVarChar, calendar.SegmentCode, 32);
            AddParameter(header, "@Revision", SqlDbType.NVarChar, calendar.Revision, 80);
            AddParameter(header, "@First", SqlDbType.Date, calendar.FirstDate.ToDateTime(TimeOnly.MinValue));
            AddParameter(header, "@Last", SqlDbType.Date, calendar.LastDate.ToDateTime(TimeOnly.MinValue));
            AddParameter(header, "@Source", SqlDbType.UniqueIdentifier, calendar.SourceId);
            await header.ExecuteNonQueryAsync(cancellationToken);
            foreach (ExchangeCalendarDay day in calendar.Days)
            {
                await using SqlCommand detail = CreateCommand(connection,
                    "INSERT dbo.TradingCalendarDay (CalendarId, TradingDate, OpensAt, ClosesAt, Reason) VALUES (@Id, @Date, @Open, @Close, @Reason);");
                detail.Transaction = transaction;
                AddParameter(detail, "@Id", SqlDbType.UniqueIdentifier, calendar.CalendarId);
                AddParameter(detail, "@Date", SqlDbType.Date, day.Date.ToDateTime(TimeOnly.MinValue));
                object opening = DBNull.Value;
                object closing = DBNull.Value;
                if (day.OpensAt.HasValue && day.ClosesAt.HasValue)
                {
                    opening = day.OpensAt.Value.ToTimeSpan();
                    closing = day.ClosesAt.Value.ToTimeSpan();
                }
                AddParameter(detail, "@Open", SqlDbType.Time, opening);
                AddParameter(detail, "@Close", SqlDbType.Time, closing);
                AddParameter(detail, "@Reason", SqlDbType.NVarChar, day.Reason, 256);
                await detail.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task<ExchangeCalendarSnapshot?> LoadAsync(string exchangeCode, string segmentCode, string revision,
            DateTimeOffset asOf, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(exchangeCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(segmentCode);
            ArgumentException.ThrowIfNullOrWhiteSpace(revision);
            await using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = CreateCommand(connection,
                "SELECT CalendarId, FirstDate, LastDate, SourceId FROM dbo.TradingCalendarRevision " +
                "WHERE ExchangeCode=@Exchange AND SegmentCode=@Segment AND Revision=@Revision AND RecordedAt<=@AsOf;");
            AddParameter(command, "@Exchange", SqlDbType.NVarChar, exchangeCode, 16);
            AddParameter(command, "@Segment", SqlDbType.NVarChar, segmentCode, 32);
            AddParameter(command, "@Revision", SqlDbType.NVarChar, revision, 80);
            AddParameter(command, "@AsOf", SqlDbType.DateTimeOffset, asOf);
            Guid calendarId;
            Guid sourceId;
            DateOnly firstDate;
            DateOnly lastDate;
            await using (SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }
                calendarId = reader.GetGuid(0);
                firstDate = DateOnly.FromDateTime(reader.GetDateTime(1));
                lastDate = DateOnly.FromDateTime(reader.GetDateTime(2));
                sourceId = reader.GetGuid(3);
            }
            await using SqlCommand details = CreateCommand(connection,
                "SELECT TradingDate, OpensAt, ClosesAt, Reason FROM dbo.TradingCalendarDay WHERE CalendarId=@Id ORDER BY TradingDate;");
            AddParameter(details, "@Id", SqlDbType.UniqueIdentifier, calendarId);
            List<ExchangeCalendarDay> days = new List<ExchangeCalendarDay>();
            await using (SqlDataReader reader = await details.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    TimeOnly? opening = null;
                    TimeOnly? closing = null;
                    if (!reader.IsDBNull(1))
                    {
                        opening = TimeOnly.FromTimeSpan(reader.GetTimeSpan(1));
                    }
                    if (!reader.IsDBNull(2))
                    {
                        closing = TimeOnly.FromTimeSpan(reader.GetTimeSpan(2));
                    }
                    days.Add(new ExchangeCalendarDay(DateOnly.FromDateTime(reader.GetDateTime(0)), opening, closing, reader.GetString(3)));
                }
            }
            return new ExchangeCalendarSnapshot(calendarId, exchangeCode, segmentCode, revision, firstDate, lastDate, sourceId, days);
        }
    }
}
