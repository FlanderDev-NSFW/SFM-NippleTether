using System;
using System.Numerics;

namespace NippleTether.Logic
{
    /// <summary>
    /// What a taut chain does to her breast: it draws the nipple out towards the pull, up to <see cref="MaxStretch"/>
    /// (<see cref="Excess"/> is what is left over; holding her back is <see cref="PointControl.Hold"/>). Fed every frame with the chain's
    /// <see cref="TetherChain.Stretch"/> measured from where the nipple is drawn (already pulled by <see cref="Offset"/>), so
    /// the offset grows by what is still missing and settles where the chain is just taut; when the chain hangs slack the
    /// breast springs back within <see cref="ReturnSeconds"/>. World space, metres.
    /// </summary>
    public sealed class BreastPull
    {
        public float MaxStretch = 0.05f;
        public float Tolerance = 0.004f;     // stretch the chain's solver shows without being taut
        public float ReturnSeconds = 0.25f;
        public float MaxSpeed = 1.5f;        // m/s the nipple can be drawn out or spring back

        /// <summary>How far the nipple is drawn out of its place.</summary>
        public Vector3 Offset { get; private set; }

        /// <summary>The pull beyond <see cref="MaxStretch"/> in the last update: how far she has to follow.</summary>
        public Vector3 Excess { get; private set; }

        public void Reset()
        {
            Offset = Vector3.Zero;
            Excess = Vector3.Zero;
        }

        public void Update(float dt, float stretch, Vector3 direction)
        {
            Excess = Vector3.Zero;
            if (!(dt > 0f)) return;
            Vector3 want = Offset;
            float taut = stretch - Tolerance;
            if (taut > 0f && direction.LengthSquared() > 1e-12f) want += Vector3.Normalize(direction) * taut;
            else want *= (float)Math.Exp(-dt / Math.Max(ReturnSeconds, 1e-3f));
            float len = want.Length();
            if (len > MaxStretch)
            {
                Excess = want * ((len - MaxStretch) / len);
                want *= MaxStretch / len;
            }
            Vector3 step = want - Offset;
            float max = MaxSpeed * dt;
            if (step.Length() > max) step *= max / step.Length();
            Offset += step;
        }
    }

    /// <summary>Moving the tether point with the keys: camera-relative on the ground, up and down along the world's y.</summary>
    public static class PointControl
    {
        /// <summary>
        /// The point's move for <paramref name="dt"/> s. <paramref name="input"/>: x right, y up, z forward (-1..1 each);
        /// <paramref name="cameraForward"/>: the camera's view (its height is ignored).
        /// </summary>
        public static Vector3 Move(Vector3 input, Vector3 cameraForward, float speed, float dt)
        {
            Vector3 fwd = new Vector3(cameraForward.X, 0f, cameraForward.Z);
            fwd = fwd.LengthSquared() < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(fwd);
            Vector3 right = new Vector3(fwd.Z, 0f, -fwd.X);   // Unity: x = y cross z (left-handed)
            Vector3 flat = right * input.X + fwd * input.Z;
            if (flat.LengthSquared() > 1f) flat = Vector3.Normalize(flat);
            return (flat + Vector3.UnitY * Math.Clamp(input.Y, -1f, 1f)) * (speed * Math.Max(dt, 0f));
        }

        /// <summary>
        /// The chain holds her back: of her own step this frame (<paramref name="moved"/>, on the ground) the part that takes the
        /// nipple further from the point than <paramref name="reach"/> allows at their height difference is undone - nothing
        /// else, so she is never pulled towards the point or slid around. <paramref name="nipple"/> is where the nipple was
        /// before the step. Returns the correction (opposite to her step, horizontal), zero within reach or when stepping back in.
        /// Pulling her towards the point instead (0.1 / 0.2) moved her around too much [user, 2026-10-26].
        /// </summary>
        public static Vector3 Hold(Vector3 nipple, Vector3 point, float reach, Vector3 moved)
        {
            moved.Y = 0f;
            Vector3 now = nipple + moved;
            Vector3 h = new Vector3(now.X - point.X, 0f, now.Z - point.Z);
            float dy = point.Y - nipple.Y;
            float room = reach > Math.Abs(dy) ? (float)Math.Sqrt(reach * reach - dy * dy) : 0f;
            float dist = h.Length();
            if (dist <= room || dist < 1e-6f) return Vector3.Zero;
            Vector3 u = h / dist;
            float outward = Vector3.Dot(moved, u);
            if (outward <= 0f) return Vector3.Zero;
            return -u * Math.Min(outward, dist - room);
        }

        /// <summary>
        /// Keep the point within <paramref name="reach"/> of <paramref name="centre"/>: a move that would take it further out is
        /// held at the sphere (one that brings it back in is always allowed), and it never rises more than
        /// <paramref name="maxRise"/> above the centre (the chain cannot lift her).
        /// </summary>
        public static Vector3 Clamp(Vector3 from, Vector3 to, Vector3 centre, float reach, float maxRise)
        {
            if (to.Y > centre.Y + maxRise && to.Y > from.Y) to.Y = Math.Max(from.Y, centre.Y + maxRise);
            Vector3 d = to - centre;
            float dist = d.Length(), before = Vector3.Distance(from, centre);
            if (dist <= reach || dist <= before) return to;
            float keep = Math.Max(reach, before);
            return centre + d * (keep / dist);
        }
    }
}
