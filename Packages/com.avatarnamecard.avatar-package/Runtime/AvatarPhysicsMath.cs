using UnityEngine;

namespace AvatarNamecard.AvatarPackage
{
    /// <summary>SDK-free conversion calibrated against small, observable PhysBone fixtures.</summary>
    public static class AvatarPhysicsMath
    {
        // PhysBone's limit Euler order differs from Unity's Quaternion.Euler (ZXY).
        public static Quaternion LimitRotation(Vector3 degrees) =>
            Quaternion.AngleAxis(degrees.z, Vector3.forward) *
            Quaternion.AngleAxis(degrees.y, Vector3.up) *
            Quaternion.AngleAxis(degrees.x, Vector3.right);

        public static float EffectiveGravity(float gravity, float falloff, Vector3 current, Vector3 initial) =>
            EffectiveForceGravity(Mathf.Clamp01(gravity), falloff, current, initial);

        // Legacy PhysBone 1.0 gravity is a force, not a 0..1 angular blend.
        // Values above one occur in existing avatars and affect SDK playback.
        public static float EffectiveForceGravity(float gravity, float falloff, Vector3 current, Vector3 initial) =>
            Mathf.Max(0, gravity) * (1 - Mathf.Clamp01(falloff) * Mathf.Clamp01(Vector3.Dot(current.normalized, initial.normalized)));

        // Length-normalized attraction. Pull remains a calibrated response, not an SDK solver copy.
        public static float Attraction(float pull, float length) =>
            Mathf.Max(0, length) * 60 * Mathf.Clamp01(pull) / Mathf.Max(0.05f, 1 - Mathf.Clamp01(pull));
    }
}
