namespace ArchiveThis;

using System;
using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Security;

public class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Mastodon-WaybackBot init");
        var services=AddServices();
        var archive=services.GetRequiredService<Archive>();
        archive.ArchiveUrlsForHashtag().Wait();
        archive.RespondHashtagResults().Wait();

     }



 private static IServiceProvider AddServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Debug);
            var logFile = "archivethis.log";
            logging.AddFile(logFile, conf => { conf.Append = true; conf.MaxRollingFiles = 1; conf.FileSizeLimitBytes = 100000; });
        });
        services.AddSingleton<Database>();
        services.AddScoped<Toot>();
        services.AddScoped<Archive>();
        services.AddScoped<Store>();
        services.AddSingleton(Config.Config.GetConfig());
        services.AddSingleton(Config.Secrets.GetSecrets());

        var provider = services.BuildServiceProvider();
        return provider;
    }


}
