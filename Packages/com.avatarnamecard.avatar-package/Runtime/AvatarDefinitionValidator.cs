using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AvatarNamecard.AvatarPackage
{
    public static class AvatarDefinitionValidator
    {
        public static void Validate(AvatarDefinition definition)
        {
            if (definition == null || (definition.version != 1 && definition.version != 2)) Fail("Unsupported avatar settings version.");
            CheckPath(definition.leftEye, true); CheckPath(definition.rightEye, true); CheckVector(definition.lookAtOffset);
            CheckArray(definition.expressions, 4096); CheckArray(definition.colliders, 4096); CheckArray(definition.springs, 4096);
            var names = new HashSet<string>(StringComparer.Ordinal);
            var presets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var expression in definition.expressions)
            {
                if (expression == null || string.IsNullOrWhiteSpace(expression.name) || !names.Add(expression.name)) Fail("Invalid or duplicate expression.");
                if (!string.IsNullOrEmpty(expression.preset) && (Array.IndexOf(new[] { "aa", "ih", "ou", "ee", "oh", "blink" }, expression.preset) < 0 || !presets.Add(expression.preset))) Fail("Invalid or duplicate expression preset.");
                CheckArray(expression.morphs, 4096);
                foreach (var morph in expression.morphs)
                {
                    if (morph == null || string.IsNullOrEmpty(morph.blendShape)) Fail("Invalid morph binding.");
                    CheckPath(morph.path); CheckRange(morph.weight, 0, 100);
                }
            }
            foreach (var collider in definition.colliders)
            {
                if (collider == null || Array.IndexOf(new[] { "Sphere", "Capsule", "Plane", "SphereInside", "CapsuleInside" }, collider.shape) < 0) Fail("Invalid collider shape.");
                CheckPath(collider.path); CheckVector(collider.offset); CheckVector(collider.tail); CheckVector(collider.normal); CheckRange(collider.radius, 0, 1000);
                if (collider.shape == "Plane" && collider.normal.sqrMagnitude < 0.00001f) Fail("Invalid collider plane normal.");
            }
            var joints = new HashSet<string>(StringComparer.Ordinal);
            foreach (var spring in definition.springs)
            {
                if (spring == null) Fail("Invalid spring.");
                CheckPath(spring.center, true); CheckArray(spring.joints, 4096); CheckArray(spring.colliders, 4096);
                if (spring.joints.Length < 2) Fail("A spring needs at least two joints.");
                foreach (var index in spring.colliders) if (index < 0 || index >= definition.colliders.Length) Fail("Invalid collider reference.");
                foreach (var joint in spring.joints)
                {
                    if (joint == null || Array.IndexOf(new[] { "None", "Cone", "Hinge", "Spherical" }, joint.limit) < 0 || definition.version == 1 && joint.limit != "None") Fail("Invalid spring joint profile.");
                    CheckPath(joint.path);
                    if (!joints.Add(joint.path) || joints.Count > 16384) Fail("Overlapping or excessive spring joints.");
                    CheckRange(joint.pitch, 0, 180); CheckRange(joint.yaw, 0, joint.limit == "Spherical" ? 90 : 180);
                    var q = joint.limitRotation;
                    CheckRange(q.x, -1, 1); CheckRange(q.y, -1, 1); CheckRange(q.z, -1, 1); CheckRange(q.w, -1, 1);
                    if (joint.limit != "None" && Mathf.Abs(q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w - 1) > 0.001f) Fail("Invalid limit frame.");
                    if (joint.angularGravity || joint.forceGravity)
                    {
                        if (definition.version < 2 || joint.angularGravity && joint.forceGravity) Fail("Invalid gravity profile.");
                        CheckRange(joint.pull, 0, 1); CheckRange(joint.gravityFalloff, 0, 1);
                        CheckRange(joint.gravity, 0, joint.forceGravity ? 1000 : 1);
                        CheckVector(joint.restDirection);
                        if (Mathf.Abs(joint.restDirection.sqrMagnitude - 1) > 0.001f) Fail("Invalid gravity rest direction.");
                    }
                    CheckRange(joint.stiffness, 0, 1000); CheckRange(joint.drag, 0, 1); CheckRange(joint.gravity, 0, 1000); CheckRange(joint.radius, 0, 1000); CheckVector(joint.gravityDirection);
                }
            }
        }
        private static void CheckArray<T>(T[] values, int maximum) { if (values == null || values.Length > maximum) Fail("Missing or excessive avatar settings."); }
        private static void CheckPath(string path, bool optional = false)
        {
            if (optional && string.IsNullOrEmpty(path)) return;
            if (path == null || path.Length > 4096 || path.StartsWith("/") || path.Contains("\\") || path.IndexOf('\0') >= 0) Fail("Invalid avatar node path.");
            if (path.Length == 0) return;
            foreach (var part in path.Split('/')) if (part.Length == 0 || part == "." || part == "..") Fail("Invalid avatar node path.");
        }
        private static void CheckVector(Vector3 v) { CheckRange(v.x, -10000, 10000); CheckRange(v.y, -10000, 10000); CheckRange(v.z, -10000, 10000); }
        private static void CheckRange(float value, float min, float max) { if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) Fail("Invalid avatar numeric setting."); }
        private static void Fail(string message) => throw new InvalidDataException(message);
    }
}
