using System;
using UnityEngine.Events;
#if DEBUG && !UNITY_WEBGL
using Unity.Development;
#endif

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
}

namespace UnityEngine.Networking.PlayerConnection
{
    public class MessageEventArgs
    {
        public int playerId;
        public byte[] data;
    }

    public class PlayerConnection
    {
        private static PlayerConnection s_Instance;

        public static PlayerConnection instance => s_Instance = s_Instance ?? new PlayerConnection();

        public void Register(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
#if DEBUG && !UNITY_WEBGL
            PlayerConnectionService.RegisterMessage(messageId, callback);
#endif
        }

        public void Unregister(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
#if DEBUG && !UNITY_WEBGL
            PlayerConnectionService.UnregisterMessage(messageId, callback);
#endif
        }

        public void Send(Guid messageId, byte[] data)
        {
#if DEBUG && !UNITY_WEBGL
            unsafe
            {
                fixed (byte* d = data)
                {
                    PlayerConnectionService.SendMessage(messageId, d, data.Length);
                }
            }
#endif
        }

    }
}
