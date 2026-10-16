using AlgoTrading.Models.Rules;

internal sealed class CrossoverTestCollector
{
    private readonly List<CrossoverOccurrence> detected = new List<CrossoverOccurrence>();

    internal List<CrossoverOccurrence> Detected
    {
        get
        {
            return detected;
        }
    }
    private readonly List<CrossoverOccurrence> completed = new List<CrossoverOccurrence>();

    internal List<CrossoverOccurrence> Completed
    {
        get
        {
            return completed;
        }
    }

    internal void Attach(RuleCrossoverMonitor monitor)
    {
        monitor.CrossoverDetected += OnDetected;
        monitor.CrossoverNextCandleAvailable += OnNextCandle;
    }

    private void OnDetected(object? sender, CrossoverEventArgs arguments)
    {
        Detected.Add(arguments.Occurrence);
    }

    private void OnNextCandle(object? sender, CrossoverEventArgs arguments)
    {
        Completed.Add(arguments.Occurrence);
    }
}


