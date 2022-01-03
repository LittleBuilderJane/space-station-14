#if DEBUG
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Robust.Client.GameObjects
{
    internal sealed class DebugLightTreeSystem : EntitySystem
    {
        private DebugLightOverlay? _lightOverlay;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;

                _enabled = value;
                var overlayManager = IoCManager.Resolve<IOverlayManager>();

                if (_enabled)
                {
                    _lightOverlay = new DebugLightOverlay(
                        IoCManager.Resolve<IEntityLookup>(),
                        IoCManager.Resolve<IEyeManager>(),
                        IoCManager.Resolve<IMapManager>(),
                        Get<RenderingTreeSystem>());

                    overlayManager.AddOverlay(_lightOverlay);
                }
                else
                {
                    overlayManager.RemoveOverlay(_lightOverlay!);
                    _lightOverlay = null;
                }
            }
        }

        private bool _enabled;

        private sealed class DebugLightOverlay : Overlay
        {
            private IEntityLookup _lookup;
            private IEyeManager _eyeManager;
            private IMapManager _mapManager;

            private RenderingTreeSystem _tree;

            public override OverlaySpace Space => OverlaySpace.WorldSpace;

            public DebugLightOverlay(IEntityLookup lookup, IEyeManager eyeManager, IMapManager mapManager, RenderingTreeSystem tree)
            {
                _lookup = lookup;
                _eyeManager = eyeManager;
                _mapManager = mapManager;
                _tree = tree;
            }

            protected internal override void Draw(in OverlayDrawArgs args)
            {
                var map = _eyeManager.CurrentMap;
                if (map == MapId.Nullspace) return;

                var viewport = _eyeManager.GetWorldViewbounds();
                var worldBounds = viewport.CalcBoundingBox();

                foreach (var tree in _tree.GetRenderTrees(map, viewport))
                {
                    foreach (var light in tree.LightTree)
                    {
                        var aabb = _lookup.GetWorldAabbFromEntity(light.Owner);
                        if (!aabb.Intersects(worldBounds)) continue;

                        args.WorldHandle.DrawRect(aabb, Color.Green.WithAlpha(0.1f));
                    }
                }
            }
        }
    }
}
#endif
