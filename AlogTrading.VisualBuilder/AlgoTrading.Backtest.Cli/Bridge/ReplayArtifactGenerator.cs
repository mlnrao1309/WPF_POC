using System.Text.Json;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AlgoTrading.Models;
using AlgoTrading.Data;
using AlgoTrading.Aggregation;
using AlgoTrading.Backtest.Runtime;

namespace AlgoTrading.Backtest.Cli.Bridge;

public sealed class ReplayArtifactDto
{
    public string ArtifactKind { get; set; } = "";
    public string ArtifactName { get; set; } = "";
    public string PayloadText { get; set; } = "";
    public long RecordCount { get; set; }
}

public sealed class ReplayArtifactGenerator
{
    public List<ReplayArtifactDto> Generate(Guid runId, RuleDefinition rule,
        IReadOnlyDictionary<Timeframe, IBarSeries> prepared, IBarSeries baseSeries, IReadOnlyList<SignalObservation>? observations = null)
    {
        var artifacts = new List<ReplayArtifactDto>();
        var evalTf = rule.EvaluationTimeframe;

        if (!prepared.TryGetValue(evalTf, out var evalBars))
            throw new InvalidOperationException($"Evaluation bars for {evalTf} not found.");

        // 1. CANDLES ARTIFACT
        foreach (var dataset in prepared.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
        {
        var candleBars = dataset.Value;
        var timestamps = new List<string>();
        var closesAt = new List<string>();
        var open = new List<decimal>();
        var high = new List<decimal>();
        var low = new List<decimal>();
        var close = new List<decimal>();
        var volume = new List<decimal>();

        for (int i = 0; i < candleBars.Count; i++)
        {
            var t = DateTime.SpecifyKind(candleBars.DateTime.Span[i], DateTimeKind.Utc);
            var tClose = DateTime.SpecifyKind(TimeframeAccessor.BarEnd(candleBars, i), DateTimeKind.Utc);

            timestamps.Add(t.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            closesAt.Add(tClose.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            open.Add(candleBars.Open.Span[i]);
            high.Add(candleBars.High.Span[i]);
            low.Add(candleBars.Low.Span[i]);
            close.Add(candleBars.Close.Span[i]);
            volume.Add(candleBars.Volume.Span[i]);
        }

        var candlePayload = JsonSerializer.Serialize(new
        {
            timeframe = dataset.Key.ToString(),
            timestampsUtc = timestamps,
            closesAtUtc = closesAt,
            open,
            high,
            low,
            close,
            volume
        });

        artifacts.Add(new ReplayArtifactDto
        {
            ArtifactKind = "Candles",
            ArtifactName = dataset.Key.ToString(),
            PayloadText = candlePayload,
            RecordCount = candleBars.Count
        });

        }

        // 2. INDICATORS ARTIFACT
        var cache = new TimeframeCache(baseSeries, prepared.GetValueOrDefault(Timeframe.Parse("D")), prepared);
        var accessor = new TimeframeAccessor(baseSeries.InstrumentToken, baseSeries, cache);
        var indicatorProvider = new IndicatorProvider(accessor);

        var indicators = new List<IndicatorExpression>();
        CollectIndicators(rule.Eligibility, indicators);
        CollectIndicators(rule.Entry, indicators);
        CollectIndicators(rule.Protection, indicators);
        CollectIndicators(rule.Exit, indicators);
        CollectIndicators(rule.FullExit, indicators);

        var distinct = indicators
            .GroupBy(x => DatasetIdentity(x, evalTf), StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        foreach (var ind in distinct)
        {
            var identity = DatasetIdentity(ind, evalTf);
            var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
            var artifactName = $"{ind.Indicator}_{ind.Timeframe ?? evalTf}_{suffix}";
            var series = indicatorProvider.Resolve(ind, evalTf);
            var indTimestamps = new List<string>();

            var aligned = accessor.Resolve(ind.Timeframe ?? evalTf);
            for (int i = 0; i < aligned.TfBarCount; i++)
            {
                var t = DateTime.SpecifyKind(aligned.Bars.DateTime.Span[i], DateTimeKind.Utc);
                indTimestamps.Add(t.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            }

            var valuesList = new List<decimal?[]>();
            foreach (var output in series.Outputs) valuesList.Add(output);

            var indPayload = JsonSerializer.Serialize(new
            {
                key = artifactName,
                indicator = ind.Indicator.ToString(),
                parameters = ind.Params ?? new Dictionary<string, decimal>(),
                source = ind.Source,
                timeframe = ind.Timeframe?.ToString() ?? evalTf.ToString(),
                timestampsUtc = indTimestamps,
                outputNames = series.OutputNames,
                values = valuesList
            });

            artifacts.Add(new ReplayArtifactDto
            {
                ArtifactKind = "Indicators",
                ArtifactName = artifactName,
                PayloadText = indPayload,
                RecordCount = aligned.TfBarCount
            });
        }

        // Only persist genuine engine observations, never placeholder false values.
        if (observations is { Count: > 0 })
        {
            artifacts.Add(new ReplayArtifactDto
            {
                ArtifactKind = "Signals",
                ArtifactName = baseSeries.Timeframe.ToString(),
                RecordCount = observations.Count,
                PayloadText = JsonSerializer.Serialize(new
                {
                    key = "Signals",
                    recordedEvaluations = true,
                    timeframe = baseSeries.Timeframe.ToString(),
                    timestampsUtc = observations.Select(o => o.TimestampUtc.ToString("O")).ToArray(),
                    availableAtUtc = observations.Select(o => o.AvailableAtUtc.ToString("O")).ToArray(),
                    signalNames = new[] { "Eligibility", "Entry", "Protection", "Exit", "FullExit", "StopLoss", "Target" },
                    values = new[]
                    {
                        observations.Select(o => o.Eligibility).ToArray(),
                        observations.Select(o => o.Entry).ToArray(),
                        observations.Select(o => o.Protection).ToArray(),
                        observations.Select(o => o.Exit).ToArray(),
                        observations.Select(o => o.FullExit).ToArray(),
                        observations.Select(o => o.StopLoss).ToArray(),
                        observations.Select(o => o.Target).ToArray()
                    }
                })
            });
        }

        return artifacts;
    }

    // Root output/offset select from an exported dataset containing all outputs.
    // Nested output/offset change source values and must remain in its identity.
    private static string DatasetIdentity(IndicatorExpression ind, Timeframe defaultTf)
        => JsonSerializer.Serialize(IndicatorIdentity(ind, defaultTf));

    private static object IndicatorIdentity(IndicatorExpression ind, Timeframe defaultTf)
    {
        var tf = ind.Timeframe ?? defaultTf;
        return new
        {
            indicator = ind.Indicator.ToString(),
            timeframe = tf.ToString(),
            parameters = (ind.Params ?? new Dictionary<string, decimal>())
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new { name = p.Key, value = p.Value.ToString("G29", CultureInfo.InvariantCulture) })
                .ToArray(),
            source = SourceIdentity(ind.Source, tf)
        };
    }

    private static object? SourceIdentity(ValueExpression? source, Timeframe tf) => source switch
    {
        null => null,
        FieldExpression f => new { kind = "field", field = f.Field.ToString(), timeframe = (f.Timeframe ?? tf).ToString(), offset = f.Offset },
        IndicatorExpression i => new { kind = "indicator", dataset = IndicatorIdentity(i, tf), output = i.Output ?? "", offset = i.Offset },
        NumberExpression n => new { kind = "number", value = n.Value.ToString("G29", CultureInfo.InvariantCulture) },
        ParameterExpression p => new { kind = "parameter", name = p.Name },
        CalculationExpression c => new { kind = "calculation", op = c.Op.ToString(), left = SourceIdentity(c.Left, tf), right = SourceIdentity(c.Right, tf) },
        _ => throw new NotSupportedException($"Unsupported replay source: {source.GetType().Name}")
    };

    private void CollectIndicators(Node? node, List<IndicatorExpression> output)
    {
        if (node is ConditionGroup group)
        {
            foreach (var child in group.Children) CollectIndicators(child, output);
        }
        else if (node is Condition cond)
        {
            CollectExpr(cond.Left, output);
            CollectExpr(cond.Right, output);
        }
    }

    private void CollectExpr(ValueExpression? expr, List<IndicatorExpression> output)
    {
        if (expr is IndicatorExpression ind)
        {
            output.Add(ind);
            if (ind.Source != null) CollectExpr(ind.Source, output);
        }
        else if (expr is CalculationExpression calc)
        {
            CollectExpr(calc.Left, output);
            CollectExpr(calc.Right, output);
        }
    }
}
