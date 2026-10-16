namespace AlgoTrading.Screener.Storage;

public interface IQueryStore
{
    /// <summary>User-saved queries, most recently run first.</summary>
    Task<IReadOnlyList<SavedQuery>> GetSavedAsync(CancellationToken ct = default);

    /// <summary>Shipped templates, in the order they appear in the JSON file.</summary>
    Task<IReadOnlyList<SavedQuery>> GetTemplatesAsync(CancellationToken ct = default);

    /// <summary>Insert or replace by Id. Preserves the other entries.</summary>
    Task UpsertAsync(SavedQuery query, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>Updates LastRunUtc and LastRunRowCount for one entry.</summary>
    Task RecordRunAsync(string id, int rowCount, CancellationToken ct = default);
}