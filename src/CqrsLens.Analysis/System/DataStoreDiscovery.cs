using System.Text.RegularExpressions;

namespace CqrsLens;

/// <summary>
/// Discovers persistence boundaries and entities across EF Core, EF6, Dapper, Linq2Db,
/// MongoDB, NHibernate, ADO.NET, Redis, Cosmos DB, and Document DBs (Marten / RavenDB),
/// maintaining identical behavior across text and Roslyn modes.
/// </summary>
internal static partial class DataStoreDiscovery
{
    [GeneratedRegex(
        @"\.\s*(?:QueryAsync|QueryFirstAsync|QueryFirstOrDefaultAsync|QuerySingleAsync|QuerySingleOrDefaultAsync|Query|QueryFirst|QueryFirstOrDefault|QuerySingle|QuerySingleOrDefault|ReadAsync|Read|GetAsync|Get|InsertAsync|Insert|UpdateAsync|Update|DeleteAsync|Delete)\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex DapperGenericCallRegex();

    [GeneratedRegex(
        @"\.\s*(?:ExecuteAsync|ExecuteScalarAsync|QueryMultipleAsync|Execute|ExecuteScalar|QueryMultiple|QueryAsync|QueryFirst|QuerySingle)\s*\(",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex DapperMethodCallRegex();

    [GeneratedRegex(
        @"(?:ITable|GetTable)\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex Linq2DbTableRegex();

    [GeneratedRegex(
        @"(?:IMongoCollection|GetCollection)\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>(?:\s*\(\s*@?""(?<collection>[A-Za-z_][A-Za-z0-9_.\-]{0,60})"")?",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex MongoCollectionRegex();

    [GeneratedRegex(
        @"\.\s*(?:Query|QueryOver|Get|GetAsync|Load|LoadAsync)\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex GenericEntityQueryRegex();

    [GeneratedRegex(
        @"\.\s*(?:ReadItemAsync|UpsertItemAsync|CreateItemAsync|ReplaceItemAsync|GetItemQueryIterator|GetItemLinqQueryable)\s*<\s*(?<entity>[A-Za-z_]\w*(?:\s*\.\s*[A-Za-z_]\w*)*)\s*>",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex CosmosMethodRegex();

    [GeneratedRegex(
        @"(?:@?""(?<lit>(?:[^""\\]|\\.|""""){1,1200})"")",
        RegexOptions.Singleline,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(
        @"\b(?:FROM|JOIN|INTO|UPDATE)\s+(?:\[|""|`)?(?:[A-Za-z_]\w*\s*\.\s*(?:\[|""|`)?)?(?<table>[A-Za-z_][A-Za-z0-9_]{1,60})(?:\]|""|`)?",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex SqlTableRegex();

    [GeneratedRegex(
        @"\b(?:SELECT|INSERT|UPDATE|DELETE|MERGE)\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex SqlVerbRegex();

    private static readonly HashSet<string> IgnoredEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "object", "string", "bool", "boolean", "byte", "sbyte", "short", "ushort", "int", "int32",
        "int64", "uint", "long", "ulong", "float", "double", "decimal", "char", "nint", "nuint",
        "dynamic", "void", "unit", "guid", "datetime", "datetimeoffset", "dateonly", "timeonly",
        "timespan", "uri", "task", "valuetask", "bsondocument", "jobject", "jtoken", "jarray",
        "jsonelement", "jsonnode", "jsonobject", "jsondocument", "xmldocument", "xdocument",
        "datatable", "datarow", "dataset", "expandoobject", "dictionary", "keyvaluepair",
        "baseentity", "entity", "ientity", "ientitykey",
    };

    private static readonly string[] NonEntitySuffixes =
    {
        "Service", "Services", "Repository", "Repositories", "Repo", "Controller", "Handler",
        "Validator", "Middleware", "Options", "Settings", "Config", "Exception", "EventArgs",
        "Attribute", "Factory", "Provider", "Builder", "Client", "Manager", "Helper", "Extensions",
        "Processor", "Worker", "Consumer", "Command", "Query",
    };

    private static readonly HashSet<string> SqlStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "select", "from", "where", "set", "values", "into", "join", "inner", "left", "right",
        "outer", "cross", "on", "as", "and", "or", "not", "null", "dual", "top", "deleted",
        "inserted", "output", "using", "when", "then", "else", "end", "case", "group", "order",
        "by", "having", "distinct", "limit", "offset", "fetch", "next", "only", "rows", "row",
        "openjson", "information_schema", "sys", "temp", "cte", "cache", "error", "data",
    };

    private sealed class ProjectCapabilities
    {
        public ProjectInfo Project { get; init; } = null!;
        public bool IsEf6 { get; set; }
        public bool UsesEf { get; set; }
        public bool UsesDapper { get; set; }
        public bool UsesLinq2Db { get; set; }
        public bool UsesMongoDb { get; set; }
        public bool UsesNHibernate { get; set; }
        public bool UsesRedis { get; set; }
        public bool UsesCosmos { get; set; }
        public bool UsesMarten { get; set; }
        public bool UsesRaven { get; set; }
        public bool UsesSqlClient { get; set; }
        public string? PrimaryDbProvider { get; set; }
    }

    internal static void Discover(SystemAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = context.Result;

        var storesByKey = new Dictionary<(string Project, DataStoreKind Kind, string Name), DataStoreInfo>();

        DataStoreInfo GetOrAddStore(
            string projectName,
            DataStoreKind kind,
            string technology,
            string name,
            string? provider,
            SourceLocation? location)
        {
            var key = (projectName, kind, name);
            if (storesByKey.TryGetValue(key, out var existing))
            {
                if (existing.Provider is null && provider is not null)
                    existing.Provider = provider;
                existing.Location ??= location;
                return existing;
            }

            var created = new DataStoreInfo
            {
                Name = name,
                Kind = kind,
                Technology = technology,
                Provider = provider,
                ProjectName = string.IsNullOrEmpty(projectName) ? null : projectName,
                Location = location,
            };
            storesByKey[key] = created;
            return created;
        }

        // Group parsed non-test files by project for quick provider/package signals.
        var filesByProject = new Dictionary<string, List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in context.ParsedFiles)
        {
            if (context.IsTestProject(entry.File.ProjectName) || string.IsNullOrWhiteSpace(entry.File.ProjectName))
                continue;
            if (!filesByProject.TryGetValue(entry.File.ProjectName, out var list))
                filesByProject[entry.File.ProjectName] = list = new List<(SourceFile, CSharpScanner.FileScan, SourceMask)>();
            list.Add(entry);
        }

        var capsByProject = new Dictionary<string, ProjectCapabilities>(StringComparer.OrdinalIgnoreCase);
        foreach (var proj in result.Projects)
        {
            if (context.IsTestProject(proj.Name))
                continue;

            filesByProject.TryGetValue(proj.Name, out var projFiles);
            capsByProject[proj.Name] = BuildCapabilities(proj, projFiles, result.DbContexts);
        }

        // 1. Record EF Core / EF6 DbContexts into DataStores.
        foreach (var ctx in result.DbContexts)
        {
            var projName = ctx.ProjectName ?? string.Empty;
            capsByProject.TryGetValue(projName, out var caps);
            var isEf6 = caps?.IsEf6 ?? false;
            var kind = isEf6 ? DataStoreKind.Ef6 : DataStoreKind.EfCore;
            var tech = isEf6 ? "EF6" : "EF Core";
            var store = GetOrAddStore(projName, kind, tech, ctx.Name, caps?.PrimaryDbProvider, ctx.Location);

            foreach (var set in ctx.DbSets)
            {
                var entity = CSharpScanner.SimpleName(set.Entity);
                if (!string.IsNullOrWhiteSpace(entity))
                    SystemAnalysisContext.AddUniqueOrdinal(store.Entities, entity);
            }
            foreach (var ent in ctx.Entities)
            {
                if (!string.IsNullOrWhiteSpace(ent.Name))
                    SystemAnalysisContext.AddUniqueOrdinal(store.Entities, ent.Name);
            }
        }

        // Determine canonical host project for Linq2Db & shared non-EF data projects across solution.
        var linq2DbHosts = capsByProject.Values
            .Where(c => c.UsesLinq2Db)
            .Select(c => c.Project.Name)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? FindReachableHostProject(string? projectName, IReadOnlySet<string> candidateHosts)
        {
            if (string.IsNullOrWhiteSpace(projectName) || candidateHosts.Count == 0)
                return null;
            if (candidateHosts.Contains(projectName))
                return projectName;

            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { projectName };
            var queue = new Queue<string>();
            queue.Enqueue(projectName);
            var hits = new List<string>();

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!context.ProjectsByName.TryGetValue(cur, out var p))
                    continue;

                foreach (var r in p.ReferenceNames)
                {
                    if (!visited.Add(r))
                        continue;
                    if (candidateHosts.Contains(r))
                        hits.Add(r);
                    queue.Enqueue(r);
                }
            }

            return hits.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        }

        var linq2DbHostSet = new HashSet<string>(linq2DbHosts, StringComparer.OrdinalIgnoreCase);
        var defaultGlobalLinq2DbHost = linq2DbHosts.Count == 1 ? linq2DbHosts[0] : null;
        var hasSolutionDapper = capsByProject.Values.Any(c => c.UsesDapper);
        var hasSolutionMongo = capsByProject.Values.Any(c => c.UsesMongoDb);
        var hasSolutionNHibernate = capsByProject.Values.Any(c => c.UsesNHibernate);

        // 2. Scan non-test source files for non-EF persistence access.
        foreach (var (file, scan, mask) in context.ParsedFiles)
        {
            if (context.IsTestProject(file.ProjectName))
                continue;

            var projName = file.ProjectName ?? string.Empty;
            capsByProject.TryGetValue(projName, out var caps);
            var provider = caps?.PrimaryDbProvider;

            var hasUsingLinq2Db = scan.Usings.Any(u => u.Target.StartsWith("LinqToDB", StringComparison.Ordinal));
            var hasUsingDapper = scan.Usings.Any(u => u.Target.StartsWith("Dapper", StringComparison.Ordinal));
            var hasUsingMongo = scan.Usings.Any(u => u.Target.StartsWith("MongoDB", StringComparison.Ordinal));
            var hasUsingNHibernate = scan.Usings.Any(u => u.Target.StartsWith("NHibernate", StringComparison.Ordinal));
            var hasUsingRedis = scan.Usings.Any(u => u.Target.StartsWith("StackExchange.Redis", StringComparison.Ordinal));
            var hasUsingCosmos = scan.Usings.Any(u => u.Target.StartsWith("Microsoft.Azure.Cosmos", StringComparison.Ordinal));
            var hasUsingMarten = scan.Usings.Any(u => u.Target.StartsWith("Marten", StringComparison.Ordinal));
            var hasUsingRaven = scan.Usings.Any(u => u.Target.StartsWith("Raven.Client", StringComparison.Ordinal));
            var hasUsingSystemData = scan.Usings.Any(u =>
                u.Target.StartsWith("System.Data", StringComparison.Ordinal) ||
                u.Target.StartsWith("Microsoft.Data", StringComparison.Ordinal) ||
                u.Target.StartsWith("Npgsql", StringComparison.Ordinal) ||
                u.Target.StartsWith("MySql", StringComparison.Ordinal));

            var linq2DbHost = FindReachableHostProject(projName, linq2DbHostSet)
                ?? (hasUsingLinq2Db ? projName : defaultGlobalLinq2DbHost);

            var checkLinq2Db = linq2DbHost is not null &&
                (hasUsingLinq2Db ||
                 file.Code.Contains("DataConnection", StringComparison.Ordinal) ||
                 file.Code.Contains("GetTable", StringComparison.Ordinal) ||
                 file.Code.Contains("ITable<", StringComparison.Ordinal) ||
                 file.Code.Contains("IDataProvider", StringComparison.Ordinal) ||
                 file.Code.Contains("INopDataProvider", StringComparison.Ordinal) ||
                 file.Code.Contains("IRepository<", StringComparison.Ordinal) ||
                 file.Code.Contains("IReadOnlyRepository<", StringComparison.Ordinal));

            var checkDapper = (hasUsingDapper || caps?.UsesDapper == true || hasSolutionDapper) &&
                (hasUsingDapper ||
                 file.Code.Contains("SqlMapper", StringComparison.Ordinal) ||
                 file.Code.Contains("QueryAsync", StringComparison.Ordinal) ||
                 file.Code.Contains("QueryFirst", StringComparison.Ordinal) ||
                 file.Code.Contains("QuerySingle", StringComparison.Ordinal) ||
                 file.Code.Contains("QueryMultiple", StringComparison.Ordinal) ||
                 file.Code.Contains("ExecuteAsync", StringComparison.Ordinal) ||
                 file.Code.Contains("ExecuteScalar", StringComparison.Ordinal));

            var checkMongo = hasUsingMongo || caps?.UsesMongoDb == true || hasSolutionMongo
                ? (file.Code.Contains("IMongoCollection", StringComparison.Ordinal) ||
                   file.Code.Contains("IMongoDatabase", StringComparison.Ordinal) ||
                   file.Code.Contains("IMongoClient", StringComparison.Ordinal) ||
                   file.Code.Contains("MongoClient", StringComparison.Ordinal) ||
                   file.Code.Contains("GetCollection", StringComparison.Ordinal))
                : (file.Code.Contains("IMongoCollection", StringComparison.Ordinal) ||
                   file.Code.Contains("IMongoDatabase", StringComparison.Ordinal) ||
                   file.Code.Contains("IMongoClient", StringComparison.Ordinal));

            var checkNHibernate = (hasUsingNHibernate || caps?.UsesNHibernate == true || hasSolutionNHibernate) &&
                (hasUsingNHibernate ||
                 file.Code.Contains("ISessionFactory", StringComparison.Ordinal) ||
                 file.Code.Contains("IStatelessSession", StringComparison.Ordinal) ||
                 file.Code.Contains("QueryOver", StringComparison.Ordinal));

            var checkRedis = file.Code.Contains("IConnectionMultiplexer", StringComparison.Ordinal) ||
                file.Code.Contains("IRedisCacheClient", StringComparison.Ordinal) ||
                ((hasUsingRedis || caps?.UsesRedis == true) && file.Code.Contains("IDatabase", StringComparison.Ordinal));

            var checkCosmos = file.Code.Contains("CosmosClient", StringComparison.Ordinal) ||
                ((hasUsingCosmos || caps?.UsesCosmos == true) &&
                 (file.Code.Contains("ReadItemAsync", StringComparison.Ordinal) ||
                  file.Code.Contains("UpsertItemAsync", StringComparison.Ordinal) ||
                  file.Code.Contains("CreateItemAsync", StringComparison.Ordinal) ||
                  file.Code.Contains("GetItemQueryIterator", StringComparison.Ordinal)));

            var checkMarten = (hasUsingMarten || caps?.UsesMarten == true) &&
                (file.Code.Contains("IDocumentSession", StringComparison.Ordinal) ||
                 file.Code.Contains("IQuerySession", StringComparison.Ordinal) ||
                 file.Code.Contains("DocumentStore", StringComparison.Ordinal));

            var checkRaven = (hasUsingRaven || caps?.UsesRaven == true) &&
                (file.Code.Contains("IDocumentStore", StringComparison.Ordinal) ||
                 file.Code.Contains("IDocumentSession", StringComparison.Ordinal) ||
                 file.Code.Contains("IAsyncDocumentSession", StringComparison.Ordinal));

            var checkAdo = (file.Code.Contains("SqlConnection", StringComparison.Ordinal) ||
                 file.Code.Contains("NpgsqlConnection", StringComparison.Ordinal) ||
                 file.Code.Contains("SqliteConnection", StringComparison.Ordinal) ||
                 file.Code.Contains("MySqlConnection", StringComparison.Ordinal) ||
                 file.Code.Contains("OracleConnection", StringComparison.Ordinal) ||
                 file.Code.Contains("NpgsqlDataSource", StringComparison.Ordinal) ||
                 file.Code.Contains("SqlCommand", StringComparison.Ordinal) ||
                 file.Code.Contains("NpgsqlCommand", StringComparison.Ordinal) ||
                 file.Code.Contains("SqliteCommand", StringComparison.Ordinal) ||
                 file.Code.Contains("MySqlCommand", StringComparison.Ordinal) ||
                 ((hasUsingSystemData || caps?.UsesSqlClient == true) &&
                  (file.Code.Contains("ExecuteReader", StringComparison.Ordinal) ||
                   file.Code.Contains("ExecuteNonQuery", StringComparison.Ordinal) ||
                   file.Code.Contains("ExecuteScalar", StringComparison.Ordinal))));

            if (!checkLinq2Db && !checkDapper && !checkMongo && !checkNHibernate &&
                !checkRedis && !checkCosmos && !checkMarten && !checkRaven && !checkAdo)
            {
                continue;
            }

            foreach (var decl in scan.Classes)
            {
                if (context.DbContextsByFullName.ContainsKey(decl.FullName))
                    continue;

                var bodyEnd = decl.BodySpan.End > decl.BodySpan.Start
                    ? Math.Min(file.Code.Length, decl.BodySpan.End)
                    : Math.Min(file.Code.Length, decl.DeclarationIndex + 300);
                var headerStart = Math.Max(0, decl.DeclarationIndex);
                var spanStart = Math.Min(headerStart, bodyEnd);
                var rawClassSpan = file.Code[spanStart..bodyEnd];
                var maskedClassSpan = mask.CodeOnly(spanStart, bodyEnd - spanStart);
                var loc = SourceLocation.Create(file.Path, result.RootPath, file.Code, decl.NameIndex);

                ClassDataAccess? access = null;
                void RecordStore(DataStoreInfo store, IEnumerable<string>? entities = null)
                {
                    access ??= context.GetOrCreateClassDataAccess(decl);
                    SystemAnalysisContext.AddUniqueOrdinal(access.DataStores, store.Name);
                    if (entities is not null)
                    {
                        foreach (var e in entities)
                        {
                            SystemAnalysisContext.AddUniqueOrdinal(access.Entities, e);
                            SystemAnalysisContext.AddUniqueOrdinal(store.Entities, e);
                        }
                    }
                }

                // Linq2Db
                var classMatchedSqlOrm = false;
                if (checkLinq2Db && linq2DbHost is not null)
                {
                    var inheritsDataConn = decl.BaseRawNames.Contains("DataConnection", StringComparer.Ordinal);
                    var hasDirectLinq2Db = inheritsDataConn ||
                        maskedClassSpan.Contains("DataConnection", StringComparison.Ordinal) ||
                        maskedClassSpan.Contains("GetTable", StringComparison.Ordinal) ||
                        maskedClassSpan.Contains("ITable<", StringComparison.Ordinal) ||
                        maskedClassSpan.Contains("INopDataProvider", StringComparison.Ordinal) ||
                        (hasUsingLinq2Db && maskedClassSpan.Contains("IDataProvider", StringComparison.Ordinal));

                    var linq2DbEntities = new List<string>();
                    if (hasDirectLinq2Db ||
                        maskedClassSpan.Contains("IRepository<", StringComparison.Ordinal) ||
                        maskedClassSpan.Contains("IReadOnlyRepository<", StringComparison.Ordinal))
                    {
                        ExtractEntities(Linq2DbTableRegex(), maskedClassSpan, context, decl, linq2DbEntities);
                        ExtractGenericRepositoryEntities(maskedClassSpan, context, decl, linq2DbEntities);
                    }

                    if (hasDirectLinq2Db || linq2DbEntities.Count > 0)
                    {
                        capsByProject.TryGetValue(linq2DbHost, out var hostCaps);
                        var store = GetOrAddStore(
                            linq2DbHost,
                            DataStoreKind.Linq2Db,
                            "Linq2Db",
                            FormatStoreName("Linq2Db", linq2DbHost),
                            hostCaps?.PrimaryDbProvider ?? provider,
                            loc);
                        if (hasDirectLinq2Db)
                            ExtractSqlTableNames(rawClassSpan, linq2DbEntities);
                        RecordStore(store, linq2DbEntities);
                        classMatchedSqlOrm = true;
                    }
                }

                // Dapper
                if (checkDapper &&
                    (hasUsingDapper ||
                     maskedClassSpan.Contains("SqlMapper", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("QueryAsync", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("QueryFirst", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("QuerySingle", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("QueryMultiple", StringComparison.Ordinal) ||
                     (caps?.UsesDapper == true &&
                      (maskedClassSpan.Contains("ExecuteAsync", StringComparison.Ordinal) ||
                       maskedClassSpan.Contains("ExecuteScalar", StringComparison.Ordinal)))))
                {
                    var dapperMatched = false;
                    try
                    {
                        dapperMatched = DapperMethodCallRegex().IsMatch(maskedClassSpan) ||
                                        DapperGenericCallRegex().IsMatch(maskedClassSpan);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                    }

                    if (dapperMatched)
                    {
                        var dapperEntities = new List<string>();
                        ExtractEntities(DapperGenericCallRegex(), maskedClassSpan, context, decl, dapperEntities);
                        ExtractSqlTableNames(rawClassSpan, dapperEntities);
                        var store = GetOrAddStore(
                            projName,
                            DataStoreKind.Dapper,
                            "Dapper",
                            FormatStoreName("Dapper", projName),
                            provider,
                            loc);
                        RecordStore(store, dapperEntities);
                        classMatchedSqlOrm = true;
                    }
                }

                // MongoDB
                if (checkMongo &&
                    (maskedClassSpan.Contains("IMongoCollection", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("IMongoDatabase", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("IMongoClient", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("MongoClient", StringComparison.Ordinal) ||
                     (hasUsingMongo && maskedClassSpan.Contains("GetCollection", StringComparison.Ordinal))))
                {
                    var mongoEntities = new List<string>();
                    ExtractMongoEntities(maskedClassSpan, rawClassSpan, context, decl, mongoEntities);
                    var store = GetOrAddStore(
                        projName,
                        DataStoreKind.MongoDb,
                        "MongoDB",
                        FormatStoreName("MongoDB", projName),
                        "MongoDB",
                        loc);
                    RecordStore(store, mongoEntities);
                }

                // NHibernate (guarded by using NHibernate or NHibernate package)
                if (checkNHibernate &&
                    (maskedClassSpan.Contains("ISessionFactory", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("IStatelessSession", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("QueryOver", StringComparison.Ordinal) ||
                     (hasUsingNHibernate &&
                      (maskedClassSpan.Contains("ISession", StringComparison.Ordinal) ||
                       maskedClassSpan.Contains(".Query<", StringComparison.Ordinal) ||
                       maskedClassSpan.Contains(".Get<", StringComparison.Ordinal) ||
                       maskedClassSpan.Contains(".Load<", StringComparison.Ordinal)))))
                {
                    var nhEntities = new List<string>();
                    ExtractEntities(GenericEntityQueryRegex(), maskedClassSpan, context, decl, nhEntities);
                    var store = GetOrAddStore(
                        projName,
                        DataStoreKind.NHibernate,
                        "NHibernate",
                        FormatStoreName("NHibernate", projName),
                        provider,
                        loc);
                    RecordStore(store, nhEntities);
                    classMatchedSqlOrm = true;
                }

                // Redis
                if (checkRedis &&
                    (maskedClassSpan.Contains("IConnectionMultiplexer", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("IRedisCacheClient", StringComparison.Ordinal) ||
                     ((hasUsingRedis || caps?.UsesRedis == true) && maskedClassSpan.Contains("IDatabase", StringComparison.Ordinal))))
                {
                    var store = GetOrAddStore(
                        projName,
                        DataStoreKind.Redis,
                        "Redis",
                        FormatStoreName("Redis", projName),
                        "Redis",
                        loc);
                    RecordStore(store);
                }

                // Cosmos DB
                if (checkCosmos &&
                    (maskedClassSpan.Contains("CosmosClient", StringComparison.Ordinal) ||
                     ((hasUsingCosmos || caps?.UsesCosmos == true) &&
                      (maskedClassSpan.Contains("ReadItemAsync", StringComparison.Ordinal) ||
                       maskedClassSpan.Contains("UpsertItemAsync", StringComparison.Ordinal) ||
                       maskedClassSpan.Contains("CreateItemAsync", StringComparison.Ordinal) ||
                       maskedClassSpan.Contains("GetItemQueryIterator", StringComparison.Ordinal)))))
                {
                    var cosmosEntities = new List<string>();
                    ExtractEntities(CosmosMethodRegex(), maskedClassSpan, context, decl, cosmosEntities);
                    var store = GetOrAddStore(
                        projName,
                        DataStoreKind.CosmosDb,
                        "Cosmos DB",
                        FormatStoreName("Cosmos DB", projName),
                        "Cosmos DB",
                        loc);
                    RecordStore(store, cosmosEntities);
                }

                // Document DB (Marten / RavenDB)
                if (checkMarten &&
                    (maskedClassSpan.Contains("IDocumentSession", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("IQuerySession", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("DocumentStore", StringComparison.Ordinal)))
                {
                    var martenEntities = new List<string>();
                    ExtractEntities(GenericEntityQueryRegex(), maskedClassSpan, context, decl, martenEntities);
                    var store = GetOrAddStore(
                        projName,
                        DataStoreKind.DocumentDb,
                        "Marten",
                        FormatStoreName("Marten", projName),
                        "PostgreSQL",
                        loc);
                    RecordStore(store, martenEntities);
                }
                else if (checkRaven &&
                    (maskedClassSpan.Contains("IDocumentStore", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("IDocumentSession", StringComparison.Ordinal) ||
                     maskedClassSpan.Contains("IAsyncDocumentSession", StringComparison.Ordinal)))
                {
                    var ravenEntities = new List<string>();
                    ExtractEntities(GenericEntityQueryRegex(), maskedClassSpan, context, decl, ravenEntities);
                    var store = GetOrAddStore(
                        projName,
                        DataStoreKind.DocumentDb,
                        "RavenDB",
                        FormatStoreName("RavenDB", projName),
                        "RavenDB",
                        loc);
                    RecordStore(store, ravenEntities);
                }

                // ADO.NET (only when not already handled by Dapper / Linq2Db / NHibernate in this class)
                if (checkAdo && !classMatchedSqlOrm && !decl.IsAbstract &&
                    HasAdoUsage(maskedClassSpan, hasUsingSystemData || caps?.UsesSqlClient == true))
                {
                    var adoEntities = new List<string>();
                    ExtractSqlTableNames(rawClassSpan, adoEntities);
                    var adoProvider = InferAdoProvider(maskedClassSpan, provider);
                    var store = GetOrAddStore(
                        projName,
                        DataStoreKind.AdoNet,
                        "ADO.NET",
                        FormatStoreName("ADO.NET", projName),
                        adoProvider,
                        loc);
                    RecordStore(store, adoEntities);
                }
            }
        }

        // Record generic repository default backing store per project (e.g. Nop.Services -> Linq2Db (Nop.Data)).
        var nonEfStoresByProject = storesByKey.Values
            .Where(s => s.Kind is not DataStoreKind.EfCore and not DataStoreKind.Ef6 && !string.IsNullOrEmpty(s.ProjectName))
            .GroupBy(s => s.ProjectName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var proj in result.Projects)
        {
            if (context.IsTestProject(proj.Name))
                continue;

            var reachableLinqHost = FindReachableHostProject(proj.Name, linq2DbHostSet);
            if (FindReachableLinq2DbStore(storesByKey, reachableLinqHost) is { } linqStore)
            {
                context.SetGenericRepoStoreForProject(proj.Name, linqStore.Name);
                continue;
            }

            if (nonEfStoresByProject.TryGetValue(proj.Name, out var ownStores) && ownStores.Count == 1)
            {
                context.SetGenericRepoStoreForProject(proj.Name, ownStores[0].Name);
                continue;
            }

            var refNonEf = proj.ReferenceNames
                .Where(r => nonEfStoresByProject.ContainsKey(r))
                .SelectMany(r => nonEfStoresByProject[r])
                .Where(s => s.Kind is DataStoreKind.Dapper or DataStoreKind.MongoDb or DataStoreKind.NHibernate or DataStoreKind.DocumentDb)
                .Select(s => s.Name)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (refNonEf.Count == 1)
                context.SetGenericRepoStoreForProject(proj.Name, refNonEf[0]);
        }

        result.DataStores.AddRange(storesByKey.Values
            .OrderBy(d => d.Kind)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ThenBy(d => d.ProjectName ?? string.Empty, StringComparer.OrdinalIgnoreCase));
    }

    private static DataStoreInfo? FindReachableLinq2DbStore(
        Dictionary<(string Project, DataStoreKind Kind, string Name), DataStoreInfo> storesByKey,
        string? reachableLinq2DbHost)
    {
        if (string.IsNullOrWhiteSpace(reachableLinq2DbHost))
            return null;

        return storesByKey.Values.FirstOrDefault(s =>
            s.Kind == DataStoreKind.Linq2Db &&
            string.Equals(s.ProjectName, reachableLinq2DbHost, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Attempts to resolve an <c>IRepository&lt;T&gt;</c> / <c>IReadOnlyRepository&lt;T&gt;</c>
    /// dependency whose entity is managed by a non-EF Core store rather than an EF DbSet.
    /// </summary>
    internal static bool TryResolveNonEfRepositoryEntity(
        SystemAnalysisContext context,
        string typeReference,
        CSharpScanner.ClassDecl? scopeDecl,
        string scopeNamespace,
        IReadOnlyList<UsingDirective> scopeUsings,
        IReadOnlyList<string> scopeContaining,
        out string? entityName,
        out string? storeName)
    {
        entityName = null;
        storeName = null;

        if (string.IsNullOrWhiteSpace(typeReference) || !typeReference.Contains('<', StringComparison.Ordinal))
            return false;

        Match m;
        try
        {
            m = DeepSlicePatterns.GenericRepositoryRegex().Match(typeReference);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        if (!m.Success)
            return false;

        var candidate = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
        if (!IsValidEntityCandidate(candidate, context, scopeDecl))
            return false;

        if (context.DbSetMappings.DbSetsByEntity.ContainsKey(candidate))
            return false;

        var resolved = SystemDependencyTracer.LookupResolution(
            context, typeReference, scopeNamespace, scopeUsings, scopeContaining);
        if (resolved?.Implementation is not null &&
            context.ClassByFullName.TryGetValue(resolved.Implementation, out var implDecl))
        {
            var implAccess = context.GetClassDataAccess(implDecl);
            if (implAccess is { DataStores.Count: > 0 })
            {
                entityName = candidate;
                storeName = implAccess.DataStores[0];
                return true;
            }
        }

        var scopeProj = scopeDecl is not null && context.FileByClass.TryGetValue(scopeDecl, out var entry)
            ? entry.File.ProjectName
            : null;
        var projectStore = context.GetGenericRepoStoreForProject(scopeProj);
        if (!string.IsNullOrWhiteSpace(projectStore))
        {
            entityName = candidate;
            storeName = projectStore;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Scans an individual method or handler body (when <c>scopeToMethodBody: true</c>) for
    /// direct non-EF persistence calls without inheriting unrelated stores from other actions.
    /// </summary>
    internal static void InspectMethodBody(
        SystemAnalysisContext context,
        SourceFile file,
        CSharpScanner.FileScan scan,
        CSharpScanner.ClassDecl? ownerClass,
        string maskedBody,
        int spanStart,
        int spanEnd,
        List<string> touchedDataStores,
        List<string> touchedEntities)
    {
        if (string.IsNullOrWhiteSpace(maskedBody) || context.Result.DataStores.Count == 0)
            return;

        var projName = file.ProjectName ?? string.Empty;
        var hasUsingDapper = scan.Usings.Any(u => u.Target.StartsWith("Dapper", StringComparison.Ordinal));
        var hasUsingMongo = scan.Usings.Any(u => u.Target.StartsWith("MongoDB", StringComparison.Ordinal));
        var hasUsingNHibernate = scan.Usings.Any(u => u.Target.StartsWith("NHibernate", StringComparison.Ordinal));
        var hasUsingLinq2Db = scan.Usings.Any(u => u.Target.StartsWith("LinqToDB", StringComparison.Ordinal));
        var hasUsingRedis = scan.Usings.Any(u => u.Target.StartsWith("StackExchange.Redis", StringComparison.Ordinal));
        var hasUsingSystemData = scan.Usings.Any(u =>
            u.Target.StartsWith("System.Data", StringComparison.Ordinal) ||
            u.Target.StartsWith("Microsoft.Data", StringComparison.Ordinal) ||
            u.Target.StartsWith("Npgsql", StringComparison.Ordinal) ||
            u.Target.StartsWith("MySql", StringComparison.Ordinal));

        var rawSpan = spanEnd > spanStart && spanStart >= 0 && spanEnd <= file.Code.Length
            ? file.Code[spanStart..spanEnd]
            : string.Empty;

        DataStoreInfo? FindStore(DataStoreKind kind)
            => context.Result.DataStores.FirstOrDefault(s =>
                   s.Kind == kind && string.Equals(s.ProjectName, projName, StringComparison.OrdinalIgnoreCase))
               ?? context.Result.DataStores.FirstOrDefault(s => s.Kind == kind);

        if (FindStore(DataStoreKind.Dapper) is { } dapperStore &&
            (hasUsingDapper ||
             maskedBody.Contains("SqlMapper", StringComparison.Ordinal) ||
             maskedBody.Contains("QueryAsync", StringComparison.Ordinal) ||
             maskedBody.Contains("QueryFirst", StringComparison.Ordinal) ||
             maskedBody.Contains("QuerySingle", StringComparison.Ordinal) ||
             maskedBody.Contains("QueryMultiple", StringComparison.Ordinal) ||
             maskedBody.Contains("ExecuteAsync", StringComparison.Ordinal) ||
             maskedBody.Contains("ExecuteScalar", StringComparison.Ordinal)))
        {
            var entities = new List<string>();
            ExtractEntities(DapperGenericCallRegex(), maskedBody, context, ownerClass, entities);
            ExtractSqlTableNames(rawSpan, entities);
            var hasCall = entities.Count > 0;
            if (!hasCall)
            {
                try { hasCall = DapperMethodCallRegex().IsMatch(maskedBody); } catch (RegexMatchTimeoutException) { }
            }
            if (hasCall)
            {
                SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, dapperStore.Name);
                foreach (var e in entities)
                    SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, e);
            }
        }

        if (FindStore(DataStoreKind.MongoDb) is { } mongoStore &&
            (maskedBody.Contains("GetCollection", StringComparison.Ordinal) ||
             maskedBody.Contains("IMongoCollection", StringComparison.Ordinal) ||
             (hasUsingMongo && (maskedBody.Contains("InsertOne", StringComparison.Ordinal) ||
                                maskedBody.Contains("Find", StringComparison.Ordinal)))))
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, mongoStore.Name);
            var entities = new List<string>();
            ExtractMongoEntities(maskedBody, rawSpan, context, ownerClass, entities);
            foreach (var e in entities)
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, e);
        }

        if (FindStore(DataStoreKind.Linq2Db) is { } linqStore &&
            (maskedBody.Contains("GetTable", StringComparison.Ordinal) ||
             maskedBody.Contains("ITable<", StringComparison.Ordinal) ||
             (hasUsingLinq2Db && maskedBody.Contains("DataConnection", StringComparison.Ordinal))))
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, linqStore.Name);
            var entities = new List<string>();
            ExtractEntities(Linq2DbTableRegex(), maskedBody, context, ownerClass, entities);
            ExtractSqlTableNames(rawSpan, entities);
            foreach (var e in entities)
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, e);
        }

        if (FindStore(DataStoreKind.NHibernate) is { } nhStore &&
            hasUsingNHibernate &&
            (maskedBody.Contains("Query", StringComparison.Ordinal) ||
             maskedBody.Contains("QueryOver", StringComparison.Ordinal) ||
             maskedBody.Contains(".Get", StringComparison.Ordinal) ||
             maskedBody.Contains(".Load", StringComparison.Ordinal)))
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, nhStore.Name);
            var entities = new List<string>();
            ExtractEntities(GenericEntityQueryRegex(), maskedBody, context, ownerClass, entities);
            foreach (var e in entities)
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, e);
        }

        if (FindStore(DataStoreKind.AdoNet) is { } adoStore &&
            HasAdoUsage(maskedBody, hasUsingSystemData))
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, adoStore.Name);
            var entities = new List<string>();
            ExtractSqlTableNames(rawSpan, entities);
            foreach (var e in entities)
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, e);
        }

        if (FindStore(DataStoreKind.CosmosDb) is { } cosmosStore &&
            (maskedBody.Contains("ReadItemAsync", StringComparison.Ordinal) ||
             maskedBody.Contains("UpsertItemAsync", StringComparison.Ordinal) ||
             maskedBody.Contains("CreateItemAsync", StringComparison.Ordinal) ||
             maskedBody.Contains("GetItemQueryIterator", StringComparison.Ordinal)))
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, cosmosStore.Name);
            var entities = new List<string>();
            ExtractEntities(CosmosMethodRegex(), maskedBody, context, ownerClass, entities);
            foreach (var e in entities)
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, e);
        }

        if (FindStore(DataStoreKind.Redis) is { } redisStore &&
            (hasUsingRedis || maskedBody.Contains("IConnectionMultiplexer", StringComparison.Ordinal)) &&
            (maskedBody.Contains("GetDatabase", StringComparison.Ordinal) ||
             maskedBody.Contains("StringGet", StringComparison.Ordinal) ||
             maskedBody.Contains("StringSet", StringComparison.Ordinal) ||
             maskedBody.Contains("HashSet", StringComparison.Ordinal) ||
             maskedBody.Contains("KeyDelete", StringComparison.Ordinal)))
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, redisStore.Name);
        }

        if (FindStore(DataStoreKind.DocumentDb) is { } docStore &&
            (maskedBody.Contains(".Query<", StringComparison.Ordinal) ||
             maskedBody.Contains(".LoadAsync<", StringComparison.Ordinal) ||
             maskedBody.Contains(".Store(", StringComparison.Ordinal)))
        {
            SystemAnalysisContext.AddUniqueOrdinal(touchedDataStores, docStore.Name);
            var entities = new List<string>();
            ExtractEntities(GenericEntityQueryRegex(), maskedBody, context, ownerClass, entities);
            foreach (var e in entities)
                SystemAnalysisContext.AddUniqueOrdinal(touchedEntities, e);
        }
    }

    /// <summary>
    /// Populates <see cref="DataStoreInfo.AccessedBy"/> and rolls resolved entity touches back
    /// into non-EF <see cref="DataStoreInfo.Entities"/> after entrypoint and service tracing.
    /// </summary>
    internal static void FinalizeAccessors(SystemAnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = context.Result;
        if (result.DataStores.Count == 0)
            return;

        var byName = result.DataStores
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var edge in result.FlowEdges)
        {
            if (edge.Kind != FlowEdgeKind.HandlerToDbContext)
                continue;
            var shortCtx = CSharpScanner.SimpleName(edge.To);
            if (byName.TryGetValue(shortCtx, out var store))
                SystemAnalysisContext.AddUniqueOrdinal(store.AccessedBy, edge.From);
        }

        foreach (var edge in result.ResolvedEdges)
        {
            if (edge.Kind != FlowEdgeKind.HandlerToDbContextResolved)
                continue;
            var shortCtx = CSharpScanner.SimpleName(edge.To);
            if (byName.TryGetValue(shortCtx, out var store))
                SystemAnalysisContext.AddUniqueOrdinal(store.AccessedBy, edge.From);
        }

        foreach (var (svcName, effect) in result.ServiceClassEffects)
        {
            foreach (var db in effect.TouchedDbContexts)
            {
                var shortCtx = CSharpScanner.SimpleName(db);
                if (byName.TryGetValue(shortCtx, out var store))
                    SystemAnalysisContext.AddUniqueOrdinal(store.AccessedBy, svcName);
            }

            foreach (var dsName in effect.TouchedDataStores)
            {
                if (!byName.TryGetValue(dsName, out var store))
                    continue;
                SystemAnalysisContext.AddUniqueOrdinal(store.AccessedBy, svcName);
                foreach (var entityLabel in effect.TouchedEntities)
                {
                    if (!entityLabel.Contains('(', StringComparison.Ordinal))
                        SystemAnalysisContext.AddUniqueOrdinal(store.Entities, entityLabel);
                }
            }
        }

        foreach (var ep in result.SystemEntrypoints)
        {
            foreach (var db in ep.TouchedDbContexts)
            {
                var shortCtx = CSharpScanner.SimpleName(db);
                if (byName.TryGetValue(shortCtx, out var store))
                    SystemAnalysisContext.AddUniqueOrdinal(store.AccessedBy, ep.Name);
            }

            foreach (var dsName in ep.TouchedDataStores)
            {
                if (!byName.TryGetValue(dsName, out var store))
                    continue;
                SystemAnalysisContext.AddUniqueOrdinal(store.AccessedBy, ep.Name);
                if (ep.TouchedDataStores.Count == 1 && ep.TouchedDbContexts.Count == 0)
                {
                    foreach (var entityLabel in ep.TouchedEntities)
                    {
                        if (!entityLabel.Contains('(', StringComparison.Ordinal))
                            SystemAnalysisContext.AddUniqueOrdinal(store.Entities, entityLabel);
                    }
                }
            }
        }

        foreach (var store in result.DataStores)
        {
            store.Entities.Sort(StringComparer.Ordinal);
            store.AccessedBy.Sort(StringComparer.Ordinal);
        }
    }

    private static ProjectCapabilities BuildCapabilities(
        ProjectInfo project,
        List<(SourceFile File, CSharpScanner.FileScan Scan, SourceMask Mask)>? projFiles,
        List<DbContextInfo> dbContexts)
    {
        bool HasPkgExact(string id) => project.PackageReferences.Any(p => p.Equals(id, StringComparison.OrdinalIgnoreCase));
        bool HasPkgPrefix(string prefix) => project.PackageReferences.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        bool HasPkgContains(string sub) => project.PackageReferences.Any(p => p.Contains(sub, StringComparison.OrdinalIgnoreCase));
        bool HasCode(string token) => projFiles?.Any(f => f.File.Code.Contains(token, StringComparison.Ordinal)) == true;
        bool HasUsingPrefix(string prefix) => projFiles?.Any(f => f.Scan.Usings.Any(u => u.Target.StartsWith(prefix, StringComparison.Ordinal))) == true;

        var isEf6 = HasPkgExact("EntityFramework") || HasUsingPrefix("System.Data.Entity");
        var usesEf = HasPkgPrefix("Microsoft.EntityFrameworkCore") ||
                     dbContexts.Any(c => string.Equals(c.ProjectName, project.Name, StringComparison.OrdinalIgnoreCase));

        string? provider = null;
        if (HasPkgPrefix("Npgsql") || HasPkgPrefix("Aspire.Npgsql") || HasCode("UseNpgsql") || HasCode("NpgsqlConnection"))
            provider = "PostgreSQL";
        else if (HasPkgContains("SqlServer") || HasPkgExact("Microsoft.Data.SqlClient") || HasPkgExact("System.Data.SqlClient") ||
                 HasCode("UseSqlServer") || HasCode("SqlConnection"))
            provider = "SQL Server";
        else if (HasPkgContains("Sqlite") || HasCode("UseSqlite") || HasCode("SqliteConnection"))
            provider = "SQLite";
        else if (HasPkgContains("MySql") || HasCode("UseMySql") || HasCode("MySqlConnection"))
            provider = "MySQL";
        else if (HasPkgContains("Oracle") || HasCode("OracleConnection"))
            provider = "Oracle";
        else if (HasPkgPrefix("MongoDB.") || HasUsingPrefix("MongoDB"))
            provider = "MongoDB";

        return new ProjectCapabilities
        {
            Project = project,
            IsEf6 = isEf6,
            UsesEf = usesEf || isEf6,
            UsesDapper = HasPkgPrefix("Dapper") || HasUsingPrefix("Dapper"),
            UsesLinq2Db = HasPkgContains("linq2db") || HasUsingPrefix("LinqToDB") || HasCode("LinqToDB."),
            UsesMongoDb = HasPkgPrefix("MongoDB.") || HasUsingPrefix("MongoDB"),
            UsesNHibernate = HasPkgPrefix("NHibernate") || HasUsingPrefix("NHibernate"),
            UsesRedis = HasPkgContains("Redis") || HasUsingPrefix("StackExchange.Redis"),
            UsesCosmos = HasPkgPrefix("Microsoft.Azure.Cosmos") || HasUsingPrefix("Microsoft.Azure.Cosmos"),
            UsesMarten = HasPkgPrefix("Marten") || HasUsingPrefix("Marten"),
            UsesRaven = HasPkgPrefix("RavenDB.") || HasUsingPrefix("Raven.Client"),
            UsesSqlClient = HasPkgExact("Microsoft.Data.SqlClient") || HasPkgExact("System.Data.SqlClient") ||
                            HasPkgPrefix("Npgsql") || HasPkgPrefix("Microsoft.Data.Sqlite") || HasPkgContains("MySql"),
            PrimaryDbProvider = provider,
        };
    }

    private static string FormatStoreName(string technology, string projectName)
        => string.IsNullOrWhiteSpace(projectName) ? technology : $"{technology} ({projectName})";

    private static bool HasAdoUsage(string maskedSpan, bool hasDataContextOrPkg)
        => maskedSpan.Contains("SqlConnection", StringComparison.Ordinal) ||
           maskedSpan.Contains("NpgsqlConnection", StringComparison.Ordinal) ||
           maskedSpan.Contains("SqliteConnection", StringComparison.Ordinal) ||
           maskedSpan.Contains("MySqlConnection", StringComparison.Ordinal) ||
           maskedSpan.Contains("OracleConnection", StringComparison.Ordinal) ||
           maskedSpan.Contains("NpgsqlDataSource", StringComparison.Ordinal) ||
           maskedSpan.Contains("SqlCommand", StringComparison.Ordinal) ||
           maskedSpan.Contains("NpgsqlCommand", StringComparison.Ordinal) ||
           maskedSpan.Contains("SqliteCommand", StringComparison.Ordinal) ||
           maskedSpan.Contains("MySqlCommand", StringComparison.Ordinal) ||
           (hasDataContextOrPkg &&
            (maskedSpan.Contains("ExecuteReader", StringComparison.Ordinal) ||
             maskedSpan.Contains("ExecuteNonQuery", StringComparison.Ordinal) ||
             maskedSpan.Contains("ExecuteScalar", StringComparison.Ordinal)));

    private static string? InferAdoProvider(string maskedSpan, string? projectProvider)
    {
        if (maskedSpan.Contains("Npgsql", StringComparison.Ordinal))
            return "PostgreSQL";
        if (maskedSpan.Contains("Sqlite", StringComparison.Ordinal))
            return "SQLite";
        if (maskedSpan.Contains("MySql", StringComparison.Ordinal))
            return "MySQL";
        if (maskedSpan.Contains("Oracle", StringComparison.Ordinal))
            return "Oracle";
        if (maskedSpan.Contains("SqlConnection", StringComparison.Ordinal) ||
            maskedSpan.Contains("SqlCommand", StringComparison.Ordinal))
            return projectProvider ?? "SQL Server";
        return projectProvider;
    }

    private static void ExtractEntities(
        Regex regex,
        string maskedCode,
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl? scopeDecl,
        List<string> target)
    {
        try
        {
            foreach (Match m in regex.Matches(maskedCode))
            {
                var entity = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                if (IsValidEntityCandidate(entity, context, scopeDecl))
                    SystemAnalysisContext.AddUniqueOrdinal(target, entity);
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
    }

    private static void ExtractGenericRepositoryEntities(
        string maskedCode,
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl? scopeDecl,
        List<string> target)
    {
        try
        {
            foreach (Match m in DeepSlicePatterns.GenericRepositoryRegex().Matches(maskedCode))
            {
                var entity = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                if (IsValidEntityCandidate(entity, context, scopeDecl) &&
                    !context.DbSetMappings.DbSetsByEntity.ContainsKey(entity))
                {
                    SystemAnalysisContext.AddUniqueOrdinal(target, entity);
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
    }

    private static void ExtractMongoEntities(
        string maskedCode,
        string rawCode,
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl? scopeDecl,
        List<string> target)
    {
        ExtractEntities(MongoCollectionRegex(), maskedCode, context, scopeDecl, target);

        if (!string.IsNullOrEmpty(rawCode) && rawCode.Contains("GetCollection", StringComparison.Ordinal))
        {
            try
            {
                foreach (Match m in MongoCollectionRegex().Matches(rawCode))
                {
                    var entity = CSharpScanner.SimpleName(m.Groups["entity"].Value.Trim());
                    if (IsValidEntityCandidate(entity, context, scopeDecl))
                        SystemAnalysisContext.AddUniqueOrdinal(target, entity);
                    else if (m.Groups["collection"].Success)
                    {
                        var coll = m.Groups["collection"].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(coll))
                            SystemAnalysisContext.AddUniqueOrdinal(target, coll);
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
            }
        }
    }

    private static void ExtractSqlTableNames(string rawCode, List<string> target)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
            return;

        try
        {
            foreach (Match litMatch in StringLiteralRegex().Matches(rawCode))
            {
                var lit = litMatch.Groups["lit"].Value;
                if (lit.Length < 10 || !SqlVerbRegex().IsMatch(lit))
                    continue;

                foreach (Match tblMatch in SqlTableRegex().Matches(lit))
                {
                    var table = tblMatch.Groups["table"].Value.Trim();
                    if (table.Length >= 2 &&
                        !SqlStopWords.Contains(table) &&
                        !IgnoredEntities.Contains(table))
                    {
                        SystemAnalysisContext.AddUniqueOrdinal(target, table);
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
    }

    internal static bool IsValidEntityCandidate(
        string simpleName,
        SystemAnalysisContext context,
        CSharpScanner.ClassDecl? scopeDecl)
    {
        if (string.IsNullOrWhiteSpace(simpleName) || simpleName.Length <= 1)
            return false;

        var trimmed = simpleName.TrimEnd('?', '[', ']');
        if (trimmed.Length <= 1 || IgnoredEntities.Contains(trimmed))
            return false;

        if (scopeDecl is not null && scopeDecl.TypeParameters.Contains(trimmed, StringComparer.Ordinal))
            return false;

        // Generic type parameter convention (TEntity, TModel, TItem) unless actually a scanned type.
        if (trimmed[0] == 'T' &&
            trimmed.Length >= 2 &&
            char.IsUpper(trimmed[1]) &&
            !context.ClassesBySimpleName.ContainsKey(trimmed))
        {
            return false;
        }

        // Reject interface names unless registered as a known entity.
        if (trimmed[0] == 'I' &&
            trimmed.Length >= 2 &&
            char.IsUpper(trimmed[1]) &&
            !context.DbSetMappings.DbSetsByEntity.ContainsKey(trimmed))
        {
            return false;
        }

        foreach (var suffix in NonEntitySuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}
