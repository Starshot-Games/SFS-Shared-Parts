using System.Collections.Generic;
using UnityEngine;

namespace SFS.Parts.Modules
{
    // The merged plume a single flame is part of. Mirrors the merge* uniforms in
    // Shaders\New Flames\Flame_Base.cginc; amount = 0 (the default) means "not merging".
    public struct FlameMerge
    {
        public float amount;
        public float contact; // how far the group has come together, 0..1, the same for every flame in it
        public float fadeLength;
        public Vector2 share;
        public Vector2 litShare;
        public Vector2 joinWeight; // how firmly it is joined to the slice either side: 0 = it ends at its own outline, as if alone
        public float shareFadeLength;
        public Vector2 nozzleBounds;
        public float drift;
        public float bend; // where its jet has eased onto the group's heading
        public Vector2 closeLength; // how far past the merge plane each join closes
        public float stripeScale;
        public float center;
        public float halfSpan;
        public float throat;
        public float nozzleRadius; // the group's average nozzle half-width, the same for every flame in it
        public bool leadsSmoke;    // whether this flame sends out the smoke of every flame in the group that leaves any, so it's drawn once
        public float smokeNozzles; // their combined nozzle half-widths, over this flame's own
        public float groupLength;
        public float alongOffset;
        public float exitPressure;
        public float throttle;
        public float airSpread;

        public Color color;
        public float additiveBlend;
        public float stripesWidth;
        public float stripesStrength;
        public float soot;
        public Vector2 turbulence; // in air and in vacuum
        public float mirror;       // -1 where this flame's x runs the other way across the group
        public float seed;
        public float eddyFlow;     // group lengths a second its turbulence streams down it
        public float lead;         // the group's mean distance from its nozzles down to the merge plane
        public float refLength;    // one plume length the whole group goes by, whatever its engines' sizes
        public FlameNeighbour left, right;

        // Restates the merge in a mesh renderer's frame, given its local x/y axes measured in module space
        public FlameMerge InSpaceOf(float acrossScale, float alongScale)
        {
            if (amount <= 0)
                return this;

            bool mirrored = acrossScale < 0;
            float across = Mathf.Abs(acrossScale);
            float along = Mathf.Abs(alongScale);

            FlameMerge m = this;
            m.lead = lead / along;
            m.refLength = refLength / along;
            m.left = (mirrored ? right : left).InSpaceOf(acrossScale, alongScale);
            m.right = (mirrored ? left : right).InSpaceOf(acrossScale, alongScale);
            m.center = center / acrossScale;
            m.mirror = mirrored ? -mirror : mirror;
            m.halfSpan = halfSpan / across;
            m.throat = throat / across;
            m.nozzleRadius = nozzleRadius / across;
            m.share = mirrored ? new Vector2(1 - share.y, 1 - share.x) : share;
            m.litShare = mirrored ? new Vector2(1 - litShare.y, 1 - litShare.x) : litShare;
            m.joinWeight = mirrored ? new Vector2(joinWeight.y, joinWeight.x) : joinWeight;
            m.nozzleBounds = mirrored ? new Vector2(nozzleBounds.y, nozzleBounds.x) / acrossScale : nozzleBounds / acrossScale;
            m.drift = drift * along / acrossScale; // an x per a y, so both scalings apply
            m.bend = bend / along;
            m.closeLength = (mirrored ? new Vector2(closeLength.y, closeLength.x) : closeLength) / along;
            m.groupLength = groupLength / along;
            m.alongOffset = alongOffset / along;
            m.fadeLength = fadeLength / along;
            m.shareFadeLength = shareFadeLength / along;
            return m;
        }
    }

    // A jet next to a flame in its merged plume, in that flame's space - its light runs on past the join between them. Mirrors neighbourFrame/Flow in the shader.
    public struct FlameNeighbour
    {
        public bool present;
        public Vector2 origin;  // its axis' x at along 0, and the along of its nozzle
        public float slope;     // its axis' sideways travel per local y
        public float drift; // travel its jet gives up easing onto the group's heading
        public float bend; // over this far down it
        public Vector2 scale;   // one of its local x and y units
        public float exitPressure;
        public float throttle;
        public float airSpread;
        public float soot;
        public float amount;    // how far it has joined the group (its own FlameMerge.amount)
        public float seed;      // its own turbulence, which it keeps until it has fully joined
        public float eddyFlow;

        public FlameNeighbour InSpaceOf(float acrossScale, float alongScale)
        {
            float along = Mathf.Abs(alongScale);

            FlameNeighbour n = this;
            n.origin = new Vector2(origin.x / acrossScale, origin.y / along);
            n.slope = slope * along / acrossScale; // an x per a y, so both scalings apply
            n.drift = drift * along / acrossScale;
            n.bend = bend / along;
            n.scale = new Vector2(scale.x / acrossScale, scale.y / along);
            return n;
        }
    }

    // A far brighter plume running past a flame, which drowns the flame out wherever it lies under it - see FlameMergeSolver.SolveGlare.
    public struct FlameGlare
    {
        public Vector2 origin;
        public float slope;
        public float depth;
        public float halfSpan;
        public float throat;
        public float length;
        public float exitPressure;
        public float throttle;
        public float airSpread;

        public FlameGlare InSpaceOf(float acrossScale, float alongScale)
        {
            float across = Mathf.Abs(acrossScale);
            float along = Mathf.Abs(alongScale);

            FlameGlare g = this;
            g.origin = new Vector2(origin.x / acrossScale, origin.y / along);
            g.slope = slope * along / acrossScale; // an x per a y, so both scalings apply
            g.halfSpan = halfSpan / across;
            g.throat = throat / across;
            g.length = length / along;
            return g;
        }

        // Mirror of GetGlare + GetDrowned in the flame shader: how much of the flame is lost at a
        // point, in the same space this is in.
        public float GetDrowned(float x, float along, float atmospherePressure, float diamondOffset)
        {
            if (depth <= 0)
                return 0;

            float t = (along - origin.y) / Mathf.Max(length, 1e-4f);
            if (t < 0)
                return 0;

            float width = halfSpan + throat * (FlameMeshModule.GetFlameWidth(t, exitPressure, throttle, airSpread, atmospherePressure, diamondOffset) - 1);
            float present = Mathf.Clamp01(1 - t / Mathf.Lerp(0.01f, 1, throttle));
            float centre = origin.x + slope * (along - origin.y);
            float across = Mathf.Abs(x - centre) / Mathf.Max(width, 1e-4f);

            return depth * present * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(FlameMergeSolver.GlareCore, 1, across)));
        }
    }
    
    public static class FlameMergeSolver
    {
        // How far two engines may point away from each other and still share a plume.
        const float MaxAngleBetweenEngines = 15;
        // How many times brighter than the other one of them may be - see BurnAlike.
        const float MaxLuminosityRatio = 2;
        // How many brighter plumes a flame can be drowned out by at once - see SolveGlare. Matches
        // glareSources in the shader.
        public const int MaxGlareSources = 3;
        // How far out from a bright plume's axis whatever is under it is drowned out in full, as a fraction of the plume's half-width
        public const float GlareCore = 0.2f;
        // Whole plume, so the contact ramp eases a late touch out
        const float TouchSearchLength = 1f;
        // how far past their glows jets pull each other in
        const float EntrainReach = 0.15f;
        // mixing layer spread per unit down, at sea level
        const float EntrainSpread = 0.1f;
        const float EntrainThinAir = 0.01f; // dies away below this pressure
        // Crossfade into one plume, as a fraction of the merged plume. A distance rather than a
        // share of each mesh, so the whole group finishes merging at the same point.
        const float FadeLength = 0.4f;
        // How soon the bright jets have spread over the dim ones, as a fraction of the merged plume.
        const float ShareFadeLength = 0.1f;
        // How strongly a bigger group stretches the plume
        const float GroupLengthPower = 0.5f;
        const int TouchSamples = 16;
        // reach past the first touch for full contact, of plume length
        const float PresenceRamp = 0.25f;
        // least bend, of plume length
        const float MinBend = 0.3f;
        // "No neighbour on this side". Finite so it survives being interpolated in the shader.
        const float FarAway = 1e6f;
        // What a pair that touches behind jets in between still counts for in where a join comes together
        const float HiddenTouch = 1e-6f;

        static readonly List<FlameMeshModule> registered = new List<FlameMeshModule>();

        // Scratch, reused every frame
        static readonly List<FlameMeshModule> firing = new List<FlameMeshModule>();
        static readonly List<Flame> flames = new List<Flame>();
        static readonly List<bool> clustered = new List<bool>();
        static readonly List<int> cluster = new List<int>();
        static readonly List<float> contacts = new List<float>(); // between every two flames of the cluster, by their slots
        static readonly List<float> touches = new List<float>();  // and where they first meet
        static readonly List<float> crossing = new List<float>(); // the firmest contact across each gap in the sequence
        static readonly List<float> boundaries = new List<float>();
        static readonly List<float> litBoundaries = new List<float>();
        static readonly List<GlareSource> glareSources = new List<GlareSource>();

        // The cluster being solved, in craft space - what ApplyGroup needs to say where its plume is
        static Vector2 clusterOrigin, clusterDown;
        static int clusterSize;
        static readonly List<float> nozzleSplits = new List<float>();
        static readonly List<int> sequence = new List<int>(); // jets in contact with any other
        static readonly List<int> run = new List<int>(); // one merged plume
        static readonly List<float> joinTouches = new List<float>(); // where the jets either side of each join come together

        // Comparers, not lambdas: these sort every frame and List.Sort wraps a Comparison in a new object.
        static readonly IComparer<FlameMeshModule> byCraft = Comparer<FlameMeshModule>.Create(
            (a, b) => a.transform.root.GetInstanceID().CompareTo(b.transform.root.GetInstanceID()));
        static readonly IComparer<int> byAcross = Comparer<int>.Create(
            (a, b) => flames[a].across.CompareTo(flames[b].across));

        static int solvedFrame = -1;


        public static void Register(FlameMeshModule module) => registered.Add(module);
        public static void Unregister(FlameMeshModule module) => registered.Remove(module);


        public static void EnsureSolved()
        {
            if (solvedFrame == Time.frameCount)
                return;

            solvedFrame = Time.frameCount;
            Solve();
        }

        static void Solve()
        {
            firing.Clear();
            for (int i = registered.Count - 1; i >= 0; i--)
            {
                FlameMeshModule module = registered[i];

                // Torn down without OnDisable getting a chance to run (scene unload)
                if (module == null)
                {
                    registered.RemoveAt(i);
                    continue;
                }

                module.merge = default;
                module.glareCount = 0;

                if (module.isActiveAndEnabled && module.appliedThrottle > 0)
                    firing.Add(module);
            }

            if (firing.Count < 2)
                return;

            // Only engines on the same craft share a plume
            firing.Sort(byCraft);

            int start = 0;
            while (start < firing.Count)
            {
                Transform root = firing[start].transform.root;

                int end = start;
                while (end < firing.Count && firing[end].transform.root == root)
                    end++;

                if (end - start >= 2)
                    SolveCraft(root, start, end);

                start = end;
            }
        }

        static void SolveCraft(Transform root, int start, int end)
        {
            flames.Clear();
            Matrix4x4 worldToRoot = root.worldToLocalMatrix;

            for (int i = start; i < end; i++)
            {
                FlameMeshModule module = firing[i];
                Matrix4x4 toRoot = worldToRoot * module.transform.localToWorldMatrix;

                // The flame's axes in craft space: how long a local y unit is (the plume runs down
                // -y) and how wide a local x unit is (1 = its nozzle half-width).
                Vector2 up = toRoot.MultiplyVector(Vector3.up);
                Vector2 right = toRoot.MultiplyVector(Vector3.right);
                float scaleAlong = up.magnitude;
                float radius = right.magnitude;

                if (scaleAlong < 1e-5f || radius < 1e-5f)
                    continue;

                flames.Add(new Flame
                {
                    module = module,
                    position = toRoot.MultiplyPoint3x4(Vector3.zero),
                    direction = -up / scaleAlong,
                    acrossAxis = right / radius,
                    scaleAlong = scaleAlong,
                    radius = radius,
                    length = FlameMeshModule.GetPlumeLength(module.exitPressure) * scaleAlong,
                    exitPressure = module.exitPressure,
                    airSpread = module.airSpread,
                    throttle = module.appliedThrottle,
                    atmospherePressure = module.appliedAtmospherePressure,
                    diamondOffset = module.DiamondOffset,
                });
            }

            // Every jet is a plume that could drown out a dimmer flame; ApplyGroup adds the merged ones
            glareSources.Clear();
            foreach (Flame flame in flames)
                glareSources.Add(new GlareSource
                {
                    origin = flame.position,
                    direction = flame.direction,
                    halfSpan = flame.radius,
                    throat = flame.radius,
                    length = flame.length,
                    exitPressure = flame.exitPressure,
                    airSpread = flame.airSpread,
                    throttle = flame.throttle,
                    luminosity = GetLuminosity(flame),
                    strength = 1,
                });

            // Engines pointing different ways have nothing to merge, so split by direction first
            float minDot = Mathf.Cos(MaxAngleBetweenEngines * Mathf.Deg2Rad);

            clustered.Clear();
            for (int i = 0; i < flames.Count; i++)
                clustered.Add(false);

            for (int i = 0; i < flames.Count; i++)
            {
                if (clustered[i])
                    continue;

                cluster.Clear();
                cluster.Add(i);
                clustered[i] = true;

                Vector2 axis = flames[i].direction;
                for (int j = i + 1; j < flames.Count; j++)
                    if (!clustered[j] && Vector2.Dot(flames[j].direction, axis) >= minDot && BurnAlike(flames[i], flames[j]))
                    {
                        clustered[j] = true;
                        cluster.Add(j);
                    }
                if (cluster.Count < 2)
                    continue;
                
                Vector2 mean = Vector2.zero;
                foreach (int k in cluster)
                    mean += flames[k].direction * flames[k].radius;

                SolveCluster(mean.sqrMagnitude > 1e-10f ? mean.normalized : axis);
            }

            SolveGlare();
        }
        
        static void SolveGlare()
        {
            float minDot = Mathf.Cos(MaxAngleBetweenEngines * Mathf.Deg2Rad);

            foreach (Flame flame in flames)
            {
                FlameMeshModule module = flame.module;
                float own = GetLuminosity(flame);

                foreach (GlareSource source in glareSources)
                {
                    // Flames that burn alike share a plume instead, and neither drowns the other out
                    if (source.luminosity <= own * MaxLuminosityRatio)
                        continue;

                    float alignment = Vector2.Dot(source.direction, flame.direction);
                    float depth = (1 - own / (source.luminosity * source.throttle)) * source.strength;
                    if (depth <= 0 || alignment < minDot)
                        continue;

                    // Restated in the flame's own space, which is where the shader works
                    Vector2 offset = source.origin - flame.position;
                    FlameGlare glare = new FlameGlare
                    {
                        origin = new Vector2(Vector2.Dot(offset, flame.acrossAxis) / flame.radius, Vector2.Dot(offset, flame.direction) / flame.scaleAlong),
                        slope = Vector2.Dot(source.direction, flame.acrossAxis) / alignment * flame.scaleAlong / flame.radius,
                        depth = depth,
                        halfSpan = source.halfSpan / flame.radius,
                        throat = source.throat / flame.radius,
                        length = source.length * alignment / flame.scaleAlong,
                        exitPressure = source.exitPressure,
                        throttle = source.throttle,
                        airSpread = source.airSpread,
                    };

                    // There is only room for so many, so the ones passing closest are kept
                    int slot = module.glareCount;
                    if (slot < MaxGlareSources)
                        module.glareCount++;
                    else
                    {
                        slot = 0;
                        for (int k = 1; k < MaxGlareSources; k++)
                            if (Mathf.Abs(module.glare[k].origin.x) > Mathf.Abs(module.glare[slot].origin.x))
                                slot = k;

                        if (Mathf.Abs(glare.origin.x) >= Mathf.Abs(module.glare[slot].origin.x))
                            continue;
                    }

                    module.glare[slot] = glare;
                }
            }
        }
        
        static void SolveCluster(Vector2 down)
        {
            Vector2 across = new Vector2(-down.y, down.x);
            Vector2 origin = flames[cluster[0]].position;

            clusterOrigin = origin;
            clusterDown = down;

            for (int k = 0; k < cluster.Count; k++)
            {
                Flame flame = flames[cluster[k]];
                Vector2 offset = flame.position - origin;

                flame.across = Vector2.Dot(offset, across);
                flame.along = Vector2.Dot(offset, down);
                
                // How much of the group's axes one of this flame's local units actually covers
                flame.acrossScale = flame.radius * Vector2.Dot(flame.acrossAxis, across);
                flame.alongScale = flame.scaleAlong * Vector2.Dot(flame.direction, down);
                flame.drift = flame.scaleAlong * Vector2.Dot(flame.direction, across) / flame.acrossScale;
                flame.reach = flame.along + flame.length * Mathf.Lerp(0.01f, 1, flame.throttle);

                flames[cluster[k]] = flame;
            }

            // Left to right, so neighbours in the merged plume are neighbours in this list.
            cluster.Sort(byAcross);

            // How firmly every two jets touch: by how far BOTH drawn plumes run on past where they would first meet.
            // One that fades out short of there touches nothing, however close the other one comes.
            clusterSize = cluster.Count;
            contacts.Clear();
            touches.Clear();
            for (int i = 0; i < clusterSize * clusterSize; i++)
            {
                contacts.Add(0);
                touches.Add(float.PositiveInfinity);
            }

            for (int i = 0; i < clusterSize; i++)
            {
                Flame flame = flames[cluster[i]];
                flame.slot = i;
                flame.presence = 0;
                flames[cluster[i]] = flame;
            }

            for (int i = 0; i < clusterSize; i++)
                for (int j = i + 1; j < clusterSize; j++)
                {
                    Flame a = flames[cluster[i]], b = flames[cluster[j]];
                    if (!TryGetTouchDistance(a, b, out float touch))
                        continue;

                    float contact = Mathf.Min(GetPresence(a, touch), GetPresence(b, touch));
                    contacts[i * clusterSize + j] = contacts[j * clusterSize + i] = contact;
                    touches[i * clusterSize + j] = touches[j * clusterSize + i] = touch;

                    a.presence = Mathf.Max(a.presence, contact);
                    b.presence = Mathf.Max(b.presence, contact);
                    flames[cluster[i]] = a;
                    flames[cluster[j]] = b;
                }

            sequence.Clear();
            foreach (int i in cluster)
                if (flames[i].presence > 0)
                    sequence.Add(i);

            // A merged plume is a run of jets with something touching across every gap in it
            crossing.Clear();
            for (int s = 0; s + 1 < sequence.Count; s++)
                crossing.Add(0);
            for (int a = 0; a < sequence.Count; a++)
                for (int b = a + 1; b < sequence.Count; b++)
                {
                    float contact = GetContact(flames[sequence[a]], flames[sequence[b]]);
                    for (int s = a; s < b; s++)
                        crossing[s] = Mathf.Max(crossing[s], contact);
                }

            int start = 0;
            for (int s = 0; s < sequence.Count; s++)
                if (s == crossing.Count || crossing[s] <= 0)
                {
                    SolveRun(start, s + 1);
                    start = s + 1;
                }
        }

        // One run of the sequence, [start, end)
        static void SolveRun(int start, int end)
        {
            if (end - start < 2)
                return;

            float firmest = 0;
            for (int s = start; s < end; s++)
                firmest = Mathf.Max(firmest, flames[sequence[s]].presence);

            // Two groups that only a faint contact runs between can't be drawn as two merged plumes and as one at
            // once, so the whole run comes together only as firmly as that contact. A lone faint jet at an end is just faint.
            float together = 1;
            for (int s = start; s + 1 < end; s++)
            {
                float left = 0, right = 0;
                for (int k = start; k <= s; k++)
                    left = Mathf.Max(left, flames[sequence[k]].presence);
                for (int k = s + 1; k < end; k++)
                    right = Mathf.Max(right, flames[sequence[k]].presence);
                together = Mathf.Min(together, crossing[s] / Mathf.Min(left, right));
            }

            run.Clear();
            for (int s = start; s < end; s++)
            {
                Flame flame = flames[sequence[s]];
                flame.weight = flame.presence / firmest;
                flames[sequence[s]] = flame;
                run.Add(sequence[s]);
            }

            ApplyGroup(firmest * together);
        }

        // How far a jet's drawn plume runs on past a distance down the cluster: 0 = it has faded out by there .. 1 = well past
        static float GetPresence(Flame flame, float distance)
            => Mathf.SmoothStep(0, 1, (flame.reach - distance) / (PresenceRamp * flame.length));

        static float GetContact(Flame a, Flame b) => contacts[a.slot * clusterSize + b.slot];
        static float GetTouch(Flame a, Flame b) => touches[a.slot * clusterSize + b.slot];

        // How far downstream two jets first touch, if they do within their length
        static bool TryGetTouchDistance(Flame a, Flame b, out float touch)
        {
            touch = 0;

            // Both jets only exist downstream of the lower of the two nozzles.
            float from = Mathf.Max(a.along, b.along);
            if (a.along + a.length <= from || b.along + b.length <= from)
                return false;
            
            float to = Mathf.Min(from + TouchSearchLength * Mathf.Max(a.length, b.length),
                                 Mathf.Min(a.along + a.length, b.along + b.length));

            // Too far apart to meet even where both are widest
            float closest = Mathf.Min(b.across - a.across, Mathf.Min(GetAxis(b, from) - GetAxis(a, from), GetAxis(b, to) - GetAxis(a, to)));
            if (GetMixingWidth(a, to) + GetMixingWidth(b, to) < closest)
                return false;

            float lastDistance = from, lastOverlap = 0;
            for (int i = 0; i <= TouchSamples; i++)
            {
                float distance = Mathf.Lerp(from, to, (float)i / TouchSamples);

                // A jet pointing away is still drawn in, as if side by side
                float separation = Mathf.Min(b.across - a.across, GetAxis(b, distance) - GetAxis(a, distance));
                float overlap = GetMixingWidth(a, distance) + GetMixingWidth(b, distance) - separation;
                if (overlap >= 0)
                {
                    // Interpolated so the touch doesn't step between samples
                    touch = i == 0 ? distance : Mathf.Lerp(lastDistance, distance, -lastOverlap / (overlap - lastOverlap));
                    return true;
                }

                lastDistance = distance;
                lastOverlap = overlap;
            }

            return false;
        }

        static float GetAxis(Flame flame, float distance) => flame.across + GetTravel(flame) * (distance - flame.along);

        // Eased onto the group's heading only as far as the jet has joined it
        static float GetDrawnAxis(Flame flame, float distance)
        {
            float travelled = Mathf.Max(distance - flame.along, 0);
            return flame.across + GetTravel(flame) * Mathf.Lerp(travelled, GetBent(travelled, flame.bend), flame.amount);
        }

        static float GetTravel(Flame flame) => flame.drift * flame.acrossScale / flame.alongScale;

        static float GetBent(float distance, float bend)
        {
            distance = Mathf.Max(distance, 0);
            return bend <= 0 ? distance : distance < bend ? distance - distance * distance / (2 * bend) : bend / 2;
        }

        // Off the smooth envelope, or the first touch would skip between diamond bulges
        static float GetMixingWidth(Flame flame, float distance)
        {
            return GetHalfWidth(flame, distance, false) * (1 + EntrainReach);
        }

        // This jet's half-width at an absolute distance down the cluster's axis, in craft units.
        static float GetHalfWidth(Flame flame, float distance, bool ripple = true)
        {
            float y = (distance - flame.along) / flame.length;
            return flame.radius * FlameMeshModule.GetFlameWidth(y, flame.exitPressure, flame.throttle, flame.airSpread, flame.atmospherePressure, flame.diamondOffset, ripple);
        }

        // Turns one run of engines into a single merged plume, come together as far as the contact says.
        static void ApplyGroup(float contact)
        {
            int count = run.Count;

            // Where the jets either side of each join come together. A barely present jet is all but looked
            // past, to where the ones either side of it would meet each other without it.
            joinTouches.Clear();
            for (int k = 0; k + 1 < count; k++)
            {
                float sum = 0, total = 0, hiddenLeft = 1;
                for (int i = k; i >= 0; i--)
                {
                    Flame left = flames[run[i]];
                    float leftShows = left.weight * hiddenLeft;
                    hiddenLeft *= 1 - left.weight;

                    float hiddenRight = 1;
                    for (int j = k + 1; j < count; j++)
                    {
                        Flame right = flames[run[j]];
                        float shows = leftShows * right.weight * hiddenRight + HiddenTouch;
                        hiddenRight *= 1 - right.weight;

                        float touch = GetTouch(left, right);
                        if (float.IsInfinity(touch))
                            continue;

                        sum += touch * shows;
                        total += shows;
                    }
                }
                joinTouches.Add(sum / total);
            }

            // The merge plane: where the last pair came together, never upstream of any nozzle. A join with
            // hardly anything on one side of it counts for as little, so a faint jet at an end can't push it down.
            float earliest = float.PositiveInfinity, highestNozzle = float.PositiveInfinity;
            foreach (float touch in joinTouches)
                earliest = Mathf.Min(earliest, touch);
            foreach (int i in run)
                highestNozzle = Mathf.Min(highestNozzle, flames[i].along);

            float mergeDistance = float.NegativeInfinity;
            for (int k = 0; k + 1 < count; k++)
            {
                float left = 0, right = 0;
                for (int i = 0; i <= k; i++)
                    left = Mathf.Max(left, flames[run[i]].weight);
                for (int i = k + 1; i < count; i++)
                    right = Mathf.Max(right, flames[run[i]].weight);
                mergeDistance = Mathf.Max(mergeDistance, Mathf.Lerp(earliest, joinTouches[k], Mathf.Min(left, right)));
            }
            foreach (int i in run)
                mergeDistance = Mathf.Max(mergeDistance, Mathf.Lerp(highestNozzle, flames[i].along, flames[i].weight));

            // A jet off the group's heading is turned onto it where it meets a neighbour
            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                float meets = Mathf.Min(k > 0 ? joinTouches[k - 1] : float.PositiveInfinity, k + 1 < count ? joinTouches[k] : float.PositiveInfinity);
                flame.bend = Mathf.Max(2 * Mathf.Max(meets - flame.along, 0), MinBend * flame.length);
                flames[run[k]] = flame;
            }

            // How far each jet takes part. A faint one out at an end keeps its own look; one with the group either side of it takes the group's.
            AddUpMasses();
            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                float inside = Mathf.Min(k > 0 ? flames[run[k - 1]].massUpTo : 0, k + 1 < count ? flames[run[k + 1]].massFrom : 0);
                flame.amount = contact * (flame.weight + (1 - flame.weight) * inside);
                flames[run[k]] = flame;
            }

            // Slice the envelope at that plane
            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                float halfWidth = GetHalfWidth(flame, mergeDistance);
                float axis = GetDrawnAxis(flame, mergeDistance);

                flame.edgeLeft = axis - halfWidth;
                flame.edgeRight = axis + halfWidth;
                flames[run[k]] = flame;
            }
            AddUp(false);

            float envelopeLeft = flames[run[0]].edgeFrom;
            float envelopeRight = flames[run[count - 1]].edgeUpTo;

            boundaries.Clear();
            boundaries.Add(envelopeLeft);
            for (int k = 1; k < count; k++)
                // Kept in order even if one jet has swallowed another, so no slice can invert.
                boundaries.Add(Mathf.Max(GetJoin(flames[run[k - 1]], flames[run[k]]), boundaries[boundaries.Count - 1]));
            boundaries.Add(Mathf.Max(envelopeRight, boundaries[boundaries.Count - 1]));

            float span = envelopeRight - envelopeLeft;
            if (span < 1e-5f)
                return;

            // The group's combined numbers, weighed by thrust
            float pressure = 0, throttle = 0, luminosity = 0, airSpread = 0, thrust = 0;

            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                float jet = flame.radius * flame.weight * flame.throttle;

                pressure += flame.exitPressure * jet;
                throttle += flame.throttle * jet;
                luminosity += GetLuminosity(flame) * jet;
                airSpread += flame.airSpread * jet;
                thrust += jet;
            }

            thrust = Mathf.Max(thrust, 1e-6f);
            pressure /= thrust;
            throttle /= thrust;
            luminosity /= thrust;
            airSpread /= thrust;

            // Each jet's part of the group's nozzle, by its thrust
            float lengthPerRadius = 0, weight = 0, parts = 0;
            float ownReach = 0; // how far past the merge plane the single plumes would go

            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                flame.part = flame.weight * flame.throttle / throttle;
                flames[run[k]] = flame;

                float radius = flame.radius * flame.part;
                lengthPerRadius += flame.length / flame.radius * radius;
                weight += radius;
                parts += flame.part;
                ownReach = Mathf.Max(ownReach, (flame.along + flame.length - mergeDistance) * flame.part);
            }

            lengthPerRadius /= weight;
            float referenceRadius = weight / parts;
            float referenceLength = lengthPerRadius * referenceRadius;

            // The group's combined look
            Color color = default;
            Vector2 turbulence = default;
            float additiveBlend = 0, stripesWidth = 0, stripesStrength = 0, soot = 0, eddySpeed = 0, light = 0;

            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                FlameMeshModule module = flame.module;
                float share = flame.radius * flame.weight * flame.throttle * GetLuminosity(flame);

                color += module.FlameColor * share;
                additiveBlend += module.appliedAdditiveBlend * share;
                stripesWidth += module.stripesWidth * share;
                stripesStrength += module.stripesStrength * share;
                soot += module.Soot * share;
                turbulence += module.Turbulence * share;
                eddySpeed += module.EddySpeed * flame.radius * share;
                light += share;
            }

            color /= light;
            additiveBlend /= light;
            stripesWidth /= light;
            stripesStrength /= light;
            soot /= light;
            turbulence /= light;
            eddySpeed /= light;

            // Never shorter than the plumes it replaces, so no engine's mesh has to shrink to join.
            float groupLength = Mathf.Max(ownReach, referenceLength * Mathf.Pow(weight / referenceRadius, GroupLengthPower));
            float centre = (envelopeLeft + envelopeRight) * 0.5f;

            // Past the merge plane it is this plume that a dimmer flame lies under, which reaches
            // well beyond where any of its jets would have on their own
            glareSources.Add(new GlareSource
            {
                origin = clusterOrigin + clusterDown * mergeDistance + new Vector2(-clusterDown.y, clusterDown.x) * centre,
                direction = clusterDown,
                halfSpan = span * 0.5f,
                throat = weight,
                length = groupLength,
                exitPressure = pressure,
                airSpread = airSpread,
                throttle = throttle,
                luminosity = luminosity,
                strength = contact,
            });
            
            float settleDistance = mergeDistance + ShareFadeLength * groupLength;

            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                float halfWidth = GetHalfWidth(flame, settleDistance);
                float axis = GetDrawnAxis(flame, settleDistance);

                flame.settleLeft = axis - halfWidth;
                flame.settleRight = axis + halfWidth;
                flames[run[k]] = flame;
            }
            AddUp(true);

            litBoundaries.Clear();
            litBoundaries.Add(0);
            for (int k = 1; k < count; k++)
            {
                Flame left = flames[run[k - 1]], right = flames[run[k]];
                float unlit = (boundaries[k] - envelopeLeft) / span;

                // -1 = only the left side gives off any light .. 1 = only the right one
                float dominance = (right.lightFrom - left.lightUpTo) / (right.lightFrom + left.lightUpTo);

                // The brighter side's own edge, and never a retreat from where it already was
                float reach = ((dominance > 0 ? right.settleFrom : left.settleUpTo) - envelopeLeft) / span;
                reach = dominance > 0 ? Mathf.Clamp(reach, 0, unlit) : Mathf.Clamp(reach, unlit, 1);

                litBoundaries.Add(Mathf.Lerp(unlit, reach, Mathf.Abs(dominance)));
            }
            litBoundaries.Add(1);

            // Bright jets either side of a dim one can both reach clean across it
            for (int k = 1; k + 1 < litBoundaries.Count - 1; k++)
                if (litBoundaries[k + 1] < litBoundaries[k])
                    litBoundaries[k] = litBoundaries[k + 1] = (litBoundaries[k] + litBoundaries[k + 1]) * 0.5f;
            for (int k = 1; k < litBoundaries.Count - 1; k++)
                litBoundaries[k] = Mathf.Max(litBoundaries[k], litBoundaries[k - 1]);

            // Streaks have to be told how many jet-widths wide that envelope is
            float ownWidths = 0;
            for (int k = 0; k < count; k++)
                ownWidths += (flames[run[k]].edgeRight - flames[run[k]].edgeLeft) * flames[run[k]].part;
            float stripeScale = span * parts / Mathf.Max(ownWidths, 1e-4f);

            // Where each pair splits at the nozzles, before the plumes spread
            nozzleSplits.Clear();
            nozzleSplits.Add(-FarAway); // the outermost edges have no neighbour to split from
            for (int k = 1; k < count; k++)
            {
                Flame left = flames[run[k - 1]], right = flames[run[k]];
                nozzleSplits.Add((left.across * right.radius + right.across * left.radius) / (left.radius + right.radius));
            }
            nozzleSplits.Add(FarAway);

            float lead = 0;
            for (int k = 0; k < count; k++)
                lead += (mergeDistance - flames[run[k]].along) * flames[run[k]].part;
            lead /= parts;

            int smokeLeader = -1;
            float smokeWeight = 0, leaderKind = 0;
            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                if (flame.module.smokeTrail == null)
                    continue;

                // A jet with the kind of smoke most of the plume's is; of those, not a faint jet at an end, which hardly sends out the group's smoke at all
                float kind = GetSmokeOfKind(count, flame.module.smokeTrail.material);
                if (smokeLeader < 0 || kind > leaderKind || kind == leaderKind && flame.amount > flames[run[smokeLeader]].amount)
                {
                    smokeLeader = k;
                    leaderKind = kind;
                }
                smokeWeight += flame.radius * flame.part;
            }

            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                float sliceStart = (boundaries[k] - envelopeLeft) / span;
                float sliceEnd = (boundaries[k + 1] - envelopeLeft) / span;
                float litStart = litBoundaries[k];
                float litEnd = litBoundaries[k + 1];
                bool mirrored = flame.acrossScale < 0;
                float splitStart = ToLocalAcross(nozzleSplits[k], flame);
                float splitEnd = ToLocalAcross(nozzleSplits[k + 1], flame);
                float closeBefore = k > 0 ? GetJoinCloseLength(k - 1, groupLength) / flame.alongScale : 0;
                float closeAfter = k + 1 < count ? GetJoinCloseLength(k, groupLength) / flame.alongScale : 0;
                FlameNeighbour before = k > 0 ? GetNeighbour(flame, flames[run[k - 1]]) : default;
                FlameNeighbour after = k + 1 < count ? GetNeighbour(flame, flames[run[k + 1]]) : default;

                float joinedBefore = k > 0 ? GetFirmness(k - 1) : 0;
                float joinedAfter = k + 1 < count ? GetFirmness(k) : 0;

                flame.module.merge = new FlameMerge
                {
                    amount = flame.amount,
                    contact = contact,
                    // A jet throttled well below the group's takes the merged look sooner
                    fadeLength = Mathf.Lerp(FadeLength, ShareFadeLength, Mathf.Clamp01(1 - flame.throttle / Mathf.Max(throttle, 1e-6f))) * groupLength / flame.alongScale,
                    share = mirrored ? new Vector2(1 - sliceEnd, 1 - sliceStart) : new Vector2(sliceStart, sliceEnd),
                    litShare = mirrored ? new Vector2(1 - litEnd, 1 - litStart) : new Vector2(litStart, litEnd),
                    joinWeight = mirrored ? new Vector2(joinedAfter, joinedBefore) : new Vector2(joinedBefore, joinedAfter),
                    shareFadeLength = ShareFadeLength * groupLength / flame.alongScale,
                    nozzleBounds = mirrored ? new Vector2(splitEnd, splitStart) : new Vector2(splitStart, splitEnd),
                    drift = flame.drift,
                    bend = flame.bend / flame.alongScale,
                    closeLength = mirrored ? new Vector2(closeAfter, closeBefore) : new Vector2(closeBefore, closeAfter),
                    stripeScale = stripeScale,
                    center = (centre - flame.across) / flame.acrossScale,
                    halfSpan = span * 0.5f / Mathf.Abs(flame.acrossScale),
                    throat = weight / Mathf.Abs(flame.acrossScale),
                    nozzleRadius = weight / parts / Mathf.Abs(flame.acrossScale),
                    leadsSmoke = k == smokeLeader,
                    smokeNozzles = smokeWeight / flame.radius,
                    groupLength = groupLength / flame.alongScale,
                    alongOffset = (mergeDistance - flame.along) / flame.alongScale,
                    exitPressure = pressure,
                    throttle = throttle,
                    airSpread = airSpread,
                    color = color,
                    additiveBlend = additiveBlend,
                    stripesWidth = stripesWidth,
                    stripesStrength = stripesStrength,
                    soot = soot,
                    turbulence = turbulence,
                    mirror = mirrored ? -1 : 1,
                    seed = flames[run[0]].module.noiseSeed,
                    eddyFlow = eddySpeed / groupLength,
                    lead = lead / flame.alongScale,
                    refLength = referenceLength / flame.alongScale,
                    left = mirrored ? after : before,
                    right = mirrored ? before : after,
                };
            }
        }

        // How much of the group there is from its left end up to each jet, and from each jet on to its right end:
        // a barely present jet lets the ones beyond it show through, so it can come and go without the rest of the group jumping.
        static void AddUpMasses()
        {
            int count = run.Count;

            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                flame.massUpTo = flame.weight + (k > 0 ? (1 - flame.weight) * flames[run[k - 1]].massUpTo : 0);
                flames[run[k]] = flame;
            }

            for (int k = count - 1; k >= 0; k--)
            {
                Flame flame = flames[run[k]];
                flame.massFrom = flame.weight + (k + 1 < count ? (1 - flame.weight) * flames[run[k + 1]].massFrom : 0);
                flames[run[k]] = flame;
            }
        }

        // Both sides of every join as the jets on them add up, seen from the join, by those masses
        static void AddUp(bool settled)
        {
            int count = run.Count;

            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                Flame before = k > 0 ? flames[run[k - 1]] : default;
                float behind = k > 0 ? (1 - flame.weight) * before.massUpTo : 0;
                float edge = settled ? flame.settleRight : flame.edgeRight;
                float edgeBefore = k == 0 ? edge : settled ? before.settleUpTo : before.edgeUpTo;

                float upTo = (flame.weight * Mathf.Max(edge, edgeBefore) + behind * edgeBefore) / flame.massUpTo;
                if (settled)
                    flame.settleUpTo = upTo;
                else
                    flame.edgeUpTo = upTo;
                flame.lightUpTo = (flame.weight * GetLuminosity(flame) + behind * before.lightUpTo) / flame.massUpTo;
                flames[run[k]] = flame;
            }

            for (int k = count - 1; k >= 0; k--)
            {
                Flame flame = flames[run[k]];
                Flame after = k + 1 < count ? flames[run[k + 1]] : default;
                float behind = k + 1 < count ? (1 - flame.weight) * after.massFrom : 0;
                float edge = settled ? flame.settleLeft : flame.edgeLeft;
                float edgeAfter = k + 1 == count ? edge : settled ? after.settleFrom : after.edgeFrom;

                float from = (flame.weight * Mathf.Min(edge, edgeAfter) + behind * edgeAfter) / flame.massFrom;
                if (settled)
                    flame.settleFrom = from;
                else
                    flame.edgeFrom = from;
                flame.lightFrom = (flame.weight * GetLuminosity(flame) + behind * after.lightFrom) / flame.massFrom;
                flames[run[k]] = flame;
            }
        }

        // Where the slices either side of a join split at the merge plane: midway between the two sides' edges, as far as both are there
        static float GetJoin(Flame left, Flame right)
            => (left.massUpTo * left.edgeUpTo + right.massFrom * right.edgeFrom) / (left.massUpTo + right.massFrom);

        // How far past the merge plane the join between run[k] and run[k + 1] closes its gap, in craft units
        static float GetJoinCloseLength(int k, float groupLength)
        {
            Flame left = flames[run[k]], right = flames[run[k + 1]];
            float gap = right.edgeFrom - left.edgeUpTo;
            float spread = EntrainSpread * Mathf.Max(Mathf.SmoothStep(0, 1, Mathf.Min(left.atmospherePressure, right.atmospherePressure) / EntrainThinAir), 0.25f);
            return gap > 0 ? Mathf.Min(gap / (2 * spread), groupLength * 0.5f) : 0;
        }

        // How firmly the join between run[k] and run[k + 1] holds: as much as there is of the group on its weaker side
        static float GetFirmness(int k) => Mathf.Min(flames[run[k]].massUpTo, flames[run[k + 1]].massFrom);

        // Another jet of the group, in this flame's space
        static FlameNeighbour GetNeighbour(Flame flame, Flame other)
        {
            // Each axis' travel across the group per unit down it
            float travel = other.drift * other.acrossScale / other.alongScale;
            float ownTravel = flame.drift * flame.acrossScale / flame.alongScale;

            return new FlameNeighbour
            {
                present = true,
                origin = new Vector2(ToLocalAcross(other.across + travel * (flame.along - other.along), flame), (other.along - flame.along) / flame.alongScale),
                slope = (travel - ownTravel) * flame.alongScale / flame.acrossScale,
                drift = travel * flame.alongScale / flame.acrossScale,
                bend = other.bend / flame.alongScale,
                scale = new Vector2(other.acrossScale / flame.acrossScale, other.alongScale / flame.alongScale),
                exitPressure = other.exitPressure,
                throttle = other.throttle,
                airSpread = other.airSpread,
                soot = other.module.Soot,
                amount = other.amount,
                seed = other.module.noiseSeed,
                eddyFlow = other.module.GetEddyFlow(),
            };
        }

        static bool BurnAlike(Flame a, Flame b)
        {
            float brighter = Mathf.Max(GetLuminosity(a), GetLuminosity(b));
            float dimmer = Mathf.Min(GetLuminosity(a), GetLuminosity(b));
            return brighter <= dimmer * MaxLuminosityRatio;
        }

        // Floored, so a group of nothing but unlit flames still has something to be divided by
        static float GetLuminosity(Flame flame)
            => Mathf.Max(flame.module.mergeLuminosity, 1e-3f);

        // How much smoke the jets of the run whose smoke is drawn with this material put out between them
        static float GetSmokeOfKind(int count, Material material)
        {
            float smoke = 0;
            for (int k = 0; k < count; k++)
            {
                Flame flame = flames[run[k]];
                SmokeTrailModule trail = flame.module.smokeTrail;
                if (trail != null && trail.material == material)
                    smoke += flame.radius * flame.part * flame.throttle * trail.Output;
            }
            return smoke;
        }

        // A position across the cluster in the units the shader uses
        static float ToLocalAcross(float across, Flame flame)
            => (across - flame.across) / flame.acrossScale;

        // A plume a dimmer flame could be drowned out by
        struct GlareSource
        {
            public Vector2 origin;
            public Vector2 direction;
            public float halfSpan;
            public float throat;
            public float length;
            public float exitPressure;
            public float throttle;
            public float airSpread;
            public float luminosity;
            public float strength; // how much of it is drawn: a merged plume only as far as its group has come together
        }

        struct Flame
        {
            public FlameMeshModule module;

            // In craft space
            public Vector2 position;
            public Vector2 direction;
            public Vector2 acrossAxis;
            public float scaleAlong;
            public float radius;
            public float length;

            public float exitPressure;
            public float throttle;
            public float airSpread;
            public float atmospherePressure;
            public float diamondOffset;

            // On the cluster's axis, filled in once its direction is known
            public int slot; // its place across the cluster
            public float across;
            public float along;
            public float acrossScale;
            public float alongScale;
            public float drift;
            public float reach; // where its drawn plume has faded out
            public float bend;
            public float presence; // 0..1, its firmest contact with any other jet
            public float weight; // that, against the firmest in its group: how far it takes part
            public float amount; // and how far into the merged look it goes (FlameMerge.amount)
            public float part; // its share of the group's nozzle
            public float edgeLeft;
            public float edgeRight;
            public float settleLeft;
            public float settleRight;

            // The group from its left end up to this jet, seen from its right (see AddUp), and from this jet on to its right end, seen from its left
            public float massUpTo, edgeUpTo, settleUpTo, lightUpTo;
            public float massFrom, edgeFrom, settleFrom, lightFrom;
        }
    }
}
