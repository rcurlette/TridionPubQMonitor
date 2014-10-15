using System;
using System.Configuration;
using System.Net;
using System.Net.Mail;
using System.Xml;

namespace TridionPubQMonitoringService
{
    public static class Mailer
    {
        public static void SendTestMail()
        {
            PublishQueueItem qItem = new PublishQueueItem()
            {
                ID = "0",
                PublicationTarget = "Test",
                ItemPath = "/one/two",
                ItemUri = "tcm:0-0-0",
                Publication = "SamplePub",
                Title = "some Title",
                User = "some user"
            };

            SendMail(qItem);
        }

        public static void SendMail(PublishQueueItem publishQueueItem)
        {
            try
            {
                if (ConfigurationManager.AppSettings["SendEmailOnError"].ToLower() != "true")
                {
                    return;
                }

                string body = String.Format("Publish Queue Monitor - Item stuck in Rendering State and removed.  {0}", publishQueueItem.ToString());
                MailMessage mail = new MailMessage();
                if (ConfigurationManager.AppSettings["MailTo"].Contains(","))
                {
                    string[] to = ConfigurationManager.AppSettings["MailTo"].Split(',');
                    foreach (var toPerson in to)
                    {
                        mail.To.Add(toPerson);
                    }
                }
                else
                {
                    mail.To.Add(ConfigurationManager.AppSettings["MailTo"]);
                }

                mail.From = new MailAddress(ConfigurationManager.AppSettings["MailFrom"]);
                mail.Subject = ConfigurationManager.AppSettings["MailSubject"];
                mail.Body = body;
                mail.IsBodyHtml = false;

                SendMail(mail);
            } catch(Exception ex)
            {
                throw;  // log later
            }
        }

        private static void SendMail(MailMessage mail)
        {
            if (ConfigurationManager.AppSettings["SendEmailOnError"].ToLower() != "true")
            {
                return;
            }

            try
            {
                string smtpServer = ConfigurationManager.AppSettings["smtpServer"];
                string smtpPort = ConfigurationManager.AppSettings["smtpPort"];
                if (smtpPort == "")
                {
                    smtpPort = "25";
                }

                string smtpUser = ConfigurationManager.AppSettings["smtpUser"];
                string smtpPw = ConfigurationManager.AppSettings["smtpPw"];

                var smtpClient = new SmtpClient(smtpServer, Int32.Parse(smtpPort));
                if (smtpUser != "")
                {
                    smtpClient.Credentials = new NetworkCredential(smtpUser, smtpPw);
                }

                if (ConfigurationManager.AppSettings["enableSSL"] == "true")
                {
                    smtpClient.EnableSsl = true;
                }

                smtpClient.Send(mail);
            } catch(Exception ex)
            {
                throw;  
            }
        }
    }
}
