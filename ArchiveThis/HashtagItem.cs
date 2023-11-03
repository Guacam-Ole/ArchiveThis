using System.Data.Common;

namespace ArchiveThis.Models;

public class HashtagItem {
    public string HashTag {get;set;}
    public List<RequestItem> RequestItems {get;set;}
}   

