using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine.Assertions;
#if ENABLE_PLAYERCONNECTION
using Unity.Development.PlayerConnection;
using UnityEngine.Scripting;
#endif
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif

namespace UnityEngine
{
    public class Component {}

    // A very simple placeholder implementation for the tests.
    public class Random
    {
        static uint seed = 1337;

        static int Rand() {
            // Xorshift
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (int)seed;
        }

        public static int Range(int one, int two)
        {
            Assert.IsTrue(two >= one);
            return one + Rand() % (two + 1 - one);
        }
    }

    // The type of the log message in the delegate registered with Application.RegisterLogCallback.
    public enum LogType
    {
        // LogType used for Errors.
        Error = 0,
        // LogType used for Asserts. (These indicate an error inside Unity itself.)
        Assert = 1,
        // LogType used for Warnings.
        Warning = 2,
        // LogType used for regular log messages.
        Log = 3,
        // LogType used for Exceptions.
        Exception = 4
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class ExecuteAlways : Attribute
    {
    }

    public static class Time
    {
        [DllImport("lib_unity_zerojobs")]
        public static extern long Time_GetTicksMicrosecondsMonotonic();

        // This isn't necessarily obsolete, but it seems to be the best way to mark something as "please don't use!"
        // while still maintaining compatibility with lots of pre-existing code.
        // Many times the float value is too large to have a digit change even per second. timeAsDouble MUST be
        // used to avoid bugs in code.
        [Obsolete("(DoNotRemove)")]
        public static float time => Time_GetTicksMicrosecondsMonotonic() / 1_000_000.0f;

        public static double timeAsDouble => Time_GetTicksMicrosecondsMonotonic() / 1_000_000.0;

        public static double realtimeSinceStartup => Time_GetTicksMicrosecondsMonotonic() / 1_000_000.0;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip)
        {
        }
    }

    public sealed class SerializeField : Attribute
    {
    }

    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
    }
}

#if !NET_DOTS
namespace UnityEngine.Serialization
{
    /// <summary>
    ///   <para>Use this attribute to rename a field without losing its serialized value.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public class FormerlySerializedAsAttribute : Attribute
    {
        private string m_oldName;

        /// <summary>
        ///   <para></para>
        /// </summary>
        /// <param name="oldName">The name of the field before renaming.</param>
        public FormerlySerializedAsAttribute(string oldName)
        {
            this.m_oldName = oldName;
        }

        /// <summary>
        ///   <para>The name of the field before the rename.</para>
        /// </summary>
        public string oldName
        {
            get
            {
                return this.m_oldName;
            }
        }
    }
}
#endif
