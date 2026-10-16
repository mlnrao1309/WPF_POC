using System;
using System.Data;
using System.IO;
using AlgoTrading.Models;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.DataAccess.Pivots
{
    /// <summary>Read-only SQL formula implementation. It does not invoke the destructive legacy procedures.</summary>
    public sealed class SqlDailyPivotFormula : IDailyPivotFormula
    {
        private readonly string connectionString;
        private readonly int commandTimeoutSeconds;
        private readonly string sql;

        public SqlDailyPivotFormula(string connectionString, int commandTimeoutSeconds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            if (commandTimeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
            }
            this.connectionString = connectionString;
            this.commandTimeoutSeconds = commandTimeoutSeconds;
            Stream? resource = typeof(SqlDailyPivotFormula).Assembly.GetManifestResourceStream("AlgoTrading.DataAccess.Sql.DailyPivotProjection.sql");
            if (resource == null)
            {
                throw new InvalidOperationException("Daily pivot SQL resource is missing.");
            }
            using (resource)
            {
                using (StreamReader reader = new StreamReader(resource))
                {
                    this.sql = reader.ReadToEnd();
                }
            }
        }

        public DailyPivotNumbers Calculate(decimal high, decimal low, decimal close, decimal openQ, decimal closeQ)
        {
            if (high < low || close < low || close > high)
            {
                throw new ArgumentException("Source OHLC bounds are invalid.");
            }
            using (SqlConnection connection = new SqlConnection(this.connectionString))
            {
                using (SqlCommand command = connection.CreateCommand())
                {
                    command.CommandText = this.sql;
                    command.CommandTimeout = this.commandTimeoutSeconds;
                    AddPrice(command, "@High", high);
                    AddPrice(command, "@Low", low);
                    AddPrice(command, "@Close", close);
                    AddPrice(command, "@OpenQ", openQ);
                    AddPrice(command, "@CloseQ", closeQ);
                    connection.Open();
                    using (SqlDataReader reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                        {
                            throw new InvalidOperationException("SQL returned no pivot calculation.");
                        }
                        SqlRangeExtensionPivots pivots = new SqlRangeExtensionPivots(reader.GetDecimal(0), reader.GetDecimal(1),
                            reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6),
                            reader.GetDecimal(7), reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10));
                        return new DailyPivotNumbers(pivots, reader.GetBoolean(11), reader.GetBoolean(12));
                    }
                }
            }
        }

        private void AddPrice(SqlCommand command, string name, decimal value)
        {
            if (Math.Round(value, 2) != value || Math.Abs(value) > 9999999999999999.99m)
            {
                throw new ArgumentException("Source price must fit decimal(18,2).", name);
            }
            SqlParameter parameter = command.Parameters.Add(name, SqlDbType.Decimal);
            parameter.Precision = 18;
            parameter.Scale = 2;
            parameter.Value = value;
        }
    }
}
