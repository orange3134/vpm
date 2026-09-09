using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using AvatarNamecard.AvatarPackage;
using AvatarNamecard.AvatarPackage.Editor;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace AvatarNamecard.Exporter
{
    public sealed class AvatarExporterWindow : EditorWindow
    {
        [SerializeField] private GameObject avatar;
        [SerializeField] private List<ExportExpression> expressions = new List<ExportExpression>();
        private ExportExpression previewExpression;
        private readonly AvatarExpressionPreview expressionPreview = new AvatarExpressionPreview();
        private void OnDisable() => expressionPreview.Dispose();
        private BuildTarget target = BuildTarget.iOS;
        private string report = "";
        private Vector2 scroll;
        [MenuItem("MEISHI Pop/Export Avatar")]
        public static void Open() => GetWindow<AvatarExporterWindow>("MEISHI Pop Export");
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("MEISHI Pop", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Exports the current outfit after NDMF processing. Materials and shaders are preserved. PhysBone is approximated with SpringBone. See the report for unsupported features.", MessageType.Info);
            avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar != null ? avatar : Selection.activeGameObject, typeof(GameObject), true);
            target = (BuildTarget)EditorGUILayout.EnumPopup("Destination", target);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("追加する表情", EditorStyles.boldLabel);
            for (var i = 0; i < expressions.Count; i++)
            {
                var expression = expressions[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        expression.name = EditorGUILayout.TextField(expression.name, GUILayout.MinWidth(70));
                        expression.clip = (AnimationClip)EditorGUILayout.ObjectField(expression.clip, typeof(AnimationClip), false);
                        if (GUILayout.Button("−", GUILayout.Width(24))) { expressions.RemoveAt(i--); expressionPreview.Dispose(); continue; }
                    }
                    var manual = EditorGUILayout.ToggleLeft("こだわり設定：採用フレームを調整", expression.manualTime);
                    if (manual && !expression.manualTime)
                    {
                        expression.time = expression.SampleTime;
                        previewExpression = expression;
                    }
                    expression.manualTime = manual;
                    if (manual && expression.clip != null)
                    {
                        expression.time = EditorGUILayout.Slider("時刻（秒）", expression.time, 0, expression.clip.length);
                        if (GUILayout.Button("プレビュー")) previewExpression = expression;
                        if (previewExpression == expression)
                        {
                            expressionPreview.Draw(GUILayoutUtility.GetRect(180, 180), avatar, expression);
                            EditorGUILayout.LabelField("元アバターのBlendShapeプレビュー", EditorStyles.miniLabel);
                        }
                    }
                }
            }
            if (GUILayout.Button("＋ 表情を追加")) expressions.Add(new ExportExpression());
            using (new EditorGUI.DisabledScope(avatar == null || EditorApplication.isPlaying))
            {
                if (GUILayout.Button("Export .mpavatar"))
                {
                    var path = EditorUtility.SaveFilePanel("Export Avatar", "", avatar.name, "mpavatar");
                    if (!string.IsNullOrEmpty(path))
                    {
                        try { report = AvatarExporter.Export(avatar, path, target, expressions); }
                        catch (Exception ex) { report = ex.Message; Debug.LogException(ex); }
                    }
                }
            }
            EditorGUILayout.SelectableLabel(report, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(220));
            EditorGUILayout.EndScrollView();
        }
    }

    public static class AvatarExporter
    {
        internal static bool IsExporting;
        public static string Export(GameObject source, string outputPath, BuildTarget target, IReadOnlyList<ExportExpression> expressions = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Export from Edit Mode.");
            if (target != BuildTarget.iOS && target != BuildTarget.StandaloneOSX && target != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException("Supported targets: iOS, StandaloneOSX, StandaloneWindows64.");
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new InvalidOperationException("Install the " + target + " Build Support module for this Unity version first.");
            if (GraphicsSettings.currentRenderPipeline != null) throw new InvalidOperationException("Built-in Render Pipeline is required.");
            var warnings = new List<string>();
            using var expressionExport = new AvatarExpressionExport(expressions);
            var session = Guid.NewGuid().ToString("N");
            var assets = "Assets/AvatarNamecardExportTemp/" + session;
            var bundleName = "avatar-" + session + ".bundle";
            var build = Path.Combine("Library", "AvatarNamecardExporter", session);
            Directory.CreateDirectory(assets);
            Directory.CreateDirectory(build);
            GameObject clone = null;
            var oldTarget = EditorUserBuildSettings.activeBuildTarget;
            var oldGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            try
            {
                // Bake the PC-authored avatar BEFORE changing the Unity bundle target.
                clone = Object.Instantiate(source);
                clone.name = source.name;
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                clone.transform.localScale = source.transform.lossyScale;
                expressionExport.Register(clone);
                ProcessNdmf(clone, warnings);
                var animator = clone.GetComponent<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
                    throw new InvalidOperationException("A valid Humanoid Animator is required after NDMF processing.");
                var definition = AvatarConversion.Convert(clone, warnings);
                expressionExport.Extract(clone, definition, warnings);
                // Animation controllers can retain SDK behaviours and clips. The app supplies its own motion controller.
                foreach (var a in clone.GetComponentsInChildren<Animator>(true)) a.runtimeAnimatorController = null;
                foreach (var c in clone.GetComponentsInChildren<Component>(true))
                {
                    if (c == null) throw new InvalidOperationException("Missing script in the processed avatar.");
                    if (IsAllowedComponent(c)) continue;
                    if (!AvatarConversion.IsConvertedType(c.GetType().Name)) warnings.Add("Removed component: " + c.GetType().FullName + " at " + PathOf(clone.transform, c.transform));
                    Object.DestroyImmediate(c);
                }
                // Create persistent copies of generated meshes, materials and Humanoid Avatar.
                var persistent = new Dictionary<Object, Object>();
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => Persist(m, assets, persistent)).ToArray();
                    if (renderer is SkinnedMeshRenderer skin) skin.sharedMesh = Persist(skin.sharedMesh, assets, persistent);
                }
                foreach (var filter in clone.GetComponentsInChildren<MeshFilter>(true)) filter.sharedMesh = Persist(filter.sharedMesh, assets, persistent);
                foreach (var a in clone.GetComponentsInChildren<Animator>(true)) a.avatar = Persist(a.avatar, assets, persistent);
                // NDMF-generated textures may not have a persistent path.
                foreach (var mat in persistent.Values.OfType<Material>().ToArray())
                    foreach (var property in mat.GetTexturePropertyNames())
                    {
                        var texture = mat.GetTexture(property);
                        if (texture != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                            mat.SetTexture(property, Persist(texture, assets, persistent));
                    }
                AssetDatabase.SaveAssets();
                clone.SetActive(false); // app attaches SDK-free behaviour before activation
                var prefabPath = assets + "/avatar.prefab";
                PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);
                AuditDependencies(prefabPath);
                var licenseNotices = CollectLicenseNotices(prefabPath, warnings);
                AvatarDefinitionValidator.Validate(definition);
                var shaderNames = clone.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(m => m.shader.name).Distinct().ToArray();
                if (shaderNames.Any(s => s.IndexOf("poiyomi", StringComparison.OrdinalIgnoreCase) >= 0))
                    warnings.Add("Poiyomi: preserve the authored lock state. Animated/renamed material properties are not converted in v0.1.");
                warnings.Add("Lighting and shader compatibility must be checked on the destination device. No shader-name substitution is performed.");
                var definitionBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(definition, true));
                IsExporting = true;
                AssetBundleManifest manifest;
                using (IosShaderSettings.ForBundle(target))
                    manifest = BuildPipeline.BuildAssetBundles(build, new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { prefabPath }, addressableNames = new[] { "avatar" } } }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
                if (manifest == null) throw new InvalidOperationException("Bundle build failed. Check the Unity Console for shader/build errors.");
                var bundlePath = Path.Combine(build, bundleName);
                string hash;
                using (var stream = File.OpenRead(bundlePath)) hash = AvatarPackageArchive.Hash(stream);
                var package = new AvatarPackageManifest
                {
                    shaderProfile = IosShaderSettings.Profile, shaderTierSettings = IosShaderSettings.DescribeIos(),
                    displayName = source.name, unityVersion = Application.unityVersion, target = target.ToString(),
                    graphicsApi = string.Join(",", PlayerSettings.GetGraphicsAPIs(target).Select(a => a.ToString())),
                    bundleSize = new FileInfo(bundlePath).Length, bundleSha256 = hash,
                    definitionSha256 = AvatarPackageArchive.Hash(definitionBytes), licenseNotices = licenseNotices, warnings = warnings.Distinct().ToArray(), shaders = shaderNames
                };
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
                var temporary = outputPath + "." + session + ".tmp";
                try
                {
                    using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                    {
                        Write(zip, "manifest.json", Encoding.UTF8.GetBytes(JsonUtility.ToJson(package, true)));
                        Write(zip, "avatar.json", definitionBytes);
                        zip.CreateEntryFromFile(bundlePath, "avatar.bundle", System.IO.Compression.CompressionLevel.NoCompression);
                    }
                    using (AvatarPackageArchive.Open(temporary, build, target.ToString())) { }
                    if (File.Exists(outputPath)) File.Replace(temporary, outputPath, null); else File.Move(temporary, outputPath);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return "Exported: " + outputPath + "\n" + definition.springs.Length + " springs, " + definition.colliders.Length + " colliders, " + definition.expressions.Length + " expressions\n" + string.Join("\n", package.warnings);
            }
            finally
            {
                IsExporting = false;
                if (clone != null) Object.DestroyImmediate(clone);
                AssetDatabase.DeleteAsset(assets);
                if (Directory.Exists(build)) Directory.Delete(build, true);
                if (EditorUserBuildSettings.activeBuildTarget != oldTarget)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(oldGroup, oldTarget);
            }
        }

        private static void ProcessNdmf(GameObject clone, List<string> warnings)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("nadena.dev.ndmf.AvatarProcessor")).FirstOrDefault(t => t != null);
            if (type == null) throw new InvalidOperationException("Install NDMF 1.14.8 or later to export this avatar.");
            var platform = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal)).SelectMany(a => a.GetTypes()).FirstOrDefault(t => t.Name == "VRChatPlatform" && t.Namespace != null && t.Namespace.StartsWith("nadena.dev.ndmf", StringComparison.Ordinal));
            if (platform == null) throw new InvalidOperationException("NDMF VRChat platform support is not available.");
            var instance = platform.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? platform.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var method = type.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "ProcessAvatar" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(GameObject) && m.GetParameters()[1].ParameterType.IsInstanceOfType(instance));
            if (method == null) throw new InvalidOperationException("Unsupported NDMF API. Use the tested NDMF 1.14.8 version.");
            try
            {
                var context = method.Invoke(null, new[] { clone, instance });
                if (context == null || !(context.GetType().GetProperty("Successful")?.GetValue(context) is bool successful) || !successful)
                    throw new InvalidOperationException("NDMF reported a build error. Review its error report before export.");
            }
            catch (TargetInvocationException e) { throw new InvalidOperationException("NDMF processing failed.", e.InnerException); }
            warnings.Add("NDMF applied once using the VRChat platform. Current baked outfit is exported; FX/menu state machines are not reproduced.");
        }

        public static bool IsAllowedComponent(Component c) => c is Transform || c is Animator || c is SkinnedMeshRenderer || c is MeshRenderer || c is MeshFilter || c is RotationConstraint || c is PositionConstraint || c is ScaleConstraint || c is ParentConstraint || c is AimConstraint || c is LookAtConstraint;
        public static string PathOf(Transform root, Transform node)
        {
            if (node == root) return "";
            if (node == null || !node.IsChildOf(root)) throw new InvalidOperationException("Avatar refers to a transform outside its hierarchy.");
            var path = AnimationUtility.CalculateTransformPath(node, root);
            if (root.Find(path) != node) throw new InvalidOperationException("Ambiguous transform path: " + path + ". Rename duplicate siblings before export.");
            return path;
        }
        private static T Persist<T>(T value, string assets, Dictionary<Object, Object> copies) where T : Object
        {
            if (value == null) return null;
            if (copies.TryGetValue(value, out var existing)) return (T)existing;
            var copy = Object.Instantiate(value);
            copy.name = value.name;
            AssetDatabase.CreateAsset(copy, assets + "/" + copies.Count + ".asset");
            copies.Add(value, copy);
            return copy;
        }
        private static void AuditDependencies(string prefab)
        {
            foreach (var path in AssetDatabase.GetDependencies(prefab, true))
            {
                if (path.StartsWith("Packages/com.vrchat.", StringComparison.OrdinalIgnoreCase)
                    || path.IndexOf("/VRCSDK/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("/WMCTool/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || AssetDatabase.LoadMainAssetAtPath(path) is MonoScript)
                    throw new InvalidOperationException("Forbidden runtime dependency remains: " + path);
            }
        }
        private static string[] CollectLicenseNotices(string prefab, List<string> warnings)
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in AssetDatabase.GetDependencies(prefab, true))
            {
                if (!asset.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) continue;
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(asset);
                var directory = info != null ? info.resolvedPath : Path.GetDirectoryName(Path.GetFullPath(asset));
                // Include shader-package and bundled include-library notices (e.g. lilToon light volumes).
                if (info != null)
                {
                    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                        if (IsNotice(file)) files.Add(file);
                }
                else
                {
                    while (!string.IsNullOrEmpty(directory) && directory != Path.GetFullPath("Assets"))
                    {
                        foreach (var file in Directory.EnumerateFiles(directory)) if (IsNotice(file)) files.Add(file);
                        directory = Path.GetDirectoryName(directory);
                    }
                }
            }
            warnings.Add("Shader license notices discovered in the source are included in manifest.json. Check additional terms for the avatar, textures and modified shaders before sharing exports.");
            return files.OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText).Distinct().ToArray();
        }
        private static bool IsNotice(string path)
        {
            var name = Path.GetFileName(path);
            return !name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                (name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LICENSE.", StringComparison.OrdinalIgnoreCase)
                 || name.Equals("NOTICE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("NOTICE.", StringComparison.OrdinalIgnoreCase));
        }
        private static void Write(ZipArchive zip, string name, byte[] bytes)
        { using var stream = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal).Open(); stream.Write(bytes, 0, bytes.Length); }
    }

    // Prevent source-project shader errors from silently producing a successful package.
    public sealed class AvatarShaderBuildCheck : IPreprocessShaders
    {
        public int callbackOrder => int.MaxValue;
        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (AvatarExporter.IsExporting && ShaderUtil.ShaderHasError(shader))
                throw new UnityEditor.Build.BuildFailedException("Shader cannot be exported: " + shader.name);
        }
    }
}
