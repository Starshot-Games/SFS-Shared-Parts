using System.Collections.Generic;
using SFS.World;
using SFS.WorldBase;
using Sirenix.OdinInspector;
using Unity.Profiling;
using UnityEngine;

namespace SFS.Parts.Modules
{
    // Leaves a trail of smoke in the air behind the flame while it fires in an atmosphere
    public class SmokeTrailModule : MonoBehaviour
    {
        const float ReferenceSpeed = 150; // m/s
        const float FlameEdge = 0.85f; // of the flame's half-width, where its mask fades out across it
        const float FlameFill = 1.2f; // of the flame's half-width, how wide the smoke in its reach is drawn, so its thinning edge shows about where the flame's does
        const float MinDensity = 0.25f; // however thin the air, it still slows the smoke as if at least this thick
        const float Unseen = 0.005f; // of the flame's brightness at the nozzle, where it no longer shows

        [Required] public FlameMeshModule flame;
        [Required] public Material material;

        [Space]
        public Color color = new(0.9f, 0.9f, 0.89f, 1);
        [Tooltip("Opacity (as optical depth) down the middle of the trail where it leaves the flame, at 150 m/s, sea level and full throttle")]
        [Min(0)] public float thickness = 2.2f;
        [Tooltip("How much thinner the air has to get to hide the smoke: its opacity goes with density ^ this")]
        [Min(0)] public float densityFalloff = 0.3f;
        [Tooltip("Air density, relative to sea level, the smoke starts to fade away at as the craft climbs")]
        [Range(0, 1)] public float thinOutFrom = 0.25f;
        [Tooltip("Air density it's all gone at. From halfway there, it only shows as it puffs out after leaving the flame, rather than all at once")]
        [Range(0, 1)] public float thinOutTo = 0.02f;
        [Min(0)] public float lifetime = 60;
        [Tooltip("How much wider than the flame the smoke puffs out in its first seconds (2 = three times as wide)")]
        [Min(0)] public float swell = 2;
        [Tooltip("s it takes the smoke to puff out about two thirds of that")]
        [Min(0.01f)] public float swellTime = 1.2f;
        [Tooltip("m the trail's radius has grown by a minute on, at sea level, per m of nozzle half-width; it spreads slowly at first and ever faster, and quicker in thinner air")]
        [Min(0)] public float expansion = 60;
        [Tooltip("Air density, relative to sea level, the smoke spreads fastest at: it spreads no faster in thinner air than this")]
        [Range(0.0001f, 1)] public float fastestSpreadAt = 0.05f;
        [Tooltip("How much of its opacity the smoke keeps as it spreads out: 0 thins it with its width, as its mass would, 1 keeps it as thick")]
        [Range(0, 1)] public float keepVisible = 0.5f;
        [Tooltip("m/s the smoke carries on down the flame's axis with once it leaves it, per m of nozzle half-width")]
        [Min(0)] public float jetSpeed = 60;
        [Tooltip("s it takes sea-level air to slow the smoke to about a third of its speed through it, so it carries on with the flame's momentum before settling; thinner air slows it less")]
        [Min(0.01f)] public float dragTime = 1.2f;
        [Tooltip("How dim the flame has got, relative to its nozzle, where the smoke starts to show")]
        [Range(0, 1)] public float fadeInFrom = 0.06f;
        [Tooltip("How dim it has got where the smoke is fully in")]
        [Range(0, 1)] public float fadeInTo = 0.002f;
        [Tooltip("How much of the smoke shows while the plume isn't reaching the ground; all of it does once the ground is up where the smoke fades in")]
        [Range(0, 1)] public float inAir = 1;
        [Tooltip("How far on from where it has faded in, towards the end of the flame's turbulent tail, the smoke stays in the flame before it lets go")]
        [Range(0, 1)] public float holdInTail = 0.3f;
        [Tooltip("Lets the smoke puff out past the flame while the flame still carries it, rather than keeping it to the flame's width until it lets go")]
        public bool swellInFlame;

        // How thick its smoke comes out where it is now, per m of nozzle at full throttle
        public float Output { get; private set; }

        SmokeTrail trail;
        const int GroundStreams = 4; // per side where the plume hits the ground
        readonly SmokeTrail[] groundTrails = new SmokeTrail[GroundStreams * 2 - 1];
        const int GroundSamples = 12; // down the plume
        const int WideningSamples = 4; // down the plume, from where the smoke shows to where the flame fades out
        readonly Double2[] groundSamples = new Double2[GroundSamples];
        const int SampleChunk = 3; // terrain samples taken at once
        static readonly double[][] chunkAngles = { null, new double[1], new double[2], new double[3] };
        static readonly ProfilerMarker GroundMarker = new("SmokeTrailModule.Ground"), FeedMarker = new("SmokeTrailModule.Feed");
        Rigidbody2D body;
        float physicsTime = float.NaN; // Time.fixedTime last frame
        float physicsStep;             // s of physics run since then: the smoke in the flame moves on it, as the smoke let go does

        void LateUpdate()
        {
            physicsStep = float.IsNaN(physicsTime) ? 0 : Mathf.Max(0, Time.fixedTime - physicsTime);
            physicsTime = Time.fixedTime;

            if (!SmokeTrails.Enabled || !TryGetAtmosphere(out Planet planet, out float density, out float thinOut))
            {
                Output = 0;
                if (flame != null)
                    flame.smokeCover = Vector3.zero;
                EndTrails();
                return;
            }
            Output = thickness * Mathf.Pow(density, densityFalloff) * thinOut;

            flame.GetPlume(out PlumeState plume);
            PlumeMath.FindFade(ref plume, fadeInFrom, fadeInTo, Unseen, out float fadeStart, out float fadeEnd, out float gone);

            // The flame fades out as its smoke thickens in, as much as that smoke shows there, so it doesn't shine through it
            flame.smokeCover = new Vector3(fadeStart, fadeEnd, inAir * (1 - GetHidden(thinOut)));
            Emit(ref plume, fadeStart, fadeEnd, gone, planet, density, thinOut);
        }

        // Feeds the trail from the plume
        void Emit(ref PlumeState plume, float fadeStart, float fadeEnd, float gone, Planet planet, float density, float thinOut)
        {
            // Smoke from a plume that hits the ground before it would show comes out there and spreads along it
            float fadedOut = PlumeMath.GetFadedOutMeshY(plume);
            float letGo = GetLetGo(plume, fadeEnd, gone);
            float ground;
            using (GroundMarker.Auto())
                ground = FindGround(plume, planet, letGo);
            bool grounded = ground < fadeEnd;
            float groundDepth = 0;
            if (grounded)
            {
                groundDepth = fadeEnd > fadeStart ? Mathf.SmoothStep(0, 1, Mathf.InverseLerp(fadeEnd, fadeStart, ground)) : 1;
                fadeEnd = ground;
                fadeStart = Mathf.Min(fadeStart, ground);
            }

            // Follows the plume as it dims, some way into its tail or until the ground
            PlumeMath.GetCrossSection(plume, 0, out Vector2 nozzle, out _, out _);
            PlumeMath.GetCrossSection(plume, fadeStart, out Vector2 localStart, out float halfWidth, out float startDistance);
            PlumeMath.GetCrossSection(plume, Mathf.Max(fadeStart - 0.05f * fadedOut, 0), out Vector2 upstream, out _, out _);
            PlumeMath.GetCrossSection(plume, fadeEnd, out _, out _, out float endDistance);
            PlumeMath.GetCrossSection(plume, ground, out Vector2 release, out _, out float releaseDistance);

            Double2 birth = WorldView.ToGlobalPosition(localStart);
            if (IsUnderwater(planet, birth))
            {
                EndTrails();
                return;
            }

            float throttle = flame.appliedThrottle;
            float scaleX = transform.TransformVector(Vector3.right).magnitude;
            float size = PlumeMath.GetNozzleRadius(plume);

            Vector2 axis = localStart - upstream;
            Vector2 direction = axis.sqrMagnitude > 1e-8f ? axis.normalized : (Vector2)transform.TransformDirection(Vector3.down);
            float speed = jetSpeed * size;
            Vector2 velocity = GetFlameVelocity(localStart) + direction * speed;
            float radius = FlameEdge * halfWidth;
            float seaLevelRadius = FlameEdge * scaleX * FlameMeshModule.GetFlameWidth(0.5f, flame.exitPressure, 1, flame.airSpread, 1, flame.DiamondOffset);

            // How wide the flame gets on down to where it fades out or meets the ground, which the smoke still within it spreads out to,
            // and how fast it widens back from the smoke's head - in thin air, fast
            float reach = ground < letGo ? ground : fadedOut;
            float widest = radius, widening = 0;
            for (int i = 1; i <= WideningSamples; i++)
            {
                float meshY = Mathf.Lerp(fadeStart, reach, (float)i / WideningSamples);
                PlumeMath.GetCrossSection(plume, meshY, out _, out float width, out float distance);
                float edge = FlameFill * width;
                widest = Mathf.Max(widest, edge);
                widening = Mathf.Max(widening, (edge - radius) / Mathf.Max(distance - startDistance, 0.1f * radius));
            }
            PlumeMath.GetCrossSection(plume, reach, out _, out _, out float reachDistance);

            // Put out at the rate that gives a trail left at the reference speed its thickness
            float massRate = thickness * throttle * Mathf.Pow(density, densityFalloff) * thinOut * ReferenceSpeed * seaLevelRadius / 0.9375f * Mathf.Lerp(inAir, 1, groundDepth);
            float expansionAfterAMinute = expansion * size / Mathf.Sqrt(Mathf.Max(density, fastestSpreadAt));
            float drag = Mathf.Max(density, MinDensity) / dragTime;

            // Smoke the flame carries into the ground, or that carries on into it once let go, spreads along it
            bool intoGround;
            using (GroundMarker.Auto())
                intoGround = ground < letGo || CarriesIntoGround(planet, release, velocity / drag);
            using ProfilerMarker.AutoScope feedScope = FeedMarker.Auto();

            double now = WorldTime.main.worldTime;
            float hidden = GetHidden(thinOut);
            bool existing = trail != null && !trail.Removed;
            float side = existing ? trail.side : Random.value < 0.5f ? -1 : 1;
            float phase = existing ? trail.spreadPhase : Random.value;

            // Off the ground it runs both ways in staggered streams, this trail the first
            if (intoGround)
            {
                float rate = massRate / (GroundStreams * 2);
                Feed(ref trail, side, phase, rate, null, plume);
                for (int i = 0; i < groundTrails.Length; i++)
                {
                    int stream = i + 1;
                    float streamPhase = phase + (stream / 2 + stream % 2 * 0.5f) / GroundStreams;
                    Feed(ref groundTrails[i], stream % 2 == 0 ? side : -side, streamPhase - Mathf.Floor(streamPhase), rate, trail, plume);
                }
            }
            else
            {
                Feed(ref trail, side, phase, massRate, null, plume);
                EndTrails(groundTrails);
            }

            void Feed(ref SmokeTrail fed, float fedSide, float fedPhase, float rate, SmokeTrail lead, in PlumeState plume)
            {
                if (fed == null || fed.Removed || fed.planet != planet || fed.side != fedSide || fed.spreadPhase != fedPhase)
                {
                    fed?.End();
                    fed = SmokeTrails.StartTrail(material, planet, fedSide, fedPhase);
                }

                Carry(plume, fed, nozzle, direction, speed, releaseDistance, reachDistance, widest, now);

                fed.color = color;
                fed.keepVisible = keepVisible;
                fed.widening = widening;
                fed.fadeIn = Mathf.Max(endDistance - startDistance, 0.1f * radius);
                fed.Feed(birth, velocity, radius, startDistance, swell, swellTime, expansionAfterAMinute, drag, lifetime, hidden, rate, now, lead);
            }
        }

        // How much of the smoke doesn't show yet where it comes out: in thin air it only shows as it puffs out
        static float GetHidden(float thinOut) => Mathf.Clamp01(2 * (1 - thinOut));

        // meshY where the flame lets go of the smoke it carries: some way into its tail, unless it's gone before
        float GetLetGo(in PlumeState plume, float fadeEnd, float gone) => Mathf.Min(Mathf.Lerp(fadeEnd, PlumeMath.GetFadedOutMeshY(plume), holdInTail), gone);

        // meshY where it first hits the ground, or end
        float FindGround(in PlumeState plume, Planet planet, float end)
        {
            const int Refinements = 5;

            PlumeMath.GetCrossSection(plume, end, out _, out _, out float reach);
            double nozzleHeight = WorldView.ToGlobalPosition(transform.position).magnitude - planet.Radius;
            if (nozzleHeight > System.Math.Max(planet.maxTerrainHeight, 0) + reach + 1)
                return end;

            // Sampled a few at a time down the plume, as the terrain costs by the sample and the first one under it ends the search
            float above = 0;
            for (int first = 0; first < GroundSamples; first += SampleChunk)
            {
                int count = Mathf.Min(SampleChunk, GroundSamples - first);
                double[] angles = chunkAngles[count];
                for (int c = 0; c < count; c++)
                {
                    PlumeMath.GetCrossSection(plume, end * (first + c + 1) / GroundSamples, out Vector2 local, out _, out _);
                    groundSamples[first + c] = WorldView.ToGlobalPosition(local);
                    angles[c] = groundSamples[first + c].AngleRadians;
                }
                double[] heights = planet.GetTerrainHeightAtAngles(angles, true);

                for (int c = 0; c < count; c++)
                {
                    int i = first + c;
                    float below = end * (i + 1) / GroundSamples;
                    if (groundSamples[i].magnitude >= planet.Radius + heights[c])
                    {
                        above = below;
                        continue;
                    }

                    for (int j = 0; j < Refinements; j++)
                    {
                        float middle = (above + below) / 2;
                        if (IsBelowSurface(plume, planet, middle))
                            below = middle;
                        else
                            above = middle;
                    }

                    return above;
                }
            }

            return end;
        }

        static readonly Double2[] carriedSamples = new Double2[SampleChunk];

        // Whether smoke let go at local, with the air slowing it to a stop over travel, reaches the ground on the way
        static bool CarriesIntoGround(Planet planet, Vector2 local, Vector2 travel)
        {
            const int Samples = 8;

            Double2 start = WorldView.ToGlobalPosition(local);
            if (start.magnitude - travel.magnitude > planet.Radius + System.Math.Max(planet.maxTerrainHeight, 0) + 1)
                return false;

            for (int first = 1; first <= Samples; first += SampleChunk)
            {
                int count = Mathf.Min(SampleChunk, Samples - first + 1);
                double[] angles = chunkAngles[count];
                for (int c = 0; c < count; c++)
                {
                    carriedSamples[c] = start + travel * ((float)(first + c) / Samples);
                    angles[c] = carriedSamples[c].AngleRadians;
                }
                double[] heights = planet.GetTerrainHeightAtAngles(angles, true);
                for (int c = 0; c < count; c++)
                    if (carriedSamples[c].magnitude < planet.Radius + heights[c])
                        return true;
            }

            return false;
        }

        static bool IsBelowSurface(in PlumeState plume, Planet planet, float meshY)
        {
            PlumeMath.GetCrossSection(plume, meshY, out Vector2 local, out _, out _);
            Double2 position = WorldView.ToGlobalPosition(local);
            return position.magnitude < planet.Radius + planet.GetTerrainHeightAtAngle(position.AngleRadians, true);
        }

        // How much of the smoke is left as the air thins out: all of it below thinOutFrom, none past thinOutTo, and evenly with height in between
        float GetThinOut(float density)
        {
            float from = Mathf.Log(Mathf.Max(thinOutFrom, 1e-6f)), to = Mathf.Log(Mathf.Max(thinOutTo, 1e-6f));
            if (to >= from)
                return density >= thinOutFrom ? 1 : 0;

            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(to, from, Mathf.Log(Mathf.Max(density, 1e-9f))));
        }

        // Carries the smoke still in the flame down it and lets it go at releaseDistance. Smoke let go but still within the flame's reach,
        // narrower than the widest it gets, spreads out as wide as the flame is drawn where it is
        void Carry(in PlumeState plume, SmokeTrail trail, Vector2 nozzle, Vector2 direction, float speed, float releaseDistance, float reach, float widest, double now)
        {
            float step = speed * physicsStep;
            List<SmokeTrail.Point> points = trail.points;

            for (int i = points.Count - 1; i >= 0; i--)
            {
                SmokeTrail.Point point = points[i];
                if (!point.held)
                {
                    if (point.GetRadius(now) >= widest)
                        break;

                    float along = Vector2.Dot(WorldView.ToLocalPosition(point.position) - nozzle, direction);
                    if (along > reach)
                        break;
                    if (point.grounded)
                        continue;

                    float alongY = PlumeMath.GetMeshY(plume, along);
                    PlumeMath.GetCrossSection(plume, alongY, out _, out float width, out _);
                    point.radius = Mathf.Max(point.radius, FlameFill * width / point.GetSwelling(now));
                    points[i] = point;
                    continue;
                }

                point.along += step;
                float meshY = PlumeMath.GetMeshY(plume, point.along);
                PlumeMath.GetCrossSection(plume, meshY, out Vector2 local, out float halfWidth, out _);

                Double2 position = WorldView.ToGlobalPosition(local);
                point.carried += position - point.position;
                point.position = position;
                point.velocity = GetFlameVelocity(local) + direction * speed;
                if (swellInFlame)
                    point.radius = Mathf.Max(point.radius, FlameFill * halfWidth / point.GetSwelling(now)); // puffs out past the flame, but never narrower than it
                else
                {
                    point.radius = Mathf.Max(point.radius, FlameFill * halfWidth); // never narrows, though the flame pinches in at its diamonds
                    point.time = now;
                }
                point.held = point.along < releaseDistance;
                points[i] = point;
            }
        }

        // How fast the flame moves through the air at this point, the craft's spin included
        Vector2 GetFlameVelocity(Vector2 localPosition)
        {
            if (body == null)
                body = GetComponentInParent<Rigidbody2D>();

            return WorldView.ToGlobalVelocity(body != null ? body.GetPointVelocity(localPosition) : Vector2.zero);
        }

        void OnTransformParentChanged() => body = null;

        // Air density at the nozzle, relative to the home planet's sea level, so thinner atmospheres give thinner smoke, and how much of the smoke that leaves
        bool TryGetAtmosphere(out Planet planet, out float density, out float thinOut)
        {
            planet = null;
            density = thinOut = 0;

            if (GameManager.main == null || WorldView.main == null || WorldTime.main == null)
                return false;
            if (flame == null || !flame.isActiveAndEnabled || flame.appliedThrottle <= 0)
                return false;

            planet = WorldView.main.ViewLocation.planet;
            if (planet == null || !planet.HasAtmospherePhysics)
                return false;

            double surface = planet.GetAtmosphericDensity(0);
            if (surface <= 0)
                return false;

            Double2 nozzle = WorldView.ToGlobalPosition(transform.position);
            if (IsUnderwater(planet, nozzle))
                return false;

            double air = planet.GetAtmosphericDensity(nozzle.magnitude - planet.Radius);
            density = Mathf.Clamp((float)(air / GetSeaLevelDensity(surface)), 0, 10);
            thinOut = GetThinOut(density);
            return air > 0 && thinOut >= 1e-3f;
        }

        // Water fills everything below sea level, as the rest of the game treats it
        static bool IsUnderwater(Planet planet, Double2 position) => planet.data.hasWater && position.magnitude < planet.Radius;

        static double GetSeaLevelDensity(double fallback)
        {
            Planet home = Base.planetLoader != null && Base.planetLoader.spaceCenter != null ? Base.planetLoader.spaceCenter.Planet : null;
            double seaLevel = home != null ? home.GetAtmosphericDensity(0) : 0;
            return seaLevel > 0 ? seaLevel : fallback;
        }

        void OnEnable()
        {
            if (flame != null)
                flame.smokeTrail = this;
        }

        void OnDisable()
        {
            if (flame != null && flame.smokeTrail == this)
                flame.smokeTrail = null;
            if (flame != null)
                flame.smokeCover = Vector3.zero;
            EndTrails();
        }

        void EndTrails()
        {
            EndTrail(ref trail);
            EndTrails(groundTrails);
        }

        static void EndTrails(SmokeTrail[] trails)
        {
            for (int i = 0; i < trails.Length; i++)
                EndTrail(ref trails[i]);
        }

        static void EndTrail(ref SmokeTrail trail)
        {
            trail?.End();
            trail = null;
        }
    }
}
