using LiteDB.Async;

namespace ArchiveThis ;

public class Database {
    private const string _db="archive.db";

    public async Task<IEnumerable<RequestItem>> GetNewItems() 
    {
        return await GetCollection().FindAsync(q => q.State == RequestItem.RequestStates.Pending);
    }

    private  ILiteCollectionAsync<RequestItem> GetCollection() {
        using var db = new LiteDatabaseAsync(_db);
        return db.GetCollection<RequestItem>();
    }

    public async Task UpdateItem(RequestItem item) {
        await GetCollection().UpdateAsync(item);
    }
 
    public async Task InsertItem(RequestItem item) {
        await GetCollection().InsertAsync(item);
    }

    public async Task DeleteFinishedItems() {
        await GetCollection().DeleteManyAsync(q=>q.State== RequestItem.RequestStates.Success || q.State== RequestItem.RequestStates.Error);
    }
}