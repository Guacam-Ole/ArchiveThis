using Newtonsoft.Json;

namespace ArchiveThis
{


    public class Config
    {
        public static Config GetConfig() {
            return JsonConvert.DeserializeObject<Config>(File.ReadAllText("./config.json")) ?? throw new FileNotFoundException("cannot read config");
        }

        public string Instance { get; set; }
        public string Secret { get; set; }
        public List<string> HashTags {get;set;}
        public List<Site> Sites {get;set;}

    }

    public class Site {
        public string Domain {get;set;}
        public string FailureContent {get;set;}
    }
}