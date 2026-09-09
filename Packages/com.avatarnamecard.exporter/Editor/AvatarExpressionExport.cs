using System;
using System.Collections.Generic;
using System.Linq;
using AvatarNamecard.AvatarPackage;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarNamecard.Exporter
{
    [Serializable]
    public sealed class ExportExpression
    {
        public string name = "";
        public AnimationClip clip;
        public bool manualTime;
        public float time;
        public float SampleTime => manualTime ? Mathf.Clamp(time, 0, clip != null ? clip.length : 0) : AvatarExpressionExport.AutomaticTime(clip);
    }

    /// <summary>Registers requested clips in the cloned FX graph so NDMF rewrites their bindings too.</summary>
    internal sealed class AvatarExpressionExport : IDisposable
    {
        private readonly IReadOnlyList<ExportExpression> requests;
        private readonly string marker = "__MeishiPopExpressions_" + Guid.NewGuid().ToString("N");
        private readonly List<Object> owned = new List<Object>();
        public AvatarExpressionExport(IReadOnlyList<ExportExpression> requests) => this.requests = requests ?? Array.Empty<ExportExpression>();

        public static float AutomaticTime(AnimationClip clip)
        {
            if (clip == null) return 0;
            var last = AnimationUtility.GetCurveBindings(clip).Where(IsMorph)
                .Select(b => AnimationUtility.GetEditorCurve(clip, b)).Where(c => c != null && c.length > 0)
                .Select(c => c.keys[c.length - 1].time).DefaultIfEmpty(0).Max();
            // One frame before the last face key avoids the wrap/reset key of many looping clips.
            return Mathf.Max(0, last - Mathf.Min(1 / Mathf.Max(1, clip.frameRate), last * .05f));
        }
        private static bool IsMorph(EditorCurveBinding b) => b.type == typeof(SkinnedMeshRenderer) && b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal);

        public void Register(GameObject avatar)
        {
            if (requests.Count == 0) return;
            if (requests.Any(r => r == null || r.clip == null || string.IsNullOrWhiteSpace(r.name)))
                throw new InvalidOperationException("追加表情には名前とクリップを指定してください。");
            if (requests.Select(r => r.name.Trim()).Distinct(StringComparer.Ordinal).Count() != requests.Count)
                throw new InvalidOperationException("追加表情の名前が重複しています。");
            var descriptor = avatar.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "VRCAvatarDescriptor");
            if (descriptor == null) throw new InvalidOperationException("追加表情にはVRCAvatarDescriptorが必要です。");
            var serialized = new SerializedObject(descriptor);
            var layers = serialized.FindProperty("baseAnimationLayers");
            SerializedProperty fx = null;
            for (var i = 0; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                var type = layer.FindPropertyRelative("type");
                if (type.enumNames[type.enumValueIndex] == "FX") { fx = layer; break; }
            }
            if (fx == null) throw new InvalidOperationException("FXレイヤーが見つかりません。");
            var original = fx.FindPropertyRelative("animatorController").objectReferenceValue as RuntimeAnimatorController;
            if (original != null && !(original is AnimatorController))
                throw new InvalidOperationException("追加表情の登録には通常のFX AnimatorControllerを使用してください（OverrideController非対応）。");
            var controller = original != null && !fx.FindPropertyRelative("isDefault").boolValue
                ? Object.Instantiate((AnimatorController)original) : new AnimatorController();
            owned.Add(controller);
            controller.name = marker;
            var machine = new AnimatorStateMachine { name = marker }; owned.Add(machine);
            var idle = machine.AddState("Idle"); idle.writeDefaultValues = false; machine.defaultState = idle;
            controller.AddParameter(marker, AnimatorControllerParameterType.Int);
            for (var i = 0; i < requests.Count; i++)
            {
                var clip = Object.Instantiate(requests[i].clip); owned.Add(clip);
                var state = machine.AddState(marker + i); state.motion = clip; state.writeDefaultValues = false;
                var transition = idle.AddTransition(state); transition.hasExitTime = false;
                transition.AddCondition(AnimatorConditionMode.Equals, i + 1, marker);
            }
            controller.AddLayer(new AnimatorControllerLayer { name = marker, defaultWeight = 1, stateMachine = machine });
            fx.FindPropertyRelative("isDefault").boolValue = false;
            fx.FindPropertyRelative("animatorController").objectReferenceValue = controller;
            serialized.FindProperty("customizeAnimationLayers").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public void Extract(GameObject avatar, AvatarDefinition definition, List<string> warnings)
        {
            if (requests.Count == 0) return;
            var descriptor = avatar.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "VRCAvatarDescriptor");
            var layers = new SerializedObject(descriptor).FindProperty("baseAnimationLayers");
            var states = new List<AnimatorState>();
            for (var i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("animatorController").objectReferenceValue is AnimatorController controller)
                    foreach (var layer in controller.layers) CollectStates(layer.stateMachine, states);
            var expressions = definition.expressions.ToList();
            for (var i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                var matches = states.Where(s => s.name == marker + i).ToArray();
                if (matches.Length != 1 || !(matches[0].motion is AnimationClip clip))
                    throw new InvalidOperationException("NDMF処理後の表情クリップを追跡できません: " + request.name);
                var expression = Sample(avatar, clip, request.SampleTime, request.name.Trim(), warnings);
                if (expressions.Any(e => e.name == expression.name))
                    throw new InvalidOperationException("自動表情と名前が重複しています: " + expression.name);
                expressions.Add(expression);
            }
            definition.expressions = expressions.ToArray();
        }
        private static void CollectStates(AnimatorStateMachine machine, List<AnimatorState> output)
        {
            if (machine == null) return;
            output.AddRange(machine.states.Select(s => s.state));
            foreach (var child in machine.stateMachines) CollectStates(child.stateMachine, output);
        }
        internal static AvatarExpression Sample(GameObject avatar, AnimationClip clip, float time, string name, List<string> warnings)
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings.Any(b => !IsMorph(b)) || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length > 0)
                warnings.Add("表情「" + name + "」: BlendShape以外（ボーン、マテリアル、表示切替など）のキーは含まれません。");
            var morphs = new List<AvatarMorph>();
            foreach (var binding in bindings.Where(IsMorph))
            {
                var node = string.IsNullOrEmpty(binding.path) ? avatar.transform : avatar.transform.Find(binding.path);
                var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                var shape = binding.propertyName.Substring("blendShape.".Length);
                if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.GetBlendShapeIndex(shape) < 0)
                    throw new InvalidOperationException("表情「" + name + "」の参照先が見つかりません: " + binding.path + "/" + shape);
                var weight = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(time);
                if (float.IsNaN(weight) || float.IsInfinity(weight)) throw new InvalidOperationException("表情の値が不正です: " + name);
                if (weight < 0 || weight > 100) warnings.Add("表情「" + name + "」: BlendShapeの値を0～100に制限しました。");
                morphs.Add(new AvatarMorph { path = AvatarExporter.PathOf(avatar.transform, node), blendShape = shape, weight = Mathf.Clamp(weight, 0, 100) });
            }
            if (morphs.Count == 0) throw new InvalidOperationException("表情「" + name + "」に使用可能なBlendShapeキーがありません。");
            return new AvatarExpression { name = name, morphs = morphs.ToArray() };
        }
        public void Dispose() { foreach (var item in owned) if (item != null) Object.DestroyImmediate(item); owned.Clear(); }
    }
}
