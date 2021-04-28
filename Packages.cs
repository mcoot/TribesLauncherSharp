using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TribesLauncherSharp
{
    class PackageException : Exception
    {
        public PackageException() : base() { }
        public PackageException(string message) : base(message) { }
        public PackageException(string message, Exception inner) : base(message, inner) { }
    }

    class SemanticVersionConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(SemanticVersion);

        public object ReadYaml(IParser parser, Type type)
        {
            var val = parser.Expect<Scalar>();
            try
            {
                SemanticVersion version = SemanticVersion.Parse(val.Value);
                return version;
            } catch (ArgumentException)
            {
                throw new YamlException(val.Start, val.End, "Expected a valid semantic version");
            }
        }

        public void WriteYaml(IEmitter emitter, object value, Type type)
        {
            var version = (SemanticVersion)value;
            emitter.Emit(new Scalar(version.ToString()));
        }
    }

    class RemotePackage
    {
        // Unique identifier for the package
        public string Id { get; set; }
        // User-facing package name
        public string DisplayName { get; set; }
        // Package description
        public string Description { get; set; }
        // Version of the package; used to determine whether a package requires updates
        public SemanticVersion Version { get; set; }
        // The key of the zipped package in S3
        public string ObjectKey { get; set; }
        // Whether the package is mandatory for TAMods
        public bool Required { get; set; }
        // A list of dependency packages which must be installed alongside this package
        public List<string> Dependencies { get; set; } = new List<string>();

        public InstalledPackage ToInstalledPackage()
        {
            return new InstalledPackage(Id, Version);
        }
    }

    class RemotePackageConfig
    {
        public List<RemotePackage> Packages { get; private set; }

        private static IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        public static RemotePackageConfig DownloadPackageConfig()
        {
            string rawData;
            try
            {
                rawData = RemoteObjectManager.DownloadObjectAsString("packageconfig.yaml");
            }
            catch (Exception ex)
            {
                throw new PackageException($"Failed to download package config: {ex.Message}", ex);
            }

            try
            {
                return deserializer.Deserialize<RemotePackageConfig>(rawData);
            }
            catch (Exception ex)
            {
                throw new PackageException($"Failed to parse package config: {ex.Message}", ex);
            }
        }
    }

    class InstalledPackage
    {
        // Unique identifier for the package, as per the remote PackageConfig
        public string Id { get; private set; }
        // Installed version of the package; used to determine whether a package requires updates
        public SemanticVersion Version { get; private set; }

        public InstalledPackage() { }

        public InstalledPackage(string id, SemanticVersion version)
        {
            Id = id;
            Version = version;
        }
    }

    class LocalPackage
    {
        public RemotePackage Remote { get; private set; }
        public InstalledPackage Local { get; private set; }

        public LocalPackage(RemotePackage remote, InstalledPackage local)
        {
            Remote = remote;
            Local = local;
        }

        public bool AvailableRemotely => Remote != null;

        public bool IsInstalled => Local != null;

        // Requires update if it's a required package or the package is installed and outdated
        public bool RequiresUpdate =>
            AvailableRemotely && ((!IsInstalled && Remote.Required) || (IsInstalled && Local.Version < Remote.Version));

        static SolidColorBrush NeedsUpdateBrush = new SolidColorBrush(Colors.Red);
        static SolidColorBrush InstalledBrush = new SolidColorBrush(Colors.Green);
        static SolidColorBrush AvailableBrush = new SolidColorBrush(Colors.Black);

        public SolidColorBrush DisplayColor
        {
            get
            {
                if (RequiresUpdate)
                {
                    return NeedsUpdateBrush;
                } else if (IsInstalled)
                {
                    return InstalledBrush;
                } else
                {
                    return AvailableBrush;
                }
            }
        }
    }

    class InstalledPackageState
    {
        private static string localFile = "packagestate.yaml";

        public List<InstalledPackage> Packages { get; private set; }

        private static ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionConverter())
            .Build();

        private static IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .WithTypeConverter(new SemanticVersionConverter())
            .IgnoreUnmatchedProperties()
            .Build();

        public InstalledPackageState()
        {
            Packages = new List<InstalledPackage>();
        }

        public static InstalledPackageState Load()
        {
            if (!File.Exists(localFile))
            {
                // No state, nothing installed
                return new InstalledPackageState();
            }

            try
            {
                string yaml = File.ReadAllText("packagestate.yaml");
                return deserializer.Deserialize<InstalledPackageState>(yaml);
            } catch (Exception ex)
            {
                throw new PackageException($"Failed to load installed package state: ${ex.Message}", ex);
            }
        }

        public void Save()
        {
            try
            {
                var yaml = serializer.Serialize(this);
                File.WriteAllText(localFile, yaml);
            }
            catch (Exception ex)
            {
                throw new PackageException($"Failed to save installed package state: ${ex.Message}", ex);
            }
        }

        public void MarkInstalled(InstalledPackage installed)
        {
            InstalledPackage matching = Packages.Where((p) => p.Id == installed.Id).FirstOrDefault();
            if (matching != null)
            {
                // Remove outdated entry
                MarkUninstalled(installed.Id);
            }
            Packages.Add(installed);
        }

        public void MarkUninstalled(string packageId)
        {
            Packages.RemoveAll((p) => p.Id == packageId);
        }

        public static void Clear()
        {
            if (File.Exists(localFile))
            {
                File.Delete(localFile);
            }
        }
    }

    class PackageState
    {
        public List<LocalPackage> LocalPackages { get; private set; } = new List<LocalPackage>();

        private LocalPackage GetById(string packageId) => 
            LocalPackages.Where((p) => p.Remote.Id == packageId).FirstOrDefault();

        // Common logic for determining deps by id which need update/install
        private IEnumerable<LocalPackage> GetPackagesByIdRelevantForInstallOrUpdate(IEnumerable<string> ids) => 
            ids.Select((id) => GetById(id)).Where((p) => p != null && (!p.IsInstalled || p.RequiresUpdate));

        public List<LocalPackage> GetPackageDependenciesForInstall(LocalPackage package) =>
            GetPackagesByIdRelevantForInstallOrUpdate(package.Remote.Dependencies).ToList();

        public List<LocalPackage> PackagesRequiringUpdate()
        {
            // Direct packages needing update...
            List<LocalPackage> toUpdate = LocalPackages.Where((p) => p.RequiresUpdate).ToList();

            // We must also update any packages we don't currently have installed, that are dependencies of packages we are updating
            // (e.g. on update, a new dep has been added to an existing package)
            HashSet<string> dedupedDepIdsToUpdate = new HashSet<string>(toUpdate.SelectMany((p) => p.Remote.Dependencies));
            toUpdate.AddRange(GetPackagesByIdRelevantForInstallOrUpdate(dedupedDepIdsToUpdate));

            return toUpdate;
        }

        public bool UpdateRequired() => PackagesRequiringUpdate().Count > 0;

        public static PackageState Load()
        {
            RemotePackageConfig remote = RemotePackageConfig.DownloadPackageConfig();
            InstalledPackageState local = InstalledPackageState.Load();

            // Match local packages to remote ones
            List<LocalPackage> result =
                (from remotePackage in remote.Packages
                 join localPackage in local.Packages on remotePackage.Id equals localPackage.Id
                 select new LocalPackage(remotePackage, localPackage)).ToList();

            HashSet<string> matchedIds = new HashSet<string>(result.Select((p) => p.Local.Id));

            // Find remote packages that don't exist locally
            result.AddRange(remote.Packages
                .Where((p) => !matchedIds.Contains(p.Id))
                .Select((p) => new LocalPackage(p, null)));

            // (Don't care about local packages that don't exist remotely)

            return new PackageState
            {
                LocalPackages = result,
            };
        }
        
    }
}
