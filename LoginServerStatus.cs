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
    class LoginServerStatus
    {
        public int? PlayersOnline { get; private set; } = null;
        public int? ServersOnline { get; private set; } = null;

        public void Update(string loginServerHost, int loginServerWebPort = 9080)
        {
            System.Diagnostics.Debug.WriteLine("Updatarino");
            using (var wc = new WebClient())
            {
                string rawData;
               
                try
                {
                    rawData = wc.DownloadString($"http://{loginServerHost}:{loginServerWebPort}/status");
                } catch (WebException)
                {
                    // Failed to get from server status API 
                    Clear();
                    return;
                }
                
                dynamic data = null;
                try
                {
                    data = JObject.Parse(rawData);
                }
                catch (JsonReaderException)
                {
                    // Failed to get data, just clear
                    Clear();
                    return;
                }

                try
                {
                    PlayersOnline = data["online_players"];
                    PlayersOnline = data["online_servers"];
                }
                catch (RuntimeBinderException)
                {
                    // Failed to parse response, just clear
                    Clear();
                    return;
                }
            }
        }

        public void Clear()
        {
            PlayersOnline = null;
            ServersOnline = null;
        }
    }
}
