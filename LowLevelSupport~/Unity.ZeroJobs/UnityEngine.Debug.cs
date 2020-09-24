using System;
using System.Diagnostics;
#if ENABLE_PLAYERCONNECTION
using Unity.Development.PlayerConnection;
#endif


namespace UnityEngine
{
    public static class Debug
    {
#if ENABLE_PLAYERCONNECTION
        private unsafe static int FindIndexOfSubString(string text, string find)
        {
            int ret = 0;
            int length = find.Length;
            int max = text.Length - length;
            fixed (char* source = text)
            fixed (char* searchFor = find)
            {
                while (ret < max)
                {
                    int i = 0;
                    while (i < length && source[ret + i] == searchFor[i])
                        i++;

                    if (i == length)
                        return ret;

                    ret++;
                }
            }

            return -1;
        }

        private static string AddStackTrace(string message)
        {
            string trace = Environment.StackTrace;

            // Skip internal methods in until starting the call stack with the Log* method as Big Unity does.
            int start = FindIndexOfSubString(trace, "   at UnityEngine.Debug.Log");

            // If we couldn't find it, just print the whole stack trace
            if (start == -1)
                start = 0;

            return string.Concat(message, "\n", trace.Substring(start));
        }

        private static void MessageObjectToString(object input, out string message, out string messageWithStack)
#else
        private static void MessageObjectToString(object input, out string message)
#endif
        {
            if (input == null)
                message = "null (null message, maybe a format which is unsupported?)";
            else if (input is string stringMessage)
                message = stringMessage;
            else if (input is int intMessage)
                message = intMessage.ToString();
            else if (input is short shortMessage)
                message = shortMessage.ToString();
            else if (input is float floatMessage)
                message = floatMessage.ToString();
            else if (input is double doubleMessage)
                message = doubleMessage.ToString();
            else
            {
                if (input is Exception exc)
                    message = string.Concat(exc.Message, "\n", exc.StackTrace);
                else
                    message = "Non-Trivially-Stringable OBJECT logged (Not supported in DOTS C#)";
#if ENABLE_PLAYERCONNECTION
                messageWithStack = message;
#endif
                return;
            }

#if ENABLE_PLAYERCONNECTION
            messageWithStack = AddStackTrace(message);
#endif
        }

        [Conditional("DEBUG")]
        private static void ProcessAndLogInternal(LogType type, object log)
        {
#if ENABLE_PLAYERCONNECTION
            MessageObjectToString(log, out string message, out string messageWithStack);
#else
            MessageObjectToString(log, out string message);
#endif
#if DEBUG
            UnityEngine.TestTools.LogAssert.CheckExpected(type, message);
#endif
#if ENABLE_PLAYERCONNECTION
            Logger.Log(messageWithStack, type);
#endif
            Console.WriteLine(message);
        }

        [Conditional("DEBUG")]
        public static void Log(object message)
        {
            ProcessAndLogInternal(LogType.Log, message);
        }

        [Conditional("DEBUG")]
        public static void LogWarning(object message)
        {
            ProcessAndLogInternal(LogType.Warning, message);
        }

        [Conditional("DEBUG")]
        public static void LogError(object message)
        {
            ProcessAndLogInternal(LogType.Error, message);
        }

        [Conditional("DEBUG")]
        public static void LogAssertion(object message)
        {
            ProcessAndLogInternal(LogType.Assert, message);
        }

        [Conditional("DEBUG")]
        public static void LogException(Exception exception)
        {
            ProcessAndLogInternal(LogType.Exception, exception);
        }

        [Conditional("DEBUG")]
        public static void Assert(bool condition, string message)
        {
            if (!condition)
                LogAssertion("Assertion failure: " + message);
        }

        [Conditional("DEBUG")]
        public static void Assert(bool condition)
        {
            if (!condition)
                LogAssertion("Assertion failure");
        }
    }
}
