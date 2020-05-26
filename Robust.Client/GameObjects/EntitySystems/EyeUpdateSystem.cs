﻿using System;
using JetBrains.Annotations;
using Robust.Client.Interfaces.Graphics.ClientEye;
using Robust.Client.ViewVariables;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Systems;
using Robust.Shared.Input.Binding;
using Robust.Shared.Interfaces.GameObjects.Systems;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

#nullable enable

namespace Robust.Client.GameObjects.EntitySystems
{
    /// <summary>
    /// Updates the position of every Eye every frame, so that the camera follows the player around.
    /// </summary>
    [UsedImplicitly]
    internal class EyeUpdateSystem : EntitySystem
    {
        // How fast the camera rotates in radians
        private const float CameraRotateSpeed = MathF.PI;
        private const float CameraSnapTolerance = 0.01f;

#pragma warning disable 649, CS8618
        // ReSharper disable once NotNullMemberIsNotInitialized
        [Dependency] private readonly IEyeManager _eyeManager;
#pragma warning restore 649, CS8618

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            EntityQuery = new TypeEntityQuery(typeof(EyeComponent));

            //WARN: Tightly couples this system with InputSystem, and assumes InputSystem exists and  is initialized
            var inputSystem = EntitySystemManager.GetEntitySystem<InputSystem>();
            inputSystem.BindMap.BindFunction(EngineKeyFunctions.CameraRotateRight, new NullInputCmdHandler());
            inputSystem.BindMap.BindFunction(EngineKeyFunctions.CameraRotateLeft, new NullInputCmdHandler());
        }

        /// <inheritdoc />
        public override void Shutdown()
        {
            //WARN: Tightly couples this system with InputSystem, and assumes InputSystem exists and is initialized
            var inputSystem = EntitySystemManager.GetEntitySystem<InputSystem>();
            inputSystem.BindMap.UnbindFunction(EngineKeyFunctions.CameraRotateRight);
            inputSystem.BindMap.UnbindFunction(EngineKeyFunctions.CameraRotateLeft);

            base.Shutdown();
        }

        /// <inheritdoc />
        public override void FrameUpdate(float frameTime)
        {
            var currentEye = _eyeManager.CurrentEye;
            var inputSystem = EntitySystemManager.GetEntitySystem<InputSystem>();

            var direction = 0;
            if (inputSystem.CmdStates[EngineKeyFunctions.CameraRotateRight] == BoundKeyState.Down)
            {
                direction += 1;
            }

            if (inputSystem.CmdStates[EngineKeyFunctions.CameraRotateLeft] == BoundKeyState.Down)
            {
                direction -= 1;
            }

            // apply camera rotation
            if(direction != 0)
            {
                currentEye.Rotation += CameraRotateSpeed * frameTime * direction;
                currentEye.Rotation = currentEye.Rotation.Reduced();
            }
            else
            {
                // snap to cardinal directions
                var closestDir = currentEye.Rotation.GetCardinalDir().ToVec();
                var currentDir = currentEye.Rotation.ToVec();

                var dot = Vector2.Dot(closestDir, currentDir);
                if (FloatMath.CloseTo(dot, 1, CameraSnapTolerance))
                {
                    currentEye.Rotation = closestDir.ToAngle();
                }
            }

            foreach (var entity in RelevantEntities)
            {
                var eyeComp = entity.GetComponent<EyeComponent>();
                eyeComp.UpdateEyePosition();
            }
        }
    }
}
