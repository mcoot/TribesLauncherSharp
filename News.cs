using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TribesLauncherSharp
{
    class News
    {
        public class NewsParsingException : Exception
        {
            public NewsParsingException() : base() { }
            public NewsParsingException(string message) : base(message) { }
            public NewsParsingException(string message, Exception inner) : base(message, inner) { }
        }

        public string HirezLoginServerHost { get; set; } = "23.239.17.171";
        public string CommunityLoginServerHost { get; set; } = "ta.kfk4ever.com";

        public string LatestLauncherVersion { get; set; } = "1.0.0.0";
        public string LauncherUpdateLink { get; set; } = "https://raw.githubusercontent.com/mcoot/tamodsupdate/release/news.json";


        public void DownloadNews(string newsUrl)
        {
            // Allow TLS 1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12;

            using (var wc = new WebClient())
            {
                var rawData = wc.DownloadString(newsUrl);
                dynamic data = null;
                try
                {
                    data = JObject.Parse(rawData);
                } catch (JsonReaderException ex)
                {
                    throw new NewsParsingException("Failed to parse update news data", ex);
                }

                try
                {
                    LatestLauncherVersion = data.latestLauncherSharpVersion;
                    LauncherUpdateLink = data.launcherUpdateLink;
                    HirezLoginServerHost = data.masterServers.hirezMasterServerHost;
                    CommunityLoginServerHost = data.masterServers.unofficialMasterServerHost;
                } catch (RuntimeBinderException ex)
                {
                    throw new NewsParsingException("Missing expected fields in update news data", ex);
                }
            }
        }
    }
}
