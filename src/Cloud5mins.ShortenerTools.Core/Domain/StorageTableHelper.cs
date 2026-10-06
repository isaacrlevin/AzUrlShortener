using Azure;
using Azure.Data.Tables;
using Cloud5mins.ShortenerTools.Core.Messages;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class StorageTableHelper
    {
        private string StorageConnectionString { get; set; }
        private readonly TableServiceClient? _tableServiceClient;

        public StorageTableHelper() { }

        public StorageTableHelper(string storageConnectionString)
        {
            StorageConnectionString = storageConnectionString;
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

        public virtual async Task<ShortUrlEntity> GetShortUrlEntity(ShortUrlEntity row)
        {
            var tableClient = GetUrlsTable();
            var result = await tableClient.GetEntityIfExistsAsync<ShortUrlEntity>(row.PartitionKey, row.RowKey);
            return (result.HasValue == true ? result.Value : null);
        }

        public async Task<List<ShortUrlEntity>> GetAllShortUrlEntities()
        {
            var tableClient = GetUrlsTable();
            List<ShortUrlEntity> lstShortUrl = new List<ShortUrlEntity>();
            Pageable<ShortUrlEntity> queryResultsLINQ = tableClient.Query<ShortUrlEntity>(ent => !ent.IsArchived && ent.RowKey != "KEY");

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
            await foreach (var item in tableClient.QueryAsync<ShortUrlEntity>(ent => !ent.IsArchived && ent.RowKey != "KEY"))
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
        public virtual async Task SaveClickStatsEntity(ClickStatsEntity newStats)
        {
            var tableClient = GetStatsTable();
            await tableClient.UpsertEntityAsync(newStats);
        }

        public async Task<ShortUrlEntity> SaveShortUrlEntity(ShortUrlEntity newShortUrl)
        {
            var tableClient = GetUrlsTable();
            await tableClient.UpsertEntityAsync(newShortUrl);
            return newShortUrl;
        }

        public async Task<bool> IfShortUrlEntityExistByVanity(string vanity)
        {
            ShortUrlEntity shortUrlEntity = await GetShortUrlEntityByVanity(vanity);
            return (shortUrlEntity != null);
        }

        public async Task<bool> IfShortUrlEntityExist(ShortUrlEntity row)
        {
            ShortUrlEntity eShortUrl = await GetShortUrlEntity(row);
            return (eShortUrl != null);
        }
        public async Task<int> GetNextTableId()
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

            ShortUrlEntity updatedEntity = await tableClient.GetEntityAsync<ShortUrlEntity>(entity.PartitionKey, entity.RowKey);

            return entity.Id;
        }


        public async Task<ShortUrlEntity> UpdateShortUrlEntity(ShortUrlEntity urlEntity)
        {
            ShortUrlEntity originalUrl = await GetShortUrlEntity(urlEntity);
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
            return stats.OrderByDescending(a=> a.Date).ToList();
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
            ShortUrlEntity originalUrl = await GetShortUrlEntity(urlEntity);
            originalUrl.IsArchived = true;

            return await SaveShortUrlEntity(originalUrl);
        }
    }
}