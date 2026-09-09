using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarNamecard.Exporter
{
    internal sealed class AvatarExpressionPreview : IDisposable
    {
        private PreviewRenderUtility preview;
        private GameObject clone;
        private GameObject source;
        private AnimationClip clip;
        public void Draw(Rect rect, GameObject avatar, ExportExpression expression)
        {
            if (avatar == null || expression.clip == null) return;
            if (source != avatar || clip != expression.clip)
            {
                Dispose(); source = avatar; clip = expression.clip;
                preview = new PreviewRenderUtility();
                clone = Object.Instantiate(avatar);
                clone.hideFlags = HideFlags.HideAndDontSave;
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var script in clone.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null) Object.DestroyImmediate(script);
                foreach (var animator in clone.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                preview.AddSingleGO(clone);
                var humanoid = clone.GetComponent<Animator>();
                var head = humanoid != null && humanoid.isHuman ? humanoid.GetBoneTransform(HumanBodyBones.Head) : null;
                var center = head != null ? head.position + Vector3.up * 0.06f : Vector3.up * 1.4f;
                preview.camera.transform.position = center + Vector3.forward * 0.65f;
                preview.camera.transform.LookAt(center);
                preview.camera.nearClipPlane = 0.01f; preview.camera.farClipPlane = 20;
                preview.camera.fieldOfView = 35;
                preview.lights[0].intensity = 1; preview.lights[0].transform.rotation = Quaternion.Euler(25, 180, 0);
                preview.lights[1].intensity = 0.5f;
                preview.ambientColor = new Color(.5f, .5f, .5f, 1);
            }
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(SkinnedMeshRenderer) || !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) continue;
                var node = string.IsNullOrEmpty(binding.path) ? clone.transform : clone.transform.Find(binding.path);
                var mesh = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                var index = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring(11)) : -1;
                if (index >= 0) mesh.SetBlendShapeWeight(index, AnimationUtility.GetEditorCurve(clip, binding).Evaluate(expression.SampleTime));
            }
            if (Event.current.type != EventType.Repaint) return;
            preview.BeginPreview(rect, GUIStyle.none);
            preview.camera.Render();
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.ScaleToFit, false);
        }
        public void Dispose()
        {
            preview?.Cleanup(); preview = null;
            if (clone != null) Object.DestroyImmediate(clone);
            clone = null; source = null; clip = null;
        }
    }
}
