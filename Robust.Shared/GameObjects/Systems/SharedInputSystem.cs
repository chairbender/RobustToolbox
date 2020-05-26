using Robust.Shared.Input.Binding;

namespace Robust.Shared.GameObjects.Systems
{
    public abstract class SharedInputSystem : EntitySystem
    {
        public abstract ICommandBindRegistry BindMap { get; }
    }
}
