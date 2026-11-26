using System;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace NCDK
{
    public enum InputDeviceType { MouseAndKeyboard, Gamepad }
    
    public enum GamepadLayoutType
    {
        Auto = 0,
        Xbox = 1,
        PlayStation = 2,
        Nintendo = 3,
        Positional = 4
    }

    public static class EInput
    {
        public static InputDeviceType CurrentDevice { get; private set; } = InputDeviceType.MouseAndKeyboard;
        
        /// <summary>
        /// What the user selected in options (Auto defaults to detecting).
        /// </summary>
        public static GamepadLayoutType PreferredGamepadLayout { get; private set; } = GamepadLayoutType.Auto;
        
        public static event Action<InputDeviceType> OnDeviceChanged;
        public static event Action OnGamepadLayoutChanged;

        private const string PREF_KEY_GAMEPAD_LAYOUT = "NCDK_GamepadLayout";

#if ENABLE_INPUT_SYSTEM
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InitTracking()
        {
            CurrentDevice = InputDeviceType.MouseAndKeyboard;
            PreferredGamepadLayout = (GamepadLayoutType)PlayerPrefs.GetInt(PREF_KEY_GAMEPAD_LAYOUT, (int)GamepadLayoutType.Auto);

            // Do not clear OnDeviceChanged / OnGamepadLayoutChanged here: Bootstrapper runs in the
            // same SubsystemRegistration phase and services like CursorManager subscribe during Install().
            InputSystem.onEvent -= OnInputReceived;
            InputSystem.onEvent += OnInputReceived;
            
            Application.quitting -= OnAppQuitting;
            Application.quitting += OnAppQuitting;
        }

        private static void OnAppQuitting()
        {
            InputSystem.onEvent -= OnInputReceived;
        }

        /// <summary>Pointer movement (in pixels per frame) required before the mouse claims the active device when already using mouse/keyboard.</summary>
        const float MouseMovementThresholdPixels = 2f;

        /// <summary>Larger pointer movement required to reclaim the active device from a gamepad (prevents desk jitter or lock warps).</summary>
        const float MouseMovementThresholdFromGamepadPixels = 16f;

        /// <summary>Stick deflection required before a gamepad claims the active device.</summary>
        const float GamepadStickThreshold = 0.2f;

        /// <summary>Cooldown after Cursor.lockState changes or switching to gamepad where mouse delta is ignored.</summary>
        const float CursorLockDeltaCooldownSeconds = 0.15f;

        static float _ignoreMouseDeltaUntilUnscaledTime;

        /// <summary>
        /// Notifies <see cref="EInput"/> that <see cref="Cursor.lockState"/> is about to change so the
        /// resulting OS pointer-centering delta does not falsely switch back to mouse/keyboard.
        /// </summary>
        public static void NotifyCursorLockChanged()
        {
            _ignoreMouseDeltaUntilUnscaledTime = Time.unscaledTime + CursorLockDeltaCooldownSeconds;
        }

        private static void OnInputReceived(InputEventPtr eventPtr, InputDevice device)
        {
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()) return;

            // Idle devices keep reporting state events (high polling rates, stick drift), so the
            // active device only switches on *meaningful* input. Otherwise an untouched mouse
            // silently steals the device from a gamepad the player is using, and the cursor
            // reappears in menus.
            InputDeviceType newDevice;
            if (device is Mouse || device is Keyboard)
            {
                if (!HasMeaningfulMouseOrKeyboardInput()) return;
                newDevice = InputDeviceType.MouseAndKeyboard;
            }
            else if (device is Gamepad pad)
            {
                if (!HasMeaningfulGamepadInput(pad)) return;
                newDevice = InputDeviceType.Gamepad;
            }
            else
            {
                return;
            }

            if (CurrentDevice != newDevice)
            {
                CurrentDevice = newDevice;
                if (newDevice == InputDeviceType.Gamepad)
                {
                    _ignoreMouseDeltaUntilUnscaledTime = Time.unscaledTime + CursorLockDeltaCooldownSeconds;
                }
                OnDeviceChanged?.Invoke(CurrentDevice);
            }
        }

        private static bool HasMeaningfulMouseOrKeyboardInput()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (Time.unscaledTime >= _ignoreMouseDeltaUntilUnscaledTime)
                {
                    float threshold = CurrentDevice == InputDeviceType.Gamepad
                        ? MouseMovementThresholdFromGamepadPixels
                        : MouseMovementThresholdPixels;
                    if (mouse.delta.ReadValue().sqrMagnitude >= threshold * threshold) return true;
                }
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed || mouse.middleButton.isPressed) return true;
                if (mouse.scroll.ReadValue().sqrMagnitude > 0f) return true;
            }

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.anyKey.isPressed;
        }

        private static bool HasMeaningfulGamepadInput(Gamepad pad)
        {
            float threshold = GamepadStickThreshold;
            if (pad.leftStick.ReadValue().sqrMagnitude >= threshold * threshold) return true;
            if (pad.rightStick.ReadValue().sqrMagnitude >= threshold * threshold) return true;
            if (pad.leftTrigger.ReadValue() >= threshold) return true;
            if (pad.rightTrigger.ReadValue() >= threshold) return true;

            return pad.buttonSouth.isPressed || pad.buttonEast.isPressed
                || pad.buttonWest.isPressed || pad.buttonNorth.isPressed
                || pad.startButton.isPressed || pad.selectButton.isPressed
                || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed
                || pad.dpad.up.isPressed || pad.dpad.down.isPressed
                || pad.dpad.left.isPressed || pad.dpad.right.isPressed;
        }
#endif

        /// <summary>
        /// Call this from UI Options to enforce a Gamepad style.
        /// </summary>
        public static void SetGamepadLayoutPreference(GamepadLayoutType layout)
        {
            if (PreferredGamepadLayout != layout)
            {
                PreferredGamepadLayout = layout;
                PlayerPrefs.SetInt(PREF_KEY_GAMEPAD_LAYOUT, (int)layout);
                OnGamepadLayoutChanged?.Invoke();
            }
        }

        /// <summary>
        /// Returns the final GamepadLayout to use.
        /// Defaults to <see cref="GamepadLayoutType.Positional"/> when set to <see cref="GamepadLayoutType.Auto"/>.
        /// </summary>
        public static GamepadLayoutType GetResolvedGamepadLayout()
        {
            if (PreferredGamepadLayout != GamepadLayoutType.Auto)
            {
                return PreferredGamepadLayout;
            }

            return GamepadLayoutType.Positional;
        }

        public static bool IsAnySkipInputHeld()
        {
#if ENABLE_INPUT_SYSTEM
            // Check Keyboard: anyKey is for press, but a simple check for common keys is more reliable for held state.
            bool keyboardHeld = Keyboard.current != null && (Keyboard.current.spaceKey.isPressed || Keyboard.current.enterKey.isPressed || Keyboard.current.escapeKey.isPressed);
            
            // Check Gamepad
            bool gamepadHeld = false;
            if (Gamepad.current != null)
            {
                gamepadHeld = Gamepad.current.buttonSouth.isPressed || Gamepad.current.buttonEast.isPressed || Gamepad.current.startButton.isPressed;
            }

            // Check Mouse
            bool mouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            
            return keyboardHeld || gamepadHeld || mouseHeld;
#else
            // Fallback for old Input Manager
            return UnityEngine.Input.anyKey;
#endif
        }

        public static bool IsAnyButtonPressed()
        {
#if ENABLE_INPUT_SYSTEM
            bool keyboardPressed = Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame;
            
            bool gamepadPressed = false;
            if (Gamepad.current != null)
            {
                gamepadPressed = Gamepad.current.buttonSouth.wasPressedThisFrame || 
                                 Gamepad.current.buttonEast.wasPressedThisFrame || 
                                 Gamepad.current.buttonWest.wasPressedThisFrame || 
                                 Gamepad.current.buttonNorth.wasPressedThisFrame || 
                                 Gamepad.current.rightShoulder.wasPressedThisFrame || 
                                 Gamepad.current.leftShoulder.wasPressedThisFrame ||
                                 Gamepad.current.rightTrigger.wasPressedThisFrame ||
                                 Gamepad.current.leftTrigger.wasPressedThisFrame;
            }

            bool mousePressed = Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame);
            
            return keyboardPressed || gamepadPressed || mousePressed;
#else
            return UnityEngine.Input.anyKeyDown;
#endif
        }
        
    }
}