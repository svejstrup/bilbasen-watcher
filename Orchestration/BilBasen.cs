using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using bilbasen.DAL;
using bilbasen.Notifier;
using bilbasen.Search;
using bilbasen.Shared.Models;
using bilbasen.Shared.Util;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;

namespace memex.bilbasen
{
    public class TableAndSearchData
    {
        public SearchPhrase SearchPhrase {get; set;}
        public Task<List<SearchResultEntity>> TableData {get; set;}
        public Task<List<SearchResult>> CurrentData {get; set;}
    }
    public static class Orchestrator
    {
        public const string OrchestratorFunctionName = "BilBasen_Watcher";

        [FunctionName(OrchestratorFunctionName)]
        public static async Task RunOrchestrator([OrchestrationTrigger] IDurableOrchestrationContext context, ILogger log)
        {
            try 
            {
                var searchPhrases = await context.CallActivityAsync<List<SearchPhrase>>(TableStorageDataManager.GetSearchPhrasesFunctionName, null);
                log.LogInformation("BilBasen: Start waiting for results");

                var searchTasks = searchPhrases.Select(searchPhrase => 
                {
                    return new TableAndSearchData
                    {
                        SearchPhrase = searchPhrase,
                        TableData = context.CallActivityAsync<List<SearchResultEntity>>(TableStorageDataManager.GetByPartitionKeyFunctionName, (searchPhrase.Model, TableName.Cars)),
                        CurrentData = context.CallActivityAsync<List<SearchResult>>(BilBasenSearcher.SearchFunctionName, searchPhrase.Model)
                    };
                });

                var triggeredNotifications = new List<SearchResultEntity>();

                foreach(var task in searchTasks)
                {
                    var tableData = await task.TableData;
                    var currentData = await task.CurrentData;
                    
                    var (entitiesToUpsert, newResults) = GetEntitiesToUpdate(tableData, currentData);

                    triggeredNotifications.AddRange(newResults.Where(nr => IsNotificationTriggered(nr, task.SearchPhrase)));

                    await context.CallActivityAsync("TableStorage_BatchInsert", entitiesToUpsert);
                }

                if (triggeredNotifications.Any())
                    await context.CallActivityAsync(EmailNotifier.NotifyFunctionName, triggeredNotifications);
            }
            catch(Exception e)
            {
                log.LogError(e, "Exception caught in orchestrator");
                throw e;
            }
        }

        private static bool IsNotificationTriggered(SearchResultEntity searchResult, SearchPhrase searchPhrase)
        {
            var triggered = searchResult.Price < searchPhrase.PriceThreshold;
            triggered &= searchPhrase.SendMail;
            triggered &= (searchPhrase.Trim == Constants.AnyTrim || searchResult.Trim.Equals(searchPhrase.Trim, StringComparison.InvariantCultureIgnoreCase));

            return triggered;
        }

        private static (List<ITableEntity>, List<SearchResultEntity>) GetEntitiesToUpdate(List<SearchResultEntity> tableData, List<SearchResult> currentData)
        {
            var tableDict = tableData.ToDictionary(td => td.Id);

            var newResults = new List<SearchResultEntity>();

            var entitiesToInsert = currentData.Select(searchResult =>
            {
                if (tableDict.ContainsKey(searchResult.Id))
                {
                    var currTableData = tableDict[searchResult.Id];
                    
                    if (currTableData.Price > searchResult.Price)
                        newResults.Add(currTableData);

                    currTableData.LastSeen = DateTimeOffset.UtcNow;
                    currTableData.Price = searchResult.Price;
                    currTableData.KmDriven = searchResult.KmDriven;

                    return currTableData;
                }
                
                var entity = new SearchResultEntity(searchResult);
                newResults.Add(entity);
                return entity;
            }).Cast<ITableEntity>().ToList();

            return (entitiesToInsert, newResults);
        }

        [FunctionName("BilBasen_TimerStart")]
        public static async Task TimerStart(
            [TimerTrigger("0 0 */10 * * *", RunOnStartup=true)] TimerInfo timer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync(OrchestratorFunctionName, null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }
    }
}