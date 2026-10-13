using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using static Suite;

internal static class LedDisplayTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("LED exposes seven and fourteen segment modes and nine common numeric presets", () => {
            Equal(2, LedDisplayOptions.SegmentFormats.Count); Equal(9, LedDisplayOptions.NumberFormats.Count);
            foreach(var pattern in LedDisplayOptions.NumberFormats) LedDisplayPresentation.Create(Scalar.From(1234.5), new(NumberFormat:pattern));
        });
        Add("LED legacy definitions retain seven-segment colors and existing number format", () => {
            var values = new Dictionary<string, Scalar>{{"value",Scalar.From(12.34)},{"format",Scalar.From("0.0")}};
            var result=LedDisplayPresentation.Read(key=>values.GetValueOrDefault(key));
            Equal("7 segment",result.Options.SegmentFormat); Equal("12.3",result.Text); Equal("#22332D",result.Options.BackgroundColor);
        });
        Add("LED explicit numberFormat overrides legacy format and uses selected locale", () => {
            var values=new Dictionary<string,Scalar>{{"value",Scalar.From(12345.67)},{"segmentFormat",Scalar.From("14 segment")},{"numberFormat",Scalar.From("#,##0.00")},{"format",Scalar.From("0")},{"locale",Scalar.From("de-DE")}};
            Equal("12.345,67",LedDisplayPresentation.Read(key=>values.GetValueOrDefault(key)).Text);
            values["locale"]=Scalar.From("en-US");Equal("12,345.67",LedDisplayPresentation.Read(key=>values.GetValueOrDefault(key)).Text);
        });
        Add("LED formats Polish grouping French decimals and percentages without changing process values", () => {
            var value=Scalar.From(12345.67);
            var polish=LedDisplayPresentation.Create(value,new(Locale:"pl-PL")); Assert(polish.Text.EndsWith(",67") && !polish.Text.Contains('.'));
            var french=LedDisplayPresentation.Create(value,new(Locale:"fr-FR")); Assert(french.Text.EndsWith(",67"));
            Equal("12.5%",LedDisplayPresentation.Create(Scalar.From(.125),new(NumberFormat:"0.0%")).Text); Equal(12345.67,value.Number);
        });
        Add("fourteen segment alphabet covers every ASCII letter without blank glyphs", () => {
            for(var ch='A';ch<='Z';ch++) { Assert(LedGlyphs.Get(ch,true).Segments!=0); Equal(LedGlyphs.Get(ch,true),LedGlyphs.Get(char.ToLowerInvariant(ch),true)); }
            Assert(LedGlyphs.Get('M',true)!=LedGlyphs.Get('N',true)); Assert(LedGlyphs.Get('X',true)!=LedGlyphs.Get('Y',true));
            Equal("Run 12:34",LedDisplayPresentation.Create(Scalar.From("Run 12:34"),new()).Text);
        });
        Add("LED never silently substitutes unsupported characters or overlong readouts", () => {
            Throws<FormatException>(()=>LedDisplayPresentation.Create(Scalar.From("W"),new("7 segment")));
            Throws<FormatException>(()=>LedDisplayPresentation.Create(Scalar.From("🚨"),new()));
            Throws<FormatException>(()=>LedDisplayPresentation.Create(Scalar.From(new string('1',65)),new()));
            Throws<FormatException>(()=>LedDisplayPresentation.Create(Scalar.From(double.MaxValue),new()));
        });
        Add("LED punctuation and international digit glyphs retain distinct readable geometry", () => {
            Assert(LedGlyphs.Get('.',true)!=LedGlyphs.Get(',',true)); Assert(LedGlyphs.Get(' ',true).Advance>0);
            Equal(LedGlyphs.Get('5',true),LedGlyphs.Get('\u0665',true)); Equal(LedGlyphs.Get('5',true),LedGlyphs.Get('\u06F5',true));
            Equal(LedMark.Percent,LedGlyphs.Get('%',false).Mark);
        });
        Add("LED validation rejects invalid modes types alpha syntax and excessive precision", () => {
            Throws<ArgumentException>(()=>new LedDisplayOptions("8 segment").Validate());
            Throws<ArgumentException>(()=>new LedDisplayOptions(BackgroundColor:"red").Validate());
            Throws<ArgumentException>(()=>new LedDisplayOptions(Locale:"bad locale").Validate());
            Throws<FormatException>(()=>new LedDisplayOptions(NumberFormat:"F100000").Validate());
            Throws<ArgumentException>(()=>LedDisplayPresentation.Create(Scalar.From(true),new()));
        });
        Add("LED presentation cache hits retain immutable snapshots without allocations", () => {
            var values=new Dictionary<string,Scalar>{{"value",Scalar.From(12.34)}};Func<string,Scalar> read=key=>values.GetValueOrDefault(key);
            var cache=new LedDisplayPresentationCache();var first=cache.Read(read);
            for(var i=0;i<100;i++)cache.Read(read);
            var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<1000;i++)Assert(ReferenceEquals(first,cache.Read(read)));
            Assert(GC.GetAllocatedBytesForCurrentThread()-before<128);Equal(1L,cache.BuildCount);
            values["value"]=Scalar.From(25);Assert(!ReferenceEquals(first,cache.Read(read)));Equal("12.3",first.Text);
        });
        Add("LED cache revalidates failed bindings and recovers without publishing partial state", () => {
            var values=new Dictionary<string,Scalar>{{"value",Scalar.From(12)}};Func<string,Scalar> read=key=>values.GetValueOrDefault(key);
            var cache=new LedDisplayPresentationCache();var first=cache.Read(read);
            values["diodeOnColor"]=Scalar.From("invalid");Throws<ArgumentException>(()=>cache.Read(read));Equal(1L,cache.BuildCount);
            values.Remove("diodeOnColor");Assert(ReferenceEquals(first,cache.Read(read)));
            Throws<InvalidOperationException>(()=>cache.Read(_=>throw new InvalidOperationException("binding")));
            Throws<InvalidOperationException>(()=>cache.Read(_=>{cache.Read(read);return Scalar.Null;}));
            Assert(ReferenceEquals(first,cache.Read(read)));
        });
    }
}
