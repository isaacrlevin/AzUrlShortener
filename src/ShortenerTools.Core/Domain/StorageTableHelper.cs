using Azure;
using Azure.Data.Tables;
using Azure.Identity;
using ShortenerTools.Core.Messages;
using System.Text.Json;

namespace ShortenerTools.Core.Domain
{
    public class StorageTableHelper
    {
        private string StorageConnectionString { get; set; }
        private readonly TableServiceClient? _tableServiceClient;

        public StorageTableHelper()
        {
        }

        public StorageTableHelper(string storageConnectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(storageConnectionString);
            StorageConnectionString = storageConnectionString;
            if (Uri.TryCreate(storageConnectionString, UriKind.Absolute, out var tableEndpoint)
                && tableEndpoint.Scheme is "https" or "http")
            {
                _tableServiceClient = new TableServiceClient(tableEndpoint, new DefaultAzureCredential());
            }
        }

        /// <summary>Creates a helper using an existing Azure Tables client.</summary>
        /// <param name="tableServiceClient">The client used for table operations.</param>
        public StorageTableHelper(TableServiceClient tableServiceClient)
        {
            ArgumentNullException.ThrowIfNull(tableServiceClient);
            _tableServiceClient = tableServiceClient;
        }

        public TableServiceClient CreateStorageAccountFromConnectionString()
        {
            TableServiceClient tableClient = _tableServiceClient ?? new TableServiceClient(StorageConnectionString);
            return tableClient;
        }

        private TableClient GetTable(string tableName)
        {
            TableServiceClient tableClient = CreateStorageAccountFromConnectionString();
            TableClient table = tableClient.GetTableClient(tableName);
            table.CreateIfNotExists();

            return table;
        }

        private TableClient GetUrlsTable()
        {
            TableClient table = GetTable("UrlsDetails");
            return table;
        }

        private TableClient GetStatsTable()
        {
            TableClient table = GetTable("ClickStats");
            return table;
        }

        private TableClient GetPostVersionsTable()
        {
            TableClient table = GetTable("PostVersions");
            return table;
        }

        /// <summary>Appends a new post version; existing versions are never modified.</summary>
        public virtual async Task<PostVersionEntity> AddPostVersion(PostVersionEntity version)
        {
            var tableClient = GetPostVersionsTable();
            await tableClient.AddEntityAsync(version);
            return version;
        }

        /// <summary>Records the outcome of publishing a version; the text itself is never modified.</summary>
        public virtual async Task MarkPostVersion(PostVersionEntity version, string status, string? externalPostId = null)
        {
            if (version.IsLegacy) return;
            version.Status = status;
            version.ExternalPostId = externalPostId ?? version.ExternalPostId;
            await GetPostVersionsTable().UpdateEntityAsync(version, version.ETag, TableUpdateMode.Replace);
        }

        /// <summary>
        /// Returns versions for a link, newest first within each account. Links created before versioning
        /// have no rows, so a single synthesized version is returned from the original record.
        /// </summary>
        /// <param name="shortcode">The link's shortcode.</param>
        /// <param name="account">Optional account to restrict the result to.</param>
        public virtual async Task<List<PostVersionEntity>> GetPostVersions(string shortcode, string? account = null)
        {
            var tableClient = GetPostVersionsTable();
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {shortcode}");
            if (!string.IsNullOrWhiteSpace(account))
            {
                // '}' sorts immediately after '|', which bounds the "{account}|" prefix range.
                var prefix = PostVersionEntity.SanitizeAccount(account) + PostVersionEntity.Separator;
                var upper = PostVersionEntity.SanitizeAccount(account) + "}";
                filter += " and " + TableClient.CreateQueryFilter($"RowKey ge {prefix} and RowKey lt {upper}");
            }

            var versions = new List<PostVersionEntity>();
            await foreach (var version in tableClient.QueryAsync<PostVersionEntity>(filter))
                versions.Add(version);
            if (versions.Count > 0) return versions;

            if (!string.IsNullOrWhiteSpace(account) && !account.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                return versions;
            var link = await GetShortUrlEntity(new ShortUrlEntity(string.Empty, shortcode));
            return link is null || string.IsNullOrEmpty(link.Message)
                ? versions
                : [PostVersionEntity.FromLegacy(link)];
        }

        /// <summary>Returns the most recent version for one account, or null when it has none.</summary>
        public virtual async Task<PostVersionEntity?> GetLatestPostVersion(string shortcode, string account) =>
            (await GetPostVersions(shortcode, account)).FirstOrDefault();

        public virtual async Task<ShortUrlEntity?> GetShortUrlEntity(ShortUrlEntity row)
        {
            var tableClient = GetUrlsTable();
            var result = await tableClient.GetEntityIfExistsAsync<ShortUrlEntity>(row.PartitionKey, row.RowKey);
            return (result.HasValue == true ? result.Value : null);
        }

        public virtual async Task<List<ShortUrlEntity>> GetAllShortUrlEntities()
        {
            var tableClient = GetUrlsTable();
            List<ShortUrlEntity> lstShortUrl = new List<ShortUrlEntity>();
            Pageable<ShortUrlEntity> queryResultsLINQ =
                tableClient.Query<ShortUrlEntity>(ent => !ent.IsArchived && ent.RowKey != "KEY");

            var stats = await GetAllStats();

            foreach (ShortUrlEntity qEntity in queryResultsLINQ)
            {
                //qEntity.ClickCount = stats.Where(a => a.PartitionKey == qEntity.RowKey).Count();
                lstShortUrl.AddRange(qEntity);
            }

            return lstShortUrl;
        }

        public async Task<(List<ShortUrlEntity> Items, int TotalCount)> GetShortUrlEntitiesPaged(int skip, int take)
        {
            var result = await GetShortUrlEntitiesPaged(new UrlListQuery { Skip = skip, Take = take });
            return (result.UrlList, result.TotalCount);
        }

        /// <summary>Returns a filtered and sorted page of active links.</summary>
        /// <param name="query">Paging, sorting, and filtering options.</param>
        /// <returns>The requested links and matching count.</returns>
        public async Task<ListResponse> GetShortUrlEntitiesPaged(UrlListQuery query)
        {
            query.Validate();
            var tableClient = GetUrlsTable();
            var items = new List<ShortUrlEntity>();
            // Table Storage orders by keys only; arbitrary column sorts require a server-side scan.
            await foreach (var item in tableClient.QueryAsync<ShortUrlEntity>(ent =>
                               !ent.IsArchived && ent.RowKey != "KEY"))
                items.Add(item);
            return query.Apply(items);
        }

        /// <summary>
        /// Returns the ShortUrlEntity of the <paramref name="vanity"/>
        /// </summary>
        /// <param name="vanity"></param>
        /// <returns>ShortUrlEntity</returns>
        public async Task<ShortUrlEntity> GetShortUrlEntityByVanity(string vanity)
        {
            var tableClient = GetUrlsTable();
            Pageable<ShortUrlEntity> results = tableClient.Query<ShortUrlEntity>(ent => ent.ShortUrl == vanity);
            return results.FirstOrDefault();
        }

        /// <summary>Returns the active link created by <paramref name="creationId"/>, if a previous attempt made one.</summary>
        public virtual async Task<ShortUrlEntity?> GetShortUrlEntityByCreationId(string creationId)
        {
            var tableClient = GetUrlsTable();
            await foreach (var item in tableClient.QueryAsync<ShortUrlEntity>(ent =>
                               ent.CreationId == creationId && !ent.IsArchived))
                return item;
            return null;
        }

        public virtual async Task SaveClickStatsEntity(ClickStatsEntity newStats)
        {
            var tableClient = GetStatsTable();
            await tableClient.UpsertEntityAsync(newStats);
        }

        /// <summary>Creates a link without overwriting any existing or archived shortcode.</summary>
        public async Task<bool> TryCreateShortUrlEntity(ShortUrlEntity newShortUrl)
        {
            var tableClient = GetUrlsTable();
            try
            {
                await tableClient.AddEntityAsync(newShortUrl);
                return true;
            }
            catch (RequestFailedException exception) when (exception.Status == 409)
            {
                return false;
            }
        }

        /// <summary>Returns the original record when retrying an identified creation; never updates its metadata.</summary>
        public virtual async Task<ShortUrlEntity?> CreateOrGetShortUrlEntity(ShortUrlEntity candidate)
        {
            if (await TryCreateShortUrlEntity(candidate)) return candidate;
            if (string.IsNullOrEmpty(candidate.CreationId)) return null;
            var existing = await GetShortUrlEntity(candidate);
            return existing is not null && !existing.IsArchived && existing.CreationId == candidate.CreationId
                   && existing.Url == candidate.Url
                ? existing
                : null;
        }

        public async Task<ShortUrlEntity> SaveShortUrlEntity(ShortUrlEntity newShortUrl)
        {
            var tableClient = GetUrlsTable();
            await tableClient.UpsertEntityAsync(newShortUrl);
            return newShortUrl;
        }

        public virtual async Task<bool> IfShortUrlEntityExistByVanity(string vanity)
        {
            ShortUrlEntity shortUrlEntity = await GetShortUrlEntityByVanity(vanity);
            return (shortUrlEntity != null);
        }

        public async Task<bool> IfShortUrlEntityExist(ShortUrlEntity row)
        {
            ShortUrlEntity eShortUrl = await GetShortUrlEntity(row);
            return (eShortUrl != null);
        }

        public virtual async Task<int> GetNextTableId()
        {
            var tableClient = GetUrlsTable();


            var result = await tableClient.GetEntityIfExistsAsync<NextId>("1", "KEY");

            NextId entity = null;

            if (result.HasValue)
            {
                entity = result.Value as NextId;
            }
            else
            {
                entity = new NextId
                {
                    PartitionKey = "1",
                    RowKey = "KEY",
                    Id = 1024
                };
            }

            entity.Id++;

            tableClient.UpsertEntity(entity);

            ShortUrlEntity updatedEntity =
                await tableClient.GetEntityAsync<ShortUrlEntity>(entity.PartitionKey, entity.RowKey);

            return entity.Id;
        }


        public virtual async Task<ShortUrlEntity> UpdateShortUrlEntity(ShortUrlEntity urlEntity)
        {
            var originalUrl = await GetShortUrlEntity(urlEntity) ??
                              throw new InvalidOperationException("The link no longer exists.");
            originalUrl.Url = urlEntity.Url;
            originalUrl.Title = urlEntity.Title;
            originalUrl.Message = urlEntity.Message;
            originalUrl.Posted = urlEntity.Posted;

            return await SaveShortUrlEntity(originalUrl);
        }


        public async Task<List<ClickStatsEntity>> GetAllStatsByVanity(string vanity)
        {
            var tableClient = GetStatsTable();
            Pageable<ClickStatsEntity> results = tableClient.Query<ClickStatsEntity>(ent => ent.PartitionKey == vanity);
            return results.OrderByDescending(a => a.Date).ToList();
        }

        public async Task<List<ClickStatsEntity>> GetAllStats()
        {
            var tableClient = GetStatsTable();
            List<ClickStatsEntity> stats = new List<ClickStatsEntity>();
            Pageable<ClickStatsEntity> queryResultsLINQ = tableClient.Query<ClickStatsEntity>();

            foreach (ClickStatsEntity qEntity in queryResultsLINQ)
            {
                stats.AddRange(qEntity);
            }

            return stats.OrderByDescending(a => a.Date).ToList();
        }

        /// <summary>Streams only stored timestamps and returns daily click counts.</summary>
        /// <param name="request">The optional vanity, inclusive date bounds, and display time zone.</param>
        /// <returns>Chronologically ordered daily counts without individual telemetry.</returns>
        public virtual async Task<ClickDateList> GetDailyStats(UrlClickStatsRequest request)
        {
            var aggregation = new DailyClickAggregation(request);
            var tableClient = GetStatsTable();
            var filter = string.IsNullOrEmpty(request.Vanity)
                ? null
                : TableClient.CreateQueryFilter($"PartitionKey eq {request.Vanity}");
            await foreach (var click in tableClient.QueryAsync<TableEntity>(
                               filter: filter, select: new[] { nameof(ClickStatsEntity.Datetime) }))
            {
                aggregation.Add(click.GetString(nameof(ClickStatsEntity.Datetime)));
            }

            return new ClickDateList { Items = aggregation.GetItems() };
        }

        public async Task<ShortUrlEntity> ArchiveShortUrlEntity(ShortUrlEntity urlEntity)
        {
            var originalUrl = await GetShortUrlEntity(urlEntity) ??
                              throw new InvalidOperationException("The link no longer exists.");
            originalUrl.IsArchived = true;

            return await SaveShortUrlEntity(originalUrl);
        }
    }
}