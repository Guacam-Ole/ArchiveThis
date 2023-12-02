using System.Diagnostics;
using LiteDB;
using Newtonsoft.Json;
using ArchiveThis.Config;
using Mastonet;

namespace ArchiveThis.Models;

[DebuggerDisplay("Id:{MastodonId}, Created: {Created}, State: {State}, Url:'{Url}'")]
public class RequestItem
{
    
    public enum RequestStates
    {
        Pending,
        Running,
        Success,
        Error,
        InvalidUrl,
        AlreadyBlocked,
        Posted,
        GivingUp
    }

    public ObjectId Id {get;set;}
    public string? Tag { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    
    public Visibility Visibility {get;set;}
    
    public string MastodonId { get; set; }
    public string? RequestedBy {get;set;}
    public string? ResponseId {get;set;}
    public string? Url { get; set; }
    public string? ArchiveUrl {get;set;}
    public RequestStates State { get; set; }
    public RequestStates? OldState {get;set;}
    public Site? Site { get; set; }
    public int ErrorCount { get; set; } = 0;


    public override string ToString()
    {
        return $"{Id}|Mastodon:{MastodonId}|Url:{Url}|State:{State}";
    }
}