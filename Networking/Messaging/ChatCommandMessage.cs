using UnityEngine.Networking;
using Windwalk.Net;

namespace Andromeda.Mod.Networking.Messaging
{
    public sealed class ChatCommandMessage : NetMessage
    {
        public const short MessageType = 251;

        public string command;

        public override NetMessage.Type MsgType => (NetMessage.Type)MessageType;

        public override void Serialize(NetworkWriter writer)
        {
            writer.Write(command ?? string.Empty);
        }

        public override void Deserialize(NetworkReader reader)
        {
            command = reader.ReadString();
        }
    }
}
