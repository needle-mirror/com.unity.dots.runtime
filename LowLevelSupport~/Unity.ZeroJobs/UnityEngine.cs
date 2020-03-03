﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
#if DEBUG && !UNITY_WEBGL
using Unity.Development;
#endif
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif


namespace UnityEngine
{
    public static class Debug
    {
        // for testing
        internal static string lastLog;
        internal static string lastWarning;
        internal static string lastError;

        private static string MessageObjectToString(object message)
        {
            if (message == null)
                return "null (null message, maybe a format which is unsupported?)";
            if (message is string stringMessage)
                return stringMessage;
            if (message is int intMessage)
                return intMessage.ToString();
            if (message is short shortMessage)
                return shortMessage.ToString();
            if (message is float floatMessage)
                return floatMessage.ToString();
            if (message is double doubleMessage)
                return doubleMessage.ToString();
            if (message is Exception exc)
                return string.Concat(exc.Message, "\n", exc.StackTrace);

            return "Non-Trivially-Stringable OBJECT logged (Not supported in DOTS C#)";
        }

        [Conditional("DEBUG")]
        private static void LogInternal(string message)
        {
#if DEBUG && !UNITY_WEBGL
            PlayerConnectionLogger.Log(message);
#endif
            Console.WriteLine(message);
        }

        [Conditional("DEBUG")]
        public static void Log(object message)
        {
            lastLog = MessageObjectToString(message);
            LogInternal(lastLog);
        }

        [Conditional("DEBUG")]
        public static void LogWarning(object message)
        {
            lastWarning = MessageObjectToString(message);
            LogInternal(lastWarning);
        }

        [Conditional("DEBUG")]
        public static void LogError(object message)
        {
            lastError = $"LogError: {MessageObjectToString(message)}";
            LogInternal(lastError);
        }

        [Conditional("DEBUG")]
        public static void LogException(Exception exception)
        {
            lastLog = "Exception";
            LogInternal(exception.Message + "\n" + exception.StackTrace);
        }
    }

    public class Component {}

    public class Random
    {
        public static void InitState(int state)
        {
        }

        public static int Range(int one, int two)
        {
            return one;
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
    public class ExecuteAlwaysAttribute : Attribute
    {
        public ExecuteAlwaysAttribute()
        {
        }
    }

    public static class Time
    {
        [DllImport("lib_unity_zerojobs")]
        public static extern long Time_GetTicksMicrosecondsMonotonic();

        public static float time => Time_GetTicksMicrosecondsMonotonic() / 1_000_000.0f;

        public static double timeAsDouble => Time_GetTicksMicrosecondsMonotonic() / 1_000_000.0;
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
}
