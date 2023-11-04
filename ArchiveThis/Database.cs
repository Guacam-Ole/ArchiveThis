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

    // public async Task InsertItem<T>(T item)
    // {
    //     using var db = new LiteDatabaseAsync(_db);
    //     var collection = db.GetCollection<T>();
    //     await collection.InsertAsync(item);
    // }

    public async Task DeleteFinishedItems()
    {
        using var db = new LiteDatabaseAsync(_db);
        var collection = db.GetCollection<RequestItem>();
        await collection.DeleteManyAsync(q => q.State == RequestItem.RequestStates.Success || q.State == RequestItem.RequestStates.Error);
    }

    public async Task<List<T>> GetAllItems<T>()
    {
        using var db = new LiteDatabaseAsync(_db);
        var collection = db.GetCollection<T>();
        return (await collection.FindAllAsync()).ToList();
    }
}