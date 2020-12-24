using System;
using System.Collections.Generic;
using System.Linq;
using Robust.Client.Console;
using Robust.Client.Input;
using Robust.Client.Interfaces.Graphics;
using Robust.Client.Interfaces.Graphics.ClientEye;
using Robust.Client.Interfaces.Input;
using Robust.Client.Interfaces.State;
using Robust.Client.Interfaces.UserInterface;
using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Interfaces.Configuration;
using Robust.Shared.Interfaces.Map;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Resources;
using Robust.Shared.Interfaces.Timing;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Robust.Client.UserInterface
{
    internal sealed class UserInterfaceManager : IDisposable, IUserInterfaceManagerInternal
    {
        [Dependency] private readonly IInputManager _inputManager = default!;
        [Dependency] private readonly IClyde _displayManager = default!;
        [Dependency] private readonly IClientConsole _console = default!;
        [Dependency] private readonly IResourceManager _resourceManager = default!;
        [Dependency] private readonly IGameTiming _gameTiming = default!;
        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] private readonly IStateManager _stateManager = default!;
        [Dependency] private readonly IClientNetManager _netManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;

        public UITheme ThemeDefaults { get; private set; } = default!;

        public Stylesheet? Stylesheet
        {
            get => _stylesheet;
            set
            {
                _stylesheet = value;

                if (RootControl?.Stylesheet != null)
                {
                    RootControl.StylesheetUpdateRecursive();
                }
            }
        }

        public Control? KeyboardFocused { get; private set; }

        // When a control receives a KeyBinding which can focus the control, such as a LMB or RMB down,
        // the control must also receive subsequent mouse up and mouse moves.
        // So, for CanFocus=true KeyBindings, we keep track of which control should be receiving the subsequent drag / key up events.
        // This is a dict because multiple different mouse buttons (or other CanFocus=true KeyBindings) can be pressed down at different times over
        // different controls.
        private Dictionary<BoundKeyFunction,Control> _controlsFocused = new();
        // This is essentially just a cached version of _controlsFocused.Values.Distinct().
        // We need to frequently iterate the unique values in _controlsFocused when we handle mouse hovers, so we maintain this list
        // to avoid allocations when iterating.
        // This should always be very small, at most like 3 elements, so it shouldn't be a perf issue to insert /
        // remove, and those operations are performed only on mouse button state changes, so FAR less frequently than iteration
        // (which is performed on mouse move).
        private List<Control> _controlsFocusedList = new(3);
        // This tracks the control which has most recently received a CanFocus=true KeyBinding keydown (most recently added to _controlsFocused)
        // I.e. _lastControlFocused will always be in at least one entry of _controlsFocused when non-null.
        // It gets cleared when there are no more entries in _controlsFocused with this control as the value.
        private Control? _lastControlFocused;
        public LayoutContainer StateRoot { get; private set; } = default!;
        public PopupContainer ModalRoot { get; private set; } = default!;
        public Control? CurrentlyHovered { get; private set; } = default!;
        public float UIScale { get; private set; } = 1;
        public float DefaultUIScale => _displayManager.DefaultWindowScale.X;
        public Control RootControl { get; private set; } = default!;
        public LayoutContainer WindowRoot { get; private set; } = default!;
        public LayoutContainer PopupRoot { get; private set; } = default!;
        public DebugConsole DebugConsole { get; private set; } = default!;
        public IDebugMonitors DebugMonitors => _debugMonitors;
        private DebugMonitors _debugMonitors = default!;

        private readonly List<Control> _modalStack = new();

        private bool _rendering = true;
        private float _tooltipTimer;
        // set to null when not counting down
        private float? _tooltipDelay;
        private Tooltip _tooltip = default!;
        private bool showingTooltip;
        private Control? _suppliedTooltip;
        private const float TooltipDelay = 1;

        private readonly Queue<Control> _styleUpdateQueue = new();
        private readonly Queue<Control> _layoutUpdateQueue = new();
        private Stylesheet? _stylesheet;
        private ICursor? _worldCursor;
        private bool _needUpdateActiveCursor;

        public void Initialize()
        {
            _configurationManager.OnValueChanged(CVars.DisplayUIScale, _uiScaleChanged, true);

            _uiScaleChanged(_configurationManager.GetCVar(CVars.DisplayUIScale));
            ThemeDefaults = new UIThemeDummy();

            _initializeCommon();

            DebugConsole = new DebugConsole(_console, _resourceManager);
            RootControl.AddChild(DebugConsole);

            _debugMonitors = new DebugMonitors(_gameTiming, _playerManager, _eyeManager, _inputManager, _stateManager,
                _displayManager, _netManager, _mapManager);
            RootControl.AddChild(_debugMonitors);

            _inputManager.SetInputCommand(EngineKeyFunctions.ShowDebugConsole,
                InputCmdHandler.FromDelegate(session => DebugConsole.Toggle()));

            _inputManager.SetInputCommand(EngineKeyFunctions.ShowDebugMonitors,
                InputCmdHandler.FromDelegate(enabled: session => { DebugMonitors.Visible = true; },
                    disabled: session => { DebugMonitors.Visible = false; }));

            _inputManager.SetInputCommand(EngineKeyFunctions.HideUI,
                InputCmdHandler.FromDelegate(
                    enabled: session => _rendering = false,
                    disabled: session => _rendering = true));

            _inputManager.UIKeyBindStateChanged += OnUIKeyBindStateChanged;
        }

        private void _initializeCommon()
        {
            RootControl = new Control
            {
                Name = "UIRoot",
                MouseFilter = Control.MouseFilterMode.Ignore,
                IsInsideTree = true
            };
            RootControl.Size = _displayManager.ScreenSize / UIScale;
            _displayManager.OnWindowResized += args => _updateRootSize();

            StateRoot = new LayoutContainer
            {
                Name = "StateRoot",
                MouseFilter = Control.MouseFilterMode.Ignore
            };
            RootControl.AddChild(StateRoot);

            WindowRoot = new LayoutContainer
            {
                Name = "WindowRoot",
                MouseFilter = Control.MouseFilterMode.Ignore
            };
            RootControl.AddChild(WindowRoot);

            PopupRoot = new LayoutContainer
            {
                Name = "PopupRoot",
                MouseFilter = Control.MouseFilterMode.Ignore
            };
            RootControl.AddChild(PopupRoot);

            ModalRoot = new PopupContainer
            {
                Name = "ModalRoot",
                MouseFilter = Control.MouseFilterMode.Ignore,
            };
            RootControl.AddChild(ModalRoot);

            _tooltip = new Tooltip();
            PopupRoot.AddChild(_tooltip);
            _tooltip.Visible = false;
        }

        public void InitializeTesting()
        {
            ThemeDefaults = new UIThemeDummy();

            _initializeCommon();
        }

        public void Dispose()
        {
            RootControl?.Dispose();
        }

        public void Update(FrameEventArgs args)
        {
            RootControl.DoUpdate(args);
        }

        public void FrameUpdate(FrameEventArgs args)
        {
            RootControl.DoFrameUpdate(args);

            // Process queued style & layout updates.
            while (_styleUpdateQueue.Count != 0)
            {
                var control = _styleUpdateQueue.Dequeue();

                if (control.Disposed)
                {
                    continue;
                }

                control.DoStyleUpdate();
            }

            while (_layoutUpdateQueue.Count != 0)
            {
                var control = _layoutUpdateQueue.Dequeue();

                if (control.Disposed)
                {
                    continue;
                }

                control.DoLayoutUpdate();
            }

            // count down tooltip delay if we're not showing one yet and
            // are hovering the mouse over a control without moving it
            if (_tooltipDelay != null && !showingTooltip)
            {
                _tooltipTimer += args.DeltaSeconds;
                if (_tooltipTimer >= _tooltipDelay)
                {
                    _showTooltip();
                }
            }

            if (_needUpdateActiveCursor)
            {
                _needUpdateActiveCursor = false;
                UpdateActiveCursor();
            }
        }

        public bool HandleCanFocusDown(Vector2 pointerPosition, IEnumerable<BoundKeyFunction> functions)
        {
            var control = MouseGetControl(pointerPosition);

            // If we have a modal open and the mouse down was outside it, close said modal.
            while (_modalStack.Count != 0)
            {
                var top = _modalStack[^1];
                var offset = pointerPosition - top.GlobalPixelPosition;
                if (!top.HasPoint(offset / UIScale))
                {
                    if (top.MouseFilter != Control.MouseFilterMode.Stop)
                        RemoveModal(top);
                    else
                    {
                        foreach (var boundKeyFunction in functions)
                        {
                            _controlsFocused[boundKeyFunction] = top;
                        }
                        _lastControlFocused = top;
                        if (!_controlsFocusedList.Contains(top))
                        {
                            _controlsFocusedList.Add(top);
                        }
                        return false; // prevent anything besides the top modal control from receiving input
                    }
                }
                else
                {
                    break;
                }
            }

            ReleaseKeyboardFocus();

            if (control == null)
            {
                return false;
            }

            foreach (var boundKeyFunction in functions)
            {
                _controlsFocused[boundKeyFunction] = control;
            }
            _lastControlFocused = control;
            if (!_controlsFocusedList.Contains(control))
            {
                _controlsFocusedList.Add(control);
            }

            if (control.CanKeyboardFocus && control.KeyboardFocusOnClick)
            {
                control.GrabKeyboardFocus();
            }

            return true;
        }

        public void HandleCanFocusUp(IEnumerable<BoundKeyFunction> functions)
        {
            var removed = false;
            foreach (var boundKeyFunction in functions)
            {
                if (!_controlsFocused.TryGetValue(boundKeyFunction, out var control)) continue;
                removed = true;
                _controlsFocused.Remove(boundKeyFunction);
                if (_lastControlFocused == control)
                {
                    _lastControlFocused = null;
                }
            }

            // clear the lastControlFocused if it's no longer in any entries of _controlsFocused.
            // Simultaneously, we refresh _controlsFocusedList now that some entries may have been removed
            if (!removed) return;
            _controlsFocusedList.Clear();
            var stillFocused = false;
            foreach (var control in _controlsFocused.Values.Distinct())
            {
                _controlsFocusedList.Add(control);
                if (_lastControlFocused == control) stillFocused = true;
            }

            if (!stillFocused) _lastControlFocused = null;
        }

        public void KeyBindDown(BoundKeyEventArgs args)
        {
            if (args.Function == EngineKeyFunctions.CloseModals && _modalStack.Count != 0)
            {
                while (_modalStack.Count > 0)
                {
                    var top = _modalStack[^1];
                    RemoveModal(top);
                }

                args.Handle();
                return;
            }

            var controlFocused = _lastControlFocused ?? KeyboardFocused ?? MouseGetControl(args.PointerLocation.Position);

            if (controlFocused == null)
            {
                return;
            }

            var guiArgs = new GUIBoundKeyEventArgs(args.Function, args.State, args.PointerLocation, args.CanFocus,
                args.PointerLocation.Position / UIScale - controlFocused.GlobalPosition,
                args.PointerLocation.Position - controlFocused.GlobalPixelPosition);

            _doGuiInput(controlFocused, guiArgs, (c, ev) => c.KeyBindDown(ev));

            if (guiArgs.Handled)
            {
                args.Handle();
            }
        }

        public void KeyBindUp(BoundKeyEventArgs args)
        {
            if (!_controlsFocused.TryGetValue(args.Function, out var controlFocused))
            {
                controlFocused = _lastControlFocused ?? KeyboardFocused ?? MouseGetControl(args.PointerLocation.Position);
            }
            if (controlFocused == null)
            {
                return;
            }

            var guiArgs = new GUIBoundKeyEventArgs(args.Function, args.State, args.PointerLocation, args.CanFocus,
                args.PointerLocation.Position / UIScale - controlFocused.GlobalPosition,
                args.PointerLocation.Position - controlFocused.GlobalPixelPosition);

            _doGuiInput(controlFocused, guiArgs, (c, ev) => c.KeyBindUp(ev));

            // Always mark this as handled.
            // The only case it should not be is if we do not have a control to click on,
            // in which case we never reach this.
            args.Handle();
        }

        public void MouseMove(MouseMoveEventArgs mouseMoveEventArgs)
        {
            _resetTooltipTimer();
            // Update which control is considered hovered.
            var newHovered = MouseGetControl(mouseMoveEventArgs.Position);
            if (newHovered != CurrentlyHovered)
            {
                _clearTooltip();
                CurrentlyHovered?.MouseExited();
                CurrentlyHovered = newHovered;
                CurrentlyHovered?.MouseEntered();
                if (CurrentlyHovered != null)
                {
                    _tooltipDelay = CurrentlyHovered.TooltipDelay ?? TooltipDelay;
                }
                else
                {
                    _tooltipDelay = null;
                }

                _needUpdateActiveCursor = true;
            }

            // all of the controls we previously have keydown'd and have not yet lifted the key for (if any)
            // should receive the mousemove. If we haven't keydown'd anything then we send the hover
            // instead to whatever control we're currently hovering.
            if (_controlsFocusedList.Count > 0)
            {
                // As this method can be called frequently, we iterate through our _controlsFocusedList here, which
                // we've been maintaining to be the same as _controlsFocused.Values.Distinct().
                foreach (var target in _controlsFocusedList)
                {
                    DoGUIMouseMoveEvent(target, mouseMoveEventArgs);
                }
            }
            else if (newHovered != null)
            {
                DoGUIMouseMoveEvent(newHovered, mouseMoveEventArgs);
            }
        }

        private void DoGUIMouseMoveEvent(Control target, MouseMoveEventArgs mouseMoveEventArgs)
        {
            var guiArgs = new GUIMouseMoveEventArgs(mouseMoveEventArgs.Relative / UIScale,
                target,
                mouseMoveEventArgs.Position / UIScale, mouseMoveEventArgs.Position,
                mouseMoveEventArgs.Position / UIScale - target.GlobalPosition,
                mouseMoveEventArgs.Position - target.GlobalPixelPosition);

            _doMouseGuiInput(target, guiArgs, (c, ev) => c.MouseMove(ev));
        }

        private void UpdateActiveCursor()
        {
            // Consider mouse input focus first so that dragging windows don't act up etc.
            var cursorTarget = _lastControlFocused ?? CurrentlyHovered;

            if (cursorTarget == null)
            {
                _displayManager.SetCursor(_worldCursor);
                return;
            }

            if (cursorTarget.CustomCursorShape != null)
            {
                _displayManager.SetCursor(cursorTarget.CustomCursorShape);
                return;
            }

            var shape = cursorTarget.DefaultCursorShape switch
            {
                Control.CursorShape.Arrow => StandardCursorShape.Arrow,
                Control.CursorShape.IBeam => StandardCursorShape.IBeam,
                Control.CursorShape.Hand => StandardCursorShape.Hand,
                Control.CursorShape.Crosshair => StandardCursorShape.Crosshair,
                Control.CursorShape.VResize => StandardCursorShape.VResize,
                Control.CursorShape.HResize => StandardCursorShape.HResize,
                _ => StandardCursorShape.Arrow
            };

            _displayManager.SetCursor(_displayManager.GetStandardCursor(shape));
        }

        public void MouseWheel(MouseWheelEventArgs args)
        {
            var control = MouseGetControl(args.Position);
            if (control == null)
            {
                return;
            }

            args.Handle();

            var guiArgs = new GUIMouseWheelEventArgs(args.Delta, control,
                args.Position / UIScale, args.Position,
                args.Position / UIScale - control.GlobalPosition, args.Position - control.GlobalPixelPosition);

            _doMouseGuiInput(control, guiArgs, (c, ev) => c.MouseWheel(ev), true);
        }

        public void TextEntered(TextEventArgs textEvent)
        {
            if (KeyboardFocused == null)
            {
                return;
            }

            var guiArgs = new GUITextEventArgs(KeyboardFocused, textEvent.CodePoint);
            KeyboardFocused.TextEntered(guiArgs);
        }

        public void DisposeAllComponents()
        {
            RootControl.DisposeAllChildren();
        }

        public void Popup(string contents, string title = "Alert!")
        {
            var popup = new SS14Window
            {
                Title = title
            };

            popup.Contents.AddChild(new Label {Text = contents});
            popup.OpenCentered();
        }

        public Control? MouseGetControl(Vector2 coordinates)
        {
            return _mouseFindControlAtPos(RootControl, coordinates);
        }

        public Vector2 MousePositionScaled => ScreenToUIPosition(_inputManager.MouseScreenPosition);
        public Vector2 ScreenToUIPosition(Vector2 position)
        {
            return position / UIScale;
        }

        public Vector2 ScreenToUIPosition(ScreenCoordinates coordinates)
        {
            return ScreenToUIPosition(coordinates.Position);
        }

        /// <inheritdoc />
        public void GrabKeyboardFocus(Control control)
        {
            if (control == null)
            {
                throw new ArgumentNullException(nameof(control));
            }

            if (!control.CanKeyboardFocus)
            {
                throw new ArgumentException("Control cannot get keyboard focus.", nameof(control));
            }

            if (control == KeyboardFocused)
            {
                return;
            }

            ReleaseKeyboardFocus();

            KeyboardFocused = control;

            KeyboardFocused.FocusEntered();
        }

        public void ReleaseKeyboardFocus()
        {
            var oldFocused = KeyboardFocused;
            oldFocused?.FocusExited();
            KeyboardFocused = null;
        }

        public void ReleaseKeyboardFocus(Control ifControl)
        {
            if (ifControl == null)
            {
                throw new ArgumentNullException(nameof(ifControl));
            }

            if (ifControl == KeyboardFocused)
            {
                ReleaseKeyboardFocus();
            }
        }

        public ICursor? WorldCursor
        {
            get => _worldCursor;
            set
            {
                _worldCursor = value;
                _needUpdateActiveCursor = true;
            }
        }

        public void ControlHidden(Control control)
        {
            // Does the same thing but it could later be changed so..
            ControlRemovedFromTree(control);
        }

        public void ControlRemovedFromTree(Control control)
        {
            ReleaseKeyboardFocus(control);
            RemoveModal(control);
            if (control == CurrentlyHovered)
            {
                control.MouseExited();
                CurrentlyHovered = null;
                _clearTooltip();
            }

            if (control == _lastControlFocused)
            {
                _lastControlFocused = null;
            }
        }

        public void PushModal(Control modal)
        {
            _modalStack.Add(modal);
        }

        public void RemoveModal(Control modal)
        {
            if (_modalStack.Remove(modal))
            {
                modal.ModalRemoved();
            }
        }

        public void Render(IRenderHandle renderHandle)
        {
            if (!_rendering)
            {
                return;
            }

            _render(renderHandle, RootControl, Vector2i.Zero, Color.White, null);
        }

        public void QueueStyleUpdate(Control control)
        {
            _styleUpdateQueue.Enqueue(control);
        }

        public void QueueLayoutUpdate(Control control)
        {
            _layoutUpdateQueue.Enqueue(control);
        }

        public void CursorChanged(Control control)
        {
            if (control == _lastControlFocused || control == CurrentlyHovered)
            {
                _needUpdateActiveCursor = true;
            }
        }

        private static void _render(IRenderHandle renderHandle, Control control, Vector2i position, Color modulate,
            UIBox2i? scissorBox)
        {
            if (!control.Visible)
            {
                return;
            }

            // Manual clip test with scissor region as optimization.
            var controlBox = UIBox2i.FromDimensions(position, control.PixelSize);

            if (scissorBox != null)
            {
                var clipMargin = control.RectDrawClipMargin;
                var clipTestBox = new UIBox2i(controlBox.Left - clipMargin, controlBox.Top - clipMargin,
                    controlBox.Right + clipMargin, controlBox.Bottom + clipMargin);

                if (!scissorBox.Value.Intersects(clipTestBox))
                {
                    return;
                }
            }

            var handle = renderHandle.DrawingHandleScreen;
            handle.SetTransform(position, Angle.Zero, Vector2.One);
            modulate *= control.Modulate;
            handle.Modulate = modulate * control.ActualModulateSelf;
            var clip = control.RectClipContent;
            var scissorRegion = scissorBox;
            if (clip)
            {
                scissorRegion = controlBox;
                if (scissorBox != null)
                {
                    // Make the final scissor region a sub region of scissorBox
                    var s = scissorBox.Value;
                    var result = s.Intersection(scissorRegion.Value);
                    if (result == null)
                    {
                        // Uhm... No intersection so... don't draw anything at all?
                        return;
                    }

                    scissorRegion = result.Value;
                }

                renderHandle.SetScissor(scissorRegion);
            }

            control.DrawInternal(renderHandle);
            handle.UseShader(null);

            foreach (var child in control.Children)
            {
                _render(renderHandle, child, position + child.PixelPosition, modulate, scissorRegion);
            }

            if (clip)
            {
                renderHandle.SetScissor(scissorBox);
            }
        }

        private Control? _mouseFindControlAtPos(Control control, Vector2 position)
        {
            for (var i = control.ChildCount - 1; i >= 0; i--)
            {
                var child = control.GetChild(i);
                if (!child.Visible || (child.RectClipContent && !child.PixelRect.Contains((Vector2i) position)))
                {
                    continue;
                }

                var maybeFoundOnChild = _mouseFindControlAtPos(child, position - child.PixelPosition);
                if (maybeFoundOnChild != null)
                {
                    return maybeFoundOnChild;
                }
            }

            if (control.MouseFilter != Control.MouseFilterMode.Ignore && control.HasPoint(position / UIScale))
            {
                return control;
            }

            return null;
        }

        private static void _doMouseGuiInput<T>(Control? control, T guiEvent, Action<Control, T> action,
            bool ignoreStop = false)
            where T : GUIMouseEventArgs
        {
            while (control != null)
            {
                guiEvent.SourceControl = control;
                if (control.MouseFilter != Control.MouseFilterMode.Ignore)
                {
                    action(control, guiEvent);

                    if (guiEvent.Handled || (!ignoreStop && control.MouseFilter == Control.MouseFilterMode.Stop))
                    {
                        break;
                    }
                }

                guiEvent.RelativePosition += control.Position;
                guiEvent.RelativePixelPosition += control.PixelPosition;
                control = control.Parent;
            }
        }

        private static void _doGuiInput<T>(Control? control, T guiEvent, Action<Control, T> action,
            bool ignoreStop = false)
            where T : GUIBoundKeyEventArgs
        {
            while (control != null)
            {
                if (control.MouseFilter != Control.MouseFilterMode.Ignore)
                {
                    action(control, guiEvent);

                    if (guiEvent.Handled || (!ignoreStop && control.MouseFilter == Control.MouseFilterMode.Stop))
                    {
                        break;
                    }
                }

                guiEvent.RelativePosition += control.Position;
                guiEvent.RelativePixelPosition += control.PixelPosition;
                control = control.Parent;
            }
        }

        private void _clearTooltip()
        {
            if (!showingTooltip) return;
            _tooltip.Visible = false;
            if (_suppliedTooltip != null)
            {
                PopupRoot.RemoveChild(_suppliedTooltip);
                _suppliedTooltip = null;
            }
            CurrentlyHovered?.PerformHideTooltip();
            _resetTooltipTimer();
            showingTooltip = false;
        }


        public void HideTooltipFor(Control control)
        {
            if (CurrentlyHovered == control)
            {
                _clearTooltip();
            }
        }

        public Control? GetSuppliedTooltipFor(Control control)
        {
            return CurrentlyHovered == control ? _suppliedTooltip : null;
        }

        private void _resetTooltipTimer()
        {
            _tooltipTimer = 0;
        }

        private void _showTooltip()
        {
            if (showingTooltip) return;
            showingTooltip = true;
            var hovered = CurrentlyHovered;
            if (hovered == null)
            {
                return;
            }

            // show supplied tooltip if there is one
            if (hovered.TooltipSupplier != null)
            {
                _suppliedTooltip = hovered.TooltipSupplier.Invoke(hovered);
                if (_suppliedTooltip != null)
                {
                    PopupRoot.AddChild(_suppliedTooltip);
                    Tooltips.PositionTooltip(_suppliedTooltip);
                }
            }
            else if (!String.IsNullOrWhiteSpace(hovered.ToolTip))
            {
                // show simple tooltip if there is one
                _tooltip.Visible = true;
                _tooltip.Text = hovered.ToolTip;
               Tooltips.PositionTooltip(_tooltip);
            }

            hovered.PerformShowTooltip();
        }

        private void _uiScaleChanged(float newValue)
        {
            UIScale = newValue == 0f ? DefaultUIScale : newValue;

            if (RootControl == null)
            {
                return;
            }

            _propagateUIScaleChanged(RootControl);
            _updateRootSize();
        }

        private static void _propagateUIScaleChanged(Control control)
        {
            control.UIScaleChanged();

            foreach (var child in control.Children)
            {
                _propagateUIScaleChanged(child);
            }
        }

        private void _updateRootSize()
        {
            RootControl.Size = _displayManager.ScreenSize / UIScale;
        }

        /// <summary>
        ///     Converts
        /// </summary>
        /// <param name="args">Event data values for a bound key state change.</param>
        private bool OnUIKeyBindStateChanged(BoundKeyEventArgs args)
        {
            if (args.State == BoundKeyState.Down)
            {
                KeyBindDown(args);
            }
            else
            {
                KeyBindUp(args);
            }

            if (!args.CanFocus && KeyboardFocused != null)
            {
                return true;
            }
            return false;
        }
    }
}
