using ArchiveThis.Models;
using LiteDB.Async;

namespace ArchiveThis ;

public class Database {
    private const string _db="archive.db";

    public async Task<List<RequestItem>> GetNewItems() 
    {
        using var db = new LiteDatabaseAsync(_db);
        var collection= db.GetCollection<RequestItem>();
        return (await collection.FindAsync(q => q.State == RequestItem.RequestStates.Pending)).ToList();
    }

    public async Task UpdateItem(RequestItem item) {
               using var db = new LiteDatabaseAsync(_db);
        var collection= db.GetCollection<RequestItem>();
        await collection.UpdateAsync(item);
    }
 
    public async Task InsertItem(RequestItem item) {
        using var db = new LiteDatabaseAsync(_db);
        var collection= db.GetCollection<RequestItem>();
        await collection.InsertAsync(item);
    }

    public async Task DeleteFinishedItems() {
        using var db = new LiteDatabaseAsync(_db);
        var collection= db.GetCollection<RequestItem>();
        await collection.DeleteManyAsync(q=>q.State== RequestItem.RequestStates.Success || q.State== RequestItem.RequestStates.Error);
    }

    public async Task<List<HashtagItem>> GetHashTagItems() {
        using var db = new LiteDatabaseAsync(_db);
        var collection = db.GetCollection<HashtagItem>();
        return (await collection.FindAllAsync()).ToList();
    }
}