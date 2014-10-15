using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TridionPubQMonitoringService
{
    public class PublishQueueItem
    {
        public string ID { get; set; }
        public string ItemUri { get; set; }
        public string Title { get; set; }
        public string User { get; set; }
        public string Publication { get; set; }
        public string ItemPath { get; set; }
        public string PublicationTarget { get; set; }

        public override string ToString()
        {
            return String.Format("User {0} published {1} with ID:{2} located at {3} published to {4}), ", User, Title, ItemUri, ItemPath, PublicationTarget);
        }
    }
}
