using System;
using UnityEngine.Events;
#if ENABLE_PLAYERCONNECTION
using Unity.Development.PlayerConnection;
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

#if ENABLE_PLAYERCONNECTION
        private unsafe MessageStreamBuilder* buffer;
#endif

        public unsafe PlayerConnection()
        {
#if ENABLE_PLAYERCONNECTION
            // This builder is tracked so all can be freed during application shutdown - no need to destroy manually
            buffer = MessageStreamManager.CreateStreamBuilder();
#endif
        }

        public void Register(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
#if ENABLE_PLAYERCONNECTION
            Connection.RegisterMessage(messageId, callback);
#endif
        }

        public void Unregister(Guid messageId, UnityAction<MessageEventArgs> callback)
        {
#if ENABLE_PLAYERCONNECTION
            Connection.UnregisterMessage(messageId, callback);
#endif
        }

        public void Send(Guid messageId, byte[] data)
        {
#if ENABLE_PLAYERCONNECTION
            unsafe
            {
                fixed (byte* d = data)
                {
                    buffer->WriteMessage(messageId, d, data.Length);
                }
            }
#endif
        }

    }
}
