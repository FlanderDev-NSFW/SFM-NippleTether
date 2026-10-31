using System;
using System.IO;
using System.Numerics;
using NipplePiercings.Logic;
using NippleTether.Logic;

namespace NippleTether.Tests
{
    internal static class Program
    {
        private static int _passed, _failed;

        private static void Check(bool ok, string name)
        {
            if (ok) { _passed++; Console.WriteLine("  PASS  " + name); }
            else { _failed++; Console.WriteLine("  FAIL  " + name); }
        }

        private static bool Near(float a, float b, float eps = 1e-4f) => Math.Abs(a - b) <= eps;

        private static int Main()
        {
            ChainTests();
            LengthTests();
            ObstacleTests();
            PullTests();
            PointTests();
            string modelPath = Path.Combine(AppContext.BaseDirectory, "piercings.npd");
            if (File.Exists(modelPath)) ModelTests(modelPath);
            else Console.WriteLine("  SKIP  model tests (" + modelPath + " missing)");

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private const float Pitch = 0.015f, Thick = 0.002f;
        private static readonly Vector3 A = new Vector3(0f, 1.2f, 0f);

        private static void Run(TetherChain c, Vector3 a, Vector3 b, float seconds)
        {
            for (float t = 0f; t < seconds; t += 1f / 60f) c.Update(1f / 60f, a, b);
        }

        private static float Lowest(TetherChain c)
        {
            float y = float.MaxValue;
            for (int k = 0; k < c.Count; k++) y = Math.Min(y, c.Joint(k).Y);
            return y;
        }

        private static float WorstSpacing(TetherChain c)
        {
            float worst = 0f;
            for (int k = 0; k < c.Segments; k++) worst = Math.Max(worst, Math.Abs(Vector3.Distance(c.Joint(k), c.Joint(k + 1)) - c.Rest(k)));
            return worst;
        }

        private static void ChainTests()
        {
            var b = A + new Vector3(0f, 0f, 0.6f);
            var c = new TetherChain(Pitch, Thick, 1f);
            Check(c.Segments == 67 && Near(c.Rest(c.Segments - 1), 1f - 66 * Pitch, 1e-5f), "chain: 1 m of 1.5 cm links = 67 segments, the last one partial (" + c.Segments + ")");
            Run(c, A, b, 4f);
            float sag = A.Y - Lowest(c);
            Check(c.Stretch < 0.004f + 0.01f, "slack: 1 m between ends 0.6 m apart reads as not taut (stretch " + c.Stretch + ")");
            Check(sag > 0.25f && sag < 0.45f, "slack: it sags (" + sag + " m below the ends)");
            Check(WorstSpacing(c) < 0.002f, "slack: links keep their length (worst " + WorstSpacing(c) + ")");
            Check(c.Joint(0) == A && c.Joint(c.Count - 1) == b, "ends held at the nipple and the point");
            Check(Vector3.Dot(c.PullDirection, Vector3.Normalize(new Vector3(0f, -1f, 0.3f))) > 0.7f, "slack: it leaves the nipple downwards and forwards (" + c.PullDirection + ")");

            var far = A + new Vector3(0f, 0f, 1.2f);
            Run(c, A, far, 1f); // more than TeleportDistance at once: hung afresh, straight
            Run(c, A, far, 2f);
            Check(Near(c.Stretch, 0.2f, 0.02f), "taut: ends 1.2 m apart on 1 m of chain = 0.2 m stretch (" + c.Stretch + ")");
            Check(Vector3.Dot(c.PullDirection, Vector3.UnitZ) > 0.99f, "taut: pulls straight towards the point (" + c.PullDirection + ")");

            // A slowly moving point: no jump, the chain follows.
            c = new TetherChain(Pitch, Thick, 1f);
            Run(c, A, b, 2f);
            bool smooth = true;
            Vector3 mid = c.Joint(c.Count / 2);
            for (int f = 0; f < 120; f++)
            {
                c.Update(1f / 60f, A, b + new Vector3(0.5f * f / 120f, 0f, 0f));
                smooth &= Vector3.Distance(c.Joint(c.Count / 2), mid) < 0.05f;
                mid = c.Joint(c.Count / 2);
            }
            Check(smooth, "point moved 0.5 m in 2 s: the chain's middle moves less than 5 cm per frame");
        }

        private static void LengthTests()
        {
            var b = A + new Vector3(0f, 0f, 0.6f);
            var c = new TetherChain(Pitch, Thick, 1f);
            Run(c, A, b, 2f);
            bool ok = true, gentle = true;
            float before = Lowest(c);
            for (int f = 0; f < 120; f++)          // let out 0.5 m in 2 s
            {
                Vector3 m = c.Joint(c.Count / 2);
                c.SetLength(1f + 0.5f * (f + 1) / 120f);
                float sum = 0f;
                for (int k = 0; k < c.Segments; k++) sum += c.Rest(k);
                float last = c.Rest(c.Segments - 1);
                ok &= Near(sum, c.Length, 1e-4f) && last >= 0.25f * Pitch - 1e-6f && last <= 1.25f * Pitch + 1e-6f;
                c.Update(1f / 60f, A, b);
                gentle &= Vector3.Distance(m, c.Joint(c.Count / 2)) < 0.05f;
            }
            Check(ok, "let out: the rests add up to the length, the last segment stays 0.25 .. 1.25 links");
            Check(c.Segments == (int)Math.Ceiling(1.5f / Pitch - 0.25f) || Math.Abs(c.Segments - 1.5f / Pitch) < 1.5f, "let out: 1.5 m = " + c.Segments + " segments");
            Check(gentle, "let out: no joint jumps (middle < 5 cm per frame)");
            Run(c, A, b, 3f);
            Check(Lowest(c) < before - 0.1f, "let out: it hangs lower (" + (A.Y - Lowest(c)) + " vs " + (A.Y - before) + " m)");
            for (int f = 0; f < 120; f++) { c.SetLength(1.5f - 1.2f * (f + 1) / 120f); c.Update(1f / 60f, A, b); }
            Run(c, A, b, 1f);
            Check(c.Segments <= 21 && Near(c.Length, 0.3f, 1e-4f), "taken in to 0.3 m: " + c.Segments + " segments");
            Check(Near(c.Stretch, 0.3f, 0.03f), "taken in shorter than the ends' distance: stretch 0.3 (" + c.Stretch + ")");
            c.SetLength(0f);
            Check(c.Segments == 1 && c.Length >= c.MinLength, "never shorter than half a link");
        }

        /// <summary>A floor at y = 0 and a post (vertical cylinder) as level geometry. <see cref="ThinFloor"/>: like a floor's
        /// mesh in the game, a joint is pushed out of it only near its top (within 2 cm below); <see cref="NoSweep"/>: not swept.
        /// As Unity's sphere cast, a sweep ignores the floor when it starts touching it.</summary>
        private sealed class Level : IObstacles
        {
            public Vector3 Post = new Vector3(0f, 0f, 0.5f);
            public float PostRadius = 0.05f;
            public bool HasPost, ThinFloor, NoSweep;

            public bool Sweep(Vector3 from, ref Vector3 to, float radius, out Vector3 normal)
            {
                normal = Vector3.UnitY;
                if (NoSweep || from.Y < radius + 1e-4f || to.Y >= radius) return false;
                float t = (from.Y - radius) / (from.Y - to.Y);
                to = Vector3.Lerp(from, to, t);
                to.Y = radius;
                return true;
            }

            public int PushOuts;

            public bool PushOut(ref Vector3 p, float radius, out Vector3 normal)
            {
                PushOuts++;
                normal = Vector3.UnitY;
                bool moved = false;
                if (p.Y < radius && !(ThinFloor && p.Y < -0.02f)) { p.Y = radius; moved = true; }
                if (HasPost)
                {
                    var d = new Vector2(p.X - Post.X, p.Z - Post.Z);
                    float r = PostRadius + radius;
                    if (d.Length() < r)
                    {
                        var n = d.Length() < 1e-6f ? Vector2.UnitX : Vector2.Normalize(d);
                        p.X = Post.X + n.X * r;
                        p.Z = Post.Z + n.Y * r;
                        normal = new Vector3(n.X, 0f, n.Y);
                        moved = true;
                    }
                }
                return moved;
            }
        }

        private static void ObstacleTests()
        {
            var level = new Level();
            var c = new TetherChain(Pitch, Thick, 2f) { Environment = level };
            var a = new Vector3(0f, 0.3f, 0f);
            var b = new Vector3(0f, 0.3f, 0.5f);
            Run(c, a, b, 4f);
            Check(Lowest(c) >= Thick - 1e-4f, "floor: the chain lies on it, not in it (lowest " + Lowest(c) + ")");
            Check(c.EnvironmentContacts > 20, "floor: many joints rest on it (" + c.EnvironmentContacts + ")");

            // Remembered contacts: a 4 m chain at rest on the floor is fully checked about once in RequeryFrames frames per
            // joint (every joint every frame cost 4.6 ms a frame in game for two such chains), and still lies on it.
            {
                var rest = new Level();
                var lying = new TetherChain(Pitch, Thick, 4f) { Environment = rest };
                Run(lying, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0.3f, 0.6f), 4f);
                int checks = lying.FullChecks, pushOuts = rest.PushOuts;
                Run(lying, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0.3f, 0.6f), 1f);
                float perFrame = (lying.FullChecks - checks) / 60f, joints = lying.Count - 2;
                Check(perFrame <= joints / TetherChain.RequeryFrames * 1.3f, "level: about joints / " + TetherChain.RequeryFrames + " full checks a frame at rest (" + perFrame.ToString("0") + " of " + joints + ")");
                Check((rest.PushOuts - pushOuts) / 60f < joints * 0.6f, "level: fewer than every joint pushed out through the level each frame (" + ((rest.PushOuts - pushOuts) / 60f).ToString("0") + ")");
                Check(Lowest(lying) >= Thick - 1e-3f, "level: with remembered contacts the chain still lies on the floor (lowest " + Lowest(lying) + ")");
            }
            Vector3 m = c.Joint(c.Count / 2);
            Run(c, a, b, 1f);
            Check(Vector3.Distance(m, c.Joint(c.Count / 2)) < 0.01f, "floor: lying chain stays put (friction)");
            // A long chain falling onto a thin floor (400 cm at home in the game: it hung through the floor). Swept, it lands on it;
            // pushed out only (control), it falls through.
            var thin = new Level { ThinFloor = true };
            var fa = new Vector3(0f, 1.2f, 0f);
            var fb = new Vector3(0f, 1.2f, 0.6f);
            c = new TetherChain(Pitch, Thick, 4f) { Environment = thin };
            Run(c, fa, fb, 4f);
            Check(Lowest(c) >= Thick - 1e-4f, "thin floor: a long chain falling onto it lies on it (lowest " + Lowest(c) + ")");
            thin.NoSweep = true;
            c = new TetherChain(Pitch, Thick, 4f) { Environment = thin };
            Run(c, fa, fb, 4f);
            Check(Lowest(c) < -0.1f, "thin floor (control, not swept): it falls through (lowest " + Lowest(c) + ")");
            // Laid out below the floor (the point put down under it): from the nipple, swept joint by joint, it stays above.
            thin.NoSweep = false;
            c = new TetherChain(Pitch, Thick, 2f) { Environment = thin };
            c.Update(1f / 60f, new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0.3f, 0.5f));
            Check(Lowest(c) >= Thick - 1e-4f, "thin floor: a new layout that would sag through it is laid on it (lowest " + Lowest(c) + ")");

            var free = new TetherChain(Pitch, Thick, 2f);
            Run(free, a, b, 4f);
            Check(Lowest(free) < -0.3f, "control without the level: it hangs through the floor (" + Lowest(free) + ")");

            // Caught on a post: the chain held straight across it is pushed round it, so its path is longer than the ends'
            // distance - which reads as a pull although the ends are no further apart than the chain is long.
            level.HasPost = true;
            level.Post = new Vector3(0f, 0f, 0.2f);
            c = new TetherChain(Pitch, Thick, 0.4f) { Environment = level, Gravity = Vector3.Zero };
            var a2 = new Vector3(0f, 0.5f, 0f);
            var b2 = new Vector3(0f, 0.5f, 0.4f);
            Run(c, a2, b2, 2f);
            Check(c.Stretch > 0.01f, "post: a chain caught round it (path round its side 0.413 m) is held longer than it is (stretch " + c.Stretch + ", ends as far apart as it is long)");
            bool outside = true;
            for (int k = 0; k < c.Count; k++)
            {
                var p = c.Joint(k);
                outside &= new Vector2(p.X - level.Post.X, p.Z - level.Post.Z).Length() > level.PostRadius + Thick - 0.01f;
            }
            Check(outside, "post: no joint inside it");

            // Her body: a sphere between the ends pushes the chain round it.
            c = new TetherChain(Pitch, Thick, 1.2f) { Gravity = Vector3.Zero };
            c.Colliders.Add(Collider.Sphere(new Vector3(0f, 1.2f, 0.5f), 0.15f));
            Run(c, A, A + new Vector3(0f, 0f, 1f), 3f);
            bool clear = true;
            for (int k = 2; k < c.Count; k++) clear &= Vector3.Distance(c.Joint(k), new Vector3(0f, 1.2f, 0.5f)) > 0.15f + Thick - 1e-3f;
            Check(clear, "body sphere: joints stay outside it");
        }

        private static void PullTests()
        {
            var p = new BreastPull { MaxStretch = 0.05f, Tolerance = 0.004f };
            // The chain's stretch as seen from the drawn nipple: the ends 1.03 m apart along x on 1 m of chain, the nipple
            // drawn out by the offset.
            for (int f = 0; f < 120; f++)
            {
                float stretch = Math.Max(0f, 1.03f - p.Offset.X - 1f);
                p.Update(1f / 60f, stretch, Vector3.UnitX);
            }
            Check(p.Offset.X > 0.02f && p.Offset.X < 0.03f && p.Excess.Length() < 1e-4f,
                  "pull 3 cm: the breast is drawn out ~3 cm less the tolerance, she is not moved (" + p.Offset.X + ")");
            for (int f = 0; f < 60; f++) p.Update(1f / 60f, 0f, Vector3.UnitX);
            Check(p.Offset.Length() < 0.001f, "slack: the breast springs back within a second (" + p.Offset.Length() + ")");
            p.Reset();
            float moved = 0f;
            for (int f = 0; f < 120; f++)
            {
                float stretch = Math.Max(0f, 1.10f - p.Offset.X - moved - 1f);
                p.Update(1f / 60f, stretch, Vector3.UnitX);
                moved += p.Excess.X;
            }
            Check(p.Offset.X > 0.045f && p.Offset.X <= 0.05f + 1e-5f, "pull 10 cm: the breast drawn out to (nearly) its 5 cm (" + p.Offset.X + ")");
            Check(moved > 0.04f && moved < 0.05f, "pull 10 cm: she is moved the rest (" + moved + " m)");
            var q = new BreastPull { MaxSpeed = 1f };
            q.Update(1f / 60f, 0.5f, Vector3.UnitY);
            Check(q.Offset.Length() <= 1f / 60f + 1e-5f, "a sudden pull draws the nipple out at most MaxSpeed");
        }

        private static void PointTests()
        {
            var v = PointControl.Move(new Vector3(0f, 0f, 1f), new Vector3(0f, -0.5f, 1f), 1f, 0.5f);
            Check(Near(v.Z, 0.5f) && Near(v.Y, 0f), "point: forward is the camera's view on the ground");
            v = PointControl.Move(new Vector3(1f, 0f, 0f), Vector3.UnitX, 1f, 1f);
            Check(Near(v.Z, -1f) && Near(v.X, 0f), "point: right of a camera looking along +x is -z (Unity)");
            v = PointControl.Move(new Vector3(1f, 1f, 1f), Vector3.UnitZ, 1f, 1f);
            Check(Near(new Vector2(v.X, v.Z).Length(), 1f) && Near(v.Y, 1f), "point: diagonal not faster on the ground; up by the up key");
            var centre = Vector3.Zero;
            var held = PointControl.Clamp(new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 1.2f), centre, 1f, 5f);
            Check(Near(held.Z, 1f), "point: not past the reach");
            var back = PointControl.Clamp(new Vector3(0f, 0f, 1.5f), new Vector3(0f, 0f, 1.4f), centre, 1f, 5f);
            Check(Near(back.Z, 1.4f), "point: beyond the reach (she was stuck) it may come back");
            var f = PointControl.Hold(new Vector3(0f, 0f, -0.95f), Vector3.Zero, 1f, new Vector3(0f, 0f, -0.1f));
            Check(Near(f.Z, 0.05f) && Near(f.X, 0f) && Near(f.Y, 0f), "hold: a step from 0.95 to 1.05 m on 1 m of reach -> 5 cm of it undone");
            f = PointControl.Hold(new Vector3(0f, 0f, -1.2f), Vector3.Zero, 1f, new Vector3(0f, 0f, 0.1f));
            Check(f.Length() < 1e-6f, "hold: beyond reach but stepping back in -> not moved (never pulled in)");
            f = PointControl.Hold(new Vector3(0f, 0f, -1.2f), Vector3.Zero, 1f, Vector3.Zero);
            Check(f.Length() < 1e-6f, "hold: beyond reach standing still -> not moved");
            f = PointControl.Hold(new Vector3(0f, 0f, -1.1f), Vector3.Zero, 1f, new Vector3(0.1f, 0f, 0f));
            Check(f.Length() > 0.008f && f.Length() < 0.0095f, "hold: beyond reach stepping 10 cm sideways (0.9 cm outward) -> only the outward bit undone (" + f.Length() + ")");
            f = PointControl.Hold(new Vector3(0f, 1.2f, 0f), Vector3.Zero, 1f, new Vector3(0.1f, 0f, 0f));
            Check(Near(f.X, -0.1f), "hold: point far below (no room) -> a step away from over it undone");
            f = PointControl.Hold(new Vector3(0f, 0.8f, -0.5f), Vector3.Zero, 1f, new Vector3(0f, 0f, -0.05f));
            Check(f.Length() < 1e-6f, "hold: within reach (0.55 aside, 0.8 up on 1 m) -> free");
            var up = PointControl.Clamp(new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.6f, 0f), centre, 5f, 0.5f);
            Check(Near(up.Y, 0.5f), "point: never more than the chain's reach above her");
        }

        private static void ModelTests(string path)
        {
            PiercingSet set;
            using (var f = File.OpenRead(path)) set = PiercingSet.Read(f);
            var link = set.Get("link");
            var ring = set.Get("ring");
            var hoop = set.Get("hoop");
            Check(link != null && link.Kind == PiercingKind.Link && link.Bones.Count == 1, "model: the chain link (one piece)");
            Check(ring != null && ring.Bones.Count == 2 && ring.Bones[1].Positions.Length > 0, "model: the ring for the nipples (pierced point, ring)");
            Check(hoop != null && hoop.Bones[hoop.Bones.Count - 1].Positions.Length > 0, "model: the hoop for the point");
        }
    }
}
