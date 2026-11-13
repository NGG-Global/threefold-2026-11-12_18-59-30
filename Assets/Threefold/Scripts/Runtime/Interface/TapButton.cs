using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Threefold.Interface
{
    /// <summary>
    /// A button for menus and prompts that reacts to a tap, or to a mouse click while the mouse is
    /// free. Like the gameplay touch controls it reads the Input System directly, so the scene
    /// needs no EventSystem. Assumes a Screen Space Overlay canvas.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class TapButton : MonoBehaviour
    {
        private RectTransform rectTransform;

        /// <summary>Raised when the button is tapped or clicked.</summary>
        public event Action Pressed;

        private void OnEnable()
        {
            rectTransform = (RectTransform)transform;
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            ReadOnlyArray<Touch> touches = Touch.activeTouches;

            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].phase == TouchPhase.Began && Contains(touches[i].screenPosition))
                {
                    Pressed?.Invoke();
                    return;
                }
            }

            // A captured mouse has no meaningful position, and a touch already produces its own press.
            Mouse mouse = Mouse.current;
            if (touches.Count == 0 && mouse != null && Cursor.lockState != CursorLockMode.Locked
                && mouse.leftButton.wasPressedThisFrame && Contains(mouse.position.ReadValue()))
            {
                Pressed?.Invoke();
            }
        }

        private bool Contains(Vector2 screenPosition)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition, null);
        }
    }
}
