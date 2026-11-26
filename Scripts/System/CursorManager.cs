using System;
using UnityEngine;

namespace NCDK
{
    public class CursorManager : IDisposable
    {
        [NonSerialized] readonly BlockingEventsManager _blockingEventsManager;

        public CursorManager(BlockingEventsManager blockingEventsManager)
        {
            _blockingEventsManager = blockingEventsManager;

            _blockingEventsManager.MenuRetainable.StateEvent += OnMenuStateChanged;
            EInput.OnDeviceChanged += OnDeviceChanged;

            Application.quitting += OnAppQuitting;
            Application.focusChanged += OnApplicationFocusChanged;

            RefreshCursorState();
        }

        void OnDeviceChanged(InputDeviceType device)
        {
            Debug.Log($"[CursorManager] Input device changed: {device}");
            RefreshCursorState();
        }

        void OnMenuStateChanged(bool onMenu)
        {
            RefreshCursorState();
        }

        void OnApplicationFocusChanged(bool hasFocus)
        {
            if (hasFocus) RefreshCursorState();
        }

        void RefreshCursorState()
        {
            bool menuOpen = _blockingEventsManager.MenuRetainable.IsRetained;
            bool isMouse = EInput.CurrentDevice == InputDeviceType.MouseAndKeyboard;

            bool shouldShow = menuOpen && isMouse;

            SetCursorState(shouldShow);
        }

        static void SetCursorState(bool visible)
        {
            var targetLock = visible ? CursorLockMode.None : CursorLockMode.Locked;
            if (Cursor.lockState != targetLock)
            {
                EInput.NotifyCursorLockChanged();
                Cursor.lockState = targetLock;
            }
            Cursor.visible = visible;
        }

        public void Dispose()
        {
            if (_blockingEventsManager != null)
                _blockingEventsManager.MenuRetainable.StateEvent -= OnMenuStateChanged;

            EInput.OnDeviceChanged -= OnDeviceChanged;

            Application.quitting -= OnAppQuitting;
            Application.focusChanged -= OnApplicationFocusChanged;
        }

        void OnAppQuitting()
        {
            Dispose();
#if UNITY_EDITOR
            SetCursorState(true);
#endif
        }
    }
}