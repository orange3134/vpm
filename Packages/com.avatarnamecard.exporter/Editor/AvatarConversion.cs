using System;
using System.Collections.Generic;
using System.Linq;
using AvatarNamecard.AvatarPackage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;

namespace AvatarNamecard.Exporter
{
    internal static class AvatarConversion
    {
        public static bool IsConvertedType(string name) => name == "VRCAvatarDescriptor" || name == "PipelineManager" || name == "VRCPhysBone" || name == "VRCPhysBoneCollider" || name.StartsWith("VRC", StringComparison.Ordinal) && name.EndsWith("Constraint", StringComparison.Ordinal);
        private static float F(SerializedObject s, string p, float fallback = 0) => s.FindProperty(p)?.floatValue ?? fallback;
        private static bool B(SerializedObject s, string p) => s.FindProperty(p)?.boolValue ?? false;
        private static int I(SerializedObject s, string p) => s.FindProperty(p)?.intValue ?? 0;
        private static Vector3 V(SerializedObject s, string p) => s.FindProperty(p)?.vector3Value ?? Vector3.zero;
        private static T Ref<T>(SerializedObject s, string p) where T : UnityEngine.Object => s.FindProperty(p)?.objectReferenceValue as T;
        private static IEnumerable<UnityEngine.Object> Refs(SerializedObject s, string p)
        {
            var a = s.FindProperty(p);
            if (a == null || !a.isArray) yield break;
            for (var i = 0; i < a.arraySize; i++) yield return a.GetArrayElementAtIndex(i).objectReferenceValue;
        }
        private static float Curve(SerializedObject s, string name, float t)
        {
            var c = s.FindProperty(name + "Curve")?.animationCurveValue;
            return F(s, name) * (c != null && c.length > 0 ? c.Evaluate(t) : 1);
        }

        public static AvatarDefinition Convert(GameObject avatar, List<string> warnings)
        {
            var root = avatar.transform;
            var all = avatar.GetComponentsInChildren<Component>(true).Where(c => c != null).ToArray();
            foreach (var node in avatar.GetComponentsInChildren<Transform>(true)) AvatarExporter.PathOf(root, node);
            var definition = new AvatarDefinition { version = 2, physicsConversion = "physbone-to-spring-v2-limits-gravity" };
            var descriptor = all.FirstOrDefault(c => c.GetType().Name == "VRCAvatarDescriptor");
            var expressions = new List<AvatarExpression>();
            if (descriptor != null)
            {
                var s = new SerializedObject(descriptor);
                var mesh = Ref<SkinnedMeshRenderer>(s, "VisemeSkinnedMesh");
                var visemes = s.FindProperty("VisemeBlendShapes");
                var ids = new[] { 10, 12, 14, 11, 13 };
                var presets = new[] { "aa", "ih", "ou", "ee", "oh" };
                if (mesh != null && mesh.sharedMesh != null && visemes != null)
                    for (var i = 0; i < ids.Length; i++)
                        if (visemes.arraySize > ids[i]) AddExpression(expressions, root, mesh, visemes.GetArrayElementAtIndex(ids[i]).stringValue, presets[i], presets[i]);
                var eyelids = Ref<SkinnedMeshRenderer>(s, "customEyeLookSettings.eyelidsSkinnedMesh");
                var blink = s.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
                if (eyelids != null && eyelids.sharedMesh != null && blink != null && blink.arraySize > 0)
                {
                    var index = blink.GetArrayElementAtIndex(0).intValue;
                    if (index >= 0 && index < eyelids.sharedMesh.blendShapeCount)
                        AddExpression(expressions, root, eyelids, eyelids.sharedMesh.GetBlendShapeName(index), "blink", "blink");
                }
                var left = Ref<Transform>(s, "customEyeLookSettings.leftEye");
                var right = Ref<Transform>(s, "customEyeLookSettings.rightEye");
                definition.leftEye = left != null ? AvatarExporter.PathOf(root, left) : null;
                definition.rightEye = right != null ? AvatarExporter.PathOf(root, right) : null;
                warnings.Add("Eye bones and blink/viseme bindings converted. Eye angle ranges use app defaults; bone eyelids and jaw-bone lip sync are not converted.");
            }
            // Explicit, editable candidates. Do not guess emotion names from arbitrary FX graphs.
            foreach (var mesh in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (mesh.sharedMesh != null && mesh.gameObject.activeInHierarchy)
                    for (var i = 0; i < mesh.sharedMesh.blendShapeCount; i++)
                    {
                        var name = mesh.sharedMesh.GetBlendShapeName(i);
                        if (name.IndexOf("happy", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("smile", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("angry", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("sad", StringComparison.OrdinalIgnoreCase) >= 0)
                            AddExpression(expressions, root, mesh, name, mesh.name + "/" + name, null);
                    }
            definition.expressions = expressions.GroupBy(e => e.name).Select(g => g.First()).ToArray();
            var colliders = new List<AvatarCollider>();
            var colliderIds = new Dictionary<Component, int>();
            foreach (var component in all.Where(c => c.GetType().Name == "VRCPhysBoneCollider"))
            {
                var s = new SerializedObject(component);
                var node = Ref<Transform>(s, "rootTransform") ?? component.transform;
                var shape = I(s, "shapeType");
                var radius = Mathf.Max(0, F(s, "radius"));
                var rotation = s.FindProperty("rotation")?.quaternionValue ?? Quaternion.identity;
                var position = V(s, "position");
                var half = Mathf.Max(0, F(s, "height") * 0.5f - radius);
                colliderIds[component] = colliders.Count;
                colliders.Add(new AvatarCollider
                {
                    path = AvatarExporter.PathOf(root, node), radius = radius,
                    shape = shape == 2 ? "Plane" : shape == 1 ? (B(s,"insideBounds") ? "CapsuleInside" : "Capsule") : (B(s,"insideBounds") ? "SphereInside" : "Sphere"),
                    offset = shape == 1 ? position - rotation * Vector3.up * half : position,
                    tail = position + rotation * Vector3.up * half, normal = rotation * Vector3.up
                });
            }
            definition.colliders = colliders.ToArray();
            var springs = new List<AvatarSpring>();
            var physBones = all.Where(c => c.GetType().Name == "VRCPhysBone").ToArray();
            var physicsRoots = new HashSet<Transform>(physBones.Select(c => Ref<Transform>(new SerializedObject(c), "rootTransform") ?? c.transform));
            var claimed = new HashSet<Transform>();
            foreach (var pb in physBones)
            {
                if (pb is Behaviour behaviour && !behaviour.isActiveAndEnabled) continue;
                var s = new SerializedObject(pb);
                var node = Ref<Transform>(s, "rootTransform") ?? pb.transform;
                var ignored = new HashSet<Transform>(Refs(s, "ignoreTransforms").OfType<Transform>());
                if (B(s, "ignoreOtherPhysBones")) ignored.UnionWith(physicsRoots.Where(n => n != node));
                var ids = Refs(s, "colliders").OfType<Component>().Where(c => colliderIds.ContainsKey(c)).Select(c => colliderIds[c]).ToArray();
                var endpoint = V(s, "endpointPosition");
                var chains = new List<List<Transform>>();
                CollectChains(node, new List<Transform>(), ignored, chains, endpoint, I(s, "multiChildType"), warnings);
                foreach (var chain in chains)
                {
                    if (chain.Count < 2) { warnings.Add("Skipped PhysBone with no simulated segment: " + AvatarExporter.PathOf(root, node)); continue; }
                    var joints = new List<AvatarJoint>();
                    for (var i = 0; i < chain.Count; i++)
                    {
                        var transform = chain[i];
                        if (!claimed.Add(transform)) throw new InvalidOperationException("Overlapping PhysBone chains at " + AvatarExporter.PathOf(root, transform) + ". Resolve overlapping roots before export.");
                        var t = (float)Depth(node, transform) / Mathf.Max(1, MaxDepth(node, ignored));
                        var gravity = Curve(s, "gravity", t);
                        joints.Add(new AvatarJoint
                        {
                            path = AvatarExporter.PathOf(root, transform),
                            // Deliberate approximation, not a copy of the SDK solver. Versioned for future calibration.
                            stiffness = Mathf.Max(0, 4 * Curve(s, "pull", t) + Curve(s, "stiffness", t)),
                            drag = Mathf.Clamp01(1 - Curve(s, "spring", t)), gravity = Mathf.Abs(gravity),
                            gravityDirection = gravity >= 0 ? Vector3.down : Vector3.up,
                            radius = Mathf.Max(0, Curve(s, "radius", t)),
                            limit = new[] { "None", "Cone", "Hinge", "Spherical" }[Mathf.Clamp(I(s, "limitType"), 0, 3)],
                            limitRotation = AvatarPhysicsMath.LimitRotation(new Vector3(
                                AxisCurve(s, "limitRotationXCurve", V(s, "limitRotation").x, t),
                                AxisCurve(s, "limitRotationYCurve", V(s, "limitRotation").y, t),
                                AxisCurve(s, "limitRotationZCurve", V(s, "limitRotation").z, t))),
                            pitch = Mathf.Clamp(Curve(s, "maxAngleX", t), 0, 180),
                            yaw = Mathf.Clamp(Curve(s, "maxAngleZ", t), 0, 90),
                            angularGravity = I(s, "version") == 1 && i < chain.Count - 1,
                            forceGravity = I(s, "version") == 0 && i < chain.Count - 1,
                            pull = Mathf.Clamp01(Curve(s, "pull", t)),
                            gravityFalloff = Mathf.Clamp01(Curve(s, "gravityFalloff", t)),
                            restDirection = i < chain.Count - 1 ? root.InverseTransformDirection(chain[i+1].position - transform.position).normalized : Vector3.up
                        });
                    }
                    springs.Add(new AvatarSpring { name = pb.name, joints = joints.ToArray(), colliders = ids });
                }
                if (F(s, "immobile") != 0 || F(s, "maxStretch") != 0 || F(s, "maxSquish") != 0)
                    warnings.Add("PhysBone approximate: " + AvatarExporter.PathOf(root, node) + " — immobile, stretch/squish and grab/pose are not reproduced.");
            }
            if (physBones.Length > 0) warnings.Add("PhysBone: angle limits and 1.0/1.1 gravity/falloff converted. Spring/momentum, advanced stiffness, branching and transient motion remain approximations.");
            definition.springs = springs.ToArray();
            foreach (var c in all.Where(c => c.GetType().Name.StartsWith("VRC", StringComparison.Ordinal) && c.GetType().Name.EndsWith("Constraint", StringComparison.Ordinal))) ConvertConstraint(root, c, warnings);
            return definition;
        }

        private static float AxisCurve(SerializedObject s, string property, float value, float t)
        {
            var curve = s.FindProperty(property)?.animationCurveValue;
            return value * (curve != null && curve.length > 0 ? curve.Evaluate(t) : 1);
        }
        private static int Depth(Transform root, Transform node)
        {
            var depth = 0;
            while (node != root && node != null) { depth++; node = node.parent; }
            return depth;
        }
        private static int MaxDepth(Transform node, HashSet<Transform> ignored)
        {
            var children = node.Cast<Transform>().Where(c => !ignored.Contains(c) && c.gameObject.activeInHierarchy).ToArray();
            return children.Length == 0 ? 0 : 1 + children.Max(c => MaxDepth(c, ignored));
        }

        private static void CollectChains(Transform node, List<Transform> chain, HashSet<Transform> ignored, List<List<Transform>> output, Vector3 endpoint, int multiChild, List<string> warnings)
        {
            if (ignored.Contains(node) || !node.gameObject.activeInHierarchy) { if (chain.Count > 1) output.Add(chain); return; }
            var children = node.Cast<Transform>().Where(c => !ignored.Contains(c) && c.gameObject.activeInHierarchy).ToArray();
            if (children.Length > 1)
            {
                // Independent branches avoid multiple solvers writing the same transform.
                chain.Add(node);
                if (chain.Count > 1) output.Add(chain);
                if (multiChild != 0) warnings.Add("Branched PhysBone converted as independent branches: " + node.name);
                foreach (var child in children) CollectChains(child, new List<Transform>(), ignored, output, endpoint, multiChild, warnings);
                return;
            }
            chain.Add(node);
            if (children.Length == 1) { CollectChains(children[0], chain, ignored, output, endpoint, multiChild, warnings); return; }
            if (endpoint.sqrMagnitude > 0.00000001f)
            {
                var end = new GameObject("__AncEndpoint_" + Guid.NewGuid().ToString("N").Substring(0,8)).transform;
                end.SetParent(node, false); end.localPosition = endpoint; chain.Add(end);
            }
            output.Add(chain);
        }
        private static void AddExpression(List<AvatarExpression> list, Transform root, SkinnedMeshRenderer mesh, string blendShape, string name, string preset)
        {
            if (string.IsNullOrEmpty(blendShape) || mesh.sharedMesh.GetBlendShapeIndex(blendShape) < 0) return;
            list.Add(new AvatarExpression { name = name, preset = preset, morphs = new[] { new AvatarMorph { path = AvatarExporter.PathOf(root, mesh.transform), blendShape = blendShape } } });
        }
        private static void ConvertConstraint(Transform root, Component c, List<string> warnings)
        {
            var s = new SerializedObject(c);
            if (B(s, "SolveInLocalSpace") || B(s, "FreezeToWorld"))
                throw new InvalidOperationException("Constraint requires unsupported local/world-freeze semantics: " + AvatarExporter.PathOf(root, c.transform));
            var target = Ref<Transform>(s, "TargetTransform") ?? c.transform;
            AvatarExporter.PathOf(root, target);
            var sources = new List<ConstraintSource>();
            var total = I(s, "Sources.totalLength");
            for (var i = 0; i < total; i++)
            {
                var prefix = i < 16 ? "Sources.source" + i : "Sources.overflowList.Array.data[" + (i - 16) + "]";
                var source = Ref<Transform>(s, prefix + ".SourceTransform");
                if (source == null) throw new InvalidOperationException("Constraint has a missing source: " + c.name);
                AvatarExporter.PathOf(root, source);
                sources.Add(new ConstraintSource { sourceTransform = source, weight = F(s, prefix + ".Weight") });
            }
            if (c.GetType().Name != "VRCRotationConstraint")
                throw new InvalidOperationException("Constraint conversion not yet supported: " + c.GetType().Name + " at " + c.name);
            var converted = target.gameObject.AddComponent<RotationConstraint>();
            converted.constraintActive = false;
            converted.SetSources(sources);
            converted.rotationAtRest = V(s, "RotationAtRest");
            converted.rotationOffset = V(s, "RotationOffset");
            converted.rotationAxis = (B(s,"AffectsRotationX") ? Axis.X : Axis.None) | (B(s,"AffectsRotationY") ? Axis.Y : Axis.None) | (B(s,"AffectsRotationZ") ? Axis.Z : Axis.None);
            converted.weight = F(s, "GlobalWeight", 1);
            converted.locked = B(s, "Locked");
            converted.constraintActive = B(s, "IsActive");
            warnings.Add("Converted VRCRotationConstraint to Unity RotationConstraint; runtime evaluation parity needs validation.");
        }
    }
}
