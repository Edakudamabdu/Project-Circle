using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectCircle.Player
{
    /// <summary>
    /// Thin adapter between Unity's New Input System and gameplay code.
    /// Exposes the current move vector as polled state, and button presses/releases as C# events,
    /// so gameplay scripts never touch <see cref="InputAction"/> directly.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [Header("Actions (from Assets/Settings/InputSystem_Actions)")]
        [Tooltip("Player/Move — Value, Vector2.")]
        [SerializeField] private InputActionReference moveAction;

        [Tooltip("Player/Jump — Button.")]
        [SerializeField] private InputActionReference jumpAction;

        [Tooltip("Player/Dash — Button.")]
        [SerializeField] private InputActionReference dashAction;

        [Tooltip("Player/Attack — Button.")]
        [SerializeField] private InputActionReference attackAction;

        /// <summary>Raised on the frame the jump button goes down.</summary>
        public event Action JumpPressed;

        /// <summary>Raised on the frame the jump button is released.</summary>
        public event Action JumpReleased;

        /// <summary>Raised on the frame the dash button goes down.</summary>
        public event Action DashPressed;

        /// <summary>Raised on the frame the attack button goes down.</summary>
        public event Action AttackPressed;

        /// <summary>Current movement input. X is horizontal, Y is vertical (used for fast-fall).</summary>
        public Vector2 Move => moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>True while the jump button is held down.</summary>
        public bool JumpHeld => jumpAction != null && jumpAction.action.IsPressed();

        private void OnEnable()
        {
            // "performed" fires on press for Button actions; "canceled" fires on release.
            if (jumpAction != null)
            {
                jumpAction.action.performed += HandleJumpPerformed;
                jumpAction.action.canceled += HandleJumpCanceled;
                jumpAction.action.Enable();
            }

            if (dashAction != null)
            {
                dashAction.action.performed += HandleDashPerformed;
                dashAction.action.Enable();
            }

            if (attackAction != null)
            {
                attackAction.action.performed += HandleAttackPerformed;
                attackAction.action.Enable();
            }

            moveAction?.action.Enable();
        }

        private void OnDisable()
        {
            // Only unsubscribe — the actions belong to the shared project-wide asset,
            // so other systems (UI, etc.) may still need them enabled.
            if (jumpAction != null)
            {
                jumpAction.action.performed -= HandleJumpPerformed;
                jumpAction.action.canceled -= HandleJumpCanceled;
            }

            if (dashAction != null)
            {
                dashAction.action.performed -= HandleDashPerformed;
            }

            if (attackAction != null)
            {
                attackAction.action.performed -= HandleAttackPerformed;
            }
        }

        private void HandleJumpPerformed(InputAction.CallbackContext _) => JumpPressed?.Invoke();
        private void HandleJumpCanceled(InputAction.CallbackContext _) => JumpReleased?.Invoke();
        private void HandleDashPerformed(InputAction.CallbackContext _) => DashPressed?.Invoke();
        private void HandleAttackPerformed(InputAction.CallbackContext _) => AttackPressed?.Invoke();
    }
}
