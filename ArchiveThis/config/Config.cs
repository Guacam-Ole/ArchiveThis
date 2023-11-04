using System.ComponentModel.DataAnnotations;
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
}

public class Site
{
    public string Domain { get; set; }
    public string FailureContent { get; set; }
}

public class Timers
{
    public SingleTimer HashTagCheck { get; set; } = new SingleTimer(0,1);
    public SingleTimer CleanUp { get; set; } = new SingleTimer { Delay=TimeSpan.Zero, Interval=TimeSpan.FromDays(14)};
    public SingleTimer SendRepliesToMastodon { get; set; } = new SingleTimer(0,5);
    public SingleTimer SendRequestsToArchive { get; set; } = new SingleTimer(1,2);
    public SingleTimer CheckForMastodonRequests {get;set;}=new SingleTimer(0,2);
}

public class SingleTimer {
    public SingleTimer(int minutesDelay, int minutesInterval) {
        Interval=TimeSpan.FromMinutes(minutesInterval);
        Delay=TimeSpan.FromMinutes(minutesDelay );
    }
    public SingleTimer() {}
    public TimeSpan Interval {get;set;}=TimeSpan.Zero;
    public TimeSpan Delay {get;set;}=TimeSpan.Zero;
}