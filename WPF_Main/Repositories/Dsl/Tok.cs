// Dsl/Tok.cs
namespace AlgoTrading.Screener.Dsl;

public enum TokKind { Ident, Number, String, Punct, Op, Keyword, Eof }

public readonly record struct Tok(TokKind Kind, string Text, int Position, object? Value = null);