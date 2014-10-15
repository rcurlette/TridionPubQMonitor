using System;
using System.Diagnostics;

namespace TridionPubQMonitoringService.Util
{
    public class Logger
    {
        private EventLog _eventLog;
        readonly string EVENT_LOG_NAME = "Tridion";
        readonly string TOOL_NAME = "Tridion Publish Queue Monitoring";
        public Logger(EventLog eventLogInstance)
        {
            _eventLog = eventLogInstance;
            try
            {
                if (!System.Diagnostics.EventLog.SourceExists(EVENT_LOG_NAME))
                {
                    System.Diagnostics.EventLog.CreateEventSource(
                        TOOL_NAME, EVENT_LOG_NAME);
                }
                _eventLog.Source = TOOL_NAME;
                _eventLog.Log = EVENT_LOG_NAME;
            } catch(Exception ex)
            {
                _eventLog = null;
                // Silence it
            }
        }

        public void WriteEntry(string logMessage)
        {
            if(_eventLog != null)
            {
                _eventLog.WriteEntry(logMessage);
            }
        }
    }
}
