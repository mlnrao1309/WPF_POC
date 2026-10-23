// Dsl/Parser.cs
namespace AlgoTrading.Screener.Dsl;

public sealed class Parser
{
    private readonly List<Tok> _t;
    private int _i;
    private string _sourceVar = string.Empty;

    public Parser(string source) => _t = Lexer.Lex(source);
    public static QueryNode Parse(string source) => new Parser(source).ParseQuery();

    private Tok Cur => _t[_i];
    private Tok Peek(int k = 1) => _t[Math.Min(_i + k, _t.Count - 1)];
    private Tok Advance() => _t[_i++];
    private bool IsKw(string k) => Cur.Kind == TokKind.Keyword && Eq(Cur.Text, k);
    private bool IsIdentAny() => Cur.Kind is TokKind.Ident or TokKind.Keyword;
    private bool IsPunct(string p) => Cur.Kind == TokKind.Punct && Cur.Text == p;
    private bool IsOp(string o) => Cur.Kind == TokKind.Op && Cur.Text == o;
    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private FormatException Err(string msg) => new($"{msg} near '{Cur.Text}' at {Cur.Position}");

    private void ExpectKw(string k) { if (!IsKw(k)) throw Err($"Expected '{k}'"); _i++; }
    private void ExpectPunct(string p) { if (!IsPunct(p)) throw Err($"Expected '{p}'"); _i++; }
    private string ExpectIdent()
    {
        if (!IsIdentAny()) throw Err("Identifier expected");
        var s = Cur.Text; _i++; return s;
    }
    private decimal ExpectNumber()
    {
        if (Cur.Kind != TokKind.Number) throw Err("Number expected");
        var v = (decimal)Cur.Value!; _i++; return v;
    }
    private bool MatchPunct(string p) { if (IsPunct(p)) { _i++; return true; } return false; }

    public QueryNode ParseQuery()
    {
        ExpectKw("from");
        var srcVar = ExpectIdent();
        _sourceVar = srcVar;
        ExpectKw("in");
        var srcColl = ExpectIdent();

        var lets = new List<LetClause>();
        Expr? where = null;
        var order = new List<OrderItem>();
        var sel = new List<SelectItem>();
        int? take = null;
        bool perBar = false;
        bool perSeen = false;

        while (Cur.Kind != TokKind.Eof)
        {
            if (IsKw("let"))
            {
                _i++;
                var name = ExpectIdent();
                if (!IsOp("=")) throw Err("Expected '=' after let name");
                _i++;
                lets.Add(new LetClause(name, ParseExpr()));
                continue;
            }

            if (IsKw("where"))
            {
                // Multiple `where` clauses are allowed and are combined with `and`,
                // matching SQL / LINQ semantics. The runner evaluates all `let`
                // bindings before the combined predicate, so clause position
                // relative to `let` does not matter.
                _i++;
                var clause = ParseExpr();
                where = where is null ? clause : new Binary("and", where, clause);
                continue;
            }

            if (IsKw("orderby"))
            {
                if (order.Count > 0) throw Err("Duplicate 'orderby' clause");
                _i++;
                do
                {
                    var e = ParseExpr();
                    bool desc = false;
                    if (IsKw("desc")) { desc = true; _i++; }
                    else if (IsKw("asc")) { _i++; }
                    order.Add(new OrderItem(e, desc));
                } while (MatchPunct(","));
                continue;
            }

            if (IsKw("select"))
            {
                if (sel.Count > 0) throw Err("Duplicate 'select' clause");
                _i++;
                do
                {
                    var e = ParseExpr();
                    string? alias = null;
                    if (IsKw("as")) { _i++; alias = ExpectIdent(); }
                    sel.Add(new SelectItem(e, alias));
                } while (MatchPunct(","));
                continue;
            }

            if (IsKw("take"))
            {
                if (take is not null) throw Err("Duplicate 'take' clause");
                _i++;
                take = (int)ExpectNumber();
                continue;
            }

            if (IsKw("per"))
            {
                if (perSeen) throw Err("Duplicate 'per' clause");
                _i++;
                var kind = ExpectIdent();
                if (string.Equals(kind, "bar", StringComparison.OrdinalIgnoreCase))
                    perBar = true;
                else if (string.Equals(kind, "symbol", StringComparison.OrdinalIgnoreCase))
                    perBar = false;
                else
                    throw Err("Expected 'per bar' or 'per symbol' after 'per'");
                perSeen = true;
                continue;
            }

            break;
        }

        if (Cur.Kind != TokKind.Eof) throw Err("Unexpected trailing token");
        return new QueryNode(srcVar, srcColl, lets, where, order, sel, take, perBar);
    }

    // ---- expression parsing ----

    private Expr ParseExpr() => ParseOr();

    private Expr ParseOr()
    {
        var l = ParseAnd();
        while (IsKw("or") || IsOp("||")) { _i++; l = new Binary("or", l, ParseAnd()); }
        return l;
    }

    private Expr ParseAnd()
    {
        var l = ParseNot();
        while (IsKw("and") || IsOp("&&")) { _i++; l = new Binary("and", l, ParseNot()); }
        return l;
    }

    private Expr ParseNot()
    {
        if (IsKw("not") || IsOp("!")) { _i++; return new Unary("not", ParseNot()); }
        return ParseCmp();
    }

    private Expr ParseCmp()
    {
        var l = ParseCross();

        // between – ternary, consumes its own "and"
        if (IsKw("between"))
        {
            _i++;
            var lower = ParseCross();
            if (!IsKw("and")) throw Err("Expected 'and' inside 'between'");
            _i++;
            var upper = ParseCross();
            return new Between(l, lower, upper);
        }

        // symbolic or keyword comparison
        while (true)
        {
            string? op = null;
            if (Cur.Kind == TokKind.Op && Cur.Text is "==" or "!=" or ">" or ">=" or "<" or "<=")
            {
                op = Advance().Text;
            }
            else if (Cur.Kind == TokKind.Keyword)
            {
                var k = Cur.Text.ToLowerInvariant();
                op = k switch
                {
                    "greaterthan"                => ">",
                    "greaterthanorequal"         => ">=",
                    "greaterthanequalto"         => ">=",
                    "greaterthanorequalto"       => ">=",
                    "lessthan"                   => "<",
                    "lessthanorequal"            => "<=",
                    "lessthanequalto"            => "<=",
                    "equals"                     => "==",
                    "notequals"                  => "!=",
                    "closedabove"                => ">",
                    "closedbelow"                => "<",
                    _ => null
                };
                if (op is not null) _i++;
            }

            if (op is null) break;
            l = new Binary(op, l, ParseCross());
        }

        return l;
    }

    private Expr ParseCross()
    {
        var l = ParseAdd();
        while (Cur.Kind == TokKind.Keyword)
        {
            var k = Cur.Text.ToLowerInvariant();
            string? op = k switch
            {
                "crossedabove" or "crossedover" => "crossedabove",
                "crossedbelow" or "crossedunder" => "crossedbelow",
                "touchedabove" => "touchedabove",
                "touchedbelow" => "touchedbelow",
                _ => null
            };
            if (op is null) break;
            _i++;
            l = new Binary(op, l, ParseAdd());
        }
        return l;
    }

    private Expr ParseAdd()
    {
        var l = ParseMul();
        while (IsOp("+") || IsOp("-"))
        {
            var op = Advance().Text;
            l = new Binary(op, l, ParseMul());
        }
        return l;
    }

    private Expr ParseMul()
    {
        var l = ParseUnary();
        while (IsOp("*") || IsOp("/") || IsOp("%"))
        {
            var op = Advance().Text;
            l = new Binary(op, l, ParseUnary());
        }
        return l;
    }

    private Expr ParseUnary()
    {
        if (IsOp("-")) { _i++; return new Unary("-", ParseUnary()); }
        return ParsePostfix();
    }

    private Expr ParsePostfix()
    {
        var e = ParsePrimary();
        while (true)
        {
            if (IsPunct("["))
            {
                _i++;
                var off = ParseExpr();
                ExpectPunct("]");
                e = new Index(e, off);
            }
            else if (IsPunct("."))
            {
                _i++;
                var name = ExpectIdent();
                e = new Member(e, name);
            }
            else break;
        }
        return e;
    }

    private Expr ParsePrimary()
    {
        if (IsPunct("(")) { _i++; var e = ParseExpr(); ExpectPunct(")"); return e; }

        if (Cur.Kind == TokKind.Number)
        {
            var v = (decimal)Cur.Value!; _i++; return new Num(v);
        }
        if (Cur.Kind == TokKind.String)
        {
            var v = (string)Cur.Value!; _i++; return new Str(v);
        }
        if (IsKw("true"))  { _i++; return new BoolLit(true); }
        if (IsKw("false")) { _i++; return new BoolLit(false); }

        if (IsIdentAny())
        {
            var name = Cur.Text; _i++;
            if (IsPunct("("))
            {
                _i++;
                var args = new List<Expr>();
                if (!IsPunct(")"))
                {
                    do { args.Add(ParseExpr()); } while (MatchPunct(","));
                }
                ExpectPunct(")");
                if (Eq(name, "SUPERTREND"))
                {
                    // Expand shorthand before dependency analysis and cached evaluation.
                    if (args.Count == 2)
                        args.InsertRange(0, new Expr[]
                        {
                            new Member(new Ident(_sourceVar), "High"),
                            new Member(new Ident(_sourceVar), "Low"),
                            new Member(new Ident(_sourceVar), "Close")
                        });
                    else if (args.Count is not (4 or 5))
                        throw Err("SUPERTREND requires (period, multiplier) or (high, low, close, period[, multiplier])");
                }
                return new Call(name, args);
            }
            return new Ident(name);
        }

        throw Err("Primary expression expected");
    }
}
