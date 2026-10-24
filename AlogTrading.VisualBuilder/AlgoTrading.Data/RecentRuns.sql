-- Preparation uses temporary tables only. It never inserts strategy/run/result rows.
CREATE OR ALTER PROCEDURE dbo.StrategyBacktest_PrepareData
    @InstrumentToken bigint, @RuleJson nvarchar(max),
    @FromUtc datetime2(7), @ToUtc datetime2(7), @ValidateOnly bit = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    IF ISJSON(@RuleJson) <> 1 OR @InstrumentToken <= 0 OR @FromUtc >= @ToUtc
        THROW 51001, 'Invalid strategy, instrument or data range.', 1;
    DECLARE @Base nvarchar(10)=JSON_VALUE(@RuleJson,'$.baseTimeframe'),
            @Evaluation nvarchar(10)=JSON_VALUE(@RuleJson,'$.evaluationTimeframe'),
            @FromLocal datetime2(7)=DATEADD(minute,330,@FromUtc),
            @ToLocal datetime2(7)=DATEADD(minute,330,@ToUtc),
            @Message nvarchar(2048), @Symbol nvarchar(100);
    IF @Base IS NULL OR @Evaluation IS NULL
        THROW 51001, 'Base and evaluation timeframes are required.', 1;
    SELECT TOP(1) @Symbol=TradingSymbol FROM dbo.MarketWatch
        WHERE InstrumentToken=@InstrumentToken AND IsDeleted=0 ORDER BY Idx;

    CREATE TABLE #Frames(Code nvarchar(10) PRIMARY KEY, DbCode nvarchar(10), Minutes int NULL, Kind char(1));
    CREATE TABLE #Nodes(Id int IDENTITY PRIMARY KEY, Payload nvarchar(max), Processed bit DEFAULT(0));
    INSERT #Nodes(Payload) VALUES(@RuleJson);
    DECLARE @NodeId int, @Payload nvarchar(max), @NeedDaily bit=0;
    WHILE EXISTS(SELECT 1 FROM #Nodes WHERE Processed=0)
    BEGIN
        SELECT TOP(1) @NodeId=Id,@Payload=Payload FROM #Nodes WHERE Processed=0 ORDER BY Id;
        UPDATE #Nodes SET Processed=1 WHERE Id=@NodeId;
        INSERT #Frames(Code)
            SELECT DISTINCT CONVERT(nvarchar(10),j.value) FROM OPENJSON(@Payload) j
            WHERE j.[key] IN ('evaluationTimeframe','baseTimeframe','timeframe') AND j.type=1
              AND NOT EXISTS(SELECT 1 FROM #Frames f WHERE f.Code=j.value);
        IF EXISTS(SELECT 1 FROM OPENJSON(@Payload) WHERE [key]='indicator' AND value IN ('PivotPointsClassic','PivotPointsCamarilla')) SET @NeedDaily=1;
        INSERT #Nodes(Payload) SELECT value FROM OPENJSON(@Payload) WHERE type IN (4,5);
    END;
    IF @NeedDaily=1 AND NOT EXISTS(SELECT 1 FROM #Frames WHERE Code='D') INSERT #Frames(Code) VALUES('D');
    UPDATE #Frames SET Kind=CASE WHEN Code IN ('D','W','M') THEN Code ELSE 'I' END,
        Minutes=CASE WHEN RIGHT(Code,1) COLLATE Latin1_General_100_BIN2='m' THEN TRY_CONVERT(int,LEFT(Code,LEN(Code)-1))
                     WHEN RIGHT(Code,1) COLLATE Latin1_General_100_BIN2='H' THEN TRY_CONVERT(int,LEFT(Code,LEN(Code)-1))*60 END;
    IF EXISTS(SELECT 1 FROM #Frames WHERE Kind='I' AND (Minutes IS NULL OR Minutes<=0))
        THROW 51001, 'Strategy contains an unsupported timeframe.', 1;
    UPDATE #Frames SET DbCode=CASE WHEN Kind='I' THEN CONVERT(nvarchar(10),Minutes) ELSE Code END;

    -- Stored timestamps are IST. Convert only request boundaries and returned timestamps.
    -- Use stored rows as supplied; there is no trading-day/hour or completeness policy.
    CREATE TABLE #Candles(Code nvarchar(10), OpensAt datetime2(7), ClosesAt datetime2(7),
        [Open] decimal(28,8), High decimal(28,8), Low decimal(28,8), [Close] decimal(28,8),
        Volume decimal(28,8), OI decimal(28,8), SourceCode nvarchar(10) NULL);
    CREATE INDEX IX_PreparedCandles ON #Candles(Code,OpensAt);
    INSERT #Candles
        SELECT f.Code,o.TimeStamp,
            CASE f.Kind WHEN 'I' THEN DATEADD(minute,f.Minutes,o.TimeStamp)
                WHEN 'D' THEN DATEADD(day,1,CONVERT(date,o.TimeStamp))
                WHEN 'W' THEN DATEADD(day,7-DATEDIFF(day,'19000101',CONVERT(date,o.TimeStamp))%7,CONVERT(date,o.TimeStamp))
                ELSE DATEADD(day,1,EOMONTH(o.TimeStamp)) END,
            o.[Open],o.High,o.Low,o.[Close],o.Volume,o.OI,NULL
        FROM dbo.vw_InstrumentBars o JOIN #Frames f ON RTRIM(o.TimeFrame)=f.DbCode
        WHERE o.InstrumentToken=@InstrumentToken AND o.TimeStamp>=@FromLocal AND o.TimeStamp<=@ToLocal;

    CREATE TABLE #SourceRows(OpensAt datetime2(7), [Open] decimal(28,8), High decimal(28,8), Low decimal(28,8),
        [Close] decimal(28,8), Volume decimal(28,8), OI decimal(28,8));
    DECLARE @Code nvarchar(10), @Kind char(1), @Minutes int, @Source nvarchar(10), @SourceMinutes int;
    DECLARE frames CURSOR LOCAL FAST_FORWARD FOR SELECT Code,Kind,Minutes FROM #Frames
        WHERE NOT EXISTS(SELECT 1 FROM #Candles c WHERE c.Code=#Frames.Code)
        ORDER BY CASE Kind WHEN 'I' THEN 0 WHEN 'D' THEN 1 WHEN 'W' THEN 2 ELSE 3 END,Minutes;
    OPEN frames;
    FETCH NEXT FROM frames INTO @Code,@Kind,@Minutes;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SET @Source=NULL;
        SET @SourceMinutes=NULL;
        -- Locate a compatible stored source only when the required timeframe is absent.
        -- Never attempt to split a larger source candle into smaller target candles.
        SELECT TOP(1) @Source=RTRIM(o.TimeFrame), @SourceMinutes=TRY_CONVERT(int,RTRIM(o.TimeFrame))
        FROM dbo.vw_InstrumentBars o
        WHERE o.InstrumentToken=@InstrumentToken AND o.TimeStamp>=@FromLocal AND o.TimeStamp<=@ToLocal
          AND ((@Kind='I' AND TRY_CONVERT(int,RTRIM(o.TimeFrame))>0
                AND TRY_CONVERT(int,RTRIM(o.TimeFrame))<@Minutes
                AND @Minutes%NULLIF(TRY_CONVERT(int,RTRIM(o.TimeFrame)),0)=0)
            OR (@Kind IN ('D','W','M') AND TRY_CONVERT(int,RTRIM(o.TimeFrame))>0)
            OR (@Kind IN ('W','M') AND RTRIM(o.TimeFrame)='D'))
        ORDER BY CASE WHEN @Kind IN ('W','M') AND RTRIM(o.TimeFrame)='D' THEN 0
                      WHEN RTRIM(o.TimeFrame)=(SELECT DbCode FROM #Frames WHERE Code=@Base) THEN 1 ELSE 2 END,
            TRY_CONVERT(int,RTRIM(o.TimeFrame)) DESC;
        IF @Source IS NULL
        BEGIN
            SET @Message=CONCAT('Cannot supply or aggregate required ',@Code,' candles for instrument ',@InstrumentToken,
                ': no compatible stored source exists in the requested range. Preparation stopped.');
            THROW 51001,@Message,1;
        END;
        TRUNCATE TABLE #SourceRows;
        INSERT #SourceRows SELECT o.TimeStamp,o.[Open],o.High,o.Low,o.[Close],o.Volume,o.OI
            FROM dbo.vw_InstrumentBars o
            WHERE o.InstrumentToken=@InstrumentToken AND RTRIM(o.TimeFrame)=@Source
              AND o.TimeStamp>=@FromLocal AND o.TimeStamp<=@ToLocal;

        ;WITH Anchored AS
        (
            SELECT *,MIN(OpensAt) OVER(PARTITION BY CONVERT(date,OpensAt)) DayStart FROM #SourceRows
        ), Bucketed AS
        (
            SELECT *,CASE @Kind
                WHEN 'I' THEN DATEADD(minute,DATEDIFF(minute,DayStart,OpensAt)/@Minutes*@Minutes,DayStart)
                WHEN 'D' THEN CONVERT(datetime2(7),CONVERT(date,OpensAt))
                WHEN 'W' THEN CONVERT(datetime2(7),DATEADD(day,-DATEDIFF(day,'19000101',CONVERT(date,OpensAt))%7,CONVERT(date,OpensAt)))
                ELSE CONVERT(datetime2(7),DATEFROMPARTS(YEAR(OpensAt),MONTH(OpensAt),1)) END BucketStart
            FROM Anchored
        ), Ranked AS
        (
            SELECT *,ROW_NUMBER() OVER(PARTITION BY BucketStart ORDER BY OpensAt) FirstRow,
                ROW_NUMBER() OVER(PARTITION BY BucketStart ORDER BY OpensAt DESC) LastRow FROM Bucketed
        )
        INSERT #Candles
            SELECT @Code,MIN(OpensAt),
                CASE @Kind WHEN 'I' THEN DATEADD(minute,@Minutes,BucketStart)
                    WHEN 'D' THEN DATEADD(day,1,BucketStart) WHEN 'W' THEN DATEADD(day,7,BucketStart)
                    ELSE DATEADD(month,1,BucketStart) END,
                MAX(CASE WHEN FirstRow=1 THEN [Open] END),MAX(High),MIN(Low),
                MAX(CASE WHEN LastRow=1 THEN [Close] END),SUM(Volume),MAX(CASE WHEN LastRow=1 THEN OI END),
                CASE WHEN @Source='D' THEN 'D' ELSE CONCAT(@SourceMinutes,'m') END
            FROM Ranked GROUP BY BucketStart;
        IF NOT EXISTS(SELECT 1 FROM #Candles WHERE Code=@Code)
        BEGIN
            SET @Message=CONCAT('Cannot build required ',@Code,' candles from ',@Source,'. Preparation stopped.');
            THROW 51001,@Message,1;
        END;
        FETCH NEXT FROM frames INTO @Code,@Kind,@Minutes;
    END;
    CLOSE frames; DEALLOCATE frames;
    IF @ValidateOnly=1
    BEGIN
        SELECT Code AS TimeframeCode,COUNT(*) CandleCount FROM #Candles GROUP BY Code;
        RETURN;
    END;
    SELECT Code AS TimeframeCode,SourceCode AS SourceTimeframeCode,COALESCE(@Symbol,'') AS Symbol,
        DATEADD(minute,-330,OpensAt) AS [DateTime],DATEADD(minute,-330,ClosesAt) AS ClosedAt,
        [Open],High,Low,[Close],Volume,OI FROM #Candles ORDER BY Code,OpensAt;
END;
GO

IF COL_LENGTH('dbo.StrategyBacktestResult', 'IsBacktestApproved') IS NULL
    ALTER TABLE dbo.StrategyBacktestResult ADD IsBacktestApproved bit NULL;
IF COL_LENGTH('dbo.StrategyBacktestResult', 'RiskAssessmentJson') IS NULL
    ALTER TABLE dbo.StrategyBacktestResult ADD RiskAssessmentJson nvarchar(max) NULL
        CONSTRAINT CK_StrategyBacktestResult_RiskAssessmentJson CHECK (RiskAssessmentJson IS NULL OR ISJSON(RiskAssessmentJson) = 1);
GO
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
        BreakdownJson = JSON_QUERY(@ResultJson, '$.metrics'),
        IsBacktestApproved = CASE JSON_VALUE(@ResultJson, '$.riskAssessment.isBacktestApproved')
            WHEN 'true' THEN 1 WHEN 'false' THEN 0 ELSE NULL END,
        RiskAssessmentJson = JSON_QUERY(@ResultJson, '$.riskAssessment')
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
        r.IsBacktestApproved, JSON_VALUE(r.ConfigurationJson, '$.lossBasis') AS LossBasis,
        r.ArtifactsPath AS ResultFilePath
    FROM dbo.StrategyBacktestResult r
    JOIN dbo.StrategyMain s ON s.StrategyId = r.StrategyId
    ORDER BY r.StartedAt DESC, r.RunId DESC;
END;
GO
