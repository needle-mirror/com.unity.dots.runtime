#if DEBUG && !UNITY_WEBGL

using static System.Text.Encoding;

namespace Unity.Development
{
    public static class PlayerConnectionLogger
    {
        public static void Log(string text)
        {
            int textBytes = UTF8.GetByteCount(text);

            unsafe
            {
                int logBytes = 4 + textBytes;
                byte* data = stackalloc byte[logBytes];

                fixed (char* t = text)
                {
                    *(int*)(data) = textBytes;
                    UTF8.GetBytes(t, text.Length, data + 4, textBytes);
                    PlayerConnectionService.SendMessage(PlayerMessageId.kLog, data, logBytes);
                }
            }
        }
    }
}

#endif
