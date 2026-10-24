namespace AlgoTrading.Backtest.Cli.Bridge;

/// <summary>
/// The single contact point between T7 (CLI/daemon) and the engine layer.
/// After T3 (Data) and T6 (Backtest) land, provide an implementation that
/// references those projects and replace the registration in Program.cs.
/// </summary>
public interface IBacktestFacade
{
    FacadeResult Run(FacadeRequest request, CancellationToken cancellationToken);
    void CompleteRun(Guid runId, string resultJson, string resultFilePath);
    void FailRun(Guid runId, string errorMessage);
}
