// Dsl/SymbolValue.cs
namespace AlgoTrading.Screener.Dsl;

public sealed record SymbolValue(string Symbol, long InstrumentToken, IReadOnlyDictionary<string, Series> Fields);