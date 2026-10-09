using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ScadaSpace.Graphics;
using SkiaSharp;
using Windows.Storage;

namespace ScadaSpace.App;

public partial class App : Application
{
    private const string FontAsset = "ms-appx:///Uno.Fonts.Roboto/Fonts/Roboto-Regular.ttf";
    public static Window? MainWindow { get; private set; }
    public App() { InitializeComponent(); RequestedTheme = ApplicationTheme.Light; }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window { Title = "ScadaSpace Designer" };
        MainWindow.Content = new TextBlock { Text = "Loading ScadaSpace…", Margin = new Thickness(24), FontSize = 18 };
        MainWindow.Activate();
        try
        {
            // Reuse the font shipped by Uno instead of relying on browser/OS font substitution.
            var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(FontAsset));
            using var stream = await file.OpenStreamForReadAsync();
            CanvasPainter.DrawingTypeface = SKTypeface.FromStream(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        { System.Diagnostics.Debug.WriteLine("Bundled drawing font could not be loaded: " + ex.Message); }
        var page = new WorkbenchPage { FontFamily = new FontFamily(FontAsset + "#Roboto") };
        MainWindow.Content = page;
        UnhandledException += (_, e) => page.ReportUnhandled(e.Exception.ToString());
        MainWindow.Closed += (_, _) => page.Dispose();
    }
}
