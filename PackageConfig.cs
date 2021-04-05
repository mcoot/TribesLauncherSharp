using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

    class Package
    {
        // Unique identifier for the package
        public string Id { get; private set; }
        // User-facing package name
        public string DisplayName { get; private set; }
        // Package description
        public string Description { get; private set; }
        // Version of the package; used to determine whether a package requires updates
        public SemanticVersion Version { get; private set; }
        // The key of the zipped package in S3
        public string ObjectKey { get; private set; }
        // Whether the package is mandatory for TAMods
        public bool Required { get; private set; }
    }

    class PackageConfig
    {
        public List<Package> Packages { get; private set; }

        private static IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .IgnoreUnmatchedProperties()
            .Build();

        public static PackageConfig DownloadPackageConfig()
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
                return deserializer.Deserialize<PackageConfig>(rawData);
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
    }

    class InstalledPackageState
    {
        private static string localFile = "packagestate.yaml";

        public List<InstalledPackage> Packages { get; private set; }

        private static ISerializer serializer = new SerializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
            .Build();

        private static IDeserializer deserializer = new DeserializerBuilder()
            .WithNamingConvention(new CamelCaseNamingConvention())
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

        public static void Clear()
        {
            if (File.Exists(localFile))
            {
                File.Delete(localFile);
            }
        }
    }
}
