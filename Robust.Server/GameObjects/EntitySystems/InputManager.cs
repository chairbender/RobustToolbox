﻿using System;
using System.Collections.Generic;
using Robust.Server.Interfaces.Player;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.Input;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.IoC;

namespace Robust.Server.GameObjects.EntitySystems
{
    /// <summary>
    ///     Server side processing of incoming user commands.
    /// </summary>
    public class InputManager : SharedInputManager, IInputManager, IPostInjectInit, IEntityEventSubscriber
    {
#pragma warning disable 649
        [Dependency] private readonly IPlayerManager _playerManager;
        [Dependency] private readonly IEntityManager _entityManager;
#pragma warning restore 649

        private readonly Dictionary<IPlayerSession, IPlayerCommandStates> _playerInputs = new Dictionary<IPlayerSession, IPlayerCommandStates>();
        private readonly CommandBindMapping _bindMap = new CommandBindMapping();

        private readonly Dictionary<IPlayerSession, uint> _lastProcessedInputCmd = new Dictionary<IPlayerSession, uint>();

        /// <summary>
        ///     Server side input command binds.
        /// </summary>
        public override ICommandBindMapping BindMap => _bindMap;

        /// <inheritdoc />
        public void PostInject()
        {
            _entityManager.EventBus.SubscribeSessionEvent<FullInputCmdMessage>(EventSource.Network, this, InputMessageHandler);
            _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
        }

        private void HandleCommandMessage(IPlayerSession session, InputCmdHandler cmdHandler, FullInputCmdMessage msg)
        {
            cmdHandler.HandleCmdMessage(session, msg);
        }

        private void InputMessageHandler(InputCmdMessage message, EntitySessionEventArgs eventArgs)
        {
            if (!(message is FullInputCmdMessage msg))
                return;

            //Client Sanitization: out of bounds functionID
            if (!_playerManager.KeyMap.TryGetKeyFunction(msg.InputFunctionId, out var function))
                return;

            //Client Sanitization: bad enum key state value
            if (!Enum.IsDefined(typeof(BoundKeyState), msg.State))
                return;

            var session = (IPlayerSession) eventArgs.SenderSession;

            if (_lastProcessedInputCmd[session] < msg.InputSequence)
                _lastProcessedInputCmd[session] = msg.InputSequence;

            // route the cmdMessage to the proper bind
            //Client Sanitization: unbound command, just ignore
            if (_bindMap.TryGetHandler(function, out var cmdHandler))
            {
                // set state, only bound key functions get state changes
                var states = GetInputStates(session);
                states.SetState(function, msg.State);

                HandleCommandMessage(session, cmdHandler, msg);
            }
        }

        public IPlayerCommandStates GetInputStates(IPlayerSession session)
        {
            return _playerInputs[session];
        }

        public uint GetLastInputCommand(IPlayerSession session)
        {
            return _lastProcessedInputCmd[session];
        }

        private void OnPlayerStatusChanged(object sender, SessionStatusEventArgs args)
        {
            switch (args.NewStatus)
            {
                case SessionStatus.Connected:
                    _playerInputs.Add(args.Session, new PlayerCommandStates());
                    _lastProcessedInputCmd.Add(args.Session, 0);
                    break;

                case SessionStatus.Disconnected:
                    _playerInputs.Remove(args.Session);
                    _lastProcessedInputCmd.Remove(args.Session);
                    break;
            }
        }
    }
}
