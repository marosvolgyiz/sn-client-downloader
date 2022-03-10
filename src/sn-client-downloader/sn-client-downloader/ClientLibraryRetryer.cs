using SenseNet.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sn_client_downloader
{
    internal class ClientLibraryRetryer
    {
        public static Config? Configuration { get; set; }
        public static NLog.Logger? Logger { get; set; }
        public static List<Content> Query(string queryText, string[] select, QuerySettings settings, ServerContext? server )
        {
            if(server == null)
                throw new Exception("Server context is null in ClientLibraryRetryer");
            if (Configuration == null)
                throw new Exception("Configuration is null in ClientLibraryRetryer");
            if (Logger == null)
                throw new Exception("Logger is null in ClientLibraryRetryer");
            var retryMilisec = Configuration.RetryMillisecond??200;
            var retryCount = Configuration.RetryCount??3;
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    var filesTask = Content.QueryAsync(queryText, select, null, settings, server);
                    filesTask.Wait();
                    if(filesTask.Result != null)
                    {
                        return filesTask.Result.ToList();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Retryer query failed:" + queryText + ", ExMessage:" + ex.Message);
                    System.Threading.Thread.Sleep(retryMilisec);
                }
            }
            return new List<Content>();
        }
    }
}
