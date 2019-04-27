﻿using Robust.Client.Graphics.ClientEye;
using Robust.Client.Interfaces;
using Robust.Client.Interfaces.GameObjects.Components;
using Robust.Client.Utility;
using Robust.Shared.GameObjects.Components.Transform;
using Robust.Shared.Interfaces.GameObjects.Components;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Robust.Client.GameObjects
{
    internal class GodotTransformComponent : TransformComponent, IGodotTransformComponent
    {
        public Godot.Node2D SceneNode { get; private set; }

        IGodotTransformComponent IGodotTransformComponent.Parent => (IGodotTransformComponent)Parent;

        private bool visibleWhileParented;

        [Dependency] private readonly ISceneTreeHolder _sceneTreeHolder;

        public override bool VisibleWhileParented
        {
            get => visibleWhileParented;
            set
            {
                visibleWhileParented = value;
                UpdateSceneVisibility();
            }
        }

        protected override void SetPosition(Vector2 position)
        {
            base.SetPosition(position);
            SceneNode.Position = (position * EyeManager.PIXELSPERMETER * new Vector2(1, -1)).Rounded().Convert();
        }

        protected override void SetRotation(Angle rotation)
        {
            base.SetRotation(rotation);
            SceneNode.Rotation = -(float) rotation + MathHelper.PiOver2;
        }

        private void UpdateSceneVisibility()
        {
            SceneNode.Visible = VisibleWhileParented || IsMapTransform;
        }

        public override void AttachParent(ITransformComponent parent)
        {
            if (parent == null)
            {
                return;
            }

            base.AttachParent(parent);
            SceneNode.GetParent().RemoveChild(SceneNode);
            ((IGodotTransformComponent)parent).SceneNode.AddChild(SceneNode);
            UpdateSceneVisibility();
        }

        public override void DetachParent()
        {
            if (Parent == null)
            {
                return;
            }

            ((IGodotTransformComponent)Parent)?.SceneNode?.RemoveChild(SceneNode);
            base.DetachParent();
            _sceneTreeHolder.WorldRoot.AddChild(SceneNode);
            UpdateSceneVisibility();
        }

        public override void OnAdd()
        {
            base.OnAdd();
            SceneNode = new Godot.Node2D
            {
                Name = $"Transform {Owner.Uid} ({Owner.Name})",
                Rotation = MathHelper.PiOver2
            };
            _sceneTreeHolder.WorldRoot.AddChild(SceneNode);
        }

        public override void OnRemove()
        {
            base.OnRemove();

            foreach (var child in SceneNode.GetChildren())
            {
                SceneNode.RemoveChild((Godot.Node)child);
            }

            SceneNode.QueueFree();
            SceneNode.Dispose();
            SceneNode = null;
        }
    }
}