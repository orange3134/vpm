using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AvatarNamecard.AvatarPackage
{
    /// <summary>Extracts only a fixed set of files; archive paths are never used as filesystem paths.</summary>
    public sealed class AvatarPackageArchive : IDisposable
    {
        public const long MaxBundleBytes = 512L * 1024 * 1024;
        public const int MaxJsonBytes = 4 * 1024 * 1024;
        public AvatarPackageManifest Manifest { get; private set; }
        public AvatarDefinition Definition { get; private set; }
        public string BundlePath { get; private set; }
        private string directory;

        public static AvatarPackageArchive Open(string path, string cacheRoot, string target)
        {
            var result = new AvatarPackageArchive();
            try
            {
                using var file = File.OpenRead(path);
                if (file.Length > MaxBundleBytes + 2 * MaxJsonBytes) throw new InvalidDataException("Avatar package exceeds the size limit.");
                using var zip = new ZipArchive(file, ZipArchiveMode.Read);
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in zip.Entries)
                {
                    if (!names.Add(entry.FullName) || (entry.FullName != "manifest.json" && entry.FullName != "avatar.json" && entry.FullName != "avatar.bundle"))
                        throw new InvalidDataException("Unexpected or duplicate avatar package entry: " + entry.FullName);
                }
                if (names.Count != 3) throw new InvalidDataException("The avatar package is incomplete.");
                result.Manifest = JsonUtility.FromJson<AvatarPackageManifest>(ReadText(zip.GetEntry("manifest.json")));
                ValidateManifest(result.Manifest, target);
                var definitionText = ReadText(zip.GetEntry("avatar.json"));
                if (Hash(Encoding.UTF8.GetBytes(definitionText)) != result.Manifest.definitionSha256)
                    throw new InvalidDataException("Avatar settings checksum mismatch.");
                result.Definition = JsonUtility.FromJson<AvatarDefinition>(definitionText);
                AvatarDefinitionValidator.Validate(result.Definition);
                var bundle = zip.GetEntry("avatar.bundle");
                if (bundle.Length != result.Manifest.bundleSize || bundle.Length <= 0 || bundle.Length > MaxBundleBytes)
                    throw new InvalidDataException("Invalid avatar bundle size.");
                result.directory = Path.Combine(cacheRoot, "anc-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(result.directory);
                result.BundlePath = Path.Combine(result.directory, "avatar.bundle");
                using (var source = bundle.Open())
                using (var destination = File.Create(result.BundlePath)) CopyBounded(source, destination, bundle.Length);
                using (var stream = File.OpenRead(result.BundlePath))
                    if (Hash(stream) != result.Manifest.bundleSha256) throw new InvalidDataException("Avatar bundle checksum mismatch.");
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public static void ValidateManifest(AvatarPackageManifest manifest, string target)
        {
            if (manifest == null || manifest.format != "avatar-namecard" || manifest.formatVersion != 1)
                throw new InvalidDataException("Unsupported avatar package format. Update the exporter/app.");
            if (manifest.target != target)
                throw new InvalidDataException("This avatar was exported for " + manifest.target + ". Export for " + target + ".");
            if (manifest.renderPipeline != "BuiltIn" || manifest.prefab != "avatar" || manifest.bundle != "avatar.bundle")
                throw new InvalidDataException("Unsupported avatar rendering contract.");
            // Limit to the 2022.3 family; individual patch/platform combinations still require device validation.
            if (manifest.unityVersion == null || !manifest.unityVersion.StartsWith("2022.3.", StringComparison.Ordinal))
                throw new InvalidDataException("Re-export this avatar using a supported Unity 2022.3 editor.");
            if (!IsHash(manifest.bundleSha256) || !IsHash(manifest.definitionSha256))
                throw new InvalidDataException("Missing avatar package checksums.");
        }

        public static string CurrentTarget => Application.platform == RuntimePlatform.IPhonePlayer ? "iOS"
            : Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer ? "StandaloneOSX"
            : Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer ? "StandaloneWindows64"
            : "Unsupported";

        public static string Hash(byte[] bytes) { using var stream = new MemoryStream(bytes, false); return Hash(stream); }
        public static string Hash(Stream stream) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        private static bool IsHash(string text) => text != null && text.Length == 64 && text.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f');
        private static string ReadText(ZipArchiveEntry entry)
        {
            if (entry.Length > MaxJsonBytes) throw new InvalidDataException("Avatar metadata exceeds the size limit.");
            using var stream = entry.Open();
            using var bytes = new MemoryStream();
            CopyBounded(stream, bytes, entry.Length);
            return new UTF8Encoding(false, true).GetString(bytes.ToArray());
        }
        private static void CopyBounded(Stream source, Stream destination, long expected)
        {
            var buffer = new byte[81920];
            long total = 0;
            int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += count;
                if (total > expected) throw new InvalidDataException("Invalid decompressed avatar size.");
                destination.Write(buffer, 0, count);
            }
            if (total != expected) throw new InvalidDataException("Truncated avatar package.");
        }
        public void Dispose()
        {
            if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
            directory = null;
        }
    }
}
