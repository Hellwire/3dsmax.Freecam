using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BlenderFreecam.Input
{
    /// <summary>
    /// One elapsed-time input sample consumed by the MAXScript update timer.
    /// </summary>
    public sealed class InputFrame
    {
        public double DeltaSeconds { get; internal set; }
        public int MouseDeltaX { get; internal set; }
        public int MouseDeltaY { get; internal set; }
        public double WheelSteps { get; internal set; }
        public double FovWheelSteps { get; internal set; }
        public bool Forward { get; internal set; }
        public bool Backward { get; internal set; }
        public bool Left { get; internal set; }
        public bool Right { get; internal set; }
        public bool Up { get; internal set; }
        public bool Down { get; internal set; }
        public bool RollLeft { get; internal set; }
        public bool RollRight { get; internal set; }
        public bool Fast { get; internal set; }
        public bool EscapePressed { get; internal set; }
        public bool IsContextActive { get; internal set; }
    }

    /// <summary>
    /// Captures relative mouse input and navigation keys while a 3ds Max viewport
    /// is in freecam mode. It does not reference the 3ds Max SDK.
    /// </summary>
    public sealed class FlyInputService : IDisposable
    {
        [Flags]
        private enum ButtonState
        {
            None = 0,
            Forward = 1 << 0,
            Backward = 1 << 1,
            Left = 1 << 2,
            Right = 1 << 3,
            UpSpace = 1 << 4,
            UpE = 1 << 5,
            DownQ = 1 << 6,
            ShiftLeft = 1 << 7,
            ShiftRight = 1 << 8,
        }

        private const int WhKeyboardLl = 13;
        private const int WhMouseLl = 14;
        private const int HcAction = 0;
        private const int GaRoot = 2;
        private const int WheelDelta = 120;

        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;
        private const int WmLButtonDown = 0x0201;
        private const int WmLButtonUp = 0x0202;
        private const int WmRButtonDown = 0x0204;
        private const int WmRButtonUp = 0x0205;
        private const int WmMButtonDown = 0x0207;
        private const int WmMButtonUp = 0x0208;
        private const int WmMouseWheel = 0x020A;
        private const int WmXButtonDown = 0x020B;
        private const int WmXButtonUp = 0x020C;
        private const int WmMouseHWheel = 0x020E;

        private const int VkEscape = 0x1B;
        private const int VkSpace = 0x20;
        private const int VkControl = 0x11;
        private const int VkShift = 0x10;
        private const int VkLControl = 0xA2;
        private const int VkRControl = 0xA3;
        private const int VkLShift = 0xA0;
        private const int VkRShift = 0xA1;
        private const int VkA = 0x41;
        private const int VkD = 0x44;
        private const int VkE = 0x45;
        private const int VkQ = 0x51;
        private const int VkS = 0x53;
        private const int VkW = 0x57;

        private readonly HookProc _keyboardProcedure;
        private readonly HookProc _mouseProcedure;

        private IntPtr _keyboardHook;
        private IntPtr _mouseHook;
        private IntPtr _viewportWindow;
        private IntPtr _mainWindow;
        private IntPtr _previousFocus;
        private Point _previousCursor;
        private Rect _previousClip;
        private Rect _currentViewportRect;
        private Point _viewportCenter;
        private bool _hasPreviousCursor;
        private bool _hasPreviousClip;
        private int _hideCursorCallCount;
        private long _lastTimestamp;
        private int _buttonState;
        private int _wheelDelta;
        private int _fovWheelDelta;
        private int _escapePressed;
        private int _active;
        private bool _escapeWasDown;
        private bool _disposed;

        public FlyInputService()
        {
            _keyboardProcedure = KeyboardHook;
            _mouseProcedure = MouseHook;
        }

        public bool Active
        {
            get { return Volatile.Read(ref _active) != 0; }
        }

        public string LastError { get; private set; }

        /// <summary>
        /// Starts capture for the supplied viewport and 3ds Max main-window HWNDs.
        /// Int64 arguments are intentional: MAXScript can marshal IntegerPtr HWNDs
        /// to them consistently in both 32-bit-value and 64-bit-value cases.
        /// </summary>
        public bool Begin(long viewportWindow, long mainWindow)
        {
            ThrowIfDisposed();
            Stop();
            LastError = null;

            _viewportWindow = new IntPtr(viewportWindow);
            _mainWindow = new IntPtr(mainWindow);

            if (!IsWindow(_viewportWindow) || !IsWindow(_mainWindow))
            {
                LastError = "3ds Max returned an invalid viewport or main-window handle.";
                ResetHandles();
                return false;
            }

            _hasPreviousCursor = GetCursorPos(out _previousCursor);
            _hasPreviousClip = GetClipCursor(out _previousClip);
            _previousFocus = GetFocus();

            bool boundsChanged;
            if (!RefreshViewportBounds(out boundsChanged))
            {
                LastError = "The active viewport has no usable client area.";
                ResetHandles();
                return false;
            }

            Interlocked.Exchange(ref _buttonState, 0);
            Interlocked.Exchange(ref _wheelDelta, 0);
            Interlocked.Exchange(ref _fovWheelDelta, 0);
            Interlocked.Exchange(ref _escapePressed, 0);
            _escapeWasDown = IsKeyDown(VkEscape);
            Interlocked.Exchange(ref _active, 1);

            try
            {
                IntPtr module = GetModuleHandle(null);
                _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProcedure, module, 0);
                if (_keyboardHook == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard hook.");
                }

                _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProcedure, module, 0);
                if (_mouseHook == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the mouse hook.");
                }

                ClipCursor(ref _currentViewportRect);
                SetCursorPos(_viewportCenter.X, _viewportCenter.Y);
                HideCursor();
                _lastTimestamp = Stopwatch.GetTimestamp();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Stop();
                return false;
            }
        }

        /// <summary>
        /// Returns movement state, relative mouse motion, wheel input and a
        /// frame-rate-independent elapsed time. Delta time is capped after stalls.
        /// </summary>
        public InputFrame Poll()
        {
            ThrowIfDisposed();

            InputFrame frame = new InputFrame();
            if (!Active)
            {
                return frame;
            }

            long now = Stopwatch.GetTimestamp();
            long previous = Interlocked.Exchange(ref _lastTimestamp, now);
            double elapsed = previous == 0
                ? 0.0
                : (double)(now - previous) / Stopwatch.Frequency;
            frame.DeltaSeconds = Math.Max(0.0, Math.Min(0.1, elapsed));

            bool contextActive = IsCaptureContextActive();
            frame.IsContextActive = contextActive;

            bool boundsChanged;
            if (contextActive && RefreshViewportBounds(out boundsChanged))
            {
                Point cursor;
                if (!boundsChanged && GetCursorPos(out cursor))
                {
                    frame.MouseDeltaX = cursor.X - _viewportCenter.X;
                    frame.MouseDeltaY = cursor.Y - _viewportCenter.Y;
                }

                ClipCursor(ref _currentViewportRect);
                SetCursorPos(_viewportCenter.X, _viewportCenter.Y);
            }

            int state = Volatile.Read(ref _buttonState);
            frame.Forward = HasState(state, ButtonState.Forward);
            frame.Backward = HasState(state, ButtonState.Backward);
            frame.Left = HasState(state, ButtonState.Left);
            frame.Right = HasState(state, ButtonState.Right);

            bool controlDown =
                IsKeyDown(VkControl) ||
                IsKeyDown(VkLControl) ||
                IsKeyDown(VkRControl);
            bool qDown = HasState(state, ButtonState.DownQ);
            bool eDown = HasState(state, ButtonState.UpE);

            frame.Up = HasState(state, ButtonState.UpSpace) || (eDown && !controlDown);
            frame.Down = qDown && !controlDown;
            frame.RollLeft = qDown && controlDown;
            frame.RollRight = eDown && controlDown;
            frame.Fast = HasState(state, ButtonState.ShiftLeft) || HasState(state, ButtonState.ShiftRight);

            /*
                Keep a physical-state fallback in addition to the hook pulse.
                Some 3ds Max input configurations consume Escape before the
                low-level callback's pulse is observed by the MAXScript timer.
            */
            bool escapeDown = IsKeyDown(VkEscape);
            bool escapeEdge = escapeDown && !_escapeWasDown;
            _escapeWasDown = escapeDown;
            frame.EscapePressed =
                Interlocked.Exchange(ref _escapePressed, 0) != 0 ||
                escapeEdge;

            /*
                Escape is an emergency release, not merely a request for the
                MAXScript controller. Release all Win32 state before returning
                so a stalled or filtered timer callback cannot leave freecam
                active or the cursor confined.
            */
            if (frame.EscapePressed)
            {
                Stop();
            }

            frame.WheelSteps = (double)Interlocked.Exchange(ref _wheelDelta, 0) / WheelDelta;
            frame.FovWheelSteps =
                (double)Interlocked.Exchange(ref _fovWheelDelta, 0) / WheelDelta;
            return frame;
        }

        /// <summary>
        /// Releases hooks, unclamps the pointer, restores cursor visibility and
        /// position, and returns focus to the window that owned it at activation.
        /// Safe to call repeatedly.
        /// </summary>
        public void Stop()
        {
            Interlocked.Exchange(ref _active, 0);

            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }

            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }

            Interlocked.Exchange(ref _buttonState, 0);
            Interlocked.Exchange(ref _wheelDelta, 0);
            Interlocked.Exchange(ref _fovWheelDelta, 0);
            Interlocked.Exchange(ref _escapePressed, 0);
            _escapeWasDown = false;

            if (_hasPreviousClip)
            {
                ClipCursor(ref _previousClip);
            }
            else
            {
                ClipCursor(IntPtr.Zero);
            }

            RestoreCursor();

            if (_hasPreviousCursor)
            {
                SetCursorPos(_previousCursor.X, _previousCursor.Y);
            }

            if (_previousFocus != IntPtr.Zero &&
                IsWindow(_previousFocus) &&
                IsMainWindowForeground())
            {
                SetFocus(_previousFocus);
            }

            _hasPreviousCursor = false;
            _hasPreviousClip = false;
            _previousFocus = IntPtr.Zero;
            _lastTimestamp = 0;
            ResetHandles();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private IntPtr KeyboardHook(int code, IntPtr message, IntPtr data)
        {
            if (code != HcAction || !Active || !IsCaptureContextActive())
            {
                return CallNextHookEx(_keyboardHook, code, message, data);
            }

            int messageCode = unchecked((int)message.ToInt64());
            bool isDown = messageCode == WmKeyDown || messageCode == WmSysKeyDown;
            bool isUp = messageCode == WmKeyUp || messageCode == WmSysKeyUp;
            if (!isDown && !isUp)
            {
                return CallNextHookEx(_keyboardHook, code, message, data);
            }

            KeyboardHookData keyboard = (KeyboardHookData)Marshal.PtrToStructure(data, typeof(KeyboardHookData));
            int virtualKey = unchecked((int)keyboard.VirtualKey);

            if (virtualKey == VkEscape)
            {
                if (isDown)
                {
                    Interlocked.Exchange(ref _escapePressed, 1);

                    /*
                        This callback runs on the thread that installed the
                        low-level hook (3ds Max's UI thread). Unhook immediately
                        and restore the pointer here instead of waiting for the
                        MAXScript timer to observe a pulse.
                    */
                    Stop();
                }
                return new IntPtr(1);
            }

            ButtonState mapped;
            if (!TryMapKey(virtualKey, keyboard.ScanCode, out mapped))
            {
                return CallNextHookEx(_keyboardHook, code, message, data);
            }

            SetButtonState(mapped, isDown);

            // Shift is observed for the speed modifier but is allowed through.
            // This preserves normal modifier state and permits a Shift-based
            // user-assigned macro shortcut to toggle freecam back off.
            if (mapped == ButtonState.ShiftLeft || mapped == ButtonState.ShiftRight)
            {
                return CallNextHookEx(_keyboardHook, code, message, data);
            }

            return new IntPtr(1);
        }

        private IntPtr MouseHook(int code, IntPtr message, IntPtr data)
        {
            if (code != HcAction || !Active || !IsCaptureContextActive())
            {
                return CallNextHookEx(_mouseHook, code, message, data);
            }

            int messageCode = unchecked((int)message.ToInt64());
            if (messageCode == WmMouseWheel)
            {
                MouseHookData mouse = (MouseHookData)Marshal.PtrToStructure(data, typeof(MouseHookData));
                int delta = unchecked((short)((mouse.MouseData >> 16) & 0xffff));
                bool controlDown =
                    IsKeyDown(VkControl) ||
                    IsKeyDown(VkLControl) ||
                    IsKeyDown(VkRControl);

                if (controlDown)
                {
                    Interlocked.Add(ref _fovWheelDelta, delta);
                }
                else
                {
                    Interlocked.Add(ref _wheelDelta, delta);
                }
                return new IntPtr(1);
            }

            if (messageCode == WmMouseHWheel ||
                messageCode == WmLButtonDown || messageCode == WmLButtonUp ||
                messageCode == WmRButtonDown || messageCode == WmRButtonUp ||
                messageCode == WmMButtonDown || messageCode == WmMButtonUp ||
                messageCode == WmXButtonDown || messageCode == WmXButtonUp)
            {
                return new IntPtr(1);
            }

            return CallNextHookEx(_mouseHook, code, message, data);
        }

        private bool IsCaptureContextActive()
        {
            return Active &&
                   IsWindow(_viewportWindow) &&
                   IsWindow(_mainWindow) &&
                   IsMainWindowForeground();
        }

        private bool IsMainWindowForeground()
        {
            if (_mainWindow == IntPtr.Zero)
            {
                return false;
            }

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            return GetAncestor(foreground, GaRoot) == GetAncestor(_mainWindow, GaRoot);
        }

        private bool RefreshViewportBounds(out bool changed)
        {
            changed = false;
            Rect client;
            if (!GetClientRect(_viewportWindow, out client) ||
                client.Right <= client.Left ||
                client.Bottom <= client.Top)
            {
                return false;
            }

            Point topLeft = new Point { X = client.Left, Y = client.Top };
            Point bottomRight = new Point { X = client.Right, Y = client.Bottom };
            if (!ClientToScreen(_viewportWindow, ref topLeft) ||
                !ClientToScreen(_viewportWindow, ref bottomRight))
            {
                return false;
            }

            Rect next = new Rect
            {
                Left = topLeft.X,
                Top = topLeft.Y,
                Right = bottomRight.X,
                Bottom = bottomRight.Y,
            };

            changed = next.Left != _currentViewportRect.Left ||
                      next.Top != _currentViewportRect.Top ||
                      next.Right != _currentViewportRect.Right ||
                      next.Bottom != _currentViewportRect.Bottom;

            _currentViewportRect = next;
            _viewportCenter = new Point
            {
                X = next.Left + ((next.Right - next.Left) / 2),
                Y = next.Top + ((next.Bottom - next.Top) / 2),
            };
            return true;
        }

        private static bool HasState(int state, ButtonState value)
        {
            return (state & (int)value) != 0;
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private void SetButtonState(ButtonState value, bool down)
        {
            int current;
            int next;
            do
            {
                current = Volatile.Read(ref _buttonState);
                next = down ? current | (int)value : current & ~(int)value;
            }
            while (Interlocked.CompareExchange(ref _buttonState, next, current) != current);
        }

        private static bool TryMapKey(int virtualKey, uint scanCode, out ButtonState state)
        {
            switch (virtualKey)
            {
                case VkW:
                    state = ButtonState.Forward;
                    return true;
                case VkS:
                    state = ButtonState.Backward;
                    return true;
                case VkA:
                    state = ButtonState.Left;
                    return true;
                case VkD:
                    state = ButtonState.Right;
                    return true;
                case VkSpace:
                    state = ButtonState.UpSpace;
                    return true;
                case VkE:
                    state = ButtonState.UpE;
                    return true;
                case VkQ:
                    state = ButtonState.DownQ;
                    return true;
                case VkLShift:
                    state = ButtonState.ShiftLeft;
                    return true;
                case VkRShift:
                    state = ButtonState.ShiftRight;
                    return true;
                case VkShift:
                    // Scan code 0x36 is right shift; 0x2A is left shift.
                    state = scanCode == 0x36 ? ButtonState.ShiftRight : ButtonState.ShiftLeft;
                    return true;
                default:
                    state = ButtonState.None;
                    return false;
            }
        }

        private void HideCursor()
        {
            _hideCursorCallCount = 0;
            int result;
            do
            {
                result = ShowCursor(false);
                _hideCursorCallCount++;
            }
            while (result >= 0 && _hideCursorCallCount < 32);
        }

        private void RestoreCursor()
        {
            while (_hideCursorCallCount > 0)
            {
                ShowCursor(true);
                _hideCursorCallCount--;
            }
        }

        private void ResetHandles()
        {
            _viewportWindow = IntPtr.Zero;
            _mainWindow = IntPtr.Zero;
            _currentViewportRect = default(Rect);
            _viewportCenter = default(Point);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("FlyInputService");
            }
        }

        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardHookData
        {
            public uint VirtualKey;
            public uint ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseHookData
        {
            public Point Position;
            public uint MouseData;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int hookId,
            HookProc procedure,
            IntPtr module,
            uint threadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(
            IntPtr hook,
            int code,
            IntPtr message,
            IntPtr data);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string moduleName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, int flags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr window);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool show);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClipCursor(out Rect rect);

        [DllImport("user32.dll", EntryPoint = "ClipCursor")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClipCursor(ref Rect rect);

        [DllImport("user32.dll", EntryPoint = "ClipCursor")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClipCursor(IntPtr rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetClientRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);
    }
}
