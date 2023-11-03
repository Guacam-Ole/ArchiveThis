namespace ArchiveThis.Models;

public class RequestItem {
    public enum RequestStates { Pending, Running, Success, Error }
    public DateTime Created {get;set; }
    public DateTime? Updated {get;set;}
    public string MastodonId {get;set;}
    public string? Url {get;set;}
    public RequestStates State {get;set;}
    public int Id {get;set;}
    public string? ArchiveUrl {get;set;}
}