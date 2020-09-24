#if ENABLE_PLAYERCONNECTION

using System;
using static System.Text.Encoding;
using Unity.Burst;
using Unity.Baselib.LowLevel;
using UnityEngine;

namespace Unity.Development.PlayerConnection
{
    public class Logger
    {
        private static readonly SharedStatic<UIntPtr> bufferTls = SharedStatic<UIntPtr>.GetOrCreate<Logger, UIntPtr>();

        private static unsafe MessageStreamBuilder* MessageStream
        {
            get {
                UIntPtr tlsHandle = bufferTls.Data;
                MessageStreamBuilder* builder = (MessageStreamBuilder*)Binding.Baselib_TLS_Get(tlsHandle);
                if (builder == null)
                {
                    // This builder is tracked so all can be freed during application shutdown - no need to destroy manually
                    builder = MessageStreamManager.CreateStreamBuilder();
                    Binding.Baselib_TLS_Set(tlsHandle, (UIntPtr)builder);
                }
                return builder;
            }
        }

        public unsafe static void Initialize()
        {
            bufferTls.Data = Binding.Baselib_TLS_Alloc();
        }

        public unsafe static void Shutdown()
        {
            Binding.Baselib_TLS_Free(bufferTls.Data);
            bufferTls.Data = UIntPtr.Subtract(UIntPtr.Zero, 1);
        }

        public static void Log(string text, LogType type = LogType.Log)
        {
            if (text.Length == 0)
                return;

            int textBytes = UTF8.GetByteCount(text);

            unsafe
            {
                byte* textBuf = stackalloc byte[textBytes];

                fixed (char* t = text)
                {
                    UTF8.GetBytes(t, text.Length, textBuf, textBytes);
                    Log(textBuf, textBytes, type);
                }
            }
        }

        public unsafe static void Log(byte* textUtf8, int textBytes, LogType type = LogType.Log)
        {
            // We have already shutdown, so don't try to use this.
            // This won't detect if we haven't yet initialized, but that's less likely to happen due to
            // a controlled init/shutdown sequence. We can't check for "0" because Baselib_TLS_Alloc()
            // may return slot 0.
            // This fixes an issue which came up in a managed object's destructor which wanted
            // to log something and tried to send that log over playerconnection.
            if (bufferTls.Data == UIntPtr.Subtract(UIntPtr.Zero, 1))
                return;

            var stream = MessageStream;

            stream->MessageBegin(EditorMessageIds.kLog);
            stream->WriteData((uint)type);
            stream->WriteRaw(textUtf8, textBytes);
            stream->MessageEnd();
        }
    }
}

#endif
