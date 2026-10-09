using Microsoft.UI.Xaml;
namespace ScadaSpace.App;
public partial class App : Application
{
    public static Window? MainWindow { get; private set; }
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = "ScadaSpace Designer" };
        var page = new WorkbenchPage(); MainWindow.Content = page;
        UnhandledException += (_, e) => page.ReportUnhandled(e.Exception.ToString());
        MainWindow.Closed += (_, _) => page.Dispose(); MainWindow.Activate();
    }
}
