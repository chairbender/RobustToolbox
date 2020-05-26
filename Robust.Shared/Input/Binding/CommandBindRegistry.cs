using System.Collections.Generic;
using System.Linq;
using Robust.Shared.Log;

namespace Robust.Shared.Input.Binding
{
    /// <summary>
    ///     Allows registering bindings so that they will receive and handle inputs.
    /// </summary>
    public interface ICommandBindRegistry
    {
        /// <summary>
        ///     Registers the indicated bindings so they can receive and handle inputs.
        /// </summary>
        /// <param name="bindings">Bindings to register.</param>
        void Register(Bindings bindings);

        /// <summary>
        ///     Gets the command handlers bound to the indicated function. Empty enumerable
        ///     if no handlers are bound.
        /// </summary>
        /// <param name="function">Key function to get the input handlers of.</param>
        /// <returns>True if the key function had a handler to return.</returns>
        IEnumerable<InputCmdHandler> GetHandlers(BoundKeyFunction function);

        /// <summary>
        ///     Unbinds all bindings associated with the given unbind handle.
        /// </summary>
        /// <param name="unbindHandle">Unbind handle whose associated bindings should be unbound.</param>
        void UnbindFunctions(UnbindHandle unbindHandle);
    }

    /// <inheritdoc />
    public class CommandBindRegistry : ICommandBindRegistry
    {

        private readonly Dictionary<BoundKeyFunction, List<InputCmdHandler>> _commandBinds = new Dictionary<BoundKeyFunction, List<InputCmdHandler>>();
        // map from unbind handle to the cmd handlers associated with it
        private readonly Dictionary<UnbindHandle, List<InputCmdHandler>> _unbindHandles = new Dictionary<UnbindHandle, List<InputCmdHandler>>();

        /// <inheritdoc />
        public void BindFunction(UnbindHandle handle, BoundKeyFunction function, InputCmdHandler command)
        {
            if (_unbindHandles.ContainsKey(handle) == false)
            {
                _unbindHandles[handle] = new List<InputCmdHandler>();
            }

            var unbindHandlers = _unbindHandles[handle];
            if (unbindHandlers.Contains())


            if (_commandBinds.ContainsKey(function) == false)
            {
                _commandBinds[function] = new List<InputCmdHandler>();

            }

            var boundHandlers = _commandBinds[function];
            if (boundHandlers.Contains(command))
            {
                Logger.Warning("Provided handler is already bound to function {0}, bind" +
                               " will be ignored.", function.FunctionName);
                return;
            }
            boundHandlers.Add(command);
        }

        /// <inheritdoc />
        public IEnumerable<InputCmdHandler> GetHandlers(BoundKeyFunction function)
        {
            if (_commandBinds.TryGetValue(function, out var handlers))
            {
                return handlers;
            }
            return Enumerable.Empty<InputCmdHandler>();
        }

        /// <inheritdoc />
        public void UnbindFunction(BoundKeyFunction function, InputCmdHandler handler)
        {
            _commandBinds.Remove(function);
        }
    }
}
