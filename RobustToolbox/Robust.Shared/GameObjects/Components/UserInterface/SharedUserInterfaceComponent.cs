using System;
using Robust.Shared.GameStates;
using Robust.Shared.IoC;
using Robust.Shared.Players;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Robust.Shared.GameObjects
{
    [NetworkedComponent]
    public abstract class SharedUserInterfaceComponent : Component
    {
        public sealed override string Name => "UserInterface";

        [DataDefinition]
        public sealed class PrototypeData : ISerializationHooks
        {
            public object UiKey { get; set; } = default!;

            [DataField("key", readOnly: true, required: true)]
            private string _uiKeyRaw = default!;

            [DataField("type", readOnly: true, required: true)]
            public string ClientType { get; set; } = default!;

            void ISerializationHooks.AfterDeserialization()
            {
                var reflectionManager = IoCManager.Resolve<IReflectionManager>();

                if (reflectionManager.TryParseEnumReference(_uiKeyRaw, out var @enum))
                {
                    UiKey = @enum;
                    return;
                }

                UiKey = _uiKeyRaw;
            }
        }
    }

    [NetSerializable, Serializable]
    public abstract class BoundUserInterfaceState
    {
    }


    [NetSerializable, Serializable]
    public class BoundUserInterfaceMessage : EntityEventArgs
    {
        /// <summary>
        ///     The UI of this message.
        ///     Only set when the message is raised as a directed event.
        /// </summary>
        public object UiKey { get; set; } = default!;

        /// <summary>
        ///     The Entity receiving the message.
        ///     Only set when the message is raised as a directed event.
        /// </summary>
        public EntityUid Entity { get; set; } = EntityUid.Invalid;

        /// <summary>
        ///     The session sending or receiving this message.
        ///     Only set when the message is raised as a directed event.
        /// </summary>
        public ICommonSession Session { get; set; } = default!;
    }

    [NetSerializable, Serializable]
    internal sealed class UpdateBoundStateMessage : BoundUserInterfaceMessage
    {
        public readonly BoundUserInterfaceState State;

        public UpdateBoundStateMessage(BoundUserInterfaceState state)
        {
            State = state;
        }
    }

    [NetSerializable, Serializable]
    internal sealed class OpenBoundInterfaceMessage : BoundUserInterfaceMessage
    {
    }

    [NetSerializable, Serializable]
    internal sealed class CloseBoundInterfaceMessage : BoundUserInterfaceMessage
    {
    }

    [Serializable, NetSerializable]
    internal sealed class BoundUIWrapMessage : EntityEventArgs
    {
        public readonly EntityUid Entity;
        public readonly BoundUserInterfaceMessage Message;
        public readonly object UiKey;

        public BoundUIWrapMessage(EntityUid entity, BoundUserInterfaceMessage message, object uiKey)
        {
            Message = message;
            UiKey = uiKey;
            Entity = entity;
        }

        public override string ToString()
        {
            return $"{nameof(BoundUIWrapMessage)}: {Message}";
        }
    }

    public class BoundUIClosedEvent : BoundUserInterfaceMessage
    {
        public BoundUIClosedEvent(object uiKey, EntityUid uid, ICommonSession session)
        {
            UiKey = uiKey;
            Entity = uid;
            Session = session;
        }
    }
}
