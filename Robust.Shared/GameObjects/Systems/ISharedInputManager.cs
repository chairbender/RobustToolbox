using Robust.Shared.Input;

namespace Robust.Shared.GameObjects.Systems
{
    public interface ISharedInputManager
    {
        public ICommandBindMapping BindMap { get; }
    }
}
