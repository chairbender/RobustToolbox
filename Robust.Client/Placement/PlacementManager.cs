﻿using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Client.Interfaces;
using Robust.Client.Interfaces.Graphics.ClientEye;
using Robust.Client.Interfaces.Input;
using Robust.Client.Interfaces.Placement;
using Robust.Client.Interfaces.ResourceManagement;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Physics;
using Robust.Shared.Interfaces.Reflection;
using Robust.Shared.Interfaces.Timing;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Map;
using Robust.Shared.Network.Messages;
using Robust.Client.Graphics;
using Robust.Client.GameObjects;
using Robust.Client.GameObjects.EntitySystems;
using Robust.Client.Graphics.Drawing;
using Robust.Client.Interfaces.Graphics;
using Robust.Client.Interfaces.Graphics.Overlays;
using Robust.Client.Player;
using Robust.Shared.Input.Binding;
using Robust.Shared.Utility;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Robust.Client.Placement
{
    public partial class PlacementManager : IPlacementManager, IDisposable
    {
#pragma warning disable 649
        [Dependency]
        public readonly IPhysicsManager PhysicsManager;
        [Dependency]
        private readonly IClientNetManager NetworkManager;
        [Dependency]
        public readonly IPlayerManager PlayerManager;
        [Dependency]
        public readonly IResourceCache ResourceCache;
        [Dependency]
        private readonly IReflectionManager ReflectionManager;
        [Dependency]
        public readonly IMapManager MapManager;
        [Dependency]
        private readonly IGameTiming _time;
        [Dependency]
        public readonly IEyeManager eyeManager;
        [Dependency]
        private readonly IInputManager _inputManager;
        [Dependency]
        private readonly IEntitySystemManager _entitySystemManager;
        [Dependency]
        private readonly IEntityManager _entityManager;
        [Dependency]
        private readonly IPrototypeManager _prototypeManager;
        [Dependency]
        private readonly IBaseClient _baseClient;
        [Dependency]
        private readonly IOverlayManager _overlayManager;
        [Dependency]
        public readonly IClyde _clyde;
#pragma warning restore 649

        /// <summary>
        ///     How long before a pending tile change is dropped.
        /// </summary>
        private static readonly TimeSpan _pendingTileTimeout = TimeSpan.FromSeconds(2.0);

        /// <summary>
        /// Dictionary of all placement mode types
        /// </summary>
        private readonly Dictionary<string, Type> _modeDictionary = new Dictionary<string, Type>();
        private readonly List<Tuple<GridCoordinates, TimeSpan>> _pendingTileChanges = new List<Tuple<GridCoordinates, TimeSpan>>();

        /// <summary>
        /// Tells this system to try to handle placement of an entity during the next frame
        /// </summary>
        private bool _placenextframe;

        /// <summary>
        /// Allows various types of placement as singular, line, or grid placement where placement mode allows this type of placement
        /// </summary>
        public PlacementTypes PlacementType { get; set; }

        /// <summary>
        /// Holds the anchor that we can try to spawn in a line or a grid from
        /// </summary>
        public GridCoordinates StartPoint { get; set; }

        /// <summary>
        /// Whether the placement manager is currently in a mode where it accepts actions
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            private set
            {
                _isActive = value;
                SwitchEditorContext(value);
            }
        }

        /// <summary>
        /// Determines whether we are using the mode to delete an entity on click
        /// </summary>
        public bool Eraser { get; private set; }

        /// <summary>
        /// The texture we use to show from our placement manager to represent the entity to place
        /// </summary>
        public IDirectionalTextureProvider CurrentBaseSprite { get; set; }

        /// <summary>
        /// Which of the placement orientations we are trying to place with
        /// </summary>
        public PlacementMode CurrentMode { get; set; }

        public PlacementInformation CurrentPermission { get; set; }

        public PlacementHijack Hijack { get; private set; }

        private EntityPrototype _currentPrototype;

        /// <summary>
        /// The prototype of the entity we are going to spawn on click
        /// </summary>
        public EntityPrototype CurrentPrototype
        {
            get => _currentPrototype;
            set
            {
                _currentPrototype = value;

                if (value != null)
                {
                    PlacementOffset = value.PlacementOffset;

                    if (value.Components.ContainsKey("BoundingBox") && value.Components.ContainsKey("Collidable"))
                    {
                        var map = value.Components["BoundingBox"];
                        var serializer = YamlObjectSerializer.NewReader(map);
                        serializer.DataField(ref _colliderAABB, "aabb", new Box2(0f, 0f, 0f, 0f));
                        return;
                    }
                }

                _colliderAABB = new Box2(0f, 0f, 0f, 0f);
            }
        }

        public Vector2i PlacementOffset { get; set; }


        private Box2 _colliderAABB = new Box2(0f, 0f, 0f, 0f);

        /// <summary>
        /// The box which certain placement modes collision checks will be done against
        /// </summary>
        public Box2 ColliderAABB
        {
            get => _colliderAABB;
            set => _colliderAABB = value;
        }

        /// <summary>
        /// The directional to spawn the entity in
        /// </summary>
        public Direction Direction { get; set; } = Direction.South;

        private PlacementOverlay _drawOverlay;
        private bool _isActive;

        public void Initialize()
        {
            NetworkManager.RegisterNetMessage<MsgPlacement>(MsgPlacement.NAME, HandlePlacementMessage);

            _modeDictionary.Clear();
            foreach (var type in ReflectionManager.GetAllChildren<PlacementMode>())
            {
                _modeDictionary.Add(type.Name, type);
            }

            MapManager.TileChanged += HandleTileChanged;

            _drawOverlay = new PlacementOverlay(this);
            _overlayManager.AddOverlay(_drawOverlay);

            // a bit ugly, oh well
            _baseClient.PlayerJoinedServer += (sender, args) => SetupInput(_entitySystemManager);
            _baseClient.PlayerLeaveServer += (sender, args) => TearDownInput(_entitySystemManager);
        }

        private void SetupInput(IEntitySystemManager entSysMan)
        {
            var inputSys = entSysMan.GetEntitySystem<InputSystem>();

            inputSys.BindMap.BindFunction(EngineKeyFunctions.EditorLinePlace, InputCmdHandler.FromDelegate(
                session =>
                {
                    if (IsActive && !Eraser) ActivateLineMode();
                }));
            inputSys.BindMap.BindFunction(EngineKeyFunctions.EditorGridPlace, InputCmdHandler.FromDelegate(
                session =>
                {
                    if (IsActive && !Eraser) ActivateGridMode();
                }));

            inputSys.BindMap.BindFunction(EngineKeyFunctions.EditorPlaceObject, new PointerStateInputCmdHandler(
                (session, coords, uid) =>
                {
                    if (!IsActive)
                        return false;

                    if (Eraser)
                    {
                        if (uid == EntityUid.Invalid)
                        {
                            return false;
                        }
                        HandleDeletion(_entityManager.GetEntity(uid));
                    }
                    else
                    {
                        _placenextframe = true;
                    }

                    return true;
                },
                (session, coords, uid) =>
                {
                    if (!IsActive || Eraser || !_placenextframe)
                        return false;

                    //Places objects for non-tile entities
                    if (!CurrentPermission.IsTile)
                        HandlePlacement();

                    _placenextframe = false;
                    return true;
                }));
            inputSys.BindMap.BindFunction(EngineKeyFunctions.EditorRotateObject, InputCmdHandler.FromDelegate(
                session =>
                {
                    if (IsActive && !Eraser) Rotate();
                }));
            inputSys.BindMap.BindFunction(EngineKeyFunctions.EditorCancelPlace, InputCmdHandler.FromDelegate(
                session =>
                {
                    if (!IsActive || Eraser)
                        return;
                    if (DeactivateSpecialPlacement())
                        return;
                    Clear();
                }));

            var localPlayer = PlayerManager.LocalPlayer;
            localPlayer.EntityAttached += OnEntityAttached;
        }

        private void TearDownInput(IEntitySystemManager entSysMan)
        {
            if (entSysMan.TryGetEntitySystem(out InputSystem inputSys))
            {
                inputSys.BindMap.UnbindFunction(EngineKeyFunctions.EditorLinePlace);
                inputSys.BindMap.UnbindFunction(EngineKeyFunctions.EditorGridPlace);
                inputSys.BindMap.UnbindFunction(EngineKeyFunctions.EditorPlaceObject);
                inputSys.BindMap.UnbindFunction(EngineKeyFunctions.EditorRotateObject);
                inputSys.BindMap.UnbindFunction(EngineKeyFunctions.EditorCancelPlace);
            }

            if (PlayerManager.LocalPlayer != null)
            {
                PlayerManager.LocalPlayer.EntityAttached -= OnEntityAttached;
            }
        }

        private void OnEntityAttached(EntityAttachedEventArgs eventArgs)
        {
            // player attached to a new entity, basically disable the editor
            Clear();
        }

        private void SwitchEditorContext(bool enabled)
        {
            if (enabled)
            {
                _inputManager.Contexts.SetActiveContext("editor");
            }
            else
            {
                _entitySystemManager.GetEntitySystem<InputSystem>().SetEntityContextActive();
            }
        }

        public void Dispose()
        {
            _drawOverlay?.Dispose();
        }

        private void HandlePlacementMessage(MsgPlacement msg)
        {
            switch (msg.PlaceType)
            {
                case PlacementManagerMessage.StartPlacement:
                    HandleStartPlacement(msg);
                    break;
                case PlacementManagerMessage.CancelPlacement:
                    Clear();
                    break;
            }
        }

        private void HandleTileChanged(object sender, TileChangedEventArgs args)
        {
            var coords = MapManager.GetGrid(args.NewTile.GridIndex).GridTileToLocal(args.NewTile.GridIndices);
            _pendingTileChanges.RemoveAll(c => c.Item1 == coords);
        }

        public event EventHandler PlacementCanceled;

        public void Clear()
        {
            Hijack = null;
            CurrentBaseSprite = null;
            CurrentPrototype = null;
            CurrentPermission = null;
            CurrentMode = null;
            DeactivateSpecialPlacement();
            if (PlacementCanceled != null && IsActive && !Eraser) PlacementCanceled(this, null);
            _placenextframe = false;
            IsActive = false;
            Eraser = false;
            PlacementOffset = Vector2i.Zero;
        }

        public void Rotate()
        {
            switch (Direction)
            {
                case Direction.North:
                    Direction = Direction.East;
                    break;
                case Direction.East:
                    Direction = Direction.South;
                    break;
                case Direction.South:
                    Direction = Direction.West;
                    break;
                case Direction.West:
                    Direction = Direction.North;
                    break;
            }

            CurrentMode?.SetSprite();
        }

        public void HandlePlacement()
        {
            if (!IsActive || Eraser)
                return;

            switch (PlacementType)
            {
                case PlacementTypes.None:
                    RequestPlacement(CurrentMode.MouseCoords);
                    break;
                case PlacementTypes.Line:
                    foreach (var coordinate in CurrentMode.LineCoordinates())
                    {
                        RequestPlacement(coordinate);
                    }

                    DeactivateSpecialPlacement();
                    break;
                case PlacementTypes.Grid:
                    foreach (var coordinate in CurrentMode.GridCoordinates())
                    {
                        RequestPlacement(coordinate);
                    }

                    DeactivateSpecialPlacement();
                    break;
            }
        }

        public void HandleDeletion(IEntity entity)
        {
            if (!IsActive || !Eraser) return;
            if (Hijack != null && Hijack.HijackDeletion(entity)) return;

            var msg = NetworkManager.CreateNetMessage<MsgPlacement>();
            msg.PlaceType = PlacementManagerMessage.RequestEntRemove;
            msg.EntityUid = entity.Uid;
            NetworkManager.ClientSendMessage(msg);
        }

        public void ToggleEraser()
        {
            if (!Eraser && !IsActive)
            {
                IsActive = true;
                Eraser = true;
            }
            else Clear();
        }

        public void ToggleEraserHijacked(PlacementHijack hijack)
        {
            if (!Eraser && !IsActive)
            {
                IsActive = true;
                Eraser = true;
                Hijack = hijack;
            }
            else Clear();
        }

        public void BeginPlacing(PlacementInformation info)
        {
            BeginHijackedPlacing(info);
        }

        public void BeginHijackedPlacing(PlacementInformation info, PlacementHijack hijack = null)
        {
            Clear();

            CurrentPermission = info;

            if (!_modeDictionary.Any(pair => pair.Key.Equals(CurrentPermission.PlacementOption)))
            {
                Clear();
                return;
            }

            var modeType = _modeDictionary.First(pair => pair.Key.Equals(CurrentPermission.PlacementOption)).Value;
            CurrentMode = (PlacementMode) Activator.CreateInstance(modeType, this);

            if (hijack != null)
            {
                Hijack = hijack;
                Hijack.StartHijack(this);
                IsActive = true;
                return;
            }

            if (info.IsTile)
                PreparePlacementTile();
            else
                PreparePlacement(info.EntityType);
        }

        private bool CurrentMousePosition(out ScreenCoordinates coordinates)
        {
            // Try to get current map.
            var map = MapId.Nullspace;
            var ent = PlayerManager.LocalPlayer.ControlledEntity;
            if (ent != null)
            {
                map = ent.Transform.MapID;
            }

            if (map == MapId.Nullspace || CurrentPermission == null || CurrentMode == null)
            {
                coordinates = new ScreenCoordinates(Vector2.Zero);
                return false;
            }

            coordinates = new ScreenCoordinates(_inputManager.MouseScreenPosition);
            return true;
        }

        /// <inheritdoc />
        public void FrameUpdate(FrameEventArgs e)
        {
            if (!CurrentMousePosition(out var mouseScreen))
                return;

            CurrentMode.AlignPlacementMode(mouseScreen);

            // purge old unapproved tile changes
            _pendingTileChanges.RemoveAll(c => c.Item2 < _time.RealTime);

            // continues tile placement but placement of entities only occurs on mouseUp
            if (_placenextframe && CurrentPermission.IsTile)
                HandlePlacement();
        }

        private void ActivateLineMode()
        {
            if (!CurrentMode.HasLineMode)
                return;

            if (!CurrentMousePosition(out var mouseScreen))
                return;

            CurrentMode.AlignPlacementMode(mouseScreen);
            StartPoint = CurrentMode.MouseCoords;
            PlacementType = PlacementTypes.Line;
        }

        private void ActivateGridMode()
        {
            if (!CurrentMode.HasGridMode)
                return;

            if (!CurrentMousePosition(out var mouseScreen))
                return;

            CurrentMode.AlignPlacementMode(mouseScreen);
            StartPoint = CurrentMode.MouseCoords;
            PlacementType = PlacementTypes.Grid;
        }

        private bool DeactivateSpecialPlacement()
        {
            if (PlacementType == PlacementTypes.None)
                return false;

            PlacementType = PlacementTypes.None;
            return true;
        }

        private void Render(DrawingHandleWorld handle)
        {
            if (CurrentMode == null || !IsActive)
                return;

            CurrentMode.Render(handle);

            if (CurrentPermission == null || CurrentPermission.Range <= 0 || !CurrentMode.RangeRequired)
                return;

            var worldPos = PlayerManager.LocalPlayer.ControlledEntity.Transform.WorldPosition;

            handle.DrawCircle(worldPos, CurrentPermission.Range, new Color(1, 1, 1, 0.25f));
        }

        private void HandleStartPlacement(MsgPlacement msg)
        {
            CurrentPermission = new PlacementInformation
            {
                Range = msg.Range,
                IsTile = msg.IsTile,
            };

            CurrentPermission.EntityType = msg.ObjType; // tile or ent type
            CurrentPermission.PlacementOption = msg.AlignOption;

            BeginPlacing(CurrentPermission);
        }

        private void PreparePlacement(string templateName)
        {
            var prototype = _prototypeManager.Index<EntityPrototype>(templateName);

            CurrentBaseSprite = IconComponent.GetPrototypeIcon(prototype, ResourceCache);
            CurrentPrototype = prototype;

            IsActive = true;
        }

        private void PreparePlacementTile()
        {
            CurrentBaseSprite = ResourceCache
                .GetResource<TextureResource>(new ResourcePath("/Textures/UserInterface/tilebuildoverlay.png")).Texture;

            IsActive = true;
        }

        private void RequestPlacement(GridCoordinates coordinates)
        {
            if (MapManager.GetGrid(coordinates.GridID).ParentMapId == MapId.Nullspace) return;
            if (CurrentPermission == null) return;
            if (!CurrentMode.IsValidPosition(coordinates)) return;
            if (Hijack != null && Hijack.HijackPlacementRequest(coordinates)) return;

            if (CurrentPermission.IsTile)
            {
                var grid = MapManager.GetGrid(coordinates.GridID);

                // no point changing the tile to the same thing.
                if (grid.GetTileRef(coordinates).Tile.TypeId == CurrentPermission.TileType)
                    return;

                foreach (var tileChange in _pendingTileChanges)
                {
                    // if change already pending, ignore it
                    if (tileChange.Item1 == coordinates)
                        return;
                }

                var tuple = new Tuple<GridCoordinates, TimeSpan>(coordinates, _time.RealTime + _pendingTileTimeout);
                _pendingTileChanges.Add(tuple);
            }

            var message = NetworkManager.CreateNetMessage<MsgPlacement>();
            message.PlaceType = PlacementManagerMessage.RequestPlacement;

            message.Align = CurrentMode.ModeName;
            message.IsTile = CurrentPermission.IsTile;

            if (CurrentPermission.IsTile)
                message.TileType = CurrentPermission.TileType;
            else
                message.EntityTemplateName = CurrentPermission.EntityType;

            // world x and y
            message.GridCoordinates = coordinates;

            message.DirRcv = Direction;

            NetworkManager.ClientSendMessage(message);
        }

        public enum PlacementTypes
        {
            None = 0,
            Line = 1,
            Grid = 2
        }
    }
}
