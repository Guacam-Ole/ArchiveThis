namespace ArchiveThis;

using System;
using System.Diagnostics;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Mastodon-WaybackBot init");
        var services = AddServices();
        var archive = services.GetRequiredService<Archive>();
        System.Console.WriteLine("Press Crtl-C to end all timers");

        Console.CancelKeyPress += (s, e) =>
        {
            System.Console.WriteLine("bye");
            Environment.Exit(0);
        };

        //var store=services.GetRequiredService<Store>();
        //store.UrlHasContent("https://web.archive.org/web/20231120162250/https://www.mopo.de/hamburg/von-altona-nach-bad-oldesloe-so-steht-es-um-die-neue-s4", "doof").Wait();

        archive.StartTimers();
        while (true)
        {
            Thread.Sleep(10000);
        }
    }

    private static IServiceProvider AddServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddSimpleConsole(options =>
            {
                options.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled;
            });   
            var logFile = "archivethis.log";
            logging.AddFile(logFile, conf => { conf.MinLevel = LogLevel.Debug; conf.Append = true; conf.MaxRollingFiles = 1; conf.FileSizeLimitBytes = 100000; });
        });
        services.AddScoped<Database>();
        services.AddScoped<Toot>();
        services.AddScoped<Archive>();
        services.AddScoped<Store>();
        services.AddSingleton(Config.Config.GetConfig());
        services.AddSingleton(Config.Secrets.GetSecrets());

        var provider = services.BuildServiceProvider();
        return provider;
    }
}
