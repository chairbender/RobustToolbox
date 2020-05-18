using Robust.Server.Interfaces.Player;
using Robust.Shared.Input;

namespace Robust.Server.GameObjects.EntitySystems
{
    /// <summary>
    ///     Server side processing of incoming user commands.
    /// </summary>
    public interface IInputManager
    {
        /// <summary>
        ///     Server side input command binds.
        /// </summary>
        ICommandBindMapping BindMap { get; }

        IPlayerCommandStates GetInputStates(IPlayerSession session);
        uint GetLastInputCommand(IPlayerSession session);
    }
}
