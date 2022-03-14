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
        private static RemoteObjectManager _instance;
        public static RemoteObjectManager Instance { get
            {
                if (_instance is null)
                {
                    _instance = new RemoteObjectManager();
                }
                return _instance;
            } 
        }

        public event EventHandler<DownloadProgressChangedEventArgs> DownloadProgressChanged;

        private static readonly string baseUrl = "https://tamods-update.s3-ap-southeast-2.amazonaws.com";

        private readonly WebClient webClient = new WebClient();

        public RemoteObjectManager()
        {
            webClient.DownloadProgressChanged += WebClient_DownloadProgressChanged;
        }

        public string DownloadObjectAsString(string key)
        {
            return webClient.DownloadString($"{baseUrl}/{key}");
        }

        public async Task DownloadObjectToFile(string key, string filePath)
        {
            await webClient.DownloadFileTaskAsync($"{baseUrl}/{key}", filePath);
        }

        private void WebClient_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            DownloadProgressChanged?.Invoke(sender, e);
        }
    }
    
    class Updater
    {
        private SemaphoreSlim updateSemaphore;

        public Config.DebugConfig Debug;

        public string ConfigBasePath { get; set; }


        public Updater(string remoteBaseUrl, string configBasePath, Config.DebugConfig debugConfig)
        {
            ConfigBasePath = configBasePath;
            updateSemaphore = new SemaphoreSlim(1, 1);
            Debug = debugConfig;
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


        public enum UpdatePhase
        {
            NotUpdating,
            Preparing,
            Downloading,
            Extracting,
            Copying,
            Finalising
        }
        public class OnUpdatePhaseChangeEventArgs : EventArgs
        {
            public UpdatePhase Phase { get; set; }

            public OnUpdatePhaseChangeEventArgs(UpdatePhase phase)
            {
                Phase = phase;
            }
        }
        public event EventHandler<OnUpdatePhaseChangeEventArgs> OnUpdatePhaseChange;

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
            try
            {
                BroadcastUpdatePhase(UpdatePhase.Preparing);
                if (!tribesExePath.ToLower().EndsWith(".exe"))
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

                BroadcastUpdatePhase(UpdatePhase.Downloading);
                // Download compressed packages to temp folder
                List<string> archives = await DownloadPackages(toInstall, ".", (percentage) =>
                {
                    // Half the progress bar for download, then half for extract / copy
                    double pct = progressBarValue + 0.5 * percentage;
                    OnProgressTick?.Invoke(this, new OnProgressTickEventArgs(pct));
                });

                progressBarValue = 0.5;

                BroadcastUpdatePhase(UpdatePhase.Extracting);
                // Extract packages and delete the zips
                await ExtractArchives(archives, (idx, totalZips) =>
                {
                    // 25% of progress bar for extracts
                    double pct = progressBarValue + 0.25 * ((double)idx + 1) / totalZips;
                    OnProgressTick?.Invoke(this, new OnProgressTickEventArgs(pct));
                });

                progressBarValue = 0.75;

                BroadcastUpdatePhase(UpdatePhase.Copying);
                // For each package we downloaded, copy its files into the local dir / config dir / Tribes dir
                await CopyDownloadedPackages("./tmp", ".", tribesBasePath, (idx, totalPackages) =>
                {
                    // 25% of progress bar for package copy
                    double pct = progressBarValue + 0.25 * ((double)idx + 1) / totalPackages;
                    OnProgressTick?.Invoke(this, new OnProgressTickEventArgs(pct));
                });
                BroadcastUpdatePhase(UpdatePhase.Finalising);

                // Save the new installed package manifest
                UpdateInstalledPackageState(toInstall);

                // Delete temp directory
                Directory.Delete($"./tmp", true);

                BroadcastUpdatePhase(UpdatePhase.NotUpdating);
                // Raise finished event
                OnUpdateComplete?.Invoke(this, new EventArgs());
            } catch (Exception e)
            {
                // Reset update phase
                BroadcastUpdatePhase(UpdatePhase.NotUpdating);
                throw e;
            }
        }

        private string generatePackageDownloadLocation(string localPath, RemotePackage package)
        {
            string packageFileName = package.ObjectKey.Substring("package/".Length);
            return $"{localPath}/tmp/{packageFileName}";
        }

        private async Task<List<string>> DownloadPackages(List<RemotePackage> packages, string localPath, Action<double> packageDownloadProgressHandler)
        {
            var packageDownloadDetails = packages
                .Select((p, i) =>
                {
                    // Object keys are under package/
                    return new { Idx = i, ObjectKey = p.ObjectKey, DownloadLocation = generatePackageDownloadLocation(localPath, p) };
                });

            var downloadedArchives = new List<string>();
            int completedPackages = 0;
            // Event handler for download progress including progress of each package
            EventHandler<DownloadProgressChangedEventArgs> downloadHandler = (object sender, DownloadProgressChangedEventArgs e) =>
            {
                double baseCompletion = ((double)completedPackages) / packageDownloadDetails.Count();
                double nextBaseCompletion = ((double)completedPackages + 1) / packageDownloadDetails.Count();

                double completion = baseCompletion + (((double)e.ProgressPercentage) / 100) * (nextBaseCompletion - baseCompletion);
                packageDownloadProgressHandler?.Invoke(completion);
            };

            RemoteObjectManager.Instance.DownloadProgressChanged += downloadHandler;

            foreach (var p in packageDownloadDetails)
            {
                await RemoteObjectManager.Instance.DownloadObjectToFile(p.ObjectKey, p.DownloadLocation);
                downloadedArchives.Add(p.DownloadLocation);
                completedPackages++;
            }

            RemoteObjectManager.Instance.DownloadProgressChanged -= downloadHandler;

            return downloadedArchives;
        }

        private async Task ExtractArchives(List<string> toExtract, Action<int, int> onExtractComplete)
        {
            foreach (var a in toExtract.Select((a, i) => new { Idx = i, Archive = a }))
            {
                await Task.Run(() =>
                {
                    var dest = $"{Path.GetDirectoryName(a.Archive)}/{Path.GetFileNameWithoutExtension(a.Archive)}";
                    Directory.CreateDirectory(dest);
                    ZipFile.ExtractToDirectory(a.Archive, dest);
                    File.Delete(a.Archive);
                });
                onExtractComplete(a.Idx, toExtract.Count);
            }
        }

        private void CopyFile(string relativeFilename, string packageRoot, string localRoot, string gameBasePath)
        {
            string normalisedRelativeFilename = relativeFilename.Replace("/", "\\");

            string copyLocation;
            if (normalisedRelativeFilename.StartsWith("!CONFIG\\"))
            {
                copyLocation = $"{ConfigBasePath}\\{normalisedRelativeFilename.Replace("!CONFIG\\", "")}";
            } else if (normalisedRelativeFilename.StartsWith("!TRIBESDIR\\"))
            {
                copyLocation = $"{gameBasePath}\\{normalisedRelativeFilename.Replace("!TRIBESDIR\\", "")}";
            } else
            {
                copyLocation = $"{localRoot}\\{normalisedRelativeFilename}";
            }

            if (Debug.DisableCopyOnUpdate)
            {
                Console.WriteLine($"Copy disabled; would write file {normalisedRelativeFilename} to {copyLocation}");
            } else
            {
                // Create directory if required
                string dir = new FileInfo(copyLocation).Directory.FullName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Read-only more like whatever we damn well want
                if (File.Exists(copyLocation)) { 
                    File.SetAttributes(copyLocation, FileAttributes.Normal);
                }

                // Copy the file
                File.Copy($"{packageRoot}\\{normalisedRelativeFilename}", copyLocation, true);
            }
        }

        private async Task CopyDownloadedPackages(string tempRoot, string localRoot, string gameBasePath, Action<int, int> onPackageComplete)
        {
            var packageDirs = Directory.GetDirectories(tempRoot);

            foreach (var p in packageDirs.Select((d, i) => new { Idx = i, Directory = d }))
            {
                var packageRoot = Path.GetFullPath(p.Directory);
                // Recursively get every file in the current package
                foreach (var file in Directory.EnumerateFiles(p.Directory, "*", SearchOption.AllDirectories))
                {
                    await Task.Run(() =>
                    {
                        CopyFile(Path.GetFullPath(file).Remove(0, packageRoot.Length + 1), packageRoot, localRoot, gameBasePath);
                    });
                }
                onPackageComplete(p.Idx, packageDirs.Count());
            }
        }

        private void BroadcastUpdatePhase(UpdatePhase phase)
        {
            OnUpdatePhaseChange?.Invoke(this, new OnUpdatePhaseChangeEventArgs(phase));
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
