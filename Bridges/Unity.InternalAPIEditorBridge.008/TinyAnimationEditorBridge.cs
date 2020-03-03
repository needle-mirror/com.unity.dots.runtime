using System;
using UnityEditor;
using UnityEngine;

namespace TinyInternal.Bridge
{
    public static class TinyAnimationEditorBridge
    {
        const string k_AnimationClipExtension = ".anim";

        public enum RotationMode
        {
            Baked = RotationCurveInterpolation.Mode.Baked,
            NonBaked = RotationCurveInterpolation.Mode.NonBaked,
            RawQuaternions = RotationCurveInterpolation.Mode.RawQuaternions,
            RawEuler = RotationCurveInterpolation.Mode.RawEuler,
            Undefined = RotationCurveInterpolation.Mode.Undefined
        }

        public static RotationMode GetRotationMode(EditorCurveBinding binding)
        {
            return (RotationMode)RotationCurveInterpolation.GetModeFromCurveData(binding);
        }

        public static string CreateRawQuaternionsBindingName(string componentName)
        {
            return $"{RotationCurveInterpolation.GetPrefixForInterpolation(RotationCurveInterpolation.Mode.RawQuaternions)}.{componentName}";
        }

        public static AnimationClipSettings GetAnimationClipSettings(AnimationClip clip)
        {
            return AnimationUtility.GetAnimationClipSettings(clip);
        }

        public static void CreateLegacyClip(string clipName)
        {
            var clip = new AnimationClip
            {
                legacy = true
            };

            if (!clipName.EndsWith(k_AnimationClipExtension, StringComparison.Ordinal))
                clipName += k_AnimationClipExtension;

            var path = ProjectWindowUtil.GetActiveFolderPath();
            ProjectWindowUtil.CreateAsset(clip, $"{path}/{clipName}");
        }
    }
}
