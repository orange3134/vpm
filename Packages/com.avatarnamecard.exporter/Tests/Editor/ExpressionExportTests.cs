using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarNamecard.Exporter.Tests
{
    public class ExpressionExportTests
    {
        private GameObject avatar;
        private Mesh mesh;
        private AnimationClip clip;
        [SetUp] public void SetUp()
        {
            avatar = new GameObject("Avatar");
            var face = new GameObject("Face"); face.transform.SetParent(avatar.transform);
            mesh = new Mesh { vertices = new[] { Vector3.zero,Vector3.right,Vector3.up }, triangles = new[] {0,1,2} };
            mesh.AddBlendShapeFrame("Eyes",100,new Vector3[3],new Vector3[3],new Vector3[3]);
            mesh.AddBlendShapeFrame("Mouth",100,new Vector3[3],new Vector3[3],new Vector3[3]);
            face.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            clip = new AnimationClip { frameRate=60 };
            Set("Eyes",AnimationCurve.Linear(0,0,1,80)); Set("Mouth",AnimationCurve.Linear(0,0,1,40));
        }
        private void Set(string shape, AnimationCurve curve) => AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("Face",typeof(SkinnedMeshRenderer),"blendShape."+shape),curve);
        [TearDown] public void TearDown() { Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh); Object.DestroyImmediate(clip); }
        [Test] public void AutomaticTimeUsesLastFaceKeyAndCombinesMorphs()
        {
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("Face",typeof(Transform),"m_LocalPosition.x"),AnimationCurve.Linear(0,0,10,1));
            var request = new ExportExpression { name="Smile",clip=clip };
            Assert.That(request.SampleTime,Is.EqualTo(59f/60).Within(.0001));
            var warnings = new List<string>();
            var result=AvatarExpressionExport.Sample(avatar,clip,request.SampleTime,request.name,warnings);
            Assert.That(result.morphs.Length,Is.EqualTo(2));
            Assert.That(result.morphs[0].weight+result.morphs[1].weight,Is.EqualTo(118).Within(.001));
            Assert.That(warnings.Count,Is.EqualTo(1));
            Assert.That(avatar.transform.Find("Face").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0),Is.Zero);
        }
        [Test] public void ManualTimeCanSelectAnEarlierFaceAndSwitchBackToAutomatic()
        {
            var request = new ExportExpression { name="Smile",clip=clip,manualTime=true,time=.5f };
            Assert.That(AvatarExpressionExport.Sample(avatar,clip,request.SampleTime,"Smile",new List<string>()).morphs[0].weight,Is.EqualTo(40).Within(.001));
            request.manualTime=false;
            Assert.That(request.SampleTime,Is.GreaterThan(.98f));
        }
        [Test] public void VeryShortClipDoesNotSelectTheStartingNeutralFrame()
        {
            Set("Eyes",AnimationCurve.Linear(0,0,1f/60,100)); Set("Mouth",AnimationCurve.Linear(0,0,1f/60,100));
            var time=AvatarExpressionExport.AutomaticTime(clip);
            Assert.That(AvatarExpressionExport.Sample(avatar,clip,time,"Short",new List<string>()).morphs[0].weight,Is.GreaterThan(90));
        }
        [Test] public void ZeroDurationClipSamplesItsOnlyFrame()
        {
            Set("Eyes",new AnimationCurve(new Keyframe(0,70))); Set("Mouth",new AnimationCurve(new Keyframe(0,30)));
            Assert.That(AvatarExpressionExport.AutomaticTime(clip),Is.Zero);
            Assert.That(AvatarExpressionExport.Sample(avatar,clip,0,"Static",new List<string>()).morphs[0].weight,Is.EqualTo(70));
        }
        [Test] public void MissingMorphAndEmptyClipFailExplicitly()
        {
            Set("Missing",AnimationCurve.Constant(0,1,10));
            Assert.Throws<InvalidOperationException>(()=>AvatarExpressionExport.Sample(avatar,clip,0,"Broken",new List<string>()));
            clip.ClearCurves();
            Assert.Throws<InvalidOperationException>(()=>AvatarExpressionExport.Sample(avatar,clip,0,"Empty",new List<string>()));
        }
        [Test] public void DuplicateNamesFailBeforeTouchingAvatar()
        {
            using var export = new AvatarExpressionExport(new[]{new ExportExpression{name="Smile",clip=clip},new ExportExpression{name=" Smile ",clip=clip}});
            Assert.Throws<InvalidOperationException>(()=>export.Register(avatar));
            Assert.That(avatar.GetComponents<Component>().Length,Is.EqualTo(1));
        }
    }
}
