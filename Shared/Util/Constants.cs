namespace bilbasen.Shared.Util
{
    public static class Constants
    {
        public const string TableStorageConnectionKey = "TableStorageConnection";
        public const string CarsTableName = "Cars";
        public const string SearchTableName = "Search";
        public const string AnyTrim = "Any";
        public const string NotificationType = "Notification";
        public const string SearchType = "Search";
    }

    public enum TableName
    {
        Cars,
        Search
    }
}