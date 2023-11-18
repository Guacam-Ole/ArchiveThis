using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Newtonsoft.Json;

namespace ArchiveThis.Config;

public class Config
{
    public static Config GetConfig()
    {
        return JsonConvert.DeserializeObject<Config>(File.ReadAllText("./config/config.json")) ?? throw new FileNotFoundException("cannot read config");
    }

    public List<string> HashTags { get; set; }=new List<string>();
    public List<Site> Sites { get; set; }=new List<Site>();
    public Timers Timers {get;set;}=new Timers();
    public int DeleteSuccessFulRequestAfterDays {get;set;}=30;
    public int DeleteFailedRequestsAfterDays {get;set;}=14;
}

public class Site
{
    public string Domain { get; set; }
    public string FailureContent { get; set; }
}

public class Timers
{
    public SingleTimer HashTagCheck { get; set; } = SingleTimer.BySeconds(0,60);
    public SingleTimer CleanUp { get; set; } = new SingleTimer { Delay=TimeSpan.Zero, Interval=TimeSpan.FromDays(14)};
    public SingleTimer SendRepliesToMastodon { get; set; } = SingleTimer.ByMinutes(5,5);
    public SingleTimer SendRequestsToArchive { get; set; } = SingleTimer.ByMinutes(1,2);
    public SingleTimer CheckForMastodonRequests {get;set;}=SingleTimer.BySeconds(5,10);
    public SingleTimer WatchDog {get;set;}=new SingleTimer{ Delay=TimeSpan.FromSeconds(10), Interval=TimeSpan.FromMinutes(60)};
}

[DebuggerDisplay("Delay: {Delay}. Interval_ {Interval}")]
public class SingleTimer {
 
    public SingleTimer() {}
    public TimeSpan Interval {get;set;}=TimeSpan.Zero;
    public TimeSpan Delay {get;set;}=TimeSpan.Zero;

    public override string ToString()
    {
        return $"Delay: {Delay}. Interval: {Interval}";
    }

    public static SingleTimer BySeconds(int secondDelay, int secondInterval) {
        return new SingleTimer { Delay=TimeSpan.FromSeconds( secondDelay), Interval= TimeSpan.FromSeconds(secondInterval)};
    }
    public static SingleTimer ByMinutes(int minutesDelay, int minutesInterval) {
        return new SingleTimer { Delay=TimeSpan.FromMinutes( minutesDelay), Interval= TimeSpan.FromMinutes(minutesInterval)};
    }
}