using System.Runtime.CompilerServices;
using ScadaSpace.Core;
using ScadaSpace.Data;
using static Suite;

internal static class TableTests
{
    [ModuleInitializer]
    internal static void Register()
    {
        Add("document table sorts numerically and preserves row identity", () => {
            var table = new DocumentTable(Scalar.FromJson("[{\"id\":\"a\",\"value\":100},{\"id\":\"b\",\"value\":2},{\"id\":\"c\",\"value\":10}]"), keyField: "id");
            var rows = table.Query(sorting: [new("value")]); Equal("\"b\"", rows[0].Key); Equal(1, rows[0].SourceIndex); Equal("\"a\"", rows[2].Key);
        });
        Add("document table filtering and bounded pages compose", () => {
            var table = new DocumentTable(Scalar.FromJson("[{\"name\":\"Pump A\",\"n\":3},{\"name\":\"Tank\",\"n\":1},{\"name\":\"Pump B\",\"n\":2}]"));
            var rows = table.Query("pump", [new("n")], 1, 1); Equal(1, rows.Count); Equal(0, rows[0].SourceIndex);
        });
        Add("multi-column sorting is stable", () => {
            var table = new DocumentTable(Scalar.FromJson("[{\"a\":1,\"b\":2},{\"a\":1,\"b\":3},{\"a\":1,\"b\":3}]"));
            var rows = table.Query(sorting: [new("a"), new("b", true)]); Equal(1, rows[0].SourceIndex); Equal(2, rows[1].SourceIndex); Equal(0, rows[2].SourceIndex);
        });
        Add("table edits preserve wrapped cell metadata and source snapshot", () => {
            var data = Scalar.FromJson("[{\"value\":{\"value\":2,\"style\":{\"color\":\"red\"}}}]"); var table = new DocumentTable(data);
            var edited = table.WithCell("0", "value", Scalar.From(8)); Equal(2d, table.Rows[0].Cells[0].Number); Equal(8d, edited.Items[0].Fields["value"].Fields["value"].Number);
            Equal("red", edited.Items[0].Fields["value"].Fields["style"].Fields["color"].Text);
        });
        Add("table edit validation enforces declared types and permissions", () => {
            var data = Scalar.FromJson("[{\"a\":1,\"b\":2}]"); var table = new DocumentTable(data, [new("a", "A", Editable: false), new("b", "B", DataType: ScalarKind.Number)]);
            Throws<InvalidOperationException>(() => table.WithCell("0", "a", Scalar.From(3))); Throws<ArgumentException>(() => table.WithCell("0", "b", Scalar.From("bad")));
            Throws<KeyNotFoundException>(() => table.WithCell("missing", "b", Scalar.From(3)));
        });
        Add("document table rejects malformed rows and duplicate identities", () => {
            Throws<ArgumentException>(() => new DocumentTable(Scalar.FromJson("[1]")));
            Throws<ArgumentException>(() => new DocumentTable(Scalar.FromJson("[{\"id\":1},{\"id\":1}]"), keyField: "id"));
            Throws<ArgumentException>(() => new DocumentTable(Scalar.Array([]), [new("x", "X", 0)]));
        });
        Add("table CSV escapes quotes and neutralizes formula text", () => {
            var table = new DocumentTable(Scalar.FromJson("[{\"formula\":\"=1+1\",\"text\":\"a,\\\"b\",\"number\":-2}]")); var csv = table.ExportCsv();
            Assert(csv.Contains("\"'=1+1\"")); Assert(csv.Contains("\"a,\"\"b\"")); Assert(csv.Contains("\"-2\""));
        });
        Add("table query rejects unknown sorting and invalid page bounds", () => {
            var table = new DocumentTable(Scalar.FromJson("[{\"a\":1}]")); Throws<ArgumentException>(() => table.Query(sorting: [new("missing")])); Throws<ArgumentException>(() => table.Query(offset: -1));
        });
    }
}
