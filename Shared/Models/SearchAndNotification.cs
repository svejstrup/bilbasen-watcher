using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace bilbasen.Shared.Models
{
    public class SearchAndNotification : TableEntity
    {
        public string Model {get; set;}
        public string Trim {get; set;}
        public int? PriceThreshold {get; set;}
        public string MaxKmDriven {get; set;}
        public string Email {get; set;}
        public string EarliestYear {get; set;}
        public string SearchOrNotification {get; set;}
    }
}