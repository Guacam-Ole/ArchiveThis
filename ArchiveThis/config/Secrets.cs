using Newtonsoft.Json;

namespace ArchiveThis.Config;

public class Secrets
{
    public static Secrets GetSecrets()
    {
        return JsonConvert.DeserializeObject<Secrets>(File.ReadAllText("./config/secrets.json")) ?? throw new FileNotFoundException("cannot read config");
    }

    public string Instance { get; set; }
    public string AccessToken { get; set; }
}