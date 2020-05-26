﻿using System;
using System.Collections.Generic;

namespace Robust.Shared.Input.Binding
{
    /// <summary>
    ///     Contains a set of created <see cref="InputCmdContext"/>s.
    /// </summary>
    public interface IInputContextContainer
    {
        /// <summary>
        ///     The current "active" context that should be used for filtering key binds.
        /// </summary>
        IInputCmdContext ActiveContext { get; }

        /// <summary>
        ///     This event is raised when ever the Active Context is changed.
        /// </summary>
        event EventHandler<ContextChangedEventArgs> ContextChanged;

        /// <summary>
        ///     Adds a new unique context to the set.
        /// </summary>
        /// <param name="uniqueName">Unique name of the new context.</param>
        /// <param name="parentName">Unique name of the parent context. Tee parent context must already exist in the set.</param>
        /// <returns>Instance of the newly created context.</returns>
        IInputCmdContext New(string uniqueName, string parentName);

        /// <summary>
        ///     Adds a new unique context to the set.
        /// </summary>
        /// <param name="uniqueName">Unique name of the new context.</param>
        /// <param name="parent">Context to set, as the parent context, of the newly created context.</param>
        /// <returns>Instance of the newly created context.</returns>
        IInputCmdContext New(string uniqueName, IInputCmdContext parent);

        /// <summary>
        ///     Checks if a context with a unique name exists in the set.
        /// </summary>
        /// <param name="uniqueName">Unique Name to search for.</param>
        /// <returns>If a context exists with the given unique name in the set.</returns>
        bool Exists(string uniqueName);

        /// <summary>
        ///     Returns the context with the given unique name from the set.
        /// </summary>
        /// <param name="uniqueName">Unique name of the context to search for.</param>
        /// <returns>Context with the given unique name in the set.</returns>
        IInputCmdContext GetContext(string uniqueName);

        /// <summary>
        ///     Tries to find a context with a given unique name.
        /// </summary>
        /// <param name="uniqueName">Unique name of the context to search for.</param>
        /// <param name="context">The context with the given unique name (if any).</param>
        /// <returns>If a context with a given unique name exists in the set.</returns>
        bool TryGetContext(string uniqueName, out IInputCmdContext context);

        /// <summary>
        ///     Removes the context with the given unique name.
        /// </summary>
        /// <param name="uniqueName">Unique name of context to remove.</param>
        void Remove(string uniqueName);

        /// <summary>
        ///     Sets the context with the given unique name as the Active context.
        /// </summary>
        /// <param name="uniqueName">Unique name of the context to set as active.</param>
        void SetActiveContext(string uniqueName);
    }

    /// <inheritdoc />
    internal class InputContextContainer : IInputContextContainer
    {
        /// <summary>
        ///     Default 'root' context unique name that always exists in the set.
        /// </summary>
        public const string DefaultContextName = "common";

        /// <inheritdoc />
        public event EventHandler<ContextChangedEventArgs> ContextChanged;

        private readonly Dictionary<string, InputCmdContext> _contexts = new Dictionary<string, InputCmdContext>();
        private InputCmdContext _activeContext;

        /// <inheritdoc />
        public IInputCmdContext ActiveContext
        {
            get => _activeContext;
            private set
            {
                var args = new ContextChangedEventArgs(_activeContext, value);
                _activeContext = (InputCmdContext) value;
                ContextChanged?.Invoke(this, args);
            }
        }

        /// <summary>
        ///     Creates a new instance of <see cref="InputCmdContext"/>.
        /// </summary>
        public InputContextContainer()
        {
            _contexts.Add(DefaultContextName, new InputCmdContext());
            SetActiveContext(DefaultContextName);
        }

        /// <inheritdoc />
        public IInputCmdContext New(string uniqueName, string parentName)
        {
            if (string.IsNullOrWhiteSpace(uniqueName))
                throw new ArgumentException("String is null or whitespace.", nameof(uniqueName));

            if (string.IsNullOrWhiteSpace(parentName))
                throw new ArgumentException("String is null or whitespace.", nameof(parentName));

            if (!_contexts.TryGetValue(parentName, out var parentContext))
                throw new ArgumentException("Parent does not exist.", nameof(parentName));

            if (_contexts.ContainsKey(uniqueName))
                throw new ArgumentException($"Context with name {uniqueName} already exists.", nameof(uniqueName));

            var newContext = new InputCmdContext(parentContext);
            _contexts.Add(uniqueName, newContext);
            return newContext;
        }

        /// <inheritdoc />
        public IInputCmdContext New(string uniqueName, IInputCmdContext parent)
        {
            if (string.IsNullOrWhiteSpace(uniqueName))
                throw new ArgumentException("String is null or whitespace.", nameof(uniqueName));

            if (_contexts.ContainsKey(uniqueName))
                throw new ArgumentException($"Context with name {uniqueName} already exists.", nameof(uniqueName));

            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            var newContext = new InputCmdContext(parent);
            _contexts.Add(uniqueName, newContext);
            return newContext;
        }

        /// <inheritdoc />
        public bool Exists(string uniqueName)
        {
            return _contexts.ContainsKey(uniqueName);
        }

        /// <inheritdoc />
        public IInputCmdContext GetContext(string uniqueName)
        {
            return _contexts[uniqueName];
        }

        /// <inheritdoc />
        public bool TryGetContext(string uniqueName, out IInputCmdContext context)
        {
            if (_contexts.TryGetValue(uniqueName, out var ctext))
            {
                context = ctext;
                return true;
            }

            context = default;
            return false;
        }

        /// <inheritdoc />
        public void Remove(string uniqueName)
        {
            if (uniqueName == DefaultContextName)
                throw new ArgumentException("The default context cannot be removed.", nameof(uniqueName));

            _contexts.Remove(uniqueName);
        }

        /// <inheritdoc />
        public void SetActiveContext(string uniqueName)
        {
            ActiveContext = _contexts[uniqueName];
        }
    }

    /// <summary>
    ///     Event arguments for an input context change.
    /// </summary>
    public class ContextChangedEventArgs : EventArgs
    {
        /// <summary>
        ///     The new context that became active.
        /// </summary>
        public IInputCmdContext NewContext { get; }

        /// <summary>
        ///     The old context that used to be active.
        /// </summary>
        public IInputCmdContext OldContext { get; }

        /// <summary>
        ///     Constructs a new instance of <see cref="ContextChangedEventArgs"/>/
        /// </summary>
        /// <param name="oldContext">The old context that used to be active.</param>
        /// <param name="newContext">The new context that became active.</param>
        public ContextChangedEventArgs(IInputCmdContext oldContext, IInputCmdContext newContext)
        {
            OldContext = oldContext;
            NewContext = newContext;
        }
    }
}
