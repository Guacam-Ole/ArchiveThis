using LiteDB;

using System.Diagnostics;

namespace ArchiveThis.Models;

[DebuggerDisplay("{Tag}")]
public class HashtagItem
{
    public ObjectId Id { get; set; }
    public string Tag { get; set; } = "unknown";
    public List<RequestItem> RequestItems { get; set; } = new List<RequestItem>();

    public override string ToString()
    {
        return $"{Tag}|{RequestItems?.Count}";
    }
}