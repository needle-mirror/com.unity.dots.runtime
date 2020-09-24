using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
#if !NET_DOTS
using System.Text.RegularExpressions;
#endif
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Assertions;

namespace UnityEngine.TestTools
{
    public static class LogAssert
    {
#if !NET_DOTS
        internal static Regex expectRegex = null;
#endif
        internal static string expectMessage = "";
        internal static LogType expectType = LogType.Log;
        internal static bool gotUnexpected = false;
        internal static bool expecting = false;

        [Conditional("DEBUG")]
        internal static void CheckExpected(LogType type, string message)
        {
            bool gotExpected = type == expectType;
#if !NET_DOTS
            if (expectRegex != null)
                gotExpected = gotExpected && expectRegex.IsMatch(message);
            else
#endif
            if (expectMessage != null)
                gotExpected = gotExpected && message == expectMessage;
            gotUnexpected = gotUnexpected || !gotExpected;
            if (expecting && gotExpected)
                expecting = false;
        }

        [Conditional("DEBUG")]
        public static void ExpectReset()
        {
            Expect(LogType.Log, "");
            expecting = false;
            gotUnexpected = false;
        }

        [Conditional("DEBUG")]
        public static void Expect(LogType type, string message)
        {
            expectType = type;
#if !NET_DOTS
            expectRegex = null;
#endif
            expectMessage = message;

            expecting = true;
        }

#if !NET_DOTS
        public static void Expect(LogType type, Regex regex)
        {
            expectType = type;
            expectRegex = regex;
            expectMessage = null;

            expecting = true;
        }
#endif
        [Conditional("DEBUG")]
        public static void NoUnexpectedReceived()
        {
            // "Unexpected" includes expecting something and not getting it

            bool oldGotUnexpected = gotUnexpected;
            bool oldExpecting = expecting;

            gotUnexpected = false;
            expecting = false;

            Assert.IsFalse(oldGotUnexpected);
            Assert.IsFalse(oldExpecting);
        }
    }
}
