using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using bilbasen.Shared.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;

namespace bilbasen.Search
{
    public static class BilBasenSearcher
    {
        const string BaseSearchUrl = "https://www.bilbasen.dk/brugt/bil";
        public const string SearchFunctionName = "BilBasen_Search";

        [FunctionName(SearchFunctionName)]
        public static async Task<List<SearchResult>> SayHello([ActivityTrigger] string searchPhrase, ILogger log)
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

                        res.AddRange(ParseHtml(pageDocument, log).Where(sr => sr.Model.Equals(searchPhrase, StringComparison.InvariantCultureIgnoreCase)));
                    }

                    log.LogInformation($"Search: Found {res.Count} matches for {searchPhrase}");

                    return res;
                }
            }
            catch(Exception e)
            {
                log.LogError(e, $"Exception caught in {SearchFunctionName}");
                throw e;
            }
        }

        static List<SearchResult> ParseHtml(HtmlDocument htmlDocument, ILogger log)
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

                var dataNodes = row.Descendants("div").Where(n => n.HasAttributes && n.Attributes["class"] != null && n.Attributes["class"].Value.Contains("listing-data")).ToList();
 
                var success = int.TryParse(dataNodes[2].InnerHtml.Replace(".", ""), out var kmDriven);
                LogIfUnsuccessful(log, success, dataNodes[2].InnerHtml);

                success = int.TryParse(dataNodes[3].InnerHtml, out var year);
                LogIfUnsuccessful(log, success, dataNodes[3].InnerHtml);

                var priceHtml = row.Descendants("div").FirstOrDefault(n => n.HasAttributes && n.Attributes["class"] != null && n.Attributes["class"].Value.Contains("listing-price"))?.InnerHtml;
                success = int.TryParse(priceHtml.Replace(" kr.", "").Replace(".",""), out var price);
                LogIfUnsuccessful(log, success, priceHtml);

                return new SearchResult 
                {
                    Id = id,
                    Href = href,
                    Model = linkText[0],
                    Trim = linkText[1],
                    KmDriven = kmDriven,
                    Year = year,
                    Price = price,
                };
            }).Where(x => x != null).ToList();

            return searchResults;
        }

        static string GetUrl(string searchPhrase, int page)
        {
            var queryParams = new Dictionary<string, string>()
            {
                {"IncludeEngrosCVR", "true"},
                {"PriceFrom", "0"},
                {"includeLeasing", "false"},
                {"free", searchPhrase},
                {"page", page.ToString()}
            };

            return QueryHelpers.AddQueryString(BaseSearchUrl, queryParams);
        }

        static void LogIfUnsuccessful(ILogger log, bool success, string html)
        {
            if (!success)
                log.LogWarning($"Unable to parse {html}");
        }
    }
}