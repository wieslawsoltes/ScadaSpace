using System.Globalization;
using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class NumericInputTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("numeric input format does not narrow signed or timestamp values to Int32", () => {
            foreach (var value in new double[] { 4102444800001, -4102444800001, int.MinValue, (double)int.MaxValue + 1, -2147483649, 0.125 })
            {
                var text = NumericInputText.FormatDouble(value);
                Equal(value, NumericInputText.ParseDouble(text)!.Value);
            }
            Equal("4102444800001",NumericInputText.FormatDouble(4102444800001));
        });
        Add("numeric input round trips finite binary64 extremes and subnormals", () => {
            foreach (var value in new[] { double.MaxValue, double.MinValue, double.Epsilon, -double.Epsilon, .1, -0d, 1.2345678901234567 })
            {
                var result = NumericInputText.ParseDouble(NumericInputText.FormatDouble(value))!.Value;
                Equal(BitConverter.DoubleToInt64Bits(value),BitConverter.DoubleToInt64Bits(result));
            }
        });
        Add("numeric text is invariant when application culture uses decimal commas", () => {
            var previous = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL"); Equal("1.5",NumericInputText.FormatDouble(1.5)); Equal(1.5,NumericInputText.ParseDouble("1.5")!.Value); Assert(NumericInputText.ParseDouble("1,5") is null); }
            finally { CultureInfo.CurrentCulture = previous; }
        });
        Add("numeric input rejects nonfinite oversized and malformed values", () => {
            foreach (var value in new string?[] { null, "", "NaN", "Infinity", "-Infinity", "1e999", "1,234", "--1", "abc", new string('0',513) }) Assert(NumericInputText.ParseDouble(value) is null);
            foreach (var value in new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity }) Equal("",NumericInputText.FormatDouble(value));
            Equal(150d,NumericInputText.ParseDouble(" +1.5e2 ")!.Value);
        });
        Add("numeric integer adapters preserve full signed and unsigned ranges", () => {
            Equal(long.MinValue,NumericInputText.ParseInt(NumericInputText.FormatInt(long.MinValue))!.Value);
            Equal(long.MaxValue,NumericInputText.ParseInt(NumericInputText.FormatInt(long.MaxValue))!.Value);
            Equal(ulong.MaxValue,NumericInputText.ParseUInt(NumericInputText.FormatUInt(ulong.MaxValue))!.Value);
            Assert(NumericInputText.ParseInt("9223372036854775808") is null); Assert(NumericInputText.ParseUInt("-1") is null); Assert(NumericInputText.ParseInt("1.5") is null);
        });
    }
}
