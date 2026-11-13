using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace StillHere
{
    /// One frame of intent, whatever it came from.
    public struct InputFrame
    {
        public Vector2 move;     // x strafe, y forward; length 0..1
        public float turn;       // radians this frame, + = right
        public float tilt;       // radians this frame, + = look up
        public bool reach;       // pressed this frame
        public bool any;         // anything at all (used to begin)
        public bool gesture;     // a deliberate touch drag began (stands you up from the chair)
    }

    /// Keyboard + mouse, gamepad and touch, folded into one InputFrame. Nothing is ever drawn.
    ///
    /// TOUCH: one thumb, anywhere on the screen. No zones, no buttons.
    ///   - Tap (any finger, even while another is walking): reach out and touch.
    ///   - Slide sideways: turn. The turn follows your thumb (half the screen's short side is about a quarter
    ///     turn) and stops when your thumb stops, so you never lose your heading.
    ///   - Slide up and hold: walk forward. Further up is a little faster. Slide down: shuffle back.
    ///     While walking, drifting well to one side steers.
    ///   The first bit of movement decides between turning and walking, so a walking thumb that wanders
    ///   doesn't quietly spin you. Lift and put it down again to switch.
    /// Sizes are fractions of the screen's short side, so it feels the same on every phone.
    public class InputHub
    {
        // ---- test hooks
        public Vector2 debugMove; public float debugTurnRate; public bool debugReach;

        public float mouseSens = 0.0022f, keyTurnRate = 1.9f, padTurnRate = 2.3f;
        // touch, in fractions of the screen's short side
        public float decide = 0.035f;          // movement before a drag counts as turning or walking
        public float tapMaxTime = 0.3f, tapMaxMove = 0.035f;
        public float turnPerShort = 3.2f;      // radians for a drag the length of the short side
        public float walkRange = 0.13f;        // drag up this far for full walking pace
        public float steerBand = 0.09f;        // while walking, sideways drift inside this band is ignored

        public enum Mode { Undecided, Turn, Walk }
        public class Finger { public int id; public Vector2 start, last; public float t0; public float travelled; public Mode mode; public float steerFrom; }
        public readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        int controller = -1;                   // the one finger that walks or turns; others can only tap
        public float clock;                    // advanced by Poll's dt, so stepped tests time taps correctly
        float turnDelta; bool tapped, gestured;
        public Vector2 touchMove;
        bool padPrev; bool mouseHeld;
        /// Set once a real touch is seen: from then on the browser's/OS's touch-to-mouse emulation is ignored.
        public bool touchSeen;

        public void Enable() { if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable(); }

        public float screenShort = 0;
        float Short => screenShort > 0 ? screenShort : Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));
        public Finger Controller => controller >= 0 && fingers.TryGetValue(controller, out var f) ? f : null;

        // ---- touch state machine (pure: tests drive it with synthetic positions)
        public void TouchBegan(int id, Vector2 pos)
        {
            fingers[id] = new Finger { id = id, start = pos, last = pos, t0 = clock, mode = Mode.Undecided };
            if (Controller == null) controller = id;
        }
        public void TouchMoved(int id, Vector2 pos)
        {
            if (!fingers.TryGetValue(id, out var f)) return;
            Vector2 step = pos - f.last; f.travelled += step.magnitude; f.last = pos;
            if (id != controller) return;
            Vector2 off = pos - f.start; float S = Short;
            if (f.mode == Mode.Undecided)
            {
                if (off.magnitude <= decide * S) return;
                f.mode = Mathf.Abs(off.x) > Mathf.Abs(off.y) ? Mode.Turn : Mode.Walk;
                gestured = true;
                if (f.mode == Mode.Turn) turnDelta += off.x / S * turnPerShort;     // include the movement it took to decide
            }
            else if (f.mode == Mode.Turn) turnDelta += step.x / S * turnPerShort;
            else
            {
                // walking: steer only with sideways drift beyond the band, and then directly (finger distance = angle)
                float band = steerBand * S, x = off.x;
                float beyond = Mathf.Sign(x) * Mathf.Max(0, Mathf.Abs(x) - band);
                turnDelta += (beyond - f.steerFrom) / S * turnPerShort;
                f.steerFrom = beyond;
            }
        }
        public void TouchEnded(int id, Vector2 pos)
        {
            if (!fingers.TryGetValue(id, out var f)) return;
            TouchMoved(id, pos);
            if (clock - f.t0 <= tapMaxTime && f.travelled <= tapMaxMove * Short && f.mode == Mode.Undecided) tapped = true;
            fingers.Remove(id);
            if (id == controller)
            {
                controller = -1;
                // hand control to a finger still down, anchored where it is now: no jump
                foreach (var o in fingers.Values) { controller = o.id; o.start = o.last; o.mode = Mode.Undecided; o.steerFrom = 0; o.t0 = clock; o.travelled = 0; break; }
            }
        }
        /// A touch that began and ended between two frames: never seen as Began.
        public void TouchQuick(Vector2 start, Vector2 end, float duration)
        {
            if (duration <= tapMaxTime && (end - start).magnitude <= tapMaxMove * Short) tapped = true;
        }
        void ResolveTouchMove()
        {
            touchMove = Vector2.zero;
            var f = Controller;
            if (f == null || f.mode != Mode.Walk) return;
            float y = (f.last.y - f.start.y) / (walkRange * Short);
            if (Mathf.Abs(y) < 0.15f) return;
            touchMove = new Vector2(0, Mathf.Clamp(y, -1, 1));
        }

        public InputFrame Poll(float dt)
        {
            var f = new InputFrame();
            clock += dt;
            var kb = Keyboard.current; var ms = Mouse.current; var gp = Gamepad.current;

            if (kb != null)
            {
                float x = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
                f.move += new Vector2(x, y);
                float tr = (kb.rightArrowKey.isPressed ? 1 : 0) - (kb.leftArrowKey.isPressed ? 1 : 0);
                tr += (kb.cKey.isPressed ? 1 : 0) - (kb.zKey.isPressed ? 1 : 0);
                f.turn += tr * keyTurnRate * dt;
                if (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) f.reach = true;
                if (kb.anyKey.wasPressedThisFrame) f.any = true;
            }
            if (EnhancedTouchSupport.enabled && Touchscreen.current != null)
            {
                foreach (var t in ETouch.activeTouches)
                {
                    touchSeen = true;
                    int id = t.touchId;
                    switch (t.phase)
                    {
                        case UnityEngine.InputSystem.TouchPhase.Began:
                            TouchBegan(id, t.screenPosition); f.any = true; break;
                        case UnityEngine.InputSystem.TouchPhase.Moved:
                        case UnityEngine.InputSystem.TouchPhase.Stationary:
                            if (!fingers.ContainsKey(id)) { TouchBegan(id, t.startScreenPosition); fingers[id].t0 = clock - (float)(t.time - t.startTime); f.any = true; }
                            TouchMoved(id, t.screenPosition); break;
                        case UnityEngine.InputSystem.TouchPhase.Ended:
                        case UnityEngine.InputSystem.TouchPhase.Canceled:
                            if (fingers.ContainsKey(id)) TouchEnded(id, t.screenPosition);
                            else { f.any = true; if (t.phase == UnityEngine.InputSystem.TouchPhase.Ended) TouchQuick(t.startScreenPosition, t.screenPosition, (float)(t.time - t.startTime)); }
                            break;
                    }
                }
            }
            if (ms != null && !touchSeen)
            {
                if (ms.leftButton.wasPressedThisFrame) { f.reach = true; f.any = true; mouseHeld = true; }
                if (ms.leftButton.wasReleasedThisFrame) mouseHeld = false;
                if (Cursor.lockState == CursorLockMode.Locked || mouseHeld)
                {
                    Vector2 d = ms.delta.ReadValue();
                    f.turn += d.x * mouseSens; f.tilt += d.y * mouseSens;
                }
            }
            if (gp != null)
            {
                Vector2 l = gp.leftStick.ReadValue(), r = gp.rightStick.ReadValue();
                f.move += l; f.turn += r.x * padTurnRate * dt; f.tilt += r.y * 1.6f * dt;
                bool pressed = gp.buttonSouth.isPressed || gp.buttonWest.isPressed || gp.buttonEast.isPressed || gp.buttonNorth.isPressed || gp.rightShoulder.isPressed || gp.rightTrigger.ReadValue() > 0.5f;
                if (pressed && !padPrev) { f.reach = true; f.any = true; }
                padPrev = pressed;
            }
            ResolveTouchMove();
            f.move += touchMove;
            f.turn += turnDelta; turnDelta = 0;
            if (tapped) { f.reach = true; f.any = true; tapped = false; }
            if (gestured) { f.gesture = true; gestured = false; }

            f.move += debugMove; f.turn += debugTurnRate * dt;
            if (debugReach) { f.reach = true; f.any = true; debugReach = false; }
            if (f.move.sqrMagnitude > 1) f.move.Normalize();
            return f;
        }

        public void Rumble(float strong, float weak, float seconds)
        {
            var gp = Gamepad.current;
            if (gp != null) { gp.SetMotorSpeeds(Mathf.Clamp01(weak), Mathf.Clamp01(strong)); rumbleOff = Time.unscaledTime + seconds; }
        }
        float rumbleOff = -1;
        public void Tick()
        {
            if (rumbleOff > 0 && Time.unscaledTime > rumbleOff) { rumbleOff = -1; var gp = Gamepad.current; if (gp != null) gp.SetMotorSpeeds(0, 0); }
        }
    }
}
