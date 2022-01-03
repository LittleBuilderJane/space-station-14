using Lidgren.Network;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    public class MsgScriptStartAck : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.Command;

        public bool WasAccepted { get; set; }
        public int ScriptSession { get; set; }

        public override void ReadFromBuffer(NetIncomingMessage buffer)
        {
            WasAccepted = buffer.ReadBoolean();
            ScriptSession = buffer.ReadInt32();
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer)
        {
            buffer.Write(WasAccepted);
            buffer.Write(ScriptSession);
        }
    }
}
