using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace AlgoTrading.DataAccess.Infrastructure
{
    /// <summary>
    /// Explicit, additive installation of the critical-level pipeline schema. It is never invoked implicitly by
    /// reads or constructors, mirroring the trading-calendar installer.
    /// </summary>
    public sealed class MarketDataSchemaInstaller
    {
        private static readonly string[] Scripts = new string[]
        {
            "AlgoTrading.DataAccess.Sql.002_CriticalLevels.sql",
            "AlgoTrading.DataAccess.Sql.003_ConfigSeed.sql"
        };

        private readonly string connectionString;
        private readonly int commandTimeoutSeconds;

        public MarketDataSchemaInstaller(string connectionString, int commandTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A connection string is required.", nameof(connectionString));
            }

            if (commandTimeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
            }

            this.connectionString = connectionString;
            this.commandTimeoutSeconds = commandTimeoutSeconds;
        }

        public async Task InstallAsync(CancellationToken cancellationToken = default)
        {
            Assembly assembly = typeof(MarketDataSchemaInstaller).Assembly;
            await using SqlConnection connection = new SqlConnection(this.connectionString);
            await connection.OpenAsync(cancellationToken);
            for (int index = 0; index < Scripts.Length; index++)
            {
                string resourceName = Scripts[index];
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    throw new InvalidOperationException("Schema resource is missing: " + resourceName);
                }

                using StreamReader reader = new StreamReader(stream);
                string script = await reader.ReadToEndAsync();
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = script;
                command.CommandTimeout = this.commandTimeoutSeconds;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }
}
