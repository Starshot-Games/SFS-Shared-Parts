using System.Collections.Generic;
using UnityEngine;

namespace SFS.Parts.Modules
{
    // The merged plume a single flame is part of. Mirrors the merge* uniforms in
    // Shaders\New Flames\Flame_Base.cginc; amount = 0 (the default) means "not merging".
    public struct FlameMerge
    {
        public float amount;
        public float fadeLength;
        public Vector2 share;
        public Vector2 litShare;
        public float shareFadeLength;
        public Vector2 nozzleBounds;
        public float drift;
        public float stripeScale;
        public float center;
        public float halfSpan;
        public float throat;
        public float groupLength;
        public float alongOffset;
        public float exitPressure;
        public float throttle;
        
        public Color color;
        public float additiveBlend;
        public float stripesWidth;
        public float stripesStrength;

        // Restates the merge in a mesh renderer's frame, given its local x/y axes measured in module space
        public FlameMerge InSpaceOf(float acrossScale, float alongScale)
        {
            if (amount <= 0)
                return this;

            bool mirrored = acrossScale < 0;
            float across = Mathf.Abs(acrossScale);
            float along = Mathf.Abs(alongScale);

            FlameMerge m = this;
            m.center = center / acrossScale;
            m.halfSpan = halfSpan / across;
            m.throat = throat / across;
            m.share = mirrored ? new Vector2(1 - share.y, 1 - share.x) : share;
            m.litShare = mirrored ? new Vector2(1 - litShare.y, 1 - litShare.x) : litShare;
            m.nozzleBounds = mirrored ? new Vector2(nozzleBounds.y, nozzleBounds.x) / acrossScale : nozzleBounds / acrossScale;
            m.drift = drift * along / acrossScale; // an x per a y, so both scalings apply
            m.groupLength = groupLength / along;
            m.alongOffset = alongOffset / along;
            m.fadeLength = fadeLength / along;
            m.shareFadeLength = shareFadeLength / along;
            return m;
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

            float width = halfSpan + throat * (FlameMeshModule.GetFlameWidth(Mathf.Clamp01(t), exitPressure, throttle, atmospherePressure, diamondOffset) - 1);
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
        // How far downstream to look for two jets meeting, as a fraction of their length.
        const float TouchSearchLength = 0.7f;
        // Crossfade into one plume, as a fraction of the merged plume. A distance rather than a
        // share of each mesh, so the whole group finishes merging at the same point.
        const float FadeLength = 0.4f;
        // How soon the bright jets have spread over the dim ones, as a fraction of the merged plume.
        const float ShareFadeLength = 0.1f;
        // How strongly a bigger group stretches the plume
        const float GroupLengthPower = 0.5f;
        const int TouchSamples = 16;
        // "No neighbour on this side". Finite so it survives being interpolated in the shader.
        const float FarAway = 1e6f;

        static readonly List<FlameMeshModule> registered = new List<FlameMeshModule>();

        // Scratch, reused every frame
        static readonly List<FlameMeshModule> firing = new List<FlameMeshModule>();
        static readonly List<Flame> flames = new List<Flame>();
        static readonly List<bool> clustered = new List<bool>();
        static readonly List<int> cluster = new List<int>();
        static readonly List<float> boundaries = new List<float>();
        static readonly List<float> litBoundaries = new List<float>();
        static readonly List<GlareSource> glareSources = new List<GlareSource>();

        // The cluster being solved, in craft space - what ApplyGroup needs to say where its plume is
        static Vector2 clusterOrigin, clusterDown;
        static readonly List<float> nozzleSplits = new List<float>();

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
                    throttle = flame.throttle,
                    luminosity = GetLuminosity(flame),
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
                    float depth = 1 - own / (source.luminosity * source.throttle);
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

                flames[cluster[k]] = flame;
            }

            // Left to right, so neighbours in the merged plume are neighbours in this list.
            cluster.Sort(byAcross);

            // Jets merge where their envelopes first meet
            int runStart = 0;
            float mergeDistance = float.NegativeInfinity;

            for (int k = 1; k < cluster.Count; k++)
            {
                bool touches = TryGetTouchDistance(flames[cluster[k - 1]], flames[cluster[k]], out float touch);

                if (touches)
                {
                    // The run is only fully merged once its last pair has come together.
                    mergeDistance = Mathf.Max(mergeDistance, touch);
                    continue;
                }

                if (k - runStart >= 2)
                    ApplyGroup(runStart, k, mergeDistance);

                runStart = k;
                mergeDistance = float.NegativeInfinity;
            }

            if (cluster.Count - runStart >= 2)
                ApplyGroup(runStart, cluster.Count, mergeDistance);
        }

        // How far downstream two jets first touch, if they do within their length
        static bool TryGetTouchDistance(Flame a, Flame b, out float touch)
        {
            touch = 0;

            float separation = Mathf.Abs(b.across - a.across);

            // Both jets only exist downstream of the lower of the two nozzles.
            float from = Mathf.Max(a.along, b.along);
            if (a.along + a.length <= from || b.along + b.length <= from)
                return false;
            
            float to = Mathf.Min(from + TouchSearchLength * Mathf.Max(a.length, b.length),
                                 Mathf.Min(a.along + a.length, b.along + b.length));

            for (int i = 0; i <= TouchSamples; i++)
            {
                float distance = Mathf.Lerp(from, to, (float)i / TouchSamples);

                if (GetHalfWidth(a, distance) + GetHalfWidth(b, distance) >= separation)
                {
                    touch = distance;
                    return true;
                }
            }

            return false;
        }

        // This jet's half-width at an absolute distance down the cluster's axis, in craft units.
        static float GetHalfWidth(Flame flame, float distance)
        {
            float y = Mathf.Clamp01((distance - flame.along) / flame.length);
            return flame.radius * FlameMeshModule.GetFlameWidth(y, flame.exitPressure, flame.throttle, flame.atmospherePressure, flame.diamondOffset);
        }

        // Turns one run of engines (from..to into cluster) into a single merged plume.
        static void ApplyGroup(int from, int to, float mergeDistance)
        {
            // The merge plane: where the last pair came together, never upstream of any nozzle.
            for (int k = from; k < to; k++)
                mergeDistance = Mathf.Max(mergeDistance, flames[cluster[k]].along);

            // Slice the envelope at that plane
            float envelopeLeft = float.PositiveInfinity;
            float envelopeRight = float.NegativeInfinity;

            for (int k = from; k < to; k++)
            {
                Flame flame = flames[cluster[k]];
                float halfWidth = GetHalfWidth(flame, mergeDistance);

                flame.edgeLeft = flame.across - halfWidth;
                flame.edgeRight = flame.across + halfWidth;
                flames[cluster[k]] = flame;

                envelopeLeft = Mathf.Min(envelopeLeft, flame.edgeLeft);
                envelopeRight = Mathf.Max(envelopeRight, flame.edgeRight);
            }

            boundaries.Clear();
            boundaries.Add(envelopeLeft);
            for (int k = from + 1; k < to; k++)
                // Kept in order even if one jet has swallowed another, so no slice can invert.
                boundaries.Add(Mathf.Max((flames[cluster[k - 1]].edgeRight + flames[cluster[k]].edgeLeft) * 0.5f, boundaries[boundaries.Count - 1]));
            boundaries.Add(Mathf.Max(envelopeRight, boundaries[boundaries.Count - 1]));

            float span = envelopeRight - envelopeLeft;
            if (span < 1e-5f)
                return;

            // The group's combined numbers
            float referenceRadius = 0, referenceLength = 0;
            float pressure = 0, throttle = 0, luminosity = 0, weight = 0;
            float ownReach = 0; // how far past the merge plane the individual plumes would have gone

            for (int k = from; k < to; k++)
            {
                Flame flame = flames[cluster[k]];

                if (flame.radius > referenceRadius)
                {
                    referenceRadius = flame.radius;
                    referenceLength = flame.length;
                }

                pressure += flame.exitPressure * flame.radius;
                throttle += flame.throttle * flame.radius;
                luminosity += GetLuminosity(flame) * flame.radius;
                weight += flame.radius;
                ownReach = Mathf.Max(ownReach, flame.along + flame.length - mergeDistance);
            }

            pressure /= weight;
            throttle /= weight;
            luminosity /= weight;

            // The group's combined look
            Color color = default;
            float additiveBlend = 0, stripesWidth = 0, stripesStrength = 0, light = 0;

            for (int k = from; k < to; k++)
            {
                Flame flame = flames[cluster[k]];
                FlameMeshModule module = flame.module;
                float share = flame.radius * flame.throttle * GetLuminosity(flame);

                color += module.FlameColor * share;
                additiveBlend += module.appliedAdditiveBlend * share;
                stripesWidth += module.stripesWidth * share;
                stripesStrength += module.stripesStrength * share;
                light += share;
            }

            color /= light;
            additiveBlend /= light;
            stripesWidth /= light;
            stripesStrength /= light;

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
                throttle = throttle,
                luminosity = luminosity,
            });
            
            float settleDistance = mergeDistance + ShareFadeLength * groupLength;

            for (int k = from; k < to; k++)
            {
                Flame flame = flames[cluster[k]];
                float halfWidth = GetHalfWidth(flame, settleDistance);

                flame.settleLeft = flame.across - halfWidth;
                flame.settleRight = flame.across + halfWidth;
                flames[cluster[k]] = flame;
            }

            litBoundaries.Clear();
            litBoundaries.Add(0);
            for (int k = from + 1; k < to; k++)
            {
                Flame left = flames[cluster[k - 1]], right = flames[cluster[k]];
                float unlit = (boundaries[k - from] - envelopeLeft) / span;

                // -1 = only the left flame gives off any light .. 1 = only the right one
                float dominance = (GetLuminosity(right) - GetLuminosity(left)) / (GetLuminosity(right) + GetLuminosity(left));

                // The brighter jet's own edge on this side, and never a retreat from where it already was
                float reach = ((dominance > 0 ? right.settleLeft : left.settleRight) - envelopeLeft) / span;
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
            for (int k = from; k < to; k++)
                ownWidths += flames[cluster[k]].edgeRight - flames[cluster[k]].edgeLeft;
            float stripeScale = span * (to - from) / Mathf.Max(ownWidths, 1e-4f);

            // Where each pair splits at the nozzles, before the plumes spread
            nozzleSplits.Clear();
            nozzleSplits.Add(-FarAway); // the outermost edges have no neighbour to split from
            for (int k = from + 1; k < to; k++)
            {
                Flame left = flames[cluster[k - 1]], right = flames[cluster[k]];
                nozzleSplits.Add((left.across * right.radius + right.across * left.radius) / (left.radius + right.radius));
            }
            nozzleSplits.Add(FarAway);

            for (int k = from; k < to; k++)
            {
                Flame flame = flames[cluster[k]];
                float sliceStart = (boundaries[k - from] - envelopeLeft) / span;
                float sliceEnd = (boundaries[k - from + 1] - envelopeLeft) / span;
                float litStart = litBoundaries[k - from];
                float litEnd = litBoundaries[k - from + 1];
                bool mirrored = flame.acrossScale < 0;
                float splitStart = ToLocalAcross(nozzleSplits[k - from], flame);
                float splitEnd = ToLocalAcross(nozzleSplits[k - from + 1], flame);

                flame.module.merge = new FlameMerge
                {
                    amount = 1,
                    fadeLength = FadeLength * groupLength / flame.alongScale,
                    share = mirrored ? new Vector2(1 - sliceEnd, 1 - sliceStart) : new Vector2(sliceStart, sliceEnd),
                    litShare = mirrored ? new Vector2(1 - litEnd, 1 - litStart) : new Vector2(litStart, litEnd),
                    shareFadeLength = ShareFadeLength * groupLength / flame.alongScale,
                    nozzleBounds = mirrored ? new Vector2(splitEnd, splitStart) : new Vector2(splitStart, splitEnd),
                    drift = flame.drift,
                    stripeScale = stripeScale,
                    center = (centre - flame.across) / flame.acrossScale,
                    halfSpan = span * 0.5f / Mathf.Abs(flame.acrossScale),
                    throat = weight / Mathf.Abs(flame.acrossScale),
                    groupLength = groupLength / flame.alongScale,
                    alongOffset = (mergeDistance - flame.along) / flame.alongScale,
                    exitPressure = pressure,
                    throttle = throttle,
                    color = color,
                    additiveBlend = additiveBlend,
                    stripesWidth = stripesWidth,
                    stripesStrength = stripesStrength,
                };
            }
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
            public float luminosity;
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
            public float atmospherePressure;
            public float diamondOffset;

            // On the cluster's axis, filled in once its direction is known
            public float across;
            public float along;
            public float acrossScale;
            public float alongScale;
            public float drift;
            public float edgeLeft;
            public float edgeRight;
            public float settleLeft;
            public float settleRight;
        }
    }
}
