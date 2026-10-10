using Uno.UI.Hosting;
namespace ScadaSpace.App;
public class Program
{
    public static async Task Main(string[] args)
    {
        var host = UnoPlatformHostBuilder.Create().App(() => new App()).UseWebAssembly().Build();
        await host.RunAsync();
    }
}
