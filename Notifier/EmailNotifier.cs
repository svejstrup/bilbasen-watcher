using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using bilbasen.Shared.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid.Helpers.Mail;

namespace bilbasen.Notifier
{
    public class EmailNotifier
    {
        public const string NotifyFunctionName = "Email_Notifier";

        [FunctionName(NotifyFunctionName)]
        public static async Task BatchInsert(
            [ActivityTrigger] OutgoingEmail emailObject, 
            [SendGrid(ApiKey = "SendGridApiKey")] IAsyncCollector<SendGridMessage> messageCollector, ILogger log)
        {
            log.LogInformation($"Notifications triggered for new cars");

            try 
            {
                var message = new SendGridMessage();
                message.AddTo(emailObject.To); // TODO - should probably be in bcc instead. But email without any 'to' address is not allowed
                message.AddContent("text/html", emailObject.Body);
                message.SetFrom(new EmailAddress(emailObject.From));
                message.SetSubject(emailObject.Subject);
                
                await messageCollector.AddAsync(message);
            }
            catch(Exception e) 
            {
                log.LogError(e, "Exception caught in EmailSender");
                throw e;
            }
        }

        public static List<OutgoingEmail> BuildEmails(List<TriggeredNotification> triggeredNotifications)
        {
            var groups = triggeredNotifications.GroupBy(tn => tn.Email);

            return groups.Select(group => BuildSingleEmail(group.Select(g => g.Car), group.Key)).ToList();
        }

        private static OutgoingEmail BuildSingleEmail(IEnumerable<SearchResult> notifications, string email)
        {
            return new OutgoingEmail 
            {
                To = email,
                From = "msv@iterator-it.dk",
                Subject = "Bilbasen-Watcher",
                Body = BuildEmailBody(notifications)
            };
        }

        private static string BuildEmailBody(IEnumerable<SearchResult> searchResult)
        {
            var body = "Notification triggered for the following cars: <br><br>";
            body += string.Join("<br><br>", searchResult.Select(sr => JsonConvert.SerializeObject(sr, Formatting.Indented)));

            return body;
        }
    }
}