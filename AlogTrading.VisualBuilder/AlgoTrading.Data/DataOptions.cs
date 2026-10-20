namespace AlgoTrading.Data;

public sealed class DataOptions
{
    public string Source { get; set; } = "Sql";
    public SqlOptions Sql { get; set; } = new();
    public CsvOptions Csv { get; set; } = new();
    public ParquetOptions Parquet { get; set; } = new();
}

public sealed class SqlOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class CsvOptions
{
    public string FolderPath { get; set; } = string.Empty;
}

public sealed class ParquetOptions
{
    public string FolderPath { get; set; } = string.Empty;
}