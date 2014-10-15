 The Pub Queue Monitor Windows Service does the following:
 At the interval defined by the config variable 'TimeIntervalForService' it runs and:
   - Gets the items in the Publish Queue for the past x seconds, defined by config var 'PubQItemsInLastXSeconds'
   - Puts items in a list
   - On next run, gets items again and checks if an item is still in the same publish phase as defined by config var 'PublishState'
   - If an item is "stuck" there, then take the following action:
      - Remove from the queue
      - Send email that the item has been removed
      - Restart publisher
      - Wait again for next interval and then run the whole thing again

 Accounts it runs as:
  - Core Service runs as the 'user' defined in the app config
  - This service should be run with an account allowed to start / stop services

 Errors and logs
  - Errors and info is logged to the Tridion Event Viewer
