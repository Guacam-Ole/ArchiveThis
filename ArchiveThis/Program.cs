namespace ArchiveThis;

using System;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Mastodon-WaybackBot init");
        var services=AddServices();
        var toot        =services.GetRequiredService<Toot>();
         toot.CheckForNewTagContents().Wait();
     }



 private static IServiceProvider AddServices()
    {
        var services = new ServiceCollection();

        // services.AddLogging(logging =>
        // {
        //     logging.ClearProviders();
        //     logging.AddConsole();
        //     logging.SetMinimumLevel(LogLevel.Debug);
        //     var logFile = "youtoot.log";
        //     logging.AddFile(logFile, conf => { conf.Append = true; conf.MaxRollingFiles = 1; conf.FileSizeLimitBytes = 100000; });
        // });
        services.AddSingleton<Database>();
        services.AddScoped<Toot>();

        var provider = services.BuildServiceProvider();
        return provider;
    }


}
