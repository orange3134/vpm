using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace AvatarNamecard.AvatarPackage.Editor
{
    /// <summary>Use iOS tier settings for desktop preview shaders, including their shadow sampling path.</summary>
    public static class IosShaderSettings
    {
        public const string Profile = AvatarPackageManifest.IosShaderProfile;
        private static readonly GraphicsTier[] Tiers = { GraphicsTier.Tier1, GraphicsTier.Tier2, GraphicsTier.Tier3 };

        public static string DescribeIos() => string.Join("\n", Tiers.Select(t => JsonUtility.ToJson(EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.iOS, t))));

        public static bool SynchronizeStandalone()
        {
            var changed = false;
            foreach (var tier in Tiers)
            {
                var ios = EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.iOS, tier);
                if (JsonUtility.ToJson(EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Standalone, tier)) == JsonUtility.ToJson(ios)) continue;
                EditorGraphicsSettings.SetTierSettings(BuildTargetGroup.Standalone, tier, ios);
                changed = true;
            }
            return changed;
        }

        public static IDisposable ForBundle(BuildTarget target) => target == BuildTarget.StandaloneOSX || target == BuildTarget.StandaloneWindows64
            ? new DesktopBuildScope() : new NoChangeScope();

        private sealed class NoChangeScope : IDisposable { public void Dispose() { } }
        private sealed class DesktopBuildScope : IDisposable
        {
            private const string Path = "ProjectSettings/GraphicsSettings.asset";
            private readonly UnityEngine.Object settings;
            private readonly string serialized;
            private readonly byte[] file;
            private readonly bool wasDirty;
            private readonly TierSettings[] original;
            private bool disposed;
            public DesktopBuildScope()
            {
                settings = AssetDatabase.LoadAllAssetsAtPath(Path).First();
                serialized = EditorJsonUtility.ToJson(settings);
                file = File.ReadAllBytes(Path);
                wasDirty = EditorUtility.IsDirty(settings);
                original = Tiers.Select(t => EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.Standalone, t)).ToArray();
                try { SynchronizeStandalone(); }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                for (var i = 0; i < Tiers.Length; i++) EditorGraphicsSettings.SetTierSettings(BuildTargetGroup.Standalone, Tiers[i], original[i]);
                // Also restore automatic-tier flags, not just effective values. The synchronous
                // build can persist project settings; preserve the source project's exact file.
                EditorJsonUtility.FromJsonOverwrite(serialized, settings);
                if (!File.ReadAllBytes(Path).SequenceEqual(file)) File.WriteAllBytes(Path, file);
                if (!wasDirty) EditorUtility.ClearDirty(settings);
            }
        }
    }
}
