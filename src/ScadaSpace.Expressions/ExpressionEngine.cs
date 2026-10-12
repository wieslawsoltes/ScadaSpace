using System.Globalization;
using System.Text;
using ScadaSpace.Core;

namespace ScadaSpace.Expressions;

public sealed class ExpressionException(string message, int position = -1) : FormatException(position < 0 ? message : $"{message} (position {position})")
{
    public int Position { get; } = position;
}

/// <summary>A bounded interpreted expression compiler. No eval, reflection, file or network access.</summary>
public sealed partial class ExpressionEngine(int cacheCapacity = 256)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CompiledExpression> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    public CompiledExpression Compile(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > 16384) throw new ExpressionException("Expression exceeds 16 KiB.");
        lock (_gate)
        {
            if (_cache.TryGetValue(source, out var cached)) return cached;
            var parser = new Parser(source);
            var root = parser.Parse();
            var expression = new CompiledExpression(root, parser.Dependencies.ToArray());
            _cache[source] = expression; _order.Enqueue(source);
            while (_order.Count > Math.Clamp(cacheCapacity, 1, 4096)) _cache.Remove(_order.Dequeue());
            return expression;
        }
    }
    public Scalar Evaluate(string source, Func<string, Scalar>? resolve = null) => Compile(source).Evaluate(resolve ?? (_ => throw new ExpressionException("Unknown reference.")));
    internal abstract record Node
    {
        public Scalar Eval(Context context)
        {
            if (--context.Budget < 0 || ++context.Depth > 128) throw new ExpressionException("Expression evaluation budget or stack depth exceeded.");
            try { return Evaluate(context); }
            finally { context.Depth--; }
        }
        protected abstract Scalar Evaluate(Context context);
    }
    internal sealed class Context(Func<string, Scalar> resolve)
    {
        public Func<string, Scalar> Resolve { get; } = resolve;
        public int Budget = 4096;
        public int Depth;
    }
    private sealed record Literal(Scalar Value) : Node { protected override Scalar Evaluate(Context c) => Value; }
    private sealed record Reference(string Path) : Node
    {
        protected override Scalar Evaluate(Context c)
        {
            var value = c.Resolve(Path);
            return value.IsValid() ? value : throw new ExpressionException("Invalid reference value: " + Path);
        }
    }
    private sealed record Unary(string Op, Node Operand) : Node
    {
        protected override Scalar Evaluate(Context c) => Op switch
        {
            "-" => Scalar.From(-Operand.Eval(c).AsNumber()),
            "+" => Scalar.From(Operand.Eval(c).AsNumber()),
            _ => Scalar.From(!Operand.Eval(c).AsBoolean())
        };
    }
    private sealed record Binary(string Op, Node Left, Node Right) : Node
    {
        protected override Scalar Evaluate(Context c)
        {
            var a = Left.Eval(c);
            if (Op is "&&" or "and") return Scalar.From(a.AsBoolean() && Right.Eval(c).AsBoolean());
            if (Op is "||" or "or") return Scalar.From(a.AsBoolean() || Right.Eval(c).AsBoolean());
            var b = Right.Eval(c);
            if (Op == "+" && (a.Kind == ScalarKind.String || b.Kind == ScalarKind.String)) return Scalar.From(Limit(a.ToString() + b.ToString()));
            if (Op is "==" or "=") return Scalar.From(a == b);
            if (Op is "!=" or "<>") return Scalar.From(a != b);
            if (Op is "<" or "<=" or ">" or ">=")
            {
                var cmp = a.Kind == ScalarKind.String && b.Kind == ScalarKind.String ? string.CompareOrdinal(a.Text, b.Text) : a.AsNumber().CompareTo(b.AsNumber());
                return Scalar.From(Op switch { "<" => cmp < 0, "<=" => cmp <= 0, ">" => cmp > 0, _ => cmp >= 0 });
            }
            var x = a.AsNumber(); var y = b.AsNumber();
            return Scalar.From(Op switch
            {
                "+" => x + y, "-" => x - y, "*" => x * y,
                "/" => y == 0 ? throw new ExpressionException("Division by zero.") : x / y,
                "%" => y == 0 ? throw new ExpressionException("Division by zero.") : x % y,
                "^" => Math.Pow(x, y), _ => throw new ExpressionException("Unknown operator.")
            });
        }
    }
    private sealed record Conditional(Node Condition, Node Yes, Node No) : Node
    {
        protected override Scalar Evaluate(Context c) => Condition.Eval(c).AsBoolean() ? Yes.Eval(c) : No.Eval(c);
    }
    private static string Limit(string value) => value.Length <= 65536 ? value : throw new ExpressionException("String result exceeds 64 KiB.");
    private static string Concat(IEnumerable<Scalar> values)
    {
        var text = new StringBuilder();
        foreach (var value in values)
        {
            var part = value.ToString();
            if (part.Length > 65536 - text.Length) throw new ExpressionException("String result exceeds 64 KiB.");
            text.Append(part);
        }
        return text.ToString();
    }
    private sealed record Call(string Name, Node[] Args) : Node
    {
        protected override Scalar Evaluate(Context c)
        {
            if (Name == "if") return Args[0].Eval(c).AsBoolean() ? Args[1].Eval(c) : Args[2].Eval(c);
            if (Name == "coalesce") { foreach (var arg in Args) { var value = arg.Eval(c); if (value.Kind != ScalarKind.Null) return value; } return Scalar.Null; }
            var a = Args.Select(x => x.Eval(c)).ToArray();
            double N(int i) => a[i].AsNumber();
            string S(int i) => a[i].ToString();
            return Name switch
            {
                "abs" => Scalar.From(Math.Abs(N(0))), "sqrt" => Scalar.From(Math.Sqrt(N(0))),
                "sin" => Scalar.From(Math.Sin(N(0))), "cos" => Scalar.From(Math.Cos(N(0))), "tan" => Scalar.From(Math.Tan(N(0))),
                "floor" => Scalar.From(Math.Floor(N(0))), "ceil" => Scalar.From(Math.Ceiling(N(0))),
                "round" => Scalar.From(Math.Round(N(0), a.Length == 2 ? checked((int)N(1)) : 0, MidpointRounding.AwayFromZero)),
                "min" => Scalar.From(a.Min(x => x.AsNumber())), "max" => Scalar.From(a.Max(x => x.AsNumber())),
                "pow" => Scalar.From(Math.Pow(N(0), N(1))), "clamp" => Scalar.From(Math.Clamp(N(0), N(1), N(2))),
                "tostring" => Scalar.From(S(0)), "tofloat" => Scalar.From(N(0)), "toint" => Scalar.From(Math.Truncate(N(0))),
                "toboolean" => Scalar.From(a[0].AsBoolean()), "isnull" => Scalar.From(a[0].Kind == ScalarKind.Null),
                "len" => Scalar.From(S(0).Length), "upper" => Scalar.From(S(0).ToUpperInvariant()), "lower" => Scalar.From(S(0).ToLowerInvariant()),
                "contains" => Scalar.From(S(0).Contains(S(1), StringComparison.Ordinal)), "startswith" => Scalar.From(S(0).StartsWith(S(1), StringComparison.Ordinal)),
                "endswith" => Scalar.From(S(0).EndsWith(S(1), StringComparison.Ordinal)),
                "substring" => Scalar.From(a.Length == 2 ? S(0)[checked((int)N(1))..] : S(0).Substring(checked((int)N(1)), checked((int)N(2)))),
                "concat" => Scalar.From(Concat(a)),
                "numberformat" => Scalar.From(NumberFormatting.Format(N(0), S(1))),
                _ => throw new ExpressionException("Unknown function.")
            };
        }
    }
    private enum TokenKind { End, Number, String, Reference, Name, Symbol }
    private readonly record struct Token(TokenKind Kind, string Text, int Position);
    private sealed class Lexer(string source)
    {
        private int _position;
        public Token Next()
        {
            while (_position < source.Length && char.IsWhiteSpace(source[_position])) _position++;
            var start = _position;
            if (_position == source.Length) return new(TokenKind.End, "", start);
            var ch = source[_position++];
            if (ch == '{')
            {
                var end = source.IndexOf('}', _position);
                if (end < 0) throw new ExpressionException("Unclosed reference.", start);
                var text = source[_position..end]; _position = end + 1;
                if (string.IsNullOrWhiteSpace(text) || text.Contains('{')) throw new ExpressionException("Invalid reference.", start);
                return new(TokenKind.Reference, text, start);
            }
            if (ch is '\'' or '"')
            {
                var text = new StringBuilder(); var closed = false;
                while (_position < source.Length)
                {
                    var c = source[_position++];
                    if (c == ch) { closed = true; break; }
                    if (c == '\\')
                    {
                        if (_position == source.Length) throw new ExpressionException("Unclosed escape.", _position);
                        c = source[_position++] switch { 'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\', '\'' => '\'', '"' => '"', _ => throw new ExpressionException("Unknown escape.", _position - 1) };
                    }
                    text.Append(c);
                }
                if (!closed) throw new ExpressionException("Unclosed string.", start);
                return new(TokenKind.String, text.ToString(), start);
            }
            if (char.IsAsciiDigit(ch) || ch == '.' && _position < source.Length && char.IsAsciiDigit(source[_position]))
            {
                while (_position < source.Length && (char.IsAsciiDigit(source[_position]) || source[_position] == '.')) _position++;
                if (_position < source.Length && source[_position] is 'e' or 'E')
                {
                    _position++;
                    if (_position < source.Length && source[_position] is '+' or '-') _position++;
                    while (_position < source.Length && char.IsAsciiDigit(source[_position])) _position++;
                }
                return new(TokenKind.Number, source[start.._position], start);
            }
            if (char.IsAsciiLetter(ch) || ch == '_')
            {
                while (_position < source.Length && (char.IsAsciiLetterOrDigit(source[_position]) || source[_position] is '_' or '.')) _position++;
                return new(TokenKind.Name, source[start.._position], start);
            }
            if (_position < source.Length && new[] { "&&", "||", "==", "!=", "<=", ">=", "<>" }.Contains(source[start..(_position + 1)])) return new(TokenKind.Symbol, source[start..++_position], start);
            if ("+-*/%^!<>=(),?:".Contains(ch)) return new(TokenKind.Symbol, ch.ToString(), start);
            throw new ExpressionException($"Unexpected character '{ch}'.", start);
        }
    }
    private sealed class Parser
    {
        private readonly Lexer _lexer;
        private readonly List<ReferenceOccurrence>? _references;
        private Token _token;
        private int _nodes;
        private int _depth;
        public HashSet<string> Dependencies { get; } = new(StringComparer.Ordinal);
        public Parser(string source, List<ReferenceOccurrence>? references = null) { _lexer = new(source); _references = references; _token = _lexer.Next(); }
        private void Next() => _token = _lexer.Next();
        private bool IsSymbol(string text) => _token.Kind == TokenKind.Symbol && _token.Text == text;
        private void Expect(string text) { if (!IsSymbol(text)) throw new ExpressionException($"Expected '{text}'.", _token.Position); Next(); }
        public Node Parse()
        {
            var node = ParseExpression(0);
            if (_token.Kind != TokenKind.End) throw new ExpressionException("Unexpected trailing input.", _token.Position);
            return node;
        }
        private static int Precedence(string op) => op.ToLowerInvariant() switch
        {
            "||" or "or" => 1, "&&" or "and" => 2, "==" or "!=" or "=" or "<>" => 3,
            "<" or "<=" or ">" or ">=" => 4, "+" or "-" => 5, "*" or "/" or "%" => 6, "^" => 7, _ => -1
        };
        private Node ParseExpression(int minimum)
        {
            if (++_depth > 64 || ++_nodes > 1024) throw new ExpressionException("Expression nesting or node limit exceeded.");
            Node left;
            var token = _token; Next();
            if (token.Kind == TokenKind.Symbol && token.Text == "(") { left = ParseExpression(0); Expect(")"); }
            else if (token.Kind == TokenKind.Symbol && token.Text is "+" or "-" or "!" || token.Kind == TokenKind.Name && token.Text.Equals("not", StringComparison.OrdinalIgnoreCase)) left = new Unary(token.Text.ToLowerInvariant(), ParseExpression(8));
            else if (token.Kind == TokenKind.Number)
            {
                if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new ExpressionException("Invalid numeric literal.", token.Position);
                left = new Literal(Scalar.From(value));
            }
            else if (token.Kind == TokenKind.String) left = new Literal(Scalar.From(token.Text));
            else if (token.Kind == TokenKind.Name && IsSymbol("("))
            {
                Next(); var args = new List<Node>();
                if (!IsSymbol(")"))
                {
                    while (true) { args.Add(ParseExpression(0)); if (!IsSymbol(",")) break; Next(); }
                }
                Expect(")"); var name = token.Text.ToLowerInvariant();
                var (min, max) = name switch
                {
                    "if" or "clamp" => (3, 3),
                    "round" or "substring" => name == "round" ? (1, 2) : (2, 3),
                    "min" or "max" or "concat" or "coalesce" => (1, 64),
                    "pow" or "contains" or "startswith" or "endswith" or "numberformat" => (2, 2),
                    "abs" or "sqrt" or "sin" or "cos" or "tan" or "floor" or "ceil" or "tostring" or "tofloat" or "toint" or "toboolean" or "isnull" or "len" or "upper" or "lower" => (1, 1),
                    _ => throw new ExpressionException($"Unsupported function '{token.Text}'.", token.Position)
                };
                if (args.Count < min || args.Count > max) throw new ExpressionException($"Wrong argument count for {name}.", token.Position);
                left = new Call(name, args.ToArray());
            }
            else if (token.Kind == TokenKind.Name && token.Text.ToLowerInvariant() is "true" or "false" or "null") left = new Literal(token.Text.ToLowerInvariant() switch { "true" => Scalar.From(true), "false" => Scalar.From(false), _ => Scalar.Null });
            else if (token.Kind is TokenKind.Reference or TokenKind.Name)
            {
                _references?.Add(new(token.Text, token.Position, token.Text.Length + (token.Kind == TokenKind.Reference ? 2 : 0)));
                Dependencies.Add(token.Text);
                if (Dependencies.Count > 256) throw new ExpressionException("Too many dependencies.");
                left = new Reference(token.Text);
            }
            else throw new ExpressionException("Expected a value.", token.Position);
            while (_token.Kind is TokenKind.Symbol or TokenKind.Name && Precedence(_token.Text) >= minimum)
            {
                var op = _token.Text.ToLowerInvariant(); var precedence = Precedence(op); Next();
                left = new Binary(op, left, ParseExpression(precedence + (op == "^" ? 0 : 1)));
            }
            if (minimum == 0 && IsSymbol("?")) { Next(); var yes = ParseExpression(0); Expect(":"); left = new Conditional(left, yes, ParseExpression(0)); }
            _depth--;
            return left;
        }
    }
}

public sealed class CompiledExpression
{
    private readonly ExpressionEngine.Node _root;
    public IReadOnlyList<string> Dependencies { get; }
    internal CompiledExpression(ExpressionEngine.Node root, string[] dependencies) { _root = root; Dependencies = Array.AsReadOnly(dependencies); }
    public Scalar Evaluate(Func<string, Scalar> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        try { return _root.Eval(new(resolve)); }
        catch (ExpressionException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or OverflowException or FormatException) { throw new ExpressionException(ex.Message); }
    }
}
