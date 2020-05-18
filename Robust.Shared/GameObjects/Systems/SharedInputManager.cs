using Robust.Shared.Input;

namespace Robust.Shared.GameObjects.Systems
{
    public abstract class SharedInputManager : ISharedInputManager
    {
        public abstract ICommandBindMapping BindMap { get; }
    }
}
