using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace bilbasen.Shared.Models
{
    public class SearchPhrase : TableEntity
    {
        public string Model {get; set;}
        public string Trim {get; set;}
        public int PriceThreshold {get; set;}
        public Boolean SendMail {get; set;}
    }
}