-- Run against Algo_Trading_CFCore. No table changes or historical imports.
CREATE OR ALTER PROCEDURE dbo.StrategyBacktest_Start
    @RunId uniqueidentifier,
    @StrategyName nvarchar(100),
    @StrategyVersion nvarchar(100),
    @InstrumentToken int,
    @DataProvider nvarchar(50),
    @ExecutionModel nvarchar(100),
    @BacktestFrom datetimeoffset(7),
    @BacktestTo datetimeoffset(7),
    @ConfigurationJson nvarchar(max),
    @InitialCapital decimal(28,8)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@ConfigurationJson) <> 1
        THROW 51000, 'Run configuration must be valid JSON.', 1;
    IF NULLIF(LTRIM(RTRIM(@StrategyName)), N'') IS NULL
        THROW 51000, 'Strategy name is required for run history.', 1;

    BEGIN TRY
        BEGIN TRANSACTION;
        DECLARE @StrategyId int, @Matches int;
        SELECT @StrategyId = MIN(StrategyId), @Matches = COUNT(*)
        FROM dbo.StrategyMain WITH (UPDLOCK, HOLDLOCK)
        WHERE StrategyName = @StrategyName;
        IF @Matches > 1
            THROW 51000, 'Multiple strategies have this name. Use a unique strategy name.', 1;
        IF @StrategyId IS NULL
        BEGIN
            INSERT dbo.StrategyMain (StrategyName, IsBacktest, IsApproved, IsLive)
            VALUES (@StrategyName, 1, 0, 0);
            SET @StrategyId = CONVERT(int, SCOPE_IDENTITY());
        END;

        INSERT dbo.StrategyBacktestResult
            (RunId, StrategyId, StrategyVersion, InstrumentId, DataProvider,
             ProviderToken, ResultUnit, ExecutionModel, RunStatus,
             BacktestFrom, BacktestTo, ConfigurationJson, InitialCapital)
        VALUES
            (@RunId, @StrategyId, @StrategyVersion, CONVERT(nvarchar(100), @InstrumentToken),
             @DataProvider, CONVERT(nvarchar(100), @InstrumentToken), N'INR',
             @ExecutionModel, 'Running', @BacktestFrom, @BacktestTo,
             @ConfigurationJson, @InitialCapital);
        COMMIT;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK;
        THROW;
    END CATCH;
END;
GO

CREATE OR ALTER PROCEDURE dbo.StrategyBacktest_Complete
    @RunId uniqueidentifier,
    @ResultJson nvarchar(max),
    @ResultFilePath nvarchar(2048)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@ResultJson) <> 1
        THROW 51000, 'Backtest result must be valid JSON.', 1;
    DECLARE @ResultRunId uniqueidentifier = TRY_CONVERT(uniqueidentifier,
        JSON_VALUE(@ResultJson, '$.reproducibility.runId'));
    IF @ResultRunId IS NULL OR @ResultRunId <> @RunId
        THROW 51000, 'Result RunId does not match the history run.', 1;

    UPDATE dbo.StrategyBacktestResult
    SET RunStatus = 'Completed',
        FinishedAt = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        ArtifactsPath = @ResultFilePath, ErrorMessage = NULL,
        NetProfit = TRY_CONVERT(decimal(28,8), JSON_VALUE(@ResultJson, '$.metrics.totalNetPnL')),
        NetProfitPercent = TRY_CONVERT(decimal(18,8), JSON_VALUE(@ResultJson, '$.metrics.netProfitPct')),
        FinalEquity = InitialCapital + TRY_CONVERT(decimal(28,8), JSON_VALUE(@ResultJson, '$.metrics.totalNetPnL')),
        MaxDrawdown = TRY_CONVERT(decimal(28,8), JSON_VALUE(@ResultJson, '$.metrics.maxDrawdownAbsolute')),
        MaxDrawdownPercent = TRY_CONVERT(decimal(18,8), JSON_VALUE(@ResultJson, '$.metrics.maxDrawdownPct')),
        ClosedTrades = TRY_CONVERT(bigint, JSON_VALUE(@ResultJson, '$.metrics.totalTrades')),
        WinningTrades = TRY_CONVERT(bigint, JSON_VALUE(@ResultJson, '$.metrics.winningTrades')),
        LosingTrades = TRY_CONVERT(bigint, JSON_VALUE(@ResultJson, '$.metrics.losingTrades')),
        BreakevenTrades = TRY_CONVERT(bigint, JSON_VALUE(@ResultJson, '$.metrics.breakevenTrades')),
        WinRatePercent = TRY_CONVERT(decimal(18,8), JSON_VALUE(@ResultJson, '$.metrics.winRatePct')),
        TradesJson = JSON_QUERY(@ResultJson, '$.trades'),
        EquityJson = JSON_QUERY(@ResultJson, '$.equityCurve'),
        BreakdownJson = JSON_QUERY(@ResultJson, '$.metrics')
    WHERE RunId = @RunId AND RunStatus = 'Running';
    IF @@ROWCOUNT <> 1
        THROW 51000, 'Running backtest history row was not found.', 1;
END;
GO

CREATE OR ALTER PROCEDURE dbo.StrategyBacktest_Fail
    @RunId uniqueidentifier,
    @ErrorMessage nvarchar(2000),
    @Cancelled bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.StrategyBacktestResult
    SET RunStatus = CASE WHEN @Cancelled = 1 THEN 'Cancelled' ELSE 'Failed' END,
        FinishedAt = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'),
        ErrorMessage = @ErrorMessage
    WHERE RunId = @RunId AND RunStatus = 'Running';
END;
GO

CREATE OR ALTER PROCEDURE dbo.StrategyBacktest_Recent
    @Limit int = 50
AS
BEGIN
    SET NOCOUNT ON;
    IF @Limit < 1 OR @Limit > 500
        THROW 51000, 'History limit must be between 1 and 500.', 1;
    SELECT TOP (@Limit)
        r.RunId, r.StrategyId, s.StrategyName,
        r.ProviderToken AS Instrument,
        JSON_VALUE(r.ConfigurationJson, '$.runMode') AS RunMode,
        JSON_VALUE(r.ConfigurationJson, '$.rule.direction') AS Direction,
        r.StartedAt, r.RunStatus AS Status, r.ErrorMessage,
        r.NetProfit AS NetPnL, r.ClosedTrades AS TotalTrades,
        r.ArtifactsPath AS ResultFilePath
    FROM dbo.StrategyBacktestResult r
    JOIN dbo.StrategyMain s ON s.StrategyId = r.StrategyId
    ORDER BY r.StartedAt DESC, r.RunId DESC;
END;
GO
