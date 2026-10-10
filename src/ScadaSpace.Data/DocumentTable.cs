using System.Globalization;
using System.Text;
using ScadaSpace.Core;

namespace ScadaSpace.Data;

public sealed record DocumentColumn(string Field, string Title, double Weight = 1, bool Editable = true, ScalarKind? DataType = null);
public sealed record DocumentRow(string Key, int SourceIndex, IReadOnlyList<Scalar> Cells);
public sealed record DocumentSort(string Field, bool Descending = false);

/// <summary>Typed comparisons, including a deterministic order between unlike property kinds.</summary>
public sealed class PropertyValueComparer : IComparer<Scalar>
{
    public static PropertyValueComparer Instance { get; } = new();
    public int Compare(Scalar x, Scalar y)
    {
        if (x.Kind != y.Kind) return x.Kind.CompareTo(y.Kind);
        return x.Kind switch
        {
            ScalarKind.Number => x.Number.CompareTo(y.Number), ScalarKind.Boolean => x.Boolean.CompareTo(y.Boolean),
            ScalarKind.String => StringComparer.OrdinalIgnoreCase.Compare(x.Text, y.Text),
            ScalarKind.Object or ScalarKind.Array => StringComparer.Ordinal.Compare(x.ToJson(), y.ToJson()), _ => 0
        };
    }
}

/// <summary>Immutable document-table adapter. Sorting/filtering preserve source row identity; edits return a new document.</summary>
public sealed class DocumentTable
{
    public Scalar Data { get; }
    public IReadOnlyList<DocumentColumn> Columns { get; }
    public IReadOnlyList<DocumentRow> Rows { get; }
    private readonly Dictionary<string, DocumentRow> _rows;
    public DocumentTable(Scalar data, IEnumerable<DocumentColumn>? columns = null, string? keyField = null)
    {
        if (!data.IsValid() || data.Kind != ScalarKind.Array) throw new ArgumentException("Table data must be an array of objects.");
        if (data.Items.Any(row => row.Kind != ScalarKind.Object)) throw new ArgumentException("Each table row must be an object.");
        Data = data;
        var fields = columns?.ToArray() ?? data.Items.SelectMany(row => row.Fields.Keys).Distinct(StringComparer.Ordinal).Select(key => new DocumentColumn(key, key)).ToArray();
        if (fields.Length > 256 || fields.Any(c => c is null || string.IsNullOrEmpty(c.Field) || c.Title is null || !double.IsFinite(c.Weight) || c.Weight <= 0 || c.DataType is { } type && !Enum.IsDefined(type)) || fields.Select(c => c.Field).Distinct(StringComparer.Ordinal).Count() != fields.Length) throw new ArgumentException("Invalid, duplicate or excessive table columns.");
        Columns = Array.AsReadOnly(fields); _rows = new(StringComparer.Ordinal); var rows = new List<DocumentRow>();
        for (var i = 0; i < data.Items.Count; i++)
        {
            var row = data.Items[i];
            var key = string.IsNullOrEmpty(keyField) ? i.ToString(CultureInfo.InvariantCulture) : row.Fields.TryGetValue(keyField, out var id) && id.Kind is ScalarKind.String or ScalarKind.Number ? id.ToJson() : throw new ArgumentException("Each row needs a scalar key field.");
            var cells = fields.Select(c => CellValue(row.Fields.GetValueOrDefault(c.Field))).ToArray();
            var entry = new DocumentRow(key, i, Array.AsReadOnly(cells));
            if (!_rows.TryAdd(key, entry)) throw new ArgumentException("Duplicate table row key: " + key);
            rows.Add(entry);
        }
        Rows = rows.AsReadOnly();
    }
    public static Scalar CellValue(Scalar cell) => cell.Kind == ScalarKind.Object && cell.Fields.TryGetValue("value", out var value) ? value : cell;
    public IReadOnlyList<DocumentRow> Query(string filter = "", IReadOnlyList<DocumentSort>? sorting = null, int offset = 0, int? limit = null)
    {
        if (filter is null || filter.Length > 1024 || offset < 0 || limit < 0) throw new ArgumentException("Invalid table query.");
        var order = sorting ?? [];
        if (order.Count > 32) throw new ArgumentException("Too many sort keys.");
        var columns = order.Select(sort => (Index: FindColumn(sort.Field), sort.Descending)).ToArray();
        IEnumerable<DocumentRow> rows = Rows;
        if (!string.IsNullOrWhiteSpace(filter)) rows = rows.Where(row => row.Cells.Any(cell => cell.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)));
        if (columns.Length > 0)
            rows = rows.OrderBy(row => row, Comparer<DocumentRow>.Create((a, b) =>
            {
                foreach (var (index, descending) in columns)
                {
                    var result = PropertyValueComparer.Instance.Compare(a.Cells[index], b.Cells[index]);
                    if (result != 0) return descending ? -Math.Sign(result) : result;
                }
                return a.SourceIndex.CompareTo(b.SourceIndex);
            }));
        rows = rows.Skip(offset); if (limit is { } count) rows = rows.Take(count);
        return Array.AsReadOnly(rows.ToArray());
    }
    private int FindColumn(string field)
    {
        for (var i = 0; i < Columns.Count; i++) if (Columns[i].Field == field) return i;
        throw new ArgumentException("Unknown table column: " + field);
    }
    public Scalar WithCell(string key, string field, Scalar value)
    {
        if (!_rows.TryGetValue(key, out var row)) throw new KeyNotFoundException("Unknown table row: " + key);
        var column = Columns[FindColumn(field)];
        if (!column.Editable) throw new InvalidOperationException("The table column is read-only.");
        if (!value.IsValid() || column.DataType is { } kind && kind != value.Kind) throw new ArgumentException("Cell value does not match the column type.");
        var source = Data.Items[row.SourceIndex]; var fields = source.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        if (fields.TryGetValue(field, out var old) && old.Kind == ScalarKind.Object && old.Fields.ContainsKey("value"))
        {
            var wrapper = old.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal); wrapper["value"] = value; value = Scalar.Object(wrapper);
        }
        fields[field] = value; var items = Data.Items.ToArray(); items[row.SourceIndex] = Scalar.Object(fields); return Scalar.Array(items);
    }
    public string ExportCsv(bool neutralizeFormulas = true)
    {
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        string Safe(string text, bool textual) => neutralizeFormulas && textual && text.TrimStart().StartsWithAny('=', '+', '-', '@') ? "'" + text : text;
        var text = new StringBuilder(); text.AppendLine(string.Join(',', Columns.Select(c => Quote(Safe(c.Title, true)))));
        foreach (var row in Rows) text.AppendLine(string.Join(',', row.Cells.Select(value => Quote(Safe(value.Kind == ScalarKind.Number ? value.Number.ToString("R", CultureInfo.InvariantCulture) : value.ToString(), value.Kind != ScalarKind.Number)))));
        return text.ToString();
    }
}

internal static class TextExtensions
{
    public static bool StartsWithAny(this string text, params char[] prefixes) => text.Length > 0 && prefixes.Contains(text[0]);
}
