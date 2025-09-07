using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

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
        var toot = services.GetRequiredService<Toot>();
        Console.WriteLine("Press Crtl-C to stop");

        var stopApplication = false;
        Console.CancelKeyPress += (_, _) =>
        {
            Console.WriteLine("stopping after loop");
            stopApplication=true;
        };
        while (!stopApplication)
        {
            try
            {
                toot.GetNotifications().Wait();
                Thread.Sleep(TimeSpan.FromSeconds(10));
            }
            catch (Exception e)

            {
                Console.WriteLine(e.ToString());
                Thread.Sleep(TimeSpan.FromMinutes(10));
            }
        }
    }

    private static IServiceProvider AddServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(cfg => cfg.SetMinimumLevel(LogLevel.Debug));
        services.AddSerilog(cfg =>
        {
            cfg.MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("job", Assembly.GetEntryAssembly()?.GetName().Name)
                .Enrich.WithProperty("service", Assembly.GetEntryAssembly()?.GetName().Name)
                .Enrich.WithProperty("desktop", Environment.GetEnvironmentVariable("DESKTOP_SESSION"))
                .Enrich.WithProperty("language", Environment.GetEnvironmentVariable("LANGUAGE"))
                .Enrich.WithProperty("lc", Environment.GetEnvironmentVariable("LC_NAME"))
                .Enrich.WithProperty("timezone", Environment.GetEnvironmentVariable("TZ"))
                .Enrich.WithProperty("dotnetVersion", Environment.GetEnvironmentVariable("DOTNET_VERSION"))
                .Enrich.WithProperty("inContainer",
                    Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"))
                .WriteTo.GrafanaLoki(Environment.GetEnvironmentVariable("LOKIURL") ?? "http://thebeast:3100",
                    propertiesAsLabels: ["job"]);
            if (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ==
                "Debug")
            {
                cfg.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });
        services.AddScoped<Toot>();
        services.AddSingleton(Config.Config.GetConfig());
        services.AddSingleton(Config.Secrets.GetSecrets());

        var provider = services.BuildServiceProvider();
        return provider;
    }
}
