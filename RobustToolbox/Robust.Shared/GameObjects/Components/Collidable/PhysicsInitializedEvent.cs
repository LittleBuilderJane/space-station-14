namespace Robust.Shared.GameObjects
{
    [ByRefEvent]
    public readonly struct PhysicsInitializedEvent
    {
        public readonly EntityUid Uid;

        public PhysicsInitializedEvent(EntityUid uid)
        {
            Uid = uid;
        }
    }
}
