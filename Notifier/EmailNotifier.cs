using System.Collections.Generic;
using System.Threading.Tasks;
using bilbasen.Shared.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace bilbasen.Notifier
{
    public class EmailNotifier
    {
        public const string NotifyFunctionName = "Email_Notifier";

        [FunctionName(NotifyFunctionName)]
        public static void BatchInsert([ActivityTrigger] List<SearchResultEntity> entities, ILogger log)
        {
            log.LogInformation($"Notifications triggered for {entities.Count} new cars");
        }
    }
}