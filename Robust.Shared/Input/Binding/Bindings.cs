using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Robust.Shared.Input.Binding
{
    /// <summary>
    /// Represents a set of bindings from BoundKeyFunctions to InputCmdHandlers.  Allows for bulk unbinding these
    /// bindings. Each set of bindings
    /// has an associated type so that you can control the order in which bound handlers are fired when multiple
    /// classes are registering bindings to the same key function. Immutable.
    /// Use Bindings.Builder() to create.
    /// </summary>
    public class Bindings
    {
        private readonly Dictionary<BoundKeyFunction, List<InputCmdHandler>> _commandBinds;

        private Bindings(Dictionary<BoundKeyFunction, List<InputCmdHandler>> commandBinds)
        {
            _commandBinds = commandBinds;
        }

        /// <summary>
        /// Builder to build a new set of Bindings
        /// </summary>
        /// <param name="type">type to associate with these bindings. Should pretty much always
        /// be the same type as the </param>
        /// <returns></returns>
        public static BindingsBuilder Builder(Type type)
        {

            return new BindingsBuilder();
        }

        /// <summary>
        /// For creating Bindings.
        /// </summary>
        public class BindingsBuilder
        {
            private readonly Dictionary<BoundKeyFunction, List<InputCmdHandler>> _commandBinds =
                new Dictionary<BoundKeyFunction, List<InputCmdHandler>>();

            /// <summary>
            /// Bind the indicated handler to the indicated function. Multiple
            /// commands may be bound to a given function.
            /// </summary>
            /// <param name="function"></param>
            /// <param name="command"></param>
            public void BindFunction(BoundKeyFunction function, InputCmdHandler command)
            {
                if (_commandBinds.ContainsKey(function) == false)
                {
                    _commandBinds[function] = new List<InputCmdHandler>();

                }

                var boundHandlers = _commandBinds[function];
                boundHandlers.Add(command);
            }

            /// <summary>
            /// Create the Bindings based on the current configuration.
            /// </summary>
            /// <returns></returns>
            public Bindings Build()
            {
                return new Bindings(_commandBinds);
            }


            /// <summary>
            /// Create the Bindings based on the current configuration and register
            /// with the indicated mappings so they will be allowed to handle inputs.
            /// </summary>
            /// <param name="registry">mappings to register these bindings with</param>
            /// <returns></returns>
            public Bindings Register(ICommandBindRegistry registry)
            {
                var bindings = Build();
                registry.Register(bindings);
                return bindings;
            }
        }
    }
}
