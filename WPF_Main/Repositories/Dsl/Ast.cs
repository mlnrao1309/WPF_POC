// Dsl/Ast.cs
namespace AlgoTrading.Screener.Dsl;

public abstract record Expr;

public sealed record Num(decimal Value)              : Expr;
public sealed record Str(string Value)               : Expr;
public sealed record BoolLit(bool Value)             : Expr;
public sealed record Ident(string Name)              : Expr;
public sealed record Member(Expr Target, string Name) : Expr;
public sealed record Index(Expr Target, Expr Offset) : Expr;
public sealed record Call(string Name, IReadOnlyList<Expr> Args) : Expr;
public sealed record Unary(string Op, Expr Operand)  : Expr;
public sealed record Binary(string Op, Expr Left, Expr Right) : Expr;
public sealed record Between(Expr Value, Expr Lower, Expr Upper) : Expr;

public sealed record QueryNode(
    string SourceVar,
    string SourceCollection,
    IReadOnlyList<LetClause> Lets,
    Expr? Where,
    IReadOnlyList<OrderItem> OrderBy,
    IReadOnlyList<SelectItem> Select,
    int? Take,
    bool PerBar);            // <-- new: false = per symbol (default), true = per bar

public sealed record LetClause(string Name, Expr Value);
public sealed record OrderItem(Expr Value, bool Descending);
public sealed record SelectItem(Expr Value, string? Alias);