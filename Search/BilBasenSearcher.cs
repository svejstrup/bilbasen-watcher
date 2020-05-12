using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using bilbasen.Shared.Models;
using bilbasen.Shared.Util;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System.Web;
using System.IO;

namespace bilbasen.Search
{
    public static class BilBasenSearcher
    {
        const string BaseSearchUrl = "https://www.bilbasen.dk/brugt/bil";
        public const string SearchFunctionName = "BilBasen_Search";

        [FunctionName(SearchFunctionName)]
        public static async Task<List<SearchResult>> SayHello([ActivityTrigger] SearchAndNotification searchPhrase, ILogger log)
        {
            try 
            {
                using (var client = new HttpClient(new HttpClientHandler() {AllowAutoRedirect = false}))
                {
                    var res = new List<SearchResult>();

                    for (int i = 1; i < 10; i++)
                    {
                        var response = await client.GetAsync(GetUrl(searchPhrase, i));

                        if (!response.IsSuccessStatusCode)
                            break;

                        var pageContents = await response.Content.ReadAsStringAsync();

                        HtmlDocument pageDocument = new HtmlDocument();
                        pageDocument.LoadHtml(pageContents);

                        res.AddRange(ParseHtml(pageDocument, searchPhrase.Model, log).Where(sr => sr.Model.Equals(searchPhrase.Model, StringComparison.InvariantCultureIgnoreCase)));
                    }

                    log.LogInformation($"Search: Found {res.Count} matches for {searchPhrase.Model}");

                    return res;
                }
            }
            catch(Exception e)
            {
                log.LogError(e, $"Exception caught in {SearchFunctionName}");
                throw e;
            }
        }

        static List<SearchResult> ParseHtml(HtmlDocument htmlDocument, string searchModel, ILogger log)
        {
            var rows = htmlDocument.DocumentNode.SelectNodes("(//div[contains(@class,'bb-listing-clickable')])");

            var searchResults = rows.Select(row => 
            {
                var linkNode = row.Descendants("a").FirstOrDefault(n => n.HasAttributes && n.Attributes["class"].Value.Contains("listing-heading"));

                if (linkNode == null)
                    return null;

                var href = linkNode.Attributes["href"].Value;
                var id = href?.Split('/').LastOrDefault();
                var linkText = linkNode.InnerHtml.Split("  ");

                var (model, trim) = GetModelAndTrim(linkText, searchModel);
                
                var dataNodes = row.Descendants("div").Where(n => n.HasAttributes && n.Attributes["class"] != null && n.Attributes["class"].Value.Contains("listing-data")).ToList();
 
                var success = int.TryParse(dataNodes[2].InnerHtml.Replace(".", ""), out var kmDriven);
                LogIfUnsuccessful(log, success, dataNodes[2].InnerHtml);

                success = int.TryParse(dataNodes[3].InnerHtml, out var year);
                LogIfUnsuccessful(log, success, dataNodes[3].InnerHtml);

                var priceHtml = row.Descendants("div").FirstOrDefault(n => n.HasAttributes && n.Attributes["class"] != null && n.Attributes["class"].Value.Contains("listing-price"))?.InnerHtml;
                success = int.TryParse(priceHtml.Replace(" kr.", "").Replace(".",""), out var price);
                LogIfUnsuccessful(log, success, priceHtml);

                var descriptionEncoded = row.Descendants("div").FirstOrDefault(n => n.HasAttributes && n.Attributes["class"] != null && n.Attributes["class"].Value.Contains("listing-description"))?.InnerHtml;

                StringWriter myWriter = new StringWriter();
                HttpUtility.HtmlDecode(descriptionEncoded, myWriter);
                string description = myWriter.ToString();

                myWriter = new StringWriter();
                var regionEncoded = row.Descendants("div").FirstOrDefault(n => n.HasAttributes && n.Attributes["class"] != null && n.Attributes["class"].Value.Contains("listing-region"))?.InnerHtml;
                HttpUtility.HtmlDecode(regionEncoded, myWriter);
                var region = myWriter.ToString();

                return new SearchResult 
                {
                    Id = id,
                    Href = href,
                    Model = model,
                    Trim = trim,
                    KmDriven = kmDriven,
                    Year = year,
                    Price = price,
                    Description = description,
                    Region = region
                };
            }).Where(x => x != null).ToList();

            return searchResults;
        }

        private static (string model, string trim) GetModelAndTrim(string[] linkText, string searchModel)
        {
            // model and trim are usually separated by a double space
            if (linkText.Count() == 2)
            {
                return (linkText[0], linkText[1]);
            }
            
            // Handle case where only a single space separates model and trim
            var modelAndTrim = linkText[0];
            if (modelAndTrim.Contains(searchModel, StringComparison.InvariantCultureIgnoreCase))
            {
                var trim = modelAndTrim.Replace(searchModel, "").Trim();
                return (searchModel, trim);
            }

            return (linkText[0], "");
        }

        static string GetUrl(SearchAndNotification searchPhrase, int page)
        {
            var modelAndTrim = searchPhrase.Model;

            if (!string.IsNullOrWhiteSpace(searchPhrase.Trim) && !Constants.AnyTrim.Equals(searchPhrase.Trim, StringComparison.InvariantCultureIgnoreCase))
                modelAndTrim += $" {searchPhrase.Trim}";

            var queryParams = new Dictionary<string, string>()
            {
                {"IncludeEngrosCVR", "true"},
                {"PriceFrom", "0"},
                {"includeLeasing", "false"},
                {"free", searchPhrase.Model},
                {"page", page.ToString()}
            };

            if (!string.IsNullOrWhiteSpace(searchPhrase.EarliestYear))
                queryParams.Add("YearFrom", searchPhrase.EarliestYear);

            if (!string.IsNullOrWhiteSpace(searchPhrase.MaxKmDriven))
                queryParams.Add("MileageTo", searchPhrase.MaxKmDriven);

            return QueryHelpers.AddQueryString(BaseSearchUrl, queryParams);
        }

        static void LogIfUnsuccessful(ILogger log, bool success, string html)
        {
            if (!success)
                log.LogWarning($"Search: Unable to parse HTML: \"{html}\"");
        }
    }
}