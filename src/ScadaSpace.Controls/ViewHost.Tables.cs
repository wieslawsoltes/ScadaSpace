using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Core;
using ScadaSpace.Data;

namespace ScadaSpace.Controls;

public partial class ViewHost
{
    private void ConnectTable(ComponentNode node, HmiDataTable table)
    {
        DocumentTable? model = null;
        Scalar previousData = Scalar.Null, previousColumns = Scalar.Null;
        string? previousKey = null;
        var editing = false;
        _refresh.Add(() =>
        {
            if (node.Type != "Table" || Text(node, "source", "tags") == "tags") { RefreshBuiltinTable(node, table); return; }
            var data = Value(node, "data", Scalar.Array([]));
            if (data.Kind == ScalarKind.String) data = Scalar.FromJson(data.Text!); // Existing projects remain readable.
            var columns = Value(node, "columns"); var key = Text(node, "rowKey");
            if (model is not null && previousData == data && previousColumns == columns && previousKey == key) return;
            model = new DocumentTable(data, columns.Kind == ScalarKind.Array && columns.Items.Count > 0 ? columns.Items.Select(Column) : null, key);
            previousData = data; previousColumns = columns; previousKey = key;
            table.SetData(model.Columns.Select(c => new TableColumn(c.Field, c.Title, c.Weight)).ToArray(),
                model.Rows.Select(r => new TableRow(r.Key, r.Cells.Select(x => x.ToString()).ToArray(), Values: r.Cells)).ToArray());
        });
        table.SelectionChanged += key =>
        {
            if (_updating || IsEditing || model is null || key is null) return;
            var row = model.Rows.FirstOrDefault(r => r.Key == key); if (row is null) return;
            Write(node, "selection", Scalar.Object(new Dictionary<string, Scalar> { ["row"] = Scalar.From(row.SourceIndex), ["key"] = Scalar.From(row.Key), ["data"] = model.Data.Items[row.SourceIndex] }));
            Action(node, "onRowClick");
        };
        table.CellEditRequested += async (key, index) =>
        {
            if (editing || model is null || Instance is null || Session is null || !Flag(node, "editable") || !Writable(node, "data") || index >= model.Columns.Count || !model.Columns[index].Editable) return;
            var snapshot = model; var row = snapshot.Rows.FirstOrDefault(r => r.Key == key); if (row is null) return;
            var old = Read(node, "data"); var instance = Instance; var column = snapshot.Columns[index];
            var kind = column.DataType ?? row.Cells[index].Kind;
            var input = new TextBox { Header = column.Title, Text = row.Cells[index].ToString(), MinWidth = 320, AcceptsReturn = kind is ScalarKind.Array or ScalarKind.Object };
            AutomationProperties.SetAutomationId(input, "table-cell-editor");
            var error = new TextBlock { Foreground = ControlTheme.Brush("#B14E37"), TextWrapping = TextWrapping.Wrap };
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Edit cell", PrimaryButtonText = "Apply", CloseButtonText = "Cancel", Content = new StackPanel { Spacing = 8, Children = { input, error } } };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    if (!ReferenceEquals(Instance, instance)) throw new InvalidOperationException("The view was replaced while editing.");
                    var updated = snapshot.WithCell(key, column.Field, Scalar.Parse(input.Text, kind));
                    var value = old.Value.Kind == ScalarKind.String ? Scalar.From(updated.ToJson()) : updated;
                    var result = instance.Write(node, "data", value, Session.Actor, old.Revision);
                    if (!result.Success) throw new InvalidOperationException(result.Message);
                    ValueChanged?.Invoke(); Refresh(); Action(node, "onEditCellCommit");
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or System.Text.Json.JsonException)
                { args.Cancel = true; error.Text = ex.Message; }
            };
            editing = true;
            try { await dialog.ShowAsync(); }
            catch (InvalidOperationException ex) { Error?.Invoke(ex.Message); }
            finally { editing = false; }
        };
        static DocumentColumn Column(Scalar item)
        {
            if (item.Kind != ScalarKind.Object) throw new FormatException("Table columns must be objects.");
            var fields = item.Fields; var name = fields.GetValueOrDefault("field").ToString();
            ScalarKind? type = fields.TryGetValue("type", out var value) && Enum.TryParse<ScalarKind>(value.ToString(), true, out var parsed) ? parsed : null;
            return new(name, fields.GetValueOrDefault("title", Scalar.From(name)).ToString(), fields.GetValueOrDefault("weight", Scalar.From(1)).AsNumber(), fields.GetValueOrDefault("editable", Scalar.From(true)).AsBoolean(), type);
        }
    }
    private void RefreshBuiltinTable(ComponentNode node, HmiDataTable table)
    {
        if (Runtime is null) return;
        if (node.Type == "AlarmStatusTable")
        {
            table.SetData([new("priority", "Priority", .7), new("name", "Alarm", 2), new("state", "State", 1.5), new("time", "Active (UTC)", 1)], Runtime.Alarms.Status(Flag(node, "showShelved", true)).Select(e => new TableRow(e.EventId.ToString(), [e.Priority.ToString(), e.Name, e.State, e.ActiveTime.UtcDateTime.ToString("HH:mm:ss")], e.Active ? "#D1903E" : "#78A290")).ToArray());
        }
        else if (node.Type == "AlarmJournalTable")
            table.SetData([new("time", "Timestamp (UTC)"), new("name", "Alarm", 2), new("transition", "Transition"), new("actor", "Actor")], Runtime.Alarms.Journal().Reverse().Select(e => new TableRow(e.Sequence.ToString(), [e.Timestamp.UtcDateTime.ToString("HH:mm:ss"), e.Name, e.Transition, e.Actor])).ToArray());
        else if (Text(node, "source", "tags") == "tags")
            table.SetData([new("path", "Tag path", 2.5), new("value", "Value"), new("unit", "Unit", .6), new("quality", "Quality")], Runtime.Tags.Browse(Text(node, "filter")).Select(d => { var value = Runtime.Tags.Read(d.Path); return new TableRow(d.Path, [d.Path, value.Value.ToString(), d.Unit, value.Quality.ToString()]); }).ToArray());
    }
}
