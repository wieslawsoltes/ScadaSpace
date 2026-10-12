using ScadaSpace.Core;

namespace ScadaSpace.Runtime;

public static class DemoProject
{
    public static ProjectDefinition Create()
    {
        var project = new ProjectDefinition { Name = "Water Treatment" };
        TagDefinition NumberTag(string path, double value, string unit, bool writable = false, double max = 100) => new() { Path = "[default]Plant/" + path, InitialValue = Scalar.From(value), DataType = ScalarKind.Number, Unit = unit, Writable = writable, Minimum = 0, Maximum = max };
        project.Tags = [
            NumberTag("Tank01/Level", 68.4, "%"), NumberTag("Tank01/Setpoint", 72, "%", true),
            NumberTag("Tank01/Temperature", 44, "°C"), NumberTag("Tank02/Level", 43.8, "%"),
            NumberTag("Pump/Flow", 112, "m³/h", max: 250), NumberTag("Pump/Pressure", 3.8, "bar", max: 10),
            new() { Path = "[default]Plant/Pump/Enabled", DataType = ScalarKind.Boolean, InitialValue = Scalar.From(true) },
            new() { Path = "[default]Plant/Mode", DataType = ScalarKind.String, InitialValue = Scalar.From("Auto") }
        ];
        project.Alarms = [
            new() { Id = "tank01-high", Name = "Tank 01 level above advisory limit", TagPath = "[default]Plant/Tank01/Level", Setpoint = 65, Deadband = 2, Priority = AlarmPriority.Medium },
            new() { Id = "tank02-high", Name = "Tank 02 high level", TagPath = "[default]Plant/Tank02/Level", Setpoint = 85, Deadband = 3, Priority = AlarmPriority.High },
            new() { Id = "pump-stopped", Name = "Transfer pump stopped", TagPath = "[default]Plant/Pump/Enabled", Mode = AlarmMode.Equal, Setpoint = 0, Deadband = 0, Priority = AlarmPriority.Low }
        ];
        project.Styles["card"] = new() { ["background"] = Scalar.From("#FFFFFF"), ["borderColor"] = Scalar.From("#D9E1E5"), ["cornerRadius"] = Scalar.From(5) };
        var overview = View("Overview", 1040, 760);
        void Add(ComponentNode node) => overview.Root.Children.Add(node);
        ComponentNode Make(string type, string name, double x, double y, double w, double h)
        { var n = ComponentCatalog.Create(type, x, y); n.Name = name; n.Width = w; n.Height = h; Add(n); return n; }
        void Label(string name, string text, double x, double y, double w, double size = 14, string color = "#354C5A")
        { var n = Make("Label", name, x, y, w, size + 12); n.Props["text"] = Scalar.From(text); n.Props["fontSize"] = Scalar.From(size); n.Props["foreground"] = Scalar.From(color); }
        var head = Make("Rectangle", "Header", 0, 0, 1040, 82); head.Props["color"] = Scalar.From("#263F50"); head.Props["strokeWidth"] = Scalar.From(0);
        Label("Title", "WATER TREATMENT", 26, 15, 600, 23, "#FFFFFF");
        Label("Subtitle", "Area 01  /  Transfer & storage", 28, 48, 500, 13, "#B9D5DF");
        Label("SimulationNotice", "ISOLATED SIMULATION", 790, 28, 230, 13, "#B6E2D6");
        Label("ProcessHeading", "PROCESS OVERVIEW", 28, 101, 500, 14);
        Label("Tank01Name", "TK-101  /  FEED TANK", 66, 145, 240, 14);
        Label("Tank02Name", "TK-102  /  STORAGE", 535, 145, 250, 14);
        var pipeA = Make("Pipe", "FeedPipe", 221, 305, 166, 74); Bind(pipeA, "value", "Pump/Enabled");
        var pipeB = Make("Pipe", "DischargePipe", 446, 272, 112, 110); Bind(pipeB, "value", "Pump/Enabled");
        var outlet = Make("Pipe", "Outlet", 706, 315, 250, 24); outlet.Props["route"] = Scalar.From("straight"); Bind(outlet, "value", "Pump/Enabled");
        var tank1 = Make("Tank", "Tank01", 64, 176, 194, 230); Bind(tank1, "value", "Tank01/Level");
        var tank2 = Make("Tank", "Tank02", 536, 176, 194, 230); Bind(tank2, "value", "Tank02/Level"); tank2.Props["color"] = Scalar.From("#6499CB");
        var pump = Make("Pump", "TransferPump", 362, 326, 102, 96); Bind(pump, "value", "Pump/Enabled");
        var valve = Make("Valve", "OutletValve", 820, 280, 105, 90); Bind(valve, "value", "Pump/Enabled");
        // Catalog symbols start in their explicit neutral state. The process example
        // must bind that state as well as retain its historical Boolean value contract.
        pump.Bindings["state"] = new() { Kind = BindingKind.Expression, Source = "if({[default]Plant/Pump/Enabled}, 'running', 'stopped')" };
        valve.Bindings["state"] = new() { Kind = BindingKind.Expression, Source = "if({[default]Plant/Pump/Enabled}, 'open', 'closed')" };
        Label("PumpLabel", "P-101", 376, 423, 100, 13);
        Label("ValveLabel", "XV-102", 841, 378, 120, 13);
        var gauge = Make("Gauge", "FlowGauge", 301, 155, 228, 155); Bind(gauge, "value", "Pump/Flow"); gauge.Props["maximum"] = Scalar.From(200); gauge.Props["unit"] = Scalar.From("m³/h");
        var temp = Make("Thermometer", "Temperature", 744, 155, 78, 152); Bind(temp, "value", "Tank01/Temperature");
        var pressure = Make("LEDDisplay", "Pressure", 859, 173, 144, 72); Bind(pressure, "value", "Pump/Pressure"); pressure.Props["unit"] = Scalar.From("bar");
        Label("PressureLabel", "DISCHARGE PRESSURE", 847, 254, 180, 11);
        Label("LevelCaption", "Level setpoint (%)", 38, 445, 210, 13);
        var setpoint = Make("NumericEntry", "LevelSetpoint", 37, 475, 125, 38); Bind(setpoint, "value", "Tank01/Setpoint", true);
        var slider = Make("Slider", "SetpointSlider", 177, 472, 193, 45); Bind(slider, "value", "Tank01/Setpoint", true);
        var enabled = Make("ToggleSwitch", "PumpEnabled", 405, 470, 180, 44); enabled.Props["text"] = Scalar.From("Transfer pump"); Bind(enabled, "value", "Pump/Enabled", true);
        var mode = Make("Dropdown", "OperatingMode", 614, 475, 154, 38); Bind(mode, "value", "Mode", true);
        var stop = Make("Button", "StopPump", 806, 473, 172, 40); stop.Props["text"] = Scalar.From("Stop transfer pump");
        stop.Events["onAction"] = [new() { Kind = ActionKind.WriteTag, Target = "[default]Plant/Pump/Enabled", Value = Scalar.From(false) }];
        var chart = Make("TrendChart", "TankLevels", 28, 546, 590, 186); chart.Props["title"] = Scalar.From("Tank levels  /  live history");
        Make("AlarmStatusTable", "ActiveAlarms", 637, 546, 374, 186);
        project.Views.Add(overview);
        var trends = View("Trends", 1040, 760);
        var trendChart = ComponentCatalog.Create("TrendChart", 20, 25); trendChart.Width = 1000; trendChart.Height = 440; trends.Root.Children.Add(trendChart);
        var bars = ComponentCatalog.Create("BarChart", 20, 485); bars.Width = 485; bars.Height = 245; trends.Root.Children.Add(bars);
        var pie = ComponentCatalog.Create("PieChart", 528, 485); pie.Width = 492; pie.Height = 245; trends.Root.Children.Add(pie);
        project.Views.Add(trends);
        var alarms = View("Alarms", 1040, 760);
        var status = ComponentCatalog.Create("AlarmStatusTable", 20, 24); status.Width = 1000; status.Height = 330;
        var journal = ComponentCatalog.Create("AlarmJournalTable", 20, 378); journal.Width = 1000; journal.Height = 352;
        alarms.Root.Children.AddRange([status, journal]); project.Views.Add(alarms);
        var detail = View("EquipmentDetail", 300, 220); detail.Parameters["equipment"] = Scalar.From("Tank01");
        var detailName = ComponentCatalog.Create("Label", 12, 8); detailName.Width = 270; detailName.Bindings["text"] = new() { Kind = BindingKind.Expression, Source = "'Equipment / ' + {view.params.equipment}" }; detail.Root.Children.Add(detailName);
        var detailGauge = ComponentCatalog.Create("Gauge", 25, 50); detailGauge.Width = 240; detailGauge.Height = 160; detailGauge.Bindings["value"] = new() { Kind = BindingKind.Tag, Source = "[default]Plant/{equipment}/Level" }; detail.Root.Children.Add(detailGauge); project.Views.Add(detail);
        var gallery = View("ComponentGallery", 1040, 1500);
        var items = ComponentCatalog.All.Where(x => x.Type is not ("Container" or "FlexRepeater" or "EmbeddedView" or "AlarmStatusTable" or "AlarmJournalTable" or "TrendChart" or "Table")).ToArray();
        gallery.Height = gallery.Root.Height = 18 + Math.Ceiling(items.Length / 4d) * 220;
        for (var i = 0; i < items.Length; i++)
        {
            var x = 26 + i % 4 * 255; var y = 18 + i / 4 * 220;
            var title = ComponentCatalog.Create("Label", x, y); title.Props["text"] = Scalar.From(items[i].DisplayName); title.Props["fontSize"] = Scalar.From(13); gallery.Root.Children.Add(title);
            var n = ComponentCatalog.Create(items[i].Type, x, y + 34); n.Width = Math.Min(n.Width, 230); n.Height = Math.Min(n.Height, 165); gallery.Root.Children.Add(n);
        }
        project.Views.Add(gallery);
        project.Pages = [new() { Path = "/", View = "Overview", Title = "Overview" }, new() { Path = "/trends", View = "Trends", Title = "Trends" }, new() { Path = "/alarms", View = "Alarms", Title = "Alarms" }, new() { Path = "/components", View = "ComponentGallery", Title = "Components" }];
        return project;
    }
    public static ViewDefinition View(string path, double width, double height) => new() { Path = path, Width = width, Height = height, Root = new() { Name = "root", Type = "Container", Width = width, Height = height } };
    private static void Bind(ComponentNode node, string property, string suffix, bool write = false) => node.Bindings[property] = new() { Kind = BindingKind.Tag, Source = "[default]Plant/" + suffix, Bidirectional = write };
}
