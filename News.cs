using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace TribesLauncherSharp
{
    class News
    {
        public string HirezLoginServerHost { get; set; }
        public string CommunityLoginServerHost { get; set; }

        public string LatestLauncherVersion { get; set; }
        public string LauncherUpdateLink { get; set; }

        public static News DownloadNews(string newsUrl)
        {
            using (var wc = new WebClient())
            {
                var rawData = wc.DownloadData(newsUrl);
            }

            var n = new News();
            return n;
        }
    }
}
