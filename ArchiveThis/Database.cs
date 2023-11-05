using ArchiveThis.Models;
using LiteDB.Async;

namespace ArchiveThis;

public class Database
{
    private const string _db = "archive.db";

    public async Task<List<RequestItem>> GetNewRequestItems()
    {
        using var db = new LiteDatabaseAsync(_db);
        var collection = db.GetCollection<RequestItem>();
        return (await collection.FindAsync(q => q.State == RequestItem.RequestStates.Pending)).ToList();
    }

    public async Task UpsertItem<T>(T item)
    {
        using var db = new LiteDatabaseAsync(_db);
        var collection = db.GetCollection<T>();
        await collection.UpsertAsync(item);
    }

    public async Task DeleteFinishedItems(List<RequestItem.RequestStates> states,  DateTime since)
    {
        using var db = new LiteDatabaseAsync(_db);
        var collection = db.GetCollection<RequestItem>();
        await collection.DeleteManyAsync(q => states.Contains(q.State) && q.Created<=since);
    }

    public async Task<List<T>> GetAllItems<T>()
    {
        using var db = new LiteDatabaseAsync(_db);
        var collection = db.GetCollection<T>();
        return (await collection.FindAllAsync()).ToList();
    }
}