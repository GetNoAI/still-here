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
    }

    /// Keyboard + mouse, gamepad and touch, folded into one InputFrame. Nothing is ever drawn.
    ///
    /// TOUCH SCHEME (phone/tablet, either orientation):
    ///   - Left half of the screen:  put a thumb down and drag = walk. The spot you first touch is the centre
    ///                               of an invisible stick; drag up to shuffle forward, sideways to sidestep.
    ///   - Right half of the screen: drag = turn your head/body (left-right) and tilt it (up-down).
    ///   - Quick tap anywhere:       reach out and touch.
    ///   - Gentle haptics (see Haptics.cs) when you touch, bump into things, stumble, or the cats brush past.
    /// Distances are measured as fractions of the screen's short side, so it feels the same on every phone
    /// and at every pixel density (web builds report no real DPI).
    public class InputHub
    {
        // ---- test hooks
        public Vector2 debugMove; public float debugTurnRate; public bool debugReach;

        public float mouseSens = 0.0022f, keyTurnRate = 1.9f, padTurnRate = 2.3f;
        // touch, in fractions of the screen's short side
        public float stickRadius = 0.11f, stickDead = 0.15f, tapMaxTime = 0.3f, tapMaxMove = 0.035f;
        public float touchTurn = 2.6f, touchTilt = 1.7f;   // radians for a drag the length of the short side

        public class Finger { public int id; public Vector2 start, last; public float t0; public bool leftSide; public float travelled; }
        public readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
        Vector2 touchMove; Vector2 touchLook; bool touchTap;
        bool padPrev; bool mouseHeld;
        /// Set once a real touch is seen: from then on the browser's/OS's touch-to-mouse emulation is ignored,
        /// so a tap is one reach (not two) and a drag turns once (not twice).
        public bool touchSeen;

        public void Enable() { if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable(); }

        public float screenShort = 0;
        float Short => screenShort > 0 ? screenShort : Mathf.Max(1, Mathf.Min(Screen.width, Screen.height));

        // ---- touch state machine (pure, so it can be driven by synthetic touches in tests)
        public void TouchBegan(int id, Vector2 pos, float time, float screenW)
        {
            fingers[id] = new Finger { id = id, start = pos, last = pos, t0 = time, leftSide = pos.x < screenW * 0.5f };
        }
        public void TouchMoved(int id, Vector2 pos)
        {
            if (!fingers.TryGetValue(id, out var f)) return;
            Vector2 d = pos - f.last; f.travelled += d.magnitude; f.last = pos;
            if (!f.leftSide) touchLook += d;
        }
        public void TouchEnded(int id, Vector2 pos, float time)
        {
            if (!fingers.TryGetValue(id, out var f)) return;
            TouchMoved(id, pos);
            if (time - f.t0 <= tapMaxTime && f.travelled <= tapMaxMove * Short) touchTap = true;
            fingers.Remove(id);
        }
        /// A touch that began and ended between two frames: never seen as Began.
        public void TouchQuick(Vector2 start, Vector2 end, float duration)
        {
            if (duration <= tapMaxTime && (end - start).magnitude <= tapMaxMove * Short) touchTap = true;
        }
        void ResolveTouchMove()
        {
            touchMove = Vector2.zero;
            foreach (var f in fingers.Values)
            {
                if (!f.leftSide) continue;
                Vector2 d = (f.last - f.start) / (stickRadius * Short);
                float m = d.magnitude; if (m < stickDead) continue;
                touchMove = d / Mathf.Max(1f, m);
            }
        }

        public InputFrame Poll(float dt)
        {
            var f = new InputFrame();
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
                    int id = t.touchId; float tnow = Time.unscaledTime;
                    switch (t.phase)
                    {
                        case UnityEngine.InputSystem.TouchPhase.Began:
                            TouchBegan(id, t.screenPosition, tnow, Screen.width); f.any = true; break;
                        case UnityEngine.InputSystem.TouchPhase.Moved:
                        case UnityEngine.InputSystem.TouchPhase.Stationary:
                            if (!fingers.ContainsKey(id)) { TouchBegan(id, t.startScreenPosition, tnow - (float)(t.time - t.startTime), Screen.width); f.any = true; }
                            TouchMoved(id, t.screenPosition); break;
                        case UnityEngine.InputSystem.TouchPhase.Ended:
                        case UnityEngine.InputSystem.TouchPhase.Canceled:
                            if (fingers.ContainsKey(id)) TouchEnded(id, t.screenPosition, tnow);
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
            f.turn += touchLook.x / Short * touchTurn;
            f.tilt += touchLook.y / Short * touchTilt;
            touchLook = Vector2.zero;
            if (touchTap) { f.reach = true; f.any = true; touchTap = false; }

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
