using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace TribesLauncherSharp
{
    sealed class RemoteObjectManager
    {
        private static readonly string baseUrl = "https://tamods-update.s3-ap-southeast-2.amazonaws.com";

        private static readonly HttpClient httpClient = new HttpClient();
        private static readonly WebClient webClient = new WebClient();

        public static string DownloadObjectAsString(string key)
        {
            return webClient.DownloadString($"{baseUrl}/{key}");
        }

        public static async Task DownloadObjectToFile(string key, string filePath)
        {
            var response = await httpClient.GetAsync(new Uri($"{baseUrl}/{key}"));
            using (Stream memStream = await response.Content.ReadAsStreamAsync())
            {
                using (Stream fileStream = File.Create(filePath))
                {
                    await memStream.CopyToAsync(fileStream);
                }
            }
        }
    }
    
    class Updater
    {
        private SemaphoreSlim updateSemaphore;

        public string ConfigBasePath { get; set; }


        public Updater(string remoteBaseUrl, string configBasePath)
        {
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

        private void UpdateInstalledPackageState(List<RemotePackage> justInstalled)
        {
            var state = InstalledPackageState.Load();
            foreach (RemotePackage installed in justInstalled) {
                state.MarkInstalled(installed.ToInstalledPackage());
            }
            state.Save();
        }

        private async Task InstallPackages(List<RemotePackage> toInstall, string tribesExePath)
        {
            if (!tribesExePath.ToLower().EndsWith("tribesascend.exe"))
            {
                throw new Exception($"Invalid tribes path {tribesExePath}");
            }

            // Exe is <basepath>/Binaries/Win32/TribesAscend.exe
            // So the directory two levels up is the base path of the install
            string tribesBasePath = Path.Combine(Path.GetDirectoryName(tribesExePath), "..", "..");

            // Clear temp directory if it exists
            if (Directory.Exists("./tmp"))
            {
                Directory.Delete($"./tmp", true);
            }
            Directory.CreateDirectory("./tmp");

            double progressBarValue = 0;

            // Download compressed packages to temp folder
            List<string> archives = await DownloadPackages(toInstall, ".", (batchIdx, totalBatches) =>
            {
                // Half the progress bar for download, then half for extract / copy
                double pct = progressBarValue + 0.5 * ((double)batchIdx + 1) / totalBatches;
                OnProgressTick?.Invoke(this, new OnProgressTickEventArgs(pct));
            });

            progressBarValue = 0.5;

            // Extract packages and delete the zips
            ExtractArchives(archives, (idx, totalZips) =>
            {
                // 25% of progress bar for extracts
                double pct = progressBarValue + 0.25 * ((double)idx + 1) / totalZips;
                OnProgressTick?.Invoke(this, new OnProgressTickEventArgs(pct));
            });

            progressBarValue = 0.75;

            // For each package we downloaded, copy its files into the local dir / config dir / Tribes dir
            CopyDownloadedPackages("./tmp", ".", tribesBasePath, (idx, totalPackages) =>
            {
                // 25% of progress bar for package copy
                double pct = progressBarValue + 0.25 * ((double)idx + 1) / totalPackages;
                OnProgressTick?.Invoke(this, new OnProgressTickEventArgs(pct));
            });

            // Save the new installed package manifest
            UpdateInstalledPackageState(toInstall);

            // Delete temp directory
            Directory.Delete($"./tmp", true);

            // Raise finished event
            OnUpdateComplete?.Invoke(this, new EventArgs());
        }

        private string generatePackageDownloadLocation(string localPath, RemotePackage package)
        {
            string packageFileName = package.ObjectKey.Substring("package/".Length);
            return $"{localPath}/tmp/{packageFileName}";
        }

        private async Task<List<String>> DownloadPackages(List<RemotePackage> packages, string localPath, Action<int, int> onPackageDownloadComplete)
        {
            // Download in parallel if we're downloading a lot of packages, at most 10 packages at a time
            var batchSize = packages.Count > 10 ? 10 : 1;
            var packageBatches = packages
                .Select((p, i) =>
                {
                    // Object keys are under package/
                    return new { Idx = i, ObjectKey = p.ObjectKey, DownloadLocation = generatePackageDownloadLocation(localPath, p) };
                })
                .Batch(batchSize)
                .Select((b, i) => new { Idx = i, Batch = b });

            var downloadedArchives = new List<String>();
            foreach (var batch in packageBatches)
            {
                var batchDownloads = await Task.WhenAll(batch.Batch.Select(async (p) =>
                {
                    await RemoteObjectManager.DownloadObjectToFile(p.ObjectKey, p.DownloadLocation);
                    onPackageDownloadComplete(batch.Idx, packageBatches.Count());
                    return p.DownloadLocation;
                }));
                foreach (var dl in batchDownloads)
                {
                    downloadedArchives.Add(dl);
                }
            }
            return downloadedArchives;
        }

        private void ExtractArchives(List<string> toExtract, Action<int, int> onExtractComplete)
        {
            foreach (var a in toExtract.Select((a, i) => new { Idx = i, Archive = a }))
            {
                var dest = $"{Path.GetDirectoryName(a.Archive)}/{Path.GetFileNameWithoutExtension(a.Archive)}";
                Directory.CreateDirectory(dest);
                ZipFile.ExtractToDirectory(a.Archive, dest);
                File.Delete(a.Archive);
                onExtractComplete(a.Idx, toExtract.Count);
            }
        }

        private void CopyFile(string relativeFilename, string tempRoot, string localRoot, string gameBasePath)
        {
            string copyLocation;
            if (relativeFilename.StartsWith("!CONFIG/"))
            {
                copyLocation = $"{ConfigBasePath}/{relativeFilename.Replace("!CONFIG/", "")}";
            } else if (relativeFilename.StartsWith("!TRIBESDIR/"))
            {
                copyLocation = $"{gameBasePath}/{relativeFilename.Replace("!TRIBESDIR/", "")}";
            } else
            {
                copyLocation = $"{localRoot}/{relativeFilename}";
            }

            Console.WriteLine($"SAVING FILE TO {copyLocation}");

            //// Create directory if required
            //string dir = new FileInfo(copyLocation).Directory.FullName;
            //if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            //// Copy the file
            //File.Copy($"{tempRoot}/{relativeFilename}", copyLocation, true);
        }

        private void CopyDownloadedPackages(string tempRoot, string localRoot, string gameBasePath, Action<int, int> onPackageComplete)
        {
            var packageDirs = Directory.GetDirectories(tempRoot);

            foreach (var p in packageDirs.Select((d, i) => new { Idx = i, Directory = d }))
            {
                var packageRoot = Path.GetFullPath(p.Directory);
                // Recursively get every file in the current package
                foreach (var file in Directory.EnumerateFiles(p.Directory, "*", SearchOption.AllDirectories))
                {
                    CopyFile(Path.GetFullPath(file).Remove(0, packageRoot.Length), tempRoot, localRoot, gameBasePath);
                }
                onPackageComplete(p.Idx, packageDirs.Count());
            }
        }

        public async Task PerformUpdate(PackageState packageState, string tribesExePath)
        {
            // TribesExePath definitely shouldn't be passed in from the UI layer as a param like this
            // but I'm retrofitting it and can't be fucked

            // Only one update or install may occur at once, if a second is attempted it will do nothing
            bool acquiredLock = await updateSemaphore.WaitAsync(0);
            if (!acquiredLock) return;

            try
            {
                List<RemotePackage> packages =
                packageState.PackagesRequiringUpdate().Select((p) => p.Remote).ToList();

                await InstallPackages(packages, tribesExePath);
            }
            finally
            {
                updateSemaphore.Release();
            }
        }

        public async Task InstallNewPackage(PackageState packageState, LocalPackage package, string tribesExePath)
        {
            // Only one update or install may occur at once, if a second is attempted it will do nothing
            bool acquiredLock = await updateSemaphore.WaitAsync(0);
            if (!acquiredLock) return;

            try
            {
                List<RemotePackage> packages = new List<RemotePackage>();
                packages.Add(package.Remote);
                // Ensure dependencies are also installed
                packages.AddRange(packageState.GetPackageDependenciesForInstall(package).Select((p) => p.Remote));
                await InstallPackages(packages, tribesExePath);
            }
            finally
            {
                updateSemaphore.Release();
            }
        }

        public bool IsUpdateInProgress() => updateSemaphore.CurrentCount == 0;

        #region Ubermenu Specific Handling
        public bool ConfigUsesUbermenu()
        {
            if (!File.Exists($"{ConfigBasePath}/config.lua"))
            {
                return false;
            }

            var configLines = File.ReadAllLines($"{ConfigBasePath}/config.lua");
            foreach (var line in configLines)
            {
                if (line.Trim() == "require(\"presets/ubermenu/preset\")" || line.Trim() == "require('presets/ubermenu/preset')")
                {
                    return true;
                }
            }

            return false;
        }

        public void SetupUbermenuPreset()
        {
            File.AppendAllLines($"{ConfigBasePath}/config.lua", new string[] {
                "",
                "require(\"presets/ubermenu/preset\")"
            });
        }

        public void BackupUbermenuConfig()
        {
            if (!File.Exists($"{ConfigBasePath}/presets/ubermenu/config/config.lua")) return;
            File.Copy($"{ConfigBasePath}/presets/ubermenu/config/config.lua", "ubermenu_config_backup.lua", true);
        }

        public void RestoreUbermenuConfig()
        {
            if (!File.Exists("ubermenu_config_backup.lua")) return;
            File.Copy("ubermenu_config_backup.lua", $"{ConfigBasePath}/presets/ubermenu/config/config.lua", true);
            File.Delete("ubermenu_config_backup.lua");
        }

        #endregion
    }
}
