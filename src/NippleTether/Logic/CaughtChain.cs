using System;
using System.Collections.Generic;
using System.Numerics;

namespace NippleTether.Logic
{
    /// <summary>
    /// A taut chain that is caught on something (her arm, a rail) on its way to its point, when breasts are moved by BreastPhysics:
    /// it draws the nipple towards the place it bends first, by <see cref="Pull"/>. That is drawn further while the chain is too
    /// short and let go when it hangs slack - slowly while it just reaches, so that the breast, which gives to it, and the chain,
    /// which the breast then slackens, do not chase each other.
    /// <para>
    /// A chain that is caught and too short has no solution, its joints jitter, and the bend with them [measured 2026-11-24: the
    /// nipple shook 2 cm a frame]. So the place follows the bend's mean, and only when that has left it by more than
    /// <see cref="Dead"/>; quickly while she moves. It is kept as a pull, not as a length to the place: a length goes stale with
    /// the place (she turns away from it) and then pulls although the chain is slack.
    /// </para>
    /// </summary>
    public sealed class CaughtChain
    {
        public const float TakeSpeed = 0.6f, EaseSpeed = 0.05f;   // m/s the pull grows, shrinks when just slack
        public const float EaseAfter = 0.1f;                      // s without any stretch
        public const float Slackness = 4f;                        // 1/s: how fast a chain that sags lets go, by how much it sags
        public const float Straight = 0.015f;                     // a joint this far off the line is a bend (m)
        public const float Near = 0.08f, GoneAfter = 0.1f;        // m, s: the chain no longer goes past the place it was caught
        public const float Dead = 0.03f, Mean = 0.3f, MeanMoving = 0.05f;   // m, s: how the place follows the bend
        public const float Still = 0.15f, Moving = 0.6f;          // m/s: her speed at rest, and from which it follows quickly

        private float _slackFor, _goneFor, _straightFor;
        private Vector3 _mean, _resting;
        private bool _following, _hasResting;

        public bool Caught { get; private set; }
        /// <summary>The place it is caught: the nipple is drawn towards it.</summary>
        public Vector3 Point { get; private set; }
        /// <summary>How far (m).</summary>
        public float Pull { get; private set; }

        public void Reset() { Caught = false; _hasResting = false; }

        /// <param name="joints">The chain's joints from the nipple to the point.</param>
        /// <param name="chainLength">Its length.</param>
        /// <param name="stretch">How much longer it is held than it is.</param>
        /// <param name="tolerance">Stretch its solver shows without being taut.</param>
        /// <param name="resting">Where the chain would start with the breast at rest.</param>
        /// <param name="most">How far the breast is drawn from there at most.</param>
        public void Update(float dt, IReadOnlyList<Vector3> joints, float chainLength, float stretch, float tolerance, Vector3 resting, float most)
        {
            if (!(dt > 0f)) return;
            int n = joints.Count - 1;
            Vector3 a = joints[0];
            float speed = _hasResting ? Vector3.Distance(resting, _resting) / dt : 0f;
            _resting = resting;
            _hasResting = true;
            if (stretch > tolerance)
            {
                int bend = FirstBend(joints);
                // Its way is longer than the straight one, which its point alone would hold.
                float excess = stretch - Math.Max(0f, Vector3.Distance(a, joints[n]) - chainLength);
                if (bend < n && excess > tolerance)
                {
                    if (!Caught)
                    {
                        Caught = true;
                        Point = _mean = joints[bend];
                        _following = false;
                        Vector3 way = Point - resting;
                        Pull = way.LengthSquared() < 1e-9f ? 0f : Math.Clamp(Vector3.Dot(a - resting, Vector3.Normalize(way)), 0f, most);
                    }
                    Follow(joints[bend], dt, Math.Clamp((speed - Still) / (Moving - Still), 0f, 1f));
                    Pull = Math.Min(most, Pull + Math.Min((stretch - tolerance) * 0.3f, TakeSpeed * dt));
                    _slackFor = _goneFor = _straightFor = 0f;
                    return;
                }
                // Straight to its point: that holds it.
                _straightFor = bend == n ? _straightFor + dt : 0f;
                if (_straightFor > GoneAfter) Caught = false;
                return;
            }
            if (!Caught) return;
            _straightFor = 0f;
            _slackFor = stretch > tolerance * 0.25f ? 0f : _slackFor + dt;
            int at = Nearest(joints, out float away);
            _goneFor = away < Near ? 0f : _goneFor + dt;
            if (_goneFor > GoneAfter || _slackFor > 4f) { Caught = false; return; }
            if (_slackFor <= EaseAfter) return;
            // How much it sags up to there is how much nearer she has come.
            float path = 0f;
            for (int i = 0; i < at; i++) path += Vector3.Distance(joints[i], joints[i + 1]);
            Pull -= (EaseSpeed + Slackness * Math.Max(0f, path - Vector3.Distance(a, joints[at]))) * dt;
            if (Pull <= 0f) Caught = false;
        }

        private void Follow(Vector3 bend, float dt, float moving)
        {
            float seconds = Mean + (MeanMoving - Mean) * moving, dead = Dead * (1f - moving);
            _mean += (bend - _mean) * (1f - (float)Math.Exp(-dt / seconds));
            float off = Vector3.Distance(_mean, Point);
            if (off > dead) _following = true;
            else if (off < 0.2f * dead) _following = false;
            if (_following) Point += (_mean - Point) * (1f - (float)Math.Exp(-dt / Math.Max(0.02f, 0.5f * seconds)));
        }

        private int Nearest(IReadOnlyList<Vector3> joints, out float away)
        {
            int at = 0;
            float best = float.MaxValue;
            for (int i = 0; i < joints.Count; i++)
            {
                float d = Vector3.DistanceSquared(joints[i], Point);
                if (d < best) { best = d; at = i; }
            }
            away = (float)Math.Sqrt(best);
            return at;
        }

        /// <summary>The last joint up to which the chain is straight from the nipple.</summary>
        public static int FirstBend(IReadOnlyList<Vector3> joints)
        {
            int n = joints.Count - 1, good = 1;
            // Coarse steps first: a 4 m chain has some 270 joints.
            for (int k = 2; k <= n; k = k < n ? Math.Min(n, k + Math.Max(1, k / 6)) : n + 1)
            {
                if (!IsStraight(joints, k)) break;
                good = k;
            }
            while (good < n && IsStraight(joints, good + 1)) good++;
            return good;
        }

        private static bool IsStraight(IReadOnlyList<Vector3> joints, int k)
        {
            Vector3 a = joints[0], d = joints[k] - a;
            float l2 = d.LengthSquared();
            if (l2 < 1e-12f) return false;
            for (int i = 1; i < k; i++)
            {
                Vector3 p = joints[i] - a;
                if ((p - d * (Vector3.Dot(p, d) / l2)).LengthSquared() > Straight * Straight) return false;
            }
            return true;
        }
    }
}
