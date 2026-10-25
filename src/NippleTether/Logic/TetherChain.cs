using System;
using System.Collections.Generic;
using System.Numerics;
using NipplePiercings.Logic;

namespace NippleTether.Logic
{
    /// <summary>Solid things in the world a chain's joints are pushed out of (the level's colliders; the game side implements it).</summary>
    public interface IObstacles
    {
        /// <summary>Move <paramref name="p"/> (a sphere of <paramref name="radius"/>) out of anything solid it overlaps; true if it
        /// moved, with the way out in <paramref name="normal"/> (unit).</summary>
        bool PushOut(ref Vector3 p, float radius, out Vector3 normal);
    }

    /// <summary>
    /// A chain held at both ends - a nipple's piercing (end A, joint 0) and the tether point (end B, the last joint) - that can be
    /// let out and taken in while it hangs: its length is a whole number of links of <see cref="Pitch"/> plus a last, partial
    /// segment at B (links feed out of and into the point's ring there, so the length changes smoothly). Position-based Verlet
    /// like NipplePiercings' <c>SaggingChain</c>: gravity, damping, lengths swept from both ends many times per step, joints kept
    /// out of <see cref="Colliders"/> (her body) every sweep and out of <see cref="Environment"/> (the level) once per frame,
    /// with friction there so a chain lying on the floor or over a rail stays put.
    /// <para>
    /// <see cref="Stretch"/> says how much longer the chain is held than it is: when the ends are further apart than its length,
    /// or it is caught around something, the joints cannot keep their spacing and the path grows. The caller turns that into a
    /// pull on her (breast, then her whole body). World space, metres.
    /// </para>
    /// </summary>
    public sealed class TetherChain
    {
        public const float StepSeconds = 1f / 120f;
        public const int MaxSteps = 6;
        public const int Iterations = 24;         // projections per step (8 left a taut chain sagging 19 degrees at the nipple)
        public const int SettleIterations = 4;   // after the environment pushed joints
        public const float TeleportDistance = 0.5f;

        private readonly List<Vector3> _x = new List<Vector3>(), _prev = new List<Vector3>();
        private readonly List<Vector3> _contact = new List<Vector3>();
        private bool[] _hit = Array.Empty<bool>();   // joints the level pushed this frame
        private float _scale = 1f;                   // the links' lengths while the ends are held further apart than the chain is long
        private float _length, _carry;
        private bool _hung;

        public float Pitch { get; }
        public float Thickness { get; }
        public float Damping = 1f;
        public float Friction = 0.6f;            // share of the sliding speed lost at a contact with the level, per frame
        public Vector3 Gravity = new Vector3(0f, -9.81f, 0f);
        public readonly List<Collider> Colliders = new List<Collider>();
        public int SkipNearA = 2;                // joints next to the nipple stay out of the body colliders' way
        public IObstacles Environment;

        /// <param name="pitch">Distance between the joints of a link, m.</param>
        /// <param name="thickness">Half thickness of a link, m (kept off surfaces).</param>
        /// <param name="length">Length, m.</param>
        public TetherChain(float pitch, float thickness, float length)
        {
            if (!(pitch > 0f)) throw new ArgumentException("pitch must be positive");
            Pitch = pitch;
            Thickness = thickness;
            _length = Math.Max(length, MinLength);
            int segments = Math.Max(1, (int)Math.Ceiling(_length / pitch - 0.25f));
            for (int k = 0; k <= segments; k++) { _x.Add(Vector3.Zero); _prev.Add(Vector3.Zero); _contact.Add(Vector3.Zero); }
        }

        public float MinLength => Pitch * 0.5f;
        public int Count => _x.Count;
        public int Segments => _x.Count - 1;
        public Vector3 Joint(int k) => _x[k];
        public float Length => _length;

        /// <summary>Rest length of segment k: a link's pitch, the last one (at B) what is left, 0.25 .. 1.25 pitches.</summary>
        public float Rest(int k) => k < Segments - 1 ? Pitch : _length - Pitch * (Segments - 1);

        /// <summary>How much longer the chain is held than it is (m, 0 when it hangs slack), after the last update.</summary>
        public float Stretch { get; private set; }

        /// <summary>The way the chain leaves end A (unit), after the last update: the way it pulls on the nipple.</summary>
        public Vector3 PullDirection { get; private set; } = Vector3.UnitZ;

        /// <summary>Number of joints that touched the level in the last update (diagnostics).</summary>
        public int EnvironmentContacts { get; private set; }

        public void Reset() => _hung = false;

        /// <summary>Let the chain out or take it in to <paramref name="length"/> (m): links are added or removed at end B.</summary>
        public void SetLength(float length)
        {
            _length = Math.Max(length, MinLength);
            for (int guard = 0; guard < 10000; guard++)
            {
                int n = Segments;
                float last = Rest(n - 1);
                if (last > 1.25f * Pitch)
                {
                    // A new joint one pitch out from the last full link, towards B; it keeps that joint's speed.
                    Vector3 from = _x[n - 1], to = _x[n];
                    float span = Vector3.Distance(from, to);
                    float t = span < 1e-9f ? 0f : Math.Min(1f, Pitch / span);
                    Vector3 p = Vector3.Lerp(from, to, t);
                    Vector3 v = _x[n - 1] - _prev[n - 1];
                    _x.Insert(n, p);
                    _prev.Insert(n, p - v);
                    _contact.Insert(n, Vector3.Zero);
                }
                else if (last < 0.25f * Pitch && n > 1)
                {
                    _x.RemoveAt(n - 1);
                    _prev.RemoveAt(n - 1);
                    _contact.RemoveAt(n - 1);
                }
                else return;
            }
        }

        public void Update(float dt, Vector3 a, Vector3 b)
        {
            int n = Segments;
            if (!_hung || Vector3.Distance(a, _x[0]) > TeleportDistance || Vector3.Distance(b, _x[n]) > TeleportDistance)
            {
                Hang(a, b);
                Measure(a, b);
                return;
            }
            if (!(dt > 0f)) return;
            _carry = Math.Min(_carry + dt, MaxSteps * StepSeconds);
            int steps = (int)(_carry / StepSeconds);
            if (steps == 0) { Measure(a, b); return; }
            _carry -= steps * StepSeconds;
            Vector3 a0 = _x[0], b0 = _x[n];
            float keep = (float)Math.Exp(-Damping * StepSeconds);
            Vector3 g = Gravity * (StepSeconds * StepSeconds);
            for (int s = 1; s <= steps; s++)
            {
                float f = (float)s / steps;
                _x[0] = _prev[0] = Vector3.Lerp(a0, a, f);
                _x[n] = _prev[n] = Vector3.Lerp(b0, b, f);
                // Held further apart than it is long the lengths have no solution, and the projection folded the chain into a
                // zigzag (path 1.6 m for ends 1.2 m apart on 1 m): it is solved stretched evenly instead (Stretch tells how much).
                // A hair shorter than the ends' distance, so the links pull and keep it straight: at exactly that length a
                // straight chain has no sideways stiffness in the projection and sagged 19 degrees under gravity.
                _scale = Math.Max(1f, 0.99f * Vector3.Distance(_x[0], _x[n]) / _length);
                for (int k = 1; k < n; k++)
                {
                    Vector3 v = (_x[k] - _prev[k]) * keep;
                    _prev[k] = _x[k];
                    _x[k] += v + g;
                    _contact[k] = Vector3.Zero;
                }
                Solve(Iterations);
                KeepContacts();
            }
            EnvironmentContacts = 0;
            if (Environment != null)
            {
                if (_hit.Length < n + 1) _hit = new bool[2 * (n + 1)];
                for (int k = 1; k < n; k++)
                {
                    Vector3 p = _x[k];
                    _hit[k] = Environment.PushOut(ref p, Thickness, out Vector3 normal);
                    if (!_hit[k]) continue;
                    EnvironmentContacts++;
                    _x[k] = p;
                    // No speed into the surface, and friction along it.
                    Vector3 v = _x[k] - _prev[k];
                    float into = Vector3.Dot(v, normal);
                    if (into < 0f) v -= normal * into;
                    v *= 1f - Math.Clamp(Friction, 0f, 1f);
                    _prev[k] = _x[k] - v;
                }
                if (EnvironmentContacts > 0)
                {
                    Solve(SettleIterations);
                    // Settling pulls some joints back in a little: those that touched are pushed out once more.
                    for (int k = 1; k < n; k++)
                    {
                        Vector3 p = _x[k];
                        if (_hit[k] && Environment.PushOut(ref p, Thickness, out _)) _x[k] = p;
                    }
                }
            }
            Measure(a, b);
        }

        private void Solve(int iterations)
        {
            int n = Segments;
            for (int it = 0; it < iterations; it++)
            {
                Project();
                if (Colliders.Count > 0 && (it % 4 == 3 || it == iterations - 1)) // every fourth: ~15 colliders x 200 joints
                    for (int k = Math.Max(1, SkipNearA); k < n; k++) PushOut(k);
            }
        }

        /// <summary>Added to the system's diagonal (Levenberg-Marquardt damping), for steadiness where the lengths cannot all be
        /// met (a chain caught on something). More (0.5) left a slack chain 5.7 % long.</summary>
        private const double Regularization = 0.01;

        private double[] _lambda = Array.Empty<double>(), _sub = Array.Empty<double>(), _diag = Array.Empty<double>(), _rhs = Array.Empty<double>();
        private Vector3[] _dir = Array.Empty<Vector3>();

        /// <summary>
        /// One step of "fast projection" (Goldenthal et al. 2007): all link lengths corrected at once by solving the linearised
        /// constraints J W J^T lambda = -C, which is tridiagonal for a chain (Thomas algorithm, O(n)). Sweeping the links one by
        /// one (Gauss-Seidel) left a 67-link chain 4.6 % long while it hung slack - read as a pull - and let a taut one sag; a
        /// few of these steps keep every length to well under a millimetre. Both ends are held (weight 0).
        /// </summary>
        private void Project()
        {
            int n = Segments;
            if (_lambda.Length < n)
            {
                int size = Math.Max(n, 2 * _lambda.Length);
                _lambda = new double[size]; _sub = new double[size]; _diag = new double[size]; _rhs = new double[size];
                _dir = new Vector3[size];
            }
            for (int k = 0; k < n; k++)
            {
                Vector3 d = _x[k + 1] - _x[k];
                float len = d.Length();
                _dir[k] = len < 1e-9f ? Vector3.UnitY : d / len;
                _rhs[k] = -(len - Rest(k) * _scale);
            }
            // Row k: (w_k + w_k+1) l_k - w_k (u_k-1 . u_k) l_k-1 - w_k+1 (u_k . u_k+1) l_k+1 = -C_k.
            for (int k = 0; k < n; k++)
            {
                double wa = k == 0 ? 0.0 : 1.0, wb = k + 1 == n ? 0.0 : 1.0;
                _diag[k] = wa + wb + Regularization;
                _sub[k] = k == 0 ? 0.0 : -wa * Vector3.Dot(_dir[k - 1], _dir[k]); // couples l_k-1 and l_k
            }
            if (n == 1 && _diag[0] <= 0.0) return; // a single segment between two held ends
            // Thomas algorithm on the symmetric tridiagonal system (sub = super).
            var c = _lambda; // reuse as scratch for the modified super diagonal
            double denom = _diag[0];
            if (Math.Abs(denom) < 1e-12) return;
            c[0] = n > 1 ? _sub[1] / denom : 0.0;
            _rhs[0] /= denom;
            for (int k = 1; k < n; k++)
            {
                denom = _diag[k] - _sub[k] * c[k - 1];
                if (Math.Abs(denom) < 1e-12) return;
                c[k] = k + 1 < n ? _sub[k + 1] / denom : 0.0;
                _rhs[k] = (_rhs[k] - _sub[k] * _rhs[k - 1]) / denom;
            }
            for (int k = n - 2; k >= 0; k--) _rhs[k] -= c[k] * _rhs[k + 1];
            // dx_j = w_j (sum over constraints of grad * lambda): -u_j l_j from segment j, +u_j-1 l_j-1 from segment j-1.
            for (int j = 1; j < n; j++)
                _x[j] += _dir[j - 1] * (float)_rhs[j - 1] - _dir[j] * (float)_rhs[j];
        }

        /// <summary>Resting against her, not bouncing off: no speed away from a body collider after a push.</summary>
        private void KeepContacts()
        {
            for (int k = 1; k < Segments; k++)
            {
                if (_contact[k] == Vector3.Zero) continue;
                Vector3 v = _x[k] - _prev[k];
                float away = Vector3.Dot(v, _contact[k]);
                if (away > 0f) _prev[k] += _contact[k] * away;
            }
        }

        private void Measure(Vector3 a, Vector3 b)
        {
            float path = 0f;
            for (int k = 0; k < Segments; k++) path += Vector3.Distance(_x[k], _x[k + 1]);
            Stretch = Math.Max(0f, Math.Max(path - _length, Vector3.Distance(a, b) - _length));
            Vector3 d = _x[Math.Min(3, Segments)] - _x[0];
            if (d.LengthSquared() < 1e-12f) d = b - a;
            if (d.LengthSquared() > 1e-12f) PullDirection = Vector3.Normalize(d);
        }

        /// <summary>A parabola between the ends with the chain's length (near enough to a hanging chain to start from); straight
        /// when the ends are further apart than that.</summary>
        private void Hang(Vector3 a, Vector3 b)
        {
            int n = Segments;
            float span = Vector3.Distance(a, b);
            float sag = _length > span ? (float)Math.Sqrt(3.0 / 8.0 * span * (_length - span)) : 0f;
            Vector3 down = Gravity.LengthSquared() > 1e-12f ? Vector3.Normalize(Gravity) : -Vector3.UnitY;
            float along = 0f;
            for (int k = 0; k <= n; k++)
            {
                float t = k == n ? 1f : along / _length;
                _x[k] = _prev[k] = Vector3.Lerp(a, b, t) + down * (sag * 4f * t * (1f - t));
                _contact[k] = Vector3.Zero;
                if (k < n) along += Rest(k);
            }
            _carry = 0f;
            _hung = true;
        }

        private void PushOut(int k)
        {
            float deepest = 0f;
            Vector3 way = Vector3.Zero;
            foreach (var c in Colliders)
            {
                float depth = c.Depth(_x[k], Thickness, out Vector3 n);
                if (depth > deepest) { deepest = depth; way = n; }
            }
            if (deepest <= 0f) return;
            _x[k] += way * deepest;
            _contact[k] = way;
        }

        /// <summary>
        /// Link k: its centre and rotation (the exported link: long axis along x, flat side facing z; neighbours turned 90
        /// degrees about the chain, as real links interlock). The last, partial segment's link is drawn a whole pitch long ending
        /// at B - it comes out of the point's ring.
        /// </summary>
        public (Vector3 Centre, Quaternion Rotation) Link(int k)
        {
            Vector3 a = _x[k], b = _x[k + 1], axis = b - a;
            float len = axis.Length();
            axis = len < 1e-9f ? Vector3.UnitX : axis / len;
            Vector3 centre = k == Segments - 1 ? b - axis * (Pitch / 2f) : (a + b) / 2f;
            Vector3 side = Vector3.Cross(axis, Vector3.UnitY);
            if (side.LengthSquared() < 1e-6f) side = Vector3.Cross(axis, Vector3.UnitZ);
            side = Vector3.Normalize(side);
            Vector3 up = Vector3.Cross(side, axis);
            Vector3 face = k % 2 == 0 ? side : up;
            Vector3 plane = k % 2 == 0 ? up : -side;
            return (centre, SaggingChain.LookRotation(face, plane));
        }
    }
}
