#if DEBUG && !UNITY_WEBGL

using UnityEngine.Networking.PlayerConnection;

namespace Unity.Development
{
    public static class PlayerConnectionProfiler
    {
        private static bool enabled = false;

        public static void Initialize()
        {
            PlayerConnectionService.RegisterMessage(PlayerMessageId.kProfileStartupInformation, (MessageEventArgs a) => enabled = (a.data[0] != 0));
        }
    }
}

#endif
