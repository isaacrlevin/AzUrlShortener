using Azure;
using Azure.Data.Tables;
using System.Text.Json;

namespace Cloud5mins.ShortenerTools.Core.Domain
{
    public class StorageTableHelper
    {
        private string StorageConnectionString { get; set; }

        public StorageTableHelper() { }

        public StorageTableHelper(string storageConnectionString)
        {
            StorageConnectionString = storageConnectionString;
        }
        public TableServiceClient CreateStorageAccountFromConnectionString()
        {
            TableServiceClient tableClient = new TableServiceClient(StorageConnectionString);
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

        public async Task<ShortUrlEntity> GetShortUrlEntity(ShortUrlEntity row)
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

            foreach (ShortUrlEntity qEntity in queryResultsLINQ)
            {
                lstShortUrl.AddRange(qEntity);
            }

            return lstShortUrl;
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
        public async Task SaveClickStatsEntity(ClickStatsEntity newStats)
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
            return results.ToList();
        }


        public async Task<ShortUrlEntity> ArchiveShortUrlEntity(ShortUrlEntity urlEntity)
        {
            ShortUrlEntity originalUrl = await GetShortUrlEntity(urlEntity);
            originalUrl.IsArchived = true;

            return await SaveShortUrlEntity(originalUrl);
        }
    }
}