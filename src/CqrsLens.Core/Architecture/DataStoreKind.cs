namespace CqrsLens;

/// <summary>Persistence or caching technology backing a discovered <see cref="DataStoreInfo"/>.</summary>
public enum DataStoreKind
{
    EfCore,
    Ef6,
    Dapper,
    Linq2Db,
    MongoDb,
    NHibernate,
    AdoNet,
    Redis,
    CosmosDb,
    DocumentDb,
    Repository,
}
