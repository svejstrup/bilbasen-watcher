# Bilbasen-Watcher
This project uses Azure Durable functions to periodically scrape Bilbasen for desired vehicles.

Azure Table Storage is used as database and allows tracking the price over time and helps determine the right price for the specific car you are looking for.

## Custom search and notification
Custom search and notification criteria can be set up by adding rows to the 'Search' table. 
Notification Emails can be sent using SendGrid once specified criteria are met.