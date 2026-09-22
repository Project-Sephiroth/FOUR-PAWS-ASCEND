using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static partial class PcsSecondPassTools
{
    private static readonly int[] LayoutDoorIds = { 57, 58, 66, 71, 80 };

    private static IEnumerator LayoutRunDoorTests()
    {
        const string boundary = "Door checks are a geometry/reset contract fixture: the Shared director is paused while its existing MoveAuthority/Present APIs move the actual Rigidbody, Collider and artwork through physics time. Device active states and ladder deployment are prepared directly, then actual ResetSection is invoked. This does not prove normal player traversal, hack input, remote synchronization, or Bear tutorial completion. No Scene is saved.";
        if (layoutPlayReport.mode == "doors") layoutPlayReport.boundary = boundary;
        else layoutPlayReport.boundary += " Door-specific exception: " + boundary;
        layoutPlayReport.diagnostics.Add(boundary);
        LayoutSelectSection(0);
        layoutPlayDirector.enabled = false;
        var colliderFrames = new Dictionary<int, Bounds>();
        var artworkFrames = new Dictionary<int, Bounds>();
        var scales = new Dictionary<int, Vector3>();
        try
        {
            foreach (int id in LayoutDoorIds)
            {
                var door = layoutPlayDirector.GetDevice(id);
                if (door == null || door.Solid == null || door.Body == null || !door.RetractWithinFrame)
                    throw new InvalidOperationException("Missing retracting door contract: " + id);
                door.ApplyPose(door.InitialPosition);
                door.Present(false);
                Physics2D.SyncTransforms();
                colliderFrames[id] = door.Solid.bounds;
                scales[id] = door.transform.localScale;
                Bounds artwork = door.Solid.bounds;
                foreach (var visual in door.Visuals) if (visual != null) artwork.Encapsulate(visual.bounds);
                artworkFrames[id] = artwork;
                LayoutCheck("door-frame-configuration-" + id, door.name,
                    door.LowerStop != null && door.UpperStop != null && !door.UpperStop.IsChildOf(door.transform) &&
                    Mathf.Abs(door.UpperStop.position.x - door.InitialPosition.x) < .001f,
                    "Closed collider=" + colliderFrames[id] + "; visual/frame=" + artwork + "; upper=" + door.UpperStop.position);
            }
            yield return LayoutDoorMotion(new[] { 57 }, true, "rabbit-first", colliderFrames, artworkFrames);
            yield return LayoutDoorMotion(new[] { 58, 66 }, true, "mouse-after-rabbit", colliderFrames, artworkFrames);
            yield return LayoutDoorMotion(LayoutDoorIds, false, "close-first-order", colliderFrames, artworkFrames);
            yield return LayoutDoorMotion(new[] { 58, 66 }, true, "mouse-first", colliderFrames, artworkFrames);
            yield return LayoutDoorMotion(new[] { 57 }, true, "rabbit-after-mouse", colliderFrames, artworkFrames);
            yield return LayoutDoorMotion(LayoutDoorIds, false, "close-second-order", colliderFrames, artworkFrames);
            yield return LayoutDoorMotion(LayoutDoorIds, true, "all-five-simultaneous", colliderFrames, artworkFrames);

            // Prepare only device states, never tutorial completion masks or puzzle success.
            foreach (int id in LayoutDoorIds)
            {
                var state = layoutPlayDirector.States[id]; state.Active = 1;
                state.Position = layoutPlayDirector.GetDevice(id).UpperStop.position;
                layoutPlayDirector.States.Set(id, state);
            }
            var ladder = layoutPlayDirector.GetDevice(64);
            var ladderState = layoutPlayDirector.States[64];
            ladderState.Active = 1; ladderState.Position = ladder.LowerStop.position;
            layoutPlayDirector.States.Set(64, ladderState);
            ladder.ApplyPose(ladderState.Position);
            yield return LayoutWait(.08f);
            ladder.Present(true);
            bool preparedHacks = true;
            foreach (int id in new[] { 59, 61, 63 })
            {
                var console = layoutPlayDirector.GetDevice(id);
                var state = layoutPlayDirector.States[id];
                state.Active = 1; state.Counter = console.RequiredInputs;
                layoutPlayDirector.States.Set(id, state);
                preparedHacks &= layoutPlayDirector.States[id].Active == 1 && layoutPlayDirector.States[id].Counter > 0;
            }
            Physics2D.SyncTransforms();
            LayoutCheck("ladder-reset-precondition", "Mouse", ladder.CanClimb, "Fixture starts with an actually deployed, climbable ladder.");
            LayoutCheck("mouse-hack-reset-precondition", "Mouse", preparedHacks,
                "Reset-only fixture explicitly prepares all three completed hack counters; this is not normal E-input evidence.");
            MethodInfo reset = typeof(PcsPuzzleDirector).GetMethod("ResetSection", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo present = typeof(PcsPuzzleDirector).GetMethod("ApplyPresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            if (reset == null || present == null) throw new MissingMethodException("Director reset/presentation contract changed.");
            foreach (var role in new[] { MyEnum.CharacterType.Rabbit, MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Frog, MyEnum.CharacterType.Bear })
            {
                yield return LayoutWait(layoutPlayDirector.ResetDelay + .1f);
                int before = layoutPlayDirector.GetResetEpoch(role);
                reset.Invoke(layoutPlayDirector, new object[] { role });
                present.Invoke(layoutPlayDirector, new object[] { true });
                Physics2D.SyncTransforms();
                yield return LayoutWait(.08f);
                // The paused director needs the same presentation refresh normally performed after physics.
                present.Invoke(layoutPlayDirector, new object[] { false });
                Physics2D.SyncTransforms();
                LayoutCheck("door-room-reset-" + role, role.ToString(), layoutPlayDirector.GetResetEpoch(role) != before,
                    "Actual ResetSection increments this room epoch; no completion mask was forced.");
            }
            foreach (int id in LayoutDoorIds)
            {
                var door = layoutPlayDirector.GetDevice(id);
                LayoutCheck("door-reset-restores-" + id, door.name,
                    layoutPlayDirector.States[id].Active == 0 && door.Solid.enabled &&
                    Vector2.Distance(door.Body.position, door.InitialPosition) < .01f &&
                    Vector3.Distance(door.transform.localScale, scales[id]) < .001f,
                    "Closed collision and original scale restored; actual position=" + door.Body.position + "; scale=" + door.transform.localScale);
            }
            bool ladderHidden = true;
            foreach (var visual in ladder.Visuals) if (visual != null && visual.enabled) ladderHidden = false;
            bool hacksReset = true;
            foreach (int id in new[] { 59, 61, 63 })
                hacksReset &= layoutPlayDirector.States[id].Active == 0 && layoutPlayDirector.States[id].Counter == 0;
            LayoutCheck("mouse-reset-stows-ladder", "Mouse", !ladder.CanClimb && !ladder.Trigger.enabled && ladderHidden &&
                Vector2.Distance(ladder.transform.position, ladder.UpperStop.position) < .01f && hacksReset,
                "Actual room reset restores stowed pose, hidden artwork, disabled climb trigger and all three hack states.");
        }
        finally { layoutPlayDirector.enabled = true; }
    }

    private static IEnumerator LayoutDoorMotion(int[] ids, bool open, string label,
        Dictionary<int, Bounds> colliderFrames, Dictionary<int, Bounds> artworkFrames)
    {
        layoutPlayReport.phase = "Door geometry: " + label;
        float deadline = Time.time + 12f, lastFixed = -1f;
        bool geometryInside = true, reached = false; int samples = 0;
        float largestTopIntrusion = 0f;
        while (!reached && Time.time < deadline)
        {
            if (Time.fixedTime > lastFixed)
            {
                foreach (int id in ids)
                {
                    var door = layoutPlayDirector.GetDevice(id);
                    door.MoveAuthority(door.Body.position, open ? (Vector2)door.UpperStop.position : door.InitialPosition, Time.fixedDeltaTime);
                }
                lastFixed = Time.fixedTime;
            }
            yield return null;
            reached = true;
            foreach (int id in LayoutDoorIds)
            {
                var door = layoutPlayDirector.GetDevice(id);
                bool targeted = Array.IndexOf(ids, id) >= 0;
                bool requestedOpen = targeted ? open : Vector2.Distance(door.Body.position, door.InitialPosition) > .01f;
                door.Present(requestedOpen);
                Physics2D.SyncTransforms();
                if (targeted) reached &= Vector2.Distance(door.Body.position, open ? (Vector2)door.UpperStop.position : door.InitialPosition) < .005f;
                if (door.Solid.enabled)
                {
                    var bounds = door.Solid.bounds;
                    largestTopIntrusion = Mathf.Max(largestTopIntrusion, bounds.max.y - colliderFrames[id].max.y);
                    geometryInside &= LayoutDoorInside(bounds, colliderFrames[id], .055f);
                }
                foreach (var visual in door.Visuals)
                    if (visual != null && visual.enabled) geometryInside &= LayoutDoorInside(visual.bounds, artworkFrames[id], .055f);
            }
            samples++;
        }
        yield return LayoutWait(.1f);
        bool endpointCollision = true;
        foreach (int id in ids)
        {
            var door = layoutPlayDirector.GetDevice(id); door.Present(open);
            endpointCollision &= door.Solid.enabled != open;
        }
        LayoutCheck("door-travel-" + label, "Tutorial doors", reached && geometryInside && endpointCollision && samples > 0,
            "Actual MovePosition/Present samples=" + samples + "; reached=" + reached + "; own closed frame retained=" + geometryInside +
            "; top intrusion=" + largestTopIntrusion.ToString("F5") + "; end collision=" + endpointCollision +
            ". 0.055U sampling tolerance; order is a geometry fixture, not normal player traversal.");
    }

    private static bool LayoutDoorInside(Bounds bounds, Bounds frame, float tolerance)
    {
        return bounds.min.x >= frame.min.x - tolerance && bounds.max.x <= frame.max.x + tolerance &&
            bounds.min.y >= frame.min.y - tolerance && bounds.max.y <= frame.max.y + tolerance;
    }
}
