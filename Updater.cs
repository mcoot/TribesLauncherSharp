using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TribesLauncherSharp
{
    class Updater
    {
        private SemaphoreSlim updateSemaphore;

        public string RemoteBaseUrl { get; set; }
        public string LocalBasePath { get; set; }
        public string ConfigBasePath { get; set; }

        public Updater(string remoteBaseUrl, string localBasePath, string configBasePath)
        {
            RemoteBaseUrl = remoteBaseUrl;
            LocalBasePath = localBasePath;
            ConfigBasePath = configBasePath;
            updateSemaphore = new SemaphoreSlim(1, 1);
        }

        public event EventHandler OnUpdateComplete;

        public class OnProgressTickEventArgs : EventArgs
        {
            public double Proportion { get; set; }

            public OnProgressTickEventArgs(double proportion)
            {
                Proportion = proportion;
            }
        }
        public event EventHandler<OnProgressTickEventArgs> OnProgressTick;

        public static bool IsUriAccessible(Uri uri)
        {
            var result = true;

            HttpWebResponse response = null;
            var request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "HEAD";

            try
            {
                response = (HttpWebResponse)request.GetResponse();
            }
            catch (WebException)
            {
                result = false;
            }
            finally
            {
                if (response != null) response.Close();
            }

            return result;
        }

        private static Dictionary<string, double> ReadManifest(XElement manifestDoc) 
            => manifestDoc
            .Descendants("files")
            .Where((e) => (string) e.Attribute("channel") == "stable")
            .Descendants("file")
            .ToDictionary(e => e.Value, e => (double)e.Attribute("version"));

        private static Dictionary<string, double> DiffManifests(Dictionary<string, double> oldManifest, Dictionary<string, double> newManifest)
            => newManifest
            .Where(f => !oldManifest.ContainsKey(f.Key) || oldManifest[f.Key] < f.Value)
            .ToDictionary(f => f.Key, f => f.Value);
        
        private Dictionary<string, double> GetFilesNeedingUpdate()
        {
            // Read remote/local manifests to find diff
            var localManifest = new Dictionary<string, double>();
            if (File.Exists($"{LocalBasePath}/version.xml"))
            {
                localManifest = ReadManifest(XElement.Load($"{LocalBasePath}/version.xml"));
            }

            var remoteManifest = ReadManifest(XElement.Load($"{RemoteBaseUrl}/version.xml"));

            return DiffManifests(localManifest, remoteManifest);
        }

        private async Task PerformUpdateInternal()
        {
            Dictionary<string, double> filesToDownload = GetFilesNeedingUpdate();

            // Download to temp folder
            using (var wc = new WebClient())
            {
                foreach (var f in filesToDownload.Keys.Select((x, i) => new { Idx = i, Filename = x }))
                {
                    // Create directory if required
                    int dirSplitPos = Math.Max(0, Math.Max(f.Filename.LastIndexOf('/'), f.Filename.LastIndexOf('\\')));
                    string relativeDir = f.Filename.Substring(0, dirSplitPos);
                    string dir = $"{LocalBasePath}/tmp/{relativeDir}";
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    // Download the file
                    await wc.DownloadFileTaskAsync(new Uri($"{RemoteBaseUrl}/{f.Filename}"), $"{LocalBasePath}/tmp/{f.Filename}");
                    double pct = ((double)f.Idx + 1) / filesToDownload.Count;
                    OnProgressTick?.Invoke(this, new OnProgressTickEventArgs(pct));
                }
            }

            // Copy files out
            foreach (string filename in filesToDownload.Keys)
            {
                string copyLocation;
                if (filename.StartsWith("!CONFIG/"))
                {
                    copyLocation = $"{ConfigBasePath}/{filename.Replace("!CONFIG/", "")}";
                }
                else
                {
                    copyLocation = $"{LocalBasePath}/{filename}";
                }

                // Create directory if required
                string dir = new FileInfo(copyLocation).Directory.FullName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Copy the file
                File.Copy($"{LocalBasePath}/tmp/{filename}", copyLocation, true);
            }

            // Delete temp directory
            Directory.Delete($"{LocalBasePath}/tmp", true);

            // Download the current version manifest
            // TODO: THIS
            var newLocalManifest = XElement.Load($"{RemoteBaseUrl}/version.xml");
            newLocalManifest.Save($"{LocalBasePath}/version.xml");

            // Raise finished event
            OnUpdateComplete?.Invoke(this, new EventArgs());
        }

        public async Task PerformUpdate()
        {
            // Only one update may occur at once, if a second is attempted it will do nothing
            bool acquiredLock = await updateSemaphore.WaitAsync(0);
            if (!acquiredLock) return;

            try
            {
                await PerformUpdateInternal();
            }
            finally
            {
                updateSemaphore.Release();
            }
        }

        public bool IsUpdateRequired() => GetFilesNeedingUpdate().Count > 0;

        public bool IsUpdateInProgress() => updateSemaphore.CurrentCount == 0;
    }
}
