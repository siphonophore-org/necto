using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using Jellyfin.Plugin.Necto.Configuration.CustomPages;
using Jellyfin.Plugin.Necto.Services.CustomPages;

namespace Jellyfin.Plugin.Necto.Tests.Fakes.CustomPages;

/// <summary>
/// A store service that finds the stores a test gives it, answers with the results a test sets, and records every
/// call with the owner or user it was made for.
/// </summary>
public sealed class RecordingStoreService : IStoreService
{
    public Dictionary<string, PageStore> Stores { get; } = new(StringComparer.OrdinalIgnoreCase);

    public (StoreWriteResult Result, StoreRecord? Record) CreateResult { get; set; } = (StoreWriteResult.Ok, new StoreRecord { Id = "r1" });

    public (StoreWriteResult Result, StoreRecord? Record) UpdateResult { get; set; } = (StoreWriteResult.Ok, new StoreRecord { Id = "r1" });

    public bool DeleteResult { get; set; } = true;

    public int ClearResult { get; set; }

    public List<Guid?> Reads { get; } = new();

    public List<Guid> Creates { get; } = new();

    public List<(string Id, Guid? Owner)> Updates { get; } = new();

    public List<(string Id, Guid? Owner)> Deletes { get; } = new();

    public List<(PageStore Store, string? UserName)> Clears { get; } = new();

    public List<CancellationToken> Sweeps { get; } = new();

    public PageStore? Find(string name) => Stores.GetValueOrDefault(name);

    public IReadOnlyList<StoreRecord> Read(PageStore store, Guid? owner)
    {
        Reads.Add(owner);
        return Array.Empty<StoreRecord>();
    }

    public StoreRecord? ReadOne(PageStore store, string id, Guid? owner) => null;

    public (StoreWriteResult Result, StoreRecord? Record) Create(PageStore store, JsonNode? data, string? label, Guid userId)
    {
        Creates.Add(userId);
        return CreateResult;
    }

    public (StoreWriteResult Result, StoreRecord? Record) Update(PageStore store, string id, JsonNode? data, string? label, Guid? owner)
    {
        Updates.Add((id, owner));
        return UpdateResult;
    }

    public bool Delete(PageStore store, string id, Guid? owner)
    {
        Deletes.Add((id, owner));
        return DeleteResult;
    }

    public int Clear(PageStore store, string? userName = null)
    {
        Clears.Add((store, userName));
        return ClearResult;
    }

    public int Count(PageStore store) => 0;

    public int SweepAll(CancellationToken cancellationToken = default)
    {
        Sweeps.Add(cancellationToken);
        return 0;
    }
}
