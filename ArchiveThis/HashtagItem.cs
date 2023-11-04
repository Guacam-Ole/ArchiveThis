using System.Data.Common;
using System.Diagnostics;
using LiteDB;
using Newtonsoft.Json;


namespace ArchiveThis.Models;

[DebuggerDisplay("{Tag}")]
public class HashtagItem {
    public ObjectId Id {get;set;}
    public string Tag {get;set;}="unknown";
    public List<RequestItem> RequestItems {get;set;}=new List<RequestItem>();
    
}   

