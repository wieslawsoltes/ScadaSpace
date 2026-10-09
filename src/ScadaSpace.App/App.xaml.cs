using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ScadaSpace.Controls;
using ScadaSpace.Graphics;

namespace ScadaSpace.App;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = "ScadaSpace Designer" };
        MainWindow.Content = new Grid { Children = { new HmiSymbol { Kind = SymbolKind.Tank, Value = 68.4, Width = 220, Height = 280, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } } };
        MainWindow.Activate();
    }
}
