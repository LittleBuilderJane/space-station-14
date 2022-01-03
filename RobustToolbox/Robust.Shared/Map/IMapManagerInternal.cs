using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Robust.Shared.Map
{
    /// <inheritdoc />
    internal interface IMapManagerInternal : IMapManager
    {
        IGameTiming GameTiming { get; }
        IEntityManager EntityManager { get; }

        void OnComponentRemoved(MapGridComponent comp);

        void ChunkRemoved(MapChunk chunk);

        /// <summary>
        ///     Raises the OnTileChanged event.
        /// </summary>
        /// <param name="tileRef">A reference to the new tile.</param>
        /// <param name="oldTile">The old tile that got replaced.</param>
        void RaiseOnTileChanged(TileRef tileRef, Tile oldTile);

        IMapGridInternal CreateGridNoEntity(MapId currentMapID, GridId? gridID = null, ushort chunkSize = 16);
    }
}
