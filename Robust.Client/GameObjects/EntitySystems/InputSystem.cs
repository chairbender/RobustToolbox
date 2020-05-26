﻿using System;
using System.Collections.Generic;
using Robust.Client.GameObjects.Components;
using Robust.Client.Interfaces.GameStates;
using Robust.Client.Interfaces.Input;
using Robust.Client.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Players;
using Robust.Shared.Utility;

namespace Robust.Client.GameObjects.EntitySystems
{
    /// <summary>
    ///     Client-side processing of all input commands through the simulation.
    /// </summary>
    public class InputSystem : EntitySystem
    {
#pragma warning disable 649
        [Dependency] private readonly IInputManager _inputManager;
        [Dependency] private readonly IPlayerManager _playerManager;
        [Dependency] private readonly IClientGameStateManager _stateManager;
#pragma warning restore 649

        private readonly IPlayerCommandStates _cmdStates = new PlayerCommandStates();
        private readonly CommandBindRegistry _bindMap = new CommandBindRegistry();

        /// <summary>
        ///     Current states for all of the keyFunctions.
        /// </summary>
        public IPlayerCommandStates CmdStates => _cmdStates;

        /// <summary>
        ///     Holds the keyFunction -> handler bindings for the simulation.
        /// </summary>
        public ICommandBindRegistry BindMap => _bindMap;

        /// <summary>
        /// If the input system is currently predicting input.
        /// </summary>
        public bool Predicted { get; private set; }

        /// <summary>
        ///     Inserts an Input Command into the simulation.
        /// </summary>
        /// <param name="session">Player session that raised the command. On client, this is always the LocalPlayer session.</param>
        /// <param name="function">Function that is being changed.</param>
        /// <param name="message">Arguments for this event.</param>
        public void HandleInputCommand(ICommonSession session, BoundKeyFunction function, FullInputCmdMessage message)
        {
            #if DEBUG

            var funcId = _inputManager.NetworkBindMap.KeyFunctionID(function);
            DebugTools.Assert(funcId == message.InputFunctionId, "Function ID in message does not match function.");

            #endif

            // set state, state change is updated regardless if it is locally bound
            if (_cmdStates.GetState(function) == message.State)
            {
                return;
            }

            _cmdStates.SetState(function, message.State);

            // handle local binds before sending off
            if (_bindMap.TryGetHandler(function, out var handler))
            {
                // local handlers can block sending over the network.
                if (handler.HandleCmdMessage(session, message))
                    return;
            }

            // send it off to the client
            DispatchInputCommand(message);
        }

        /// <summary>
        /// Handle a predicted input command.
        /// </summary>
        /// <param name="inputCmd">Input command to handle as predicted.</param>
        public void PredictInputCommand(FullInputCmdMessage inputCmd)
        {
            var keyFunc = _inputManager.NetworkBindMap.KeyFunctionName(inputCmd.InputFunctionId);

            if (!_bindMap.TryGetHandler(keyFunc, out var handler))
                return;

            Predicted = true;

            var session = _playerManager.LocalPlayer.Session;
            handler.HandleCmdMessage(session, inputCmd);

            Predicted = false;
        }

        private void DispatchInputCommand(FullInputCmdMessage message)
        {
            _stateManager.InputCommandDispatched(message);
            RaiseNetworkEvent(message);
        }

        public override void Initialize()
        {
            SubscribeLocalEvent<PlayerAttachSysMessage>(OnAttachedEntityChanged);
        }

        private void OnAttachedEntityChanged(PlayerAttachSysMessage message)
        {
            if (message.AttachedEntity != null) // attach
            {
                SetEntityContextActive(_inputManager, message.AttachedEntity);
            }
            else // detach
            {
                _inputManager.Contexts.SetActiveContext(InputContextContainer.DefaultContextName);
            }
        }

        private static void SetEntityContextActive(IInputManager inputMan, IEntity entity)
        {
            if(entity == null || !entity.IsValid())
                throw new ArgumentNullException(nameof(entity));

            if (!entity.TryGetComponent(out InputComponent inputComp))
            {
                Logger.DebugS("input.context", $"AttachedEnt has no InputComponent: entId={entity.Uid}, entProto={entity.Prototype}");
                return;
            }

            if (inputMan.Contexts.Exists(inputComp.ContextName))
            {
                inputMan.Contexts.SetActiveContext(inputComp.ContextName);
            }
            else
            {
                Logger.ErrorS("input.context", $"Unknown context: entId={entity.Uid}, entProto={entity.Prototype}, context={inputComp.ContextName}");
            }
        }

        /// <summary>
        ///     Sets the active context to the defined context on the attached entity.
        /// </summary>
        public void SetEntityContextActive()
        {
            if (_playerManager.LocalPlayer.ControlledEntity == null)
            {
                return;
            }

            SetEntityContextActive(_inputManager, _playerManager.LocalPlayer.ControlledEntity);
        }
    }

    /// <summary>
    ///     Entity system message that is raised when the player changes attached entities.
    /// </summary>
    public class PlayerAttachSysMessage : EntitySystemMessage
    {
        /// <summary>
        ///     New entity the player is attached to.
        /// </summary>
        public IEntity AttachedEntity { get; }

        /// <summary>
        ///     Creates a new instance of <see cref="PlayerAttachSysMessage"/>.
        /// </summary>
        /// <param name="attachedEntity">New entity the player is attached to.</param>
        public PlayerAttachSysMessage(IEntity attachedEntity)
        {
            AttachedEntity = attachedEntity;
        }
    }
}
