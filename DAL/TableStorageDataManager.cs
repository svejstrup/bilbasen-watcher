using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using bilbasen.Shared.Models;
using bilbasen.Shared.Util;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace bilbasen.DAL
{
    public static class TableStorageDataManager
    {
        private const int maxBatchSize = 100; // Table storage does not allow batches larger than 100
        public const string GetSearchPhrasesFunctionName = "TableStorage_GetSearchPhrases";
        public const string GetByPartitionKeyFunctionName = "TableStorage_GetByPartitionKey";
        public const string BatchInsertFunctionName = "TableStorage_BatchInsert";

        private async static Task<CloudTable> GetCloudTableFromName(TableName name)
        {
            CloudStorageAccount storageAccount;
            try
            {
                storageAccount = CloudStorageAccount.Parse(Environment.GetEnvironmentVariable("TableStorageConnection"));
            }
            catch (FormatException)
            {
                Console.WriteLine("Invalid storage account information provided");
                throw;
            }
            catch (ArgumentException)
            {
                Console.WriteLine("Invalid storage account information provided");
                Console.ReadLine();
                throw;
            }

            var tableClient = storageAccount.CreateCloudTableClient();

            switch (name)
            {
                case TableName.Cars:
                    var carCloudTable = tableClient.GetTableReference(Constants.CarsTableName);
                    await carCloudTable.CreateIfNotExistsAsync();
                    return carCloudTable;
                case TableName.Search:
                    var searchCloudTable = tableClient.GetTableReference(Constants.SearchTableName);
                    await searchCloudTable.CreateIfNotExistsAsync();
                    return searchCloudTable;
                default:
                    return null;
            }
        }

        [FunctionName(GetByPartitionKeyFunctionName)]
        public static async Task<List<SearchResultEntity>> GetByPartitionKey(
            [ActivityTrigger] Tuple<string, TableName> partitionKeyAndTableName, 
            ILogger log)
        {
            try 
            {
                TableContinuationToken token = null;
                var (partitionKey, tableName) = partitionKeyAndTableName;
                var table = await GetCloudTableFromName(tableName);

                TableQuery<SearchResultEntity> rangeQuery = new TableQuery<SearchResultEntity>()
                    .Where(TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, partitionKey));

                var entities = new List<SearchResultEntity>();

                do
                {
                    var queryResult = await table.ExecuteQuerySegmentedAsync(rangeQuery, token);
                    entities.AddRange(queryResult.Results);
                    token = queryResult.ContinuationToken;
                } while (token != null);

                return entities;
            }
            catch(Exception e)
            {
                log.LogError(e, $"Exception caught in {GetByPartitionKeyFunctionName}");
                throw e;
            }
        }

        private static async Task BatchInsert(List<ITableEntity> entityList, TableName tableName)
        {
            var table = await GetCloudTableFromName(tableName);

            if (!entityList.Any() || table == null)
				return;

			// Check if all entities have the same partition key (serial number) - we can only do batch insert of entities
			// that have the same partition key
			if (entityList.All(f => f.PartitionKey == entityList.First().PartitionKey))
			{
 				await BatchInsertSamePartitionKey(entityList, table);
			}
			else 
			{
				var insertTasks = entityList.Select(async e => await InsertSingle(e, table)).ToArray();
				await Task.WhenAll(insertTasks);
			}   
        }

        [FunctionName(BatchInsertFunctionName)]
        public static async Task BatchInsert(
            [ActivityTrigger] List<SearchResultEntity> entities,
            ILogger log)
        {
            try 
            {
                await BatchInsert(entities.Cast<ITableEntity>().ToList(), TableName.Cars);
            }
            catch(Exception e)
            {
                log.LogError(e, $"Exception caught in {BatchInsertFunctionName}");
                throw e;
            }
        }

        [FunctionName(GetSearchPhrasesFunctionName)]
        public static async Task<List<SearchAndNotification>> GetSearchPhrases([ActivityTrigger] ILogger log)
        {
            try 
            {
                var table = await GetCloudTableFromName(TableName.Search);
                var query = new TableQuery<SearchAndNotification>();

                var queryResult = await table.ExecuteQuerySegmentedAsync(query, null);
                
                return queryResult.ToList();
            }
            catch(Exception e)
            {
                log.LogError(e, $"Exception caught in {GetSearchPhrasesFunctionName}");
                throw e;
            }
        }

        /// <summary>        
        /// Perform batch insert of entities. 
        /// It is assumed that all entities in entityList have the same partition key
        /// otherwise an exception will be thrown
        /// </summary>
        private static async Task BatchInsertSamePartitionKey(List<ITableEntity> entityList, CloudTable table)
        {
            var batchOperation = new TableBatchOperation();

            // Insert entities in batches of at most 'maxBatchSize'
            foreach(var entity in entityList)
            {
                batchOperation.InsertOrMerge(entity);

                if (batchOperation.Count == maxBatchSize)
                {
                    await table.ExecuteBatchAsync(batchOperation);
                    batchOperation.Clear();
                }
            }

            // Handle last smaller batch
            if (batchOperation.Count > 0)
            {
                await table.ExecuteBatchAsync(batchOperation);
            }
        }

        private static async Task InsertSingle(ITableEntity entity, CloudTable table)
        {
            var tableOperation = TableOperation.InsertOrMerge(entity);
            await table.ExecuteAsync(tableOperation);
        }

    }

}