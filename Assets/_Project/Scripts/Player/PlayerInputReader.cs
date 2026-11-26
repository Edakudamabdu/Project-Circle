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

        [Tooltip("Player/Interact — Button (talk to NPCs, rest at Mirrors).")]
        [SerializeField] private InputActionReference interactAction;

        [Tooltip("Player/Parry — Button (the cape wraps around the circle to deflect projectiles and lasers).")]
        [SerializeField] private InputActionReference parryAction;

        [Tooltip("Player/Heal — Button, held (Mend). Optional: if empty, the project-wide 'Player/Heal' action is used.")]
        [SerializeField] private InputActionReference healAction;

        private InputAction heal;

        /// <summary>True while the heal (Mend) button is held, unless something other than <paramref name="self"/> has locked the controls.</summary>
        public bool HealHeld(object self)
        {
            if (heal == null || !heal.IsPressed()) return false;
            foreach (object owner in controlLocks) if (owner != self) return false;
            return true;
        }

        /// <summary>
        /// While true (during a conversation, at a Mirror...) the circle ignores gameplay input: no movement,
        /// jump, dash or attack. <see cref="ConfirmPressed"/> and <see cref="RawMove"/> still work so menus and
        /// dialogue can be used. Locked while at least one system holds a lock (<see cref="AddControlLock"/>).
        /// </summary>
        public bool ControlsLocked => controlLocks.Count > 0;

        /// <summary>Movement input even while controls are locked (for navigating menus).</summary>
        public Vector2 RawMove => moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;

        private readonly System.Collections.Generic.HashSet<object> controlLocks = new System.Collections.Generic.HashSet<object>();

        /// <summary>Locks gameplay controls until <paramref name="owner"/> calls <see cref="RemoveControlLock"/>.</summary>
        public void AddControlLock(object owner) => controlLocks.Add(owner);

        /// <summary>Releases <paramref name="owner"/>'s lock. Controls return once no locks remain.</summary>
        public void RemoveControlLock(object owner) => controlLocks.Remove(owner);

        /// <summary>Raised on the frame the jump button goes down.</summary>
        public event Action JumpPressed;

        /// <summary>Raised on the frame the jump button is released.</summary>
        public event Action JumpReleased;

        /// <summary>Raised on the frame the dash button goes down.</summary>
        public event Action DashPressed;

        /// <summary>Raised on the frame the attack button goes down.</summary>
        public event Action AttackPressed;

        /// <summary>Raised on the frame the interact button goes down (not while controls are locked).</summary>
        public event Action InteractPressed;

        /// <summary>Raised on the frame the parry button goes down (not while controls are locked).</summary>
        public event Action ParryPressed;

        /// <summary>
        /// Raised when Interact, Jump or Attack is pressed, even while controls are locked.
        /// Used to advance dialogue.
        /// </summary>
        public event Action ConfirmPressed;

        /// <summary>Current movement input. X is horizontal, Y is vertical (used for fast-fall).</summary>
        public Vector2 Move => !ControlsLocked && moveAction != null ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;

        /// <summary>True while the jump button is held down.</summary>
        public bool JumpHeld => !ControlsLocked && jumpAction != null && jumpAction.action.IsPressed();
        private void OnEnable()
        {
            heal = healAction != null ? healAction.action : InputSystem.actions?.FindAction("Player/Heal");
            heal?.Enable();

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

            if (interactAction != null)
            {
                interactAction.action.performed += HandleInteractPerformed;
                interactAction.action.Enable();
            }

            if (parryAction != null)
            {
                parryAction.action.performed += HandleParryPerformed;
                parryAction.action.Enable();
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

            if (interactAction != null)
            {
                interactAction.action.performed -= HandleInteractPerformed;
            }

            if (parryAction != null)
            {
                parryAction.action.performed -= HandleParryPerformed;
            }
        }

        private void HandleParryPerformed(InputAction.CallbackContext _)
        {
            if (!ControlsLocked) ParryPressed?.Invoke();
        }

        private void HandleJumpPerformed(InputAction.CallbackContext _)
        {
            ConfirmPressed?.Invoke();
            if (!ControlsLocked) JumpPressed?.Invoke();
        }

        private void HandleJumpCanceled(InputAction.CallbackContext _) => JumpReleased?.Invoke();

        private void HandleDashPerformed(InputAction.CallbackContext _)
        {
            if (!ControlsLocked) DashPressed?.Invoke();
        }

        private void HandleAttackPerformed(InputAction.CallbackContext _)
        {
            ConfirmPressed?.Invoke();
            if (!ControlsLocked) AttackPressed?.Invoke();
        }

        private void HandleInteractPerformed(InputAction.CallbackContext _)
        {
            ConfirmPressed?.Invoke();
            if (!ControlsLocked) InteractPressed?.Invoke();
        }
    }
}
