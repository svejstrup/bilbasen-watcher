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
        public SearchAndNotification SearchPhrase {get; set;}
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
                var searchAndNotifications = await context.CallActivityAsync<List<SearchAndNotification>>(TableStorageDataManager.GetSearchPhrasesFunctionName, null);
                var searchPhrases = searchAndNotifications.Where(sn => sn.SearchOrNotification == Constants.SearchType);
                var notificationTriggers = searchAndNotifications.Where(sn => sn.SearchOrNotification == Constants.NotificationType);

                var searchTasks = searchPhrases.Select(searchPhrase => 
                {
                    return new TableAndSearchData
                    {
                        SearchPhrase = searchPhrase,
                        TableData = context.CallActivityAsync<List<SearchResultEntity>>(TableStorageDataManager.GetByPartitionKeyFunctionName, (searchPhrase.Model, TableName.Cars)),
                        CurrentData = context.CallActivityAsync<List<SearchResult>>(BilBasenSearcher.SearchFunctionName, searchPhrase)
                    };
                });

                var triggeredNotifications = new List<TriggeredNotification>();

                foreach(var task in searchTasks)
                {
                    var tableData = await task.TableData;
                    var currentData = await task.CurrentData;
                    var triggersForModel = notificationTriggers
                        .Where(nt => nt.Model.Equals(task.SearchPhrase.Model, StringComparison.InvariantCultureIgnoreCase))
                        .ToList();
                    
                    var (entitiesToUpsert, newResults) = GetEntitiesToUpdate(tableData, currentData);

                    triggeredNotifications.AddRange(newResults.SelectMany(nr => GetTriggeredNotifications(nr, triggersForModel)));

                    await context.CallActivityAsync("TableStorage_BatchInsert", entitiesToUpsert);
                }

                if (triggeredNotifications.Any())
                {
                    var emails = EmailNotifier.BuildEmails(triggeredNotifications);

                    await Task.WhenAll(emails.Select(e => context.CallActivityAsync(EmailNotifier.NotifyFunctionName, e)));
                }
            }
            catch(Exception e)
            {
                log.LogError(e, "Exception caught in orchestrator");
                throw e;
            }
        }

        private static List<TriggeredNotification> GetTriggeredNotifications(SearchResultEntity searchResult, List<SearchAndNotification> triggers)
        {
            var notifications = new List<TriggeredNotification>();

            foreach(var trigger in triggers)
            {
                var triggered = true;

                if (trigger.PriceThreshold.HasValue)
                    triggered &= searchResult.Price < trigger.PriceThreshold;

                if (!string.IsNullOrWhiteSpace(trigger.EarliestYear) && int.TryParse(trigger.EarliestYear, out var triggerYear))
                    triggered &= searchResult.Year >= triggerYear;

                if (!string.IsNullOrWhiteSpace(trigger.MaxKmDriven) && int.TryParse(trigger.MaxKmDriven, out var triggerKm))
                    triggered &= searchResult.KmDriven <= triggerKm;
                    
                triggered &= (trigger.Trim.Equals(Constants.AnyTrim, StringComparison.InvariantCultureIgnoreCase) || searchResult.Trim.Equals(trigger.Trim, StringComparison.InvariantCultureIgnoreCase));

                if (triggered)
                    notifications.Add(new TriggeredNotification {Car = searchResult, Email = trigger.Email});
            }

            return notifications;
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
                    currTableData.Description = searchResult.Description;
                    currTableData.Region = searchResult.Region;

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
            [TimerTrigger("0 0 */10 * * *")] TimerInfo timer,
            [DurableClient] IDurableOrchestrationClient starter,
            ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync(OrchestratorFunctionName, null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");
        }
    }
}