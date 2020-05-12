using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace bilbasen.Shared.Models
{
    public class SearchResult
    {
        public string Id {get; set;}
        public string Model {get; set;}
        public string Trim {get; set;}
        public string Href {get; set;}
        public int KmDriven {get; set;}
        public int Year {get; set;}
        public int Price {get; set;}
        public string Description {get; set;}
        public string Region {get; set;}
    }

    public class SearchResultEntity : SearchResult, ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string ETag { get; set; }
        public DateTimeOffset FirstSeen { get; set; }
        public DateTimeOffset LastSeen { get; set; }


        public void ReadEntity(IDictionary<string, EntityProperty> properties, OperationContext operationContext)
        {
            TableEntity.ReadUserObject(this, properties, operationContext);
        }

        public IDictionary<string, EntityProperty> WriteEntity(OperationContext operationContext)
        {
            return TableEntity.WriteUserObject(this, operationContext);
        }

        public SearchResultEntity(SearchResult searchResult)
        {
            foreach(var prop in searchResult.GetType().GetProperties())
            {
                prop.SetValue(this, prop.GetValue(searchResult));
            }

            PartitionKey = searchResult.Model;
            RowKey = searchResult.Id;
            FirstSeen = DateTimeOffset.UtcNow;
            LastSeen = DateTimeOffset.UtcNow;
        }

        public SearchResultEntity()
        {
           
        }
    }
}