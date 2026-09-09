using System;
using UnityEngine;

namespace AvatarNamecard.AvatarPackage
{
    // No SDK types, Unity object references, or serialized MonoBehaviours cross this boundary.
    [Serializable]
    public sealed class AvatarPackageManifest
    {
        public const string IosShaderProfile = "ios-tiers-v1";
        public string format = "avatar-namecard";
        public int formatVersion = 1;
        public string exporterVersion = "0.2.2";
        public string unityVersion;
        public string target;
        public string renderPipeline = "BuiltIn";
        public string graphicsApi;
        public string shaderProfile;
        public string shaderTierSettings;
        public string displayName;
        public string prefab = "avatar";
        public string bundle = "avatar.bundle";
        public long bundleSize;
        public string bundleSha256;
        public string definitionSha256;
        public string[] warnings = Array.Empty<string>();
        public string[] licenseNotices = Array.Empty<string>();
        public string[] shaders = Array.Empty<string>();
    }

    [Serializable]
    public sealed class AvatarDefinition
    {
        public int version = 1;
        public string physicsConversion = "physbone-to-spring-v1-approximate";
        public AvatarExpression[] expressions = Array.Empty<AvatarExpression>();
        public AvatarCollider[] colliders = Array.Empty<AvatarCollider>();
        public AvatarSpring[] springs = Array.Empty<AvatarSpring>();
        public string leftEye;
        public string rightEye;
        public Vector3 lookAtOffset = new Vector3(0, 0.06f, 0);
    }

    [Serializable]
    public sealed class AvatarExpression
    {
        public string name;
        public string preset;
        public AvatarMorph[] morphs = Array.Empty<AvatarMorph>();
    }

    [Serializable]
    public sealed class AvatarMorph
    {
        public string path;
        public string blendShape;
        public float weight = 100;
    }

    [Serializable]
    public sealed class AvatarCollider
    {
        public string path;
        public string shape;
        public Vector3 offset;
        public Vector3 tail;
        public Vector3 normal;
        public float radius;
    }

    [Serializable]
    public sealed class AvatarSpring
    {
        public string name;
        public AvatarJoint[] joints = Array.Empty<AvatarJoint>();
        public int[] colliders = Array.Empty<int>();
        public string center;
    }

    [Serializable]
    public sealed class AvatarJoint
    {
        public string path;
        public float stiffness;
        public float drag;
        public float gravity;
        public Vector3 gravityDirection = Vector3.down;
        public float radius;
        public string limit = "None";
        public Quaternion limitRotation = Quaternion.identity;
        // Limit angles are degrees; rotation is in the +Y segment frame.
        public float pitch = 180;
        public float yaw = 180;
        // v2: independently calibrated gravity adapters for PhysBone 1.0 and 1.1.
        public bool angularGravity;
        public bool forceGravity;
        public float pull;
        public float gravityFalloff;
        public Vector3 restDirection;
    }
}
