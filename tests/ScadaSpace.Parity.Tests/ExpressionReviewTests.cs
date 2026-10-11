using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Expressions;
using static Suite;

internal static class ExpressionReviewTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("quoted expression punctuation remains literal text", () => {
            var engine = new ExpressionEngine();
            foreach (var text in new[] { "(", ")", ",", "?", ":", "+", "-", "and", "or", "!", "=", "^" })
            {
                Equal(text, engine.Evaluate("'" + text + "'").Text);
                Equal(text + "x", engine.Evaluate("concat('" + text + "', 'x')").Text);
            }
        });
        Add("quoted operators are rejected instead of executed", () => {
            var engine = new ExpressionEngine();
            foreach (var expression in new[] { "1 '+' 2", "true 'and' false", "2 '^' 3", "1 '?' 2 : 3", "(1 ')'", "concat('a' ',' 'b')", "true ? 1 ':' 2" })
                Throws<ExpressionException>(() => engine.Compile(expression));
        });
        Add("literal parentheses compose with actual calls and ternaries", () => {
            var engine = new ExpressionEngine();
            Equal("(3)", engine.Evaluate("concat('(', 1+2, ')')").Text);
            Equal(")", engine.Evaluate("if(true, ')', '(')").Text);
            Equal("(", engine.Evaluate("false ? ')' : '('").Text);
            Equal(7d, engine.Evaluate("1 + (2 * 3)").Number);
        });
        Add("punctuation references are values rather than syntax", () => {
            var engine = new ExpressionEngine();
            Equal("open", engine.Evaluate("{(}", key => Scalar.From(key == "(" ? "open" : "other")).Text);
            Throws<ExpressionException>(() => engine.Compile("1 {+} 2"));
        });
    }
}
