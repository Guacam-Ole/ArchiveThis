using System.Diagnostics;
using LiteDB;
using Newtonsoft.Json;

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
        //Ignored,
        AlreadyBlocked,
        Posted
    }

    public ObjectId Id {get;set;}
    public string? Tag { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    
    
    public string MastodonId { get; set; }// MastodonId
    public string? ResponseId {get;set;}
    public string? Url { get; set; }
    public RequestStates State { get; set; }
    public Site? Site { get; set; }
}