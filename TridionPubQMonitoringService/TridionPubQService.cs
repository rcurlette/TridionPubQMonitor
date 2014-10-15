using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.ServiceProcess;
using System.Xml.Linq;
using Tridion.ContentManager.CoreService.Client;
using TridionPubQMonitoringService.CoreService;
using TridionPubQMonitoringService.Util;

// The Windows Service does the following:
// At the interval defined by the config variable 'TimeIntervalForService' it runs and:
//   - Gets the items in the Publish Queue for the past x seconds, defined by config var 'PubQItemsInLastXSeconds'
//   - Puts items in a list
//   - On next run, gets items again and checks if an item is still in the same publish phase as defined by config var 'PublishState'
//   - If an item is "stuck" there, then take the following action:
//      - Remove from the queue
//      - Send email that the item has been removed
//      - Restart publisher
//      - Wait again for next interval and then run the whole thing again

// Accounts it runs as:
//  - Core Service runs as the 'user' defined in the app config
//  - This service should be run with an account allowed to start / stop services

// Errors and logs
//  - Errors and info is logged to the Tridion Event Viewer

namespace TridionPubQMonitoringService
{
    public partial class TridionPubQMonitoringService : ServiceBase
    {
        List<PublishQueueItem> currentPublishQueueItems = new List<PublishQueueItem>();
        List<PublishQueueItem> oldPublishQueueItems = new List<PublishQueueItem>();
        readonly string PUBLISHER_SERVICE_NAME = "TcmPublisher";
        readonly string DO_NOT_PUBLISH = " PLEASE DO NOT PUBLISH THIS PAGE - HAS PUBLISHING ISSUE.  CONTACT SUPPORT.";
        Logger logger;

        public TridionPubQMonitoringService()
        {
            InitializeComponent();  // Windows Service thing
            logger = new Logger(eventLog);
        }

        // "Constructor" for the Service
        protected override void OnStart(string[] args)
        {
            StopPublisherService();
            StartPublisherService();

             logger.WriteEntry(
                String.Format(
                "Tridion Pub Queue Monitor Service Started with settings: Core Service User:'{0}', Runs every '{1}' seconds and gets Queue items from the last '{2}' seconds. If the item is in Publish state of '{3}' for 2 runs, it is removed and the Publisher service is restarted.  Also, an email is sent with subject {4} to {5} from {6} using SMTP server {7}.",
                ConfigurationManager.AppSettings["user"],
                ConfigurationManager.AppSettings["TimeIntervalForService"],
                ConfigurationManager.AppSettings["PubQItemsInLastXSeconds"],
                ConfigurationManager.AppSettings["PublishState"],
                ConfigurationManager.AppSettings["MailSubject"],
                ConfigurationManager.AppSettings["MailTo"],
                ConfigurationManager.AppSettings["MailFrom"],
                ConfigurationManager.AppSettings["smtpServer"]
                ));
            System.Timers.Timer timer = new System.Timers.Timer();

            if(ConfigurationManager.AppSettings["SendTestEmailOnStartup"].ToLower() == "true")
            {
                Mailer.SendTestMail();
            }

            // Run at the seconds define in the app config variable 'TimeIntervalForService'
            int serviceIntervalTime = Int32.Parse(ConfigurationManager.AppSettings["TimeIntervalForService"]);
            timer.Interval = serviceIntervalTime * 1000; // milliseconds
            timer.Elapsed += new System.Timers.ElapsedEventHandler(this.OnTimer);
            timer.Start();
        }

        protected override void OnStop()
        {
            logger.WriteEntry("Tridion Pub Q Monitor Stopped");
        }

        // The Main RUN event, ran every x seconds defined in 'TimeIntervalForService'
        private void OnTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            //string endpointName = "basicHttp_2013";
            //using (CoreServiceClient client = new CoreServiceClient(endpointName))
            //{

            CoreServiceClient client = TridionCoreServiceFactory.CreateCoreService();

            try
            {
                // Get Recent Queue Items and if item is in Queue for 2 iterations, remove it and email
                List<PublishQueueItem> recentPublishItems = GetPublishQueueItemsInLastPeriod(client);
                CopyCurrentQueueItems();
                AddQueueItemsToCurrentList(recentPublishItems);
                List<PublishQueueItem> stuckPubQItems = GetPubQueueItemsThatAreStuck();

                if (stuckPubQItems.Count > 0)
                {
                    logger.WriteEntry(String.Format("Tridion Publish Queue Monitor - Stuck in 'Rendering' phase are {0} items.  Begin removing from Publish Queue.", stuckPubQItems.Count));
                    RemoveItemsFromPublishQueue(client, stuckPubQItems);
                    StopPublisherService();
                    StartPublisherService();
                }

            } catch(Exception ex)
            {
                logger.WriteEntry(String.Format("Publish Queue Monitor ERROR - {0}, {1}, {2}", ex.Source, ex.Message, ex.StackTrace));
            }
            client.Close();
        }

        /// <summary>
        /// Get any items still in status 'Rendering' after the set wait period (~15 mins) and remove them
        /// </summary>
        /// <param name="client"></param>
        private void RemoveItemsFromPublishQueue(CoreServiceClient client, List<PublishQueueItem> stuckPubQItems)
        {
            foreach (var pubQItem in stuckPubQItems)
            {
                client.Delete(pubQItem.ID);
                RenameItem(client, pubQItem);
                logger.WriteEntry(String.Format("Publish Queue Monitor - Item stuck in Rendering State and removed.  {0}", pubQItem.ToString()));
                Mailer.SendMail(pubQItem);
            }
        }

        private void RenameItem(CoreServiceClient client, PublishQueueItem pubQItem)
        {
            if (ConfigurationManager.AppSettings["RenameItem"].ToLower() == "true")
            {
                try
                {
                    var item = client.Read(pubQItem.ItemUri, new ReadOptions());
                    string newTitle = item.Title + DO_NOT_PUBLISH;
                    client.CheckOut(item.Id, false, new ReadOptions());
                    if (item.IsEditable == true)
                    {
                        item.Title = newTitle;
                        client.Save(item, new ReadOptions());
                        client.CheckIn(item.Id, new ReadOptions());
                    }
                } catch(Exception ex)
                {
                    logger.WriteEntry("Cannot checkout and rename " + pubQItem.ItemUri + ", " + pubQItem.Title);
                    logger.WriteEntry("Error: " + String.Concat(ex.Source, ex.Message, ex.StackTrace));
                }
                
            }
        } 

        private List<PublishQueueItem> GetPubQueueItemsThatAreStuck()
        {
            List<PublishQueueItem> stuckItems = new List<PublishQueueItem>();
            foreach (var pubQItem in currentPublishQueueItems)
            {
                var match = this.oldPublishQueueItems.FirstOrDefault(i => i.ID == pubQItem.ID);
                if (match != null)
                {
                    stuckItems.Add(pubQItem);
                }
            }
            return stuckItems;
        }

        private void CopyCurrentQueueItems()
        {
            oldPublishQueueItems = currentPublishQueueItems;
        }

        private void AddQueueItemsToCurrentList(List<PublishQueueItem> recentPublishItems)
        {
            currentPublishQueueItems.Clear();
            foreach (PublishQueueItem publishItem in recentPublishItems)
            {
                currentPublishQueueItems.Add(publishItem);
            }
        }

        private List<PublishQueueItem> GetPublishQueueItemsInLastPeriod(CoreServiceClient client)
        {
            var filter = CreateFilter();

            XElement publishTransactions = client.GetSystemWideListXml(filter);
            List<PublishQueueItem> pubQueueItems = new List<PublishQueueItem>();
            foreach(XElement queueItem in publishTransactions.Descendants())
            {
                pubQueueItems.Add(new PublishQueueItem()
                {
                    ID = queueItem.Attribute("ID").Value,
                    Title = queueItem.Attribute("Title").Value,
                    ItemUri = queueItem.Attribute("ItemID").Value,
                    User = queueItem.Attribute("User").Value,
                    Publication = queueItem.Attribute("Publication").Value,
                    PublicationTarget = queueItem.Attribute("PublicationTarget").Value,
                    ItemPath = queueItem.Attribute("ItemPath").Value
                });
            }
            return pubQueueItems;
        }

        private static PublishTransactionsFilterData CreateFilter()
        {
            int timePeriod = Int32.Parse(ConfigurationManager.AppSettings["PubQItemsInLastXSeconds"]);
            var filter = new PublishTransactionsFilterData();
            filter.StartDate = System.DateTime.Now.AddSeconds(-(timePeriod));
            filter.EndDate = System.DateTime.Now;

            // Success and Failed are mostly used for testing when we don't have items stuck in rendering
            string publishState = ConfigurationManager.AppSettings["PublishState"];

            if (publishState == "Rendering")
            {
                filter.PublishTransactionState = PublishTransactionState.Rendering;
                //logger.WriteEntry("Rendering Phase");
            }
            else if (publishState == "Failed")
            {
                filter.PublishTransactionState = PublishTransactionState.Failed;
                //logger.WriteEntry("Failed Phase");
            }
            else if (publishState == "Success")
            {
                filter.PublishTransactionState = PublishTransactionState.Success;
                //logger.WriteEntry("Success Phase");
            }
            return filter;
        }


        // Below Start and Stop Service methods not working...
        private void StopPublisherService()
        {   
            if (ConfigurationManager.AppSettings["RestartPublisher"].ToLower() != "true")
            {
                return;
            }

            ServiceController sc = new ServiceController();
            
            sc.ServiceName = PUBLISHER_SERVICE_NAME;

            if (sc.Status == ServiceControllerStatus.Running)
            {
                try
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, new TimeSpan(0, 5, 0));  // wait 5 mins 
                    
                }
                catch (InvalidOperationException)
                {
                    throw new Exception("Could not stop the Publisher service.");
                }
            }
        }

        private void StartPublisherService()
        {
            if(ConfigurationManager.AppSettings["RestartPublisher"].ToLower() != "true")
            {
                return;
            }

            ServiceController sc = new ServiceController();
            sc.ServiceName = PUBLISHER_SERVICE_NAME;

            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                try
                {
                    sc.Start();
                    sc.WaitForStatus(ServiceControllerStatus.Running, new TimeSpan(0, 5, 0));  // wait 5 mins 
                    logger.WriteEntry("Tridion Pub Queue Monitor Restarted Publisher");
                }
                catch (InvalidOperationException)
                {
                    throw new Exception("Could not stop the Publisher service.");
                }
            }
        } 
    }
}
