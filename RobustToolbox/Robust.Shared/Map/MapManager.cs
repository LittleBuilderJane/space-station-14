using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Robust.Shared.Map
{
    /// <inheritdoc cref="IMapManager"/>
    internal class MapManager : IMapManagerInternal
    {
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;

        public IGameTiming GameTiming => _gameTiming;

        public IEntityManager EntityManager => _entityManager;

        /// <inheritdoc />
        public MapId DefaultMap => MapId.Nullspace;

        /// <inheritdoc />
        public event EventHandler<TileChangedEventArgs>? TileChanged;

        public event GridEventHandler? OnGridCreated;

        public event GridEventHandler? OnGridRemoved;

        /// <summary>
        ///     Should the OnTileChanged event be suppressed? This is useful for initially loading the map
        ///     so that you don't spam an event for each of the million station tiles.
        /// </summary>
        /// <inheritdoc />
        public event EventHandler<GridChangedEventArgs>? GridChanged;

        /// <inheritdoc />
        public event EventHandler<MapEventArgs>? MapCreated;

        /// <inheritdoc />
        public event EventHandler<MapEventArgs>? MapDestroyed;

        /// <inheritdoc />
        public bool SuppressOnTileChanged { get; set; }

        private MapId HighestMapID = MapId.Nullspace;
        private GridId HighestGridID = GridId.Invalid;

        private protected readonly HashSet<MapId> _maps = new();
        private protected readonly Dictionary<MapId, GameTick> _mapCreationTick = new();

        private protected readonly Dictionary<GridId, MapGrid> _grids = new();
        private protected readonly Dictionary<MapId, EntityUid> _mapEntities = new();

#if DEBUG
        private bool _dbgGuardInit = false;
        private bool _dbgGuardRunning = false;
#endif

        /// <inheritdoc />
        public void Initialize()
        {
#if DEBUG
            DebugTools.Assert(!_dbgGuardInit);
            DebugTools.Assert(!_dbgGuardRunning);
            _dbgGuardInit = true;
#endif
        }

        /// <inheritdoc />
        public void Startup()
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardInit);
            _dbgGuardRunning = true;
#endif

            Logger.DebugS("map", "Starting...");

            if (!_maps.Contains(MapId.Nullspace))
            {
                CreateMap(MapId.Nullspace);
            }
            else if (_mapEntities.TryGetValue(MapId.Nullspace, out var mapEntId))
            {
                var mapEnt = mapEntId;

                foreach (var childTransform in _entityManager.GetComponent<TransformComponent>(mapEnt).Children.ToArray())
                {
                    _entityManager.DeleteEntity(childTransform.Owner);
                }
            }

            DebugTools.Assert(_grids.Count == 0);
            DebugTools.Assert(!GridExists(GridId.Invalid));
        }

        public void OnComponentRemoved(MapGridComponent comp)
        {
            var gridIndex = comp.GridIndex;
            if (gridIndex != GridId.Invalid)
            {
                if (GridExists(gridIndex))
                {
                    Logger.DebugS("map",
                        $"Entity {comp.Owner} removed grid component, removing bound grid {gridIndex}");
                    DeleteGrid(gridIndex);
                }
            }
        }

        public virtual void ChunkRemoved(MapChunk chunk)
        {
            return;
        }

        /// <inheritdoc />
        public void Shutdown()
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardInit);
#endif
            Logger.DebugS("map", "Stopping...");

            foreach (var map in _maps.ToArray())
            {
                if (map != MapId.Nullspace)
                {
                    DeleteMap(map);
                }
            }

            if (_mapEntities.TryGetValue(MapId.Nullspace, out var entId))
            {
                Logger.InfoS("map", $"Deleting map entity {entId}");
                _entityManager.DeleteEntity(entId);

                if (_mapEntities.Remove(MapId.Nullspace))
                    Logger.InfoS("map", "Removing nullspace map entity.");
            }

#if DEBUG
            DebugTools.Assert(_grids.Count == 0);
            DebugTools.Assert(!GridExists(GridId.Invalid));
            _dbgGuardRunning = false;
#endif
        }

        /// <inheritdoc />
        public void Restart()
        {
            Logger.DebugS("map", "Restarting...");

            Shutdown();
            Startup();
        }

        /// <summary>
        ///     Raises the OnTileChanged event.
        /// </summary>
        /// <param name="tileRef">A reference to the new tile.</param>
        /// <param name="oldTile">The old tile that got replaced.</param>
        public void RaiseOnTileChanged(TileRef tileRef, Tile oldTile)
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardRunning);
#endif

            if (SuppressOnTileChanged)
                return;

            TileChanged?.Invoke(this, new TileChangedEventArgs(tileRef, oldTile));
        }

        /// <inheritdoc />
        public virtual void DeleteMap(MapId mapID)
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardRunning);
#endif

            if (!_maps.Contains(mapID))
            {
                throw new InvalidOperationException($"Attempted to delete nonexistant map '{mapID}'");
            }

            // grids are cached because Delete modifies collection
            foreach (var grid in GetAllMapGrids(mapID).ToList())
            {
                DeleteGrid(grid.Index);
            }

            if (mapID != MapId.Nullspace)
            {
                MapDestroyed?.Invoke(this, new MapEventArgs(mapID));
                _maps.Remove(mapID);
                _mapCreationTick.Remove(mapID);
            }

            if (_mapEntities.TryGetValue(mapID, out var ent))
            {
                _entityManager.DeleteEntity(ent);
                _mapEntities.Remove(mapID);
            }

            Logger.InfoS("map", $"Deleting map {mapID}");
        }

        public MapId CreateMap(MapId? mapID = null)
        {
            return CreateMap(mapID, default);
        }

        public MapId CreateMap(MapId? mapID, EntityUid entityUid)
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardRunning);
#endif

            MapId actualID;
            if (mapID != null)
            {
                actualID = mapID.Value;
            }
            else
            {
                actualID = new MapId(HighestMapID.Value + 1);
            }

            if (MapExists(actualID))
            {
                throw new InvalidOperationException($"A map with ID {actualID} already exists");
            }

            if (HighestMapID.Value < actualID.Value)
            {
                HighestMapID = actualID;
            }

            _maps.Add(actualID);
            _mapCreationTick.Add(actualID, _gameTiming.CurTick);
            Logger.InfoS("map", $"Creating new map {actualID}");

            if (actualID != MapId.Nullspace) // nullspace isn't bound to an entity
            {
                var mapComps = _entityManager.EntityQuery<MapComponent>(true);

                IMapComponent? result = null;
                foreach (var mapComp in mapComps)
                {
                    if (mapComp.WorldMap != actualID)
                        continue;

                    result = mapComp;
                    break;
                }

                if (result != null)
                {
                    _mapEntities.Add(actualID, result.Owner);
                    Logger.DebugS("map", $"Rebinding map {actualID} to entity {result.Owner}");
                }
                else
                {
                    var newEnt = _entityManager.CreateEntityUninitialized(null, entityUid);
                    _mapEntities.Add(actualID, newEnt);

                    var mapComp = _entityManager.AddComponent<MapComponent>(newEnt);
                    mapComp.WorldMap = actualID;
                    _entityManager.InitializeComponents(newEnt);
                    _entityManager.StartComponents(newEnt);
                    Logger.DebugS("map", $"Binding map {actualID} to entity {newEnt}");
                }
            }

            MapCreated?.Invoke(this, new MapEventArgs(actualID));

            return actualID;
        }

        /// <inheritdoc />
        public bool MapExists(MapId mapID)
        {
            return _maps.Contains(mapID);
        }

        public EntityUid CreateNewMapEntity(MapId mapId)
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardRunning);
#endif

            var newEntity = _entityManager.CreateEntityUninitialized(null);
            SetMapEntity(mapId, newEntity);

            _entityManager.InitializeComponents(newEntity);
            _entityManager.StartComponents(newEntity);

            return newEntity;
        }

        /// <inheritdoc />
        public void SetMapEntity(MapId mapId, EntityUid newMapEntity)
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardRunning);
#endif

            if (!_maps.Contains(mapId))
                throw new InvalidOperationException($"Map {mapId} does not exist.");

            foreach (var kvEntity in _mapEntities)
            {
                if (kvEntity.Value == newMapEntity)
                {
                    throw new InvalidOperationException(
                        $"Entity {newMapEntity} is already the root node of map {kvEntity.Key}.");
                }
            }

            // remove existing graph
            if (_mapEntities.TryGetValue(mapId, out var oldEntId))
            {
                //Note: This prevents setting a subgraph as the root, since the subgraph will be deleted
                _entityManager.DeleteEntity(oldEntId);
            }
            else
            {
                _mapEntities.Add(mapId, EntityUid.Invalid);
            }

            // re-use or add map component
            if (!_entityManager.TryGetComponent(newMapEntity, out MapComponent? mapComp))
            {
                mapComp = _entityManager.AddComponent<MapComponent>(newMapEntity);
            }
            else
            {
                if (mapComp.WorldMap != mapId)
                    Logger.WarningS("map",
                        $"Setting map {mapId} root to entity {newMapEntity}, but entity thinks it is root node of map {mapComp.WorldMap}.");
            }

            Logger.DebugS("map", $"Setting map {mapId} entity to {newMapEntity}");

            // set as new map entity
            mapComp.WorldMap = mapId;
            _mapEntities[mapId] = newMapEntity;
        }

        public EntityUid GetMapEntityId(MapId mapId)
        {
            if (_mapEntities.TryGetValue(mapId, out var entId))
                return entId;

            return EntityUid.Invalid;
        }

        /// <summary>
        /// Replaces GetMapEntity()'s throw-on-failure semantics.
        /// </summary>
        public EntityUid GetMapEntityIdOrThrow(MapId mapId)
        {
            return _mapEntities[mapId];
        }

        public bool HasMapEntity(MapId mapId)
        {
            return _mapEntities.ContainsKey(mapId);
        }

        public IEnumerable<MapId> GetAllMapIds()
        {
            return _maps;
        }

        public IEnumerable<IMapGrid> GetAllGrids()
        {
            return _grids.Values;
        }

        public IMapGrid CreateGrid(MapId currentMapID, GridId? gridID = null, ushort chunkSize = 16)
        {
            return CreateGridImpl(currentMapID, gridID, chunkSize, true, default);
        }

        public IMapGrid CreateGrid(MapId currentMapID, GridId gridID, ushort chunkSize, EntityUid euid)
        {
            return CreateGridImpl(currentMapID, gridID, chunkSize, true, euid);
        }

        private IMapGridInternal CreateGridImpl(MapId currentMapID, GridId? gridID, ushort chunkSize, bool createEntity,
            EntityUid euid)
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardRunning);
#endif

            GridId actualID;
            if (gridID != null)
            {
                actualID = gridID.Value;
            }
            else
            {
                actualID = new GridId(HighestGridID.Value + 1);
            }

            DebugTools.Assert(actualID != GridId.Invalid);

            if (GridExists(actualID))
            {
                throw new InvalidOperationException($"A grid with ID {actualID} already exists");
            }

            if (HighestGridID.Value < actualID.Value)
            {
                HighestGridID = actualID;
            }

            var grid = new MapGrid(this, _entityManager, actualID, chunkSize, currentMapID);
            _grids.Add(actualID, grid);
            Logger.InfoS("map", $"Creating new grid {actualID}");

            if (actualID != GridId.Invalid && createEntity) // nullspace default grid is not bound to an entity
            {
                // the entity may already exist from map deserialization
                IMapGridComponent? result = null;
                foreach (var comp in _entityManager.EntityQuery<MapGridComponent>(true))
                {
                    if (comp.GridIndex != actualID)
                        continue;

                    result = comp;
                    break;
                }

                if (result != null)
                {
                    grid.GridEntityId = result.Owner;
                    Logger.DebugS("map", $"Rebinding grid {actualID} to entity {grid.GridEntityId}");
                }
                else
                {
                    var gridEnt = EntityManager.CreateEntityUninitialized(null, euid);

                    grid.GridEntityId = gridEnt;

                    Logger.DebugS("map", $"Binding grid {actualID} to entity {grid.GridEntityId}");

                    var gridComp = _entityManager.AddComponent<MapGridComponent>(gridEnt);
                    gridComp.GridIndex = grid.Index;

                    //TODO: This is a hack to get TransformComponent.MapId working before entity states
                    //are applied. After they are applied the parent may be different, but the MapId will
                    //be the same. This causes TransformComponent.ParentUid of a grid to be unsafe to
                    //use in transform states anytime before the state parent is properly set.
                    _entityManager.GetComponent<TransformComponent>(gridEnt).AttachParent(GetMapEntityIdOrThrow(currentMapID));

                    _entityManager.InitializeComponents(gridEnt);
                    _entityManager.StartComponents(gridEnt);
                }
            }
            else
            {
                Logger.DebugS("map", $"Skipping entity binding for gridId {actualID}");
            }

            OnGridCreated?.Invoke(currentMapID, actualID);
            return grid;
        }

        public IMapGridInternal CreateGridNoEntity(MapId currentMapID, GridId? gridID = null, ushort chunkSize = 16)
        {
            return CreateGridImpl(currentMapID, gridID, chunkSize, false, default);
        }

        public IMapGrid GetGrid(GridId gridID)
        {
            return _grids[gridID];
        }

        public bool IsGrid(EntityUid uid)
        {
            return _grids.Any(x => x.Value.GridEntityId == uid);
        }

        public bool IsMap(EntityUid uid)
        {
            return _mapEntities.Any(x => x.Value == uid);
        }

        public bool TryGetGrid(GridId gridId, [NotNullWhen(true)] out IMapGrid? grid)
        {
            if (_grids.TryGetValue(gridId, out var gridinterface))
            {
                grid = gridinterface;
                return true;
            }

            grid = null;
            return false;
        }

        public bool GridExists(GridId gridID)
        {
            return _grids.ContainsKey(gridID);
        }

        public IEnumerable<IMapGrid> GetAllMapGrids(MapId mapId)
        {
            return _grids.Values.Where(m => m.ParentMapId == mapId);
        }

        /// <inheritdoc />
        public bool TryFindGridAt(MapId mapId, Vector2 worldPos, [NotNullWhen(true)] out IMapGrid? grid)
        {
            foreach (var (_, mapGrid) in _grids)
            {
                // So not sure if doing the transform for WorldBounds and early out here is faster than just
                // checking if we have a relevant chunk, need to profile.
                if (mapGrid.ParentMapId != mapId)
                    continue;

                // Turn the worldPos into a localPos and work out the relevant chunk we need to check
                // This is much faster than iterating over every chunk individually.
                // (though now we need some extra calcs up front).

                // Doesn't use WorldBounds because it's just an AABB.
                var matrix = _entityManager.GetComponent<TransformComponent>(mapGrid.GridEntityId).InvWorldMatrix;
                var localPos = matrix.Transform(worldPos);

                // NOTE:
                // If you change this to use fixtures instead (i.e. if you want half-tiles) then you need to make sure
                // you account for the fact that fixtures are shrunk slightly!
                var tile = new Vector2i((int) Math.Floor(localPos.X), (int) Math.Floor(localPos.Y));
                var chunkIndices = mapGrid.GridTileToChunkIndices(tile);

                if (!mapGrid.HasChunk(chunkIndices)) continue;

                var chunk = mapGrid.GetChunk(chunkIndices);
                var chunkTile = chunk.GetTileRef(chunk.GridTileToChunkTile(tile));

                if (chunkTile.Tile.IsEmpty) continue;
                grid = mapGrid;
                return true;
            }

            grid = null;
            return false;
        }

        /// <inheritdoc />
        public bool TryFindGridAt(MapCoordinates mapCoordinates, [NotNullWhen(true)] out IMapGrid? grid)
        {
            return TryFindGridAt(mapCoordinates.MapId, mapCoordinates.Position, out grid);
        }

        public void FindGridsIntersectingEnumerator(MapId mapId, Box2 worldAABB, out FindGridsEnumerator enumerator, bool approx = false)
        {
            enumerator = new FindGridsEnumerator(_entityManager, _grids.GetEnumerator(), mapId, worldAABB, approx);
        }

        /// <inheritdoc />
        public IEnumerable<IMapGrid> FindGridsIntersecting(MapId mapId, Box2 worldAABB, bool approx = false)
        {
            // So despite the fact we have accurate bounds the reason I didn't make this tile-based is because
            // at some stage we may want to overwrite the default behavior e.g. if you allow diagonals
            foreach (var (_, grid) in _grids)
            {
                if (grid.ParentMapId != mapId) continue;

                var xformComp = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
                var invMatrix3 = xformComp.InvWorldMatrix;
                var localAABB = invMatrix3.TransformBox(worldAABB);

                if (!localAABB.Intersects(grid.LocalBounds)) continue;

                var intersects = false;

                if (_entityManager.HasComponent<PhysicsComponent>(grid.GridEntityId))
                {
                    grid.GetLocalMapChunks(localAABB, out var enumerator);

                    if (!approx)
                    {
                        var (worldPos, worldRot) = xformComp.GetWorldPositionRotation();

                        var transform = new Transform(worldPos, worldRot);

                        while (!intersects && enumerator.MoveNext(out var chunk))
                        {
                            foreach (var fixture in chunk.Fixtures)
                            {
                                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                                {
                                    // TODO: Need to use collisionmanager for overlapsies
                                    if (!fixture.Shape.ComputeAABB(transform, i).Intersects(worldAABB)) continue;

                                    intersects = true;
                                    break;
                                }

                                if (intersects) break;
                            }
                        }
                    }
                    else
                    {
                        intersects = enumerator.MoveNext(out _);
                    }
                }

                if (intersects || grid.ChunkCount == 0 && worldAABB.Contains(xformComp.WorldPosition))
                {
                    yield return grid;
                }
            }
        }

        /// <inheritdoc />
        public IEnumerable<IMapGrid> FindGridsIntersecting(MapId mapId, Box2Rotated worldArea, bool approx = false)
        {
            var worldAABB = worldArea.CalcBoundingBox();

            foreach (var (_, grid) in _grids)
            {
                if (grid.ParentMapId != mapId) continue;

                var xformComp = _entityManager.GetComponent<TransformComponent>(grid.GridEntityId);
                var invMatrix3 = xformComp.InvWorldMatrix;
                var localAABB = invMatrix3.TransformBox(worldAABB);

                if (!localAABB.Intersects(grid.LocalBounds)) continue;

                var intersects = false;

                if (_entityManager.HasComponent<PhysicsComponent>(grid.GridEntityId))
                {
                    grid.GetLocalMapChunks(localAABB, out var enumerator);

                    if (!approx)
                    {
                        var (worldPos, worldRot) = xformComp.GetWorldPositionRotation();
                        var transform = new Transform(worldPos, worldRot);

                        while (!intersects && enumerator.MoveNext(out var chunk))
                        {
                            foreach (var fixture in chunk.Fixtures)
                            {
                                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                                {
                                    if (!fixture.Shape.ComputeAABB(transform, i).Intersects(worldAABB)) continue;

                                    intersects = true;
                                    break;
                                }

                                if (intersects) break;
                            }
                        }
                    }
                    else
                    {
                        intersects = enumerator.MoveNext(out _);
                    }
                }

                if (intersects || grid.ChunkCount == 0 && worldAABB.Contains(xformComp.WorldPosition))
                {
                    yield return grid;
                }
            }
        }

        public virtual void DeleteGrid(GridId gridID)
        {
#if DEBUG
            DebugTools.Assert(_dbgGuardRunning);
#endif
            // Possible the grid was already deleted / is invalid
            if (!_grids.TryGetValue(gridID, out var grid) || grid.Deleting)
                return;

            grid.Deleting = true;

            var mapId = grid.ParentMapId;

            var entityId = grid.GridEntityId;

            if (_entityManager.EntityExists(entityId))
            {
                // DeleteGrid may be triggered by the entity being deleted,
                // so make sure that's not the case.
                if (_entityManager.GetComponent<MetaDataComponent>(entityId).EntityLifeStage <= EntityLifeStage.MapInitialized)
                    _entityManager.DeleteEntity(entityId);
            }

            grid.Dispose();
            _grids.Remove(grid.Index);

            Logger.DebugS("map", $"Deleted grid {gridID}");
            OnGridRemoved?.Invoke(mapId, gridID);
        }

        public MapId NextMapId()
        {
            return HighestMapID = new MapId(HighestMapID.Value + 1);
        }

        public GridId NextGridId()
        {
            return HighestGridID = new GridId(HighestGridID.Value + 1);
        }

        protected void InvokeGridChanged(object? sender, GridChangedEventArgs ev)
        {
            GridChanged?.Invoke(sender, ev);
        }
    }

    /// <summary>
    ///     Arguments for when a map is created or deleted locally ore remotely.
    /// </summary>
    public class MapEventArgs : EventArgs
    {
        /// <summary>
        ///     Map that is being modified.
        /// </summary>
        public MapId Map { get; }

        /// <summary>
        ///     Creates a new instance of this class.
        /// </summary>
        public MapEventArgs(MapId map)
        {
            Map = map;
        }
    }

    /// <summary>
    ///     Arguments for when a single tile on a grid is changed locally or remotely.
    /// </summary>
    public class TileChangedEventArgs : EventArgs
    {
        /// <summary>
        ///     New tile that replaced the old one.
        /// </summary>
        public TileRef NewTile { get; }

        /// <summary>
        ///     Old tile that was replaced.
        /// </summary>
        public Tile OldTile { get; }

        /// <summary>
        ///     Creates a new instance of this class.
        /// </summary>
        public TileChangedEventArgs(TileRef newTile, Tile oldTile)
        {
            NewTile = newTile;
            OldTile = oldTile;
        }
    }

    /// <summary>
    ///     Arguments for when a one or more tiles on a grid is changed at once.
    /// </summary>
    public class GridChangedEventArgs : EventArgs
    {
        /// <summary>
        ///     Grid being changed.
        /// </summary>
        public IMapGrid Grid { get; }

        public IReadOnlyCollection<(Vector2i position, Tile tile)> Modified { get; }

        /// <summary>
        ///     Creates a new instance of this class.
        /// </summary>
        public GridChangedEventArgs(IMapGrid grid, IReadOnlyCollection<(Vector2i position, Tile tile)> modified)
        {
            Grid = grid;
            Modified = modified;
        }
    }
}
