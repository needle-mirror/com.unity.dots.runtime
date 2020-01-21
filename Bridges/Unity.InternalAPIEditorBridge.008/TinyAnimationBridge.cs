using UnityEditor;

namespace TinyInternal.Bridge
{
    public static class TinyAnimationBridge
    {
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
    }
}
