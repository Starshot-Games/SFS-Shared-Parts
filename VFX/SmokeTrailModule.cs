using System.Collections.Generic;
using SFS.World;
using SFS.WorldBase;
using Sirenix.OdinInspector;
using UnityEngine;

namespace SFS.Parts.Modules
{
    // Leaves a trail of smoke in the air behind the flame while it fires in an atmosphere
    public class SmokeTrailModule : MonoBehaviour
    {
        const float ReferenceSpeed = 150; // m/s
        const float FlameEdge = 0.85f; // of the flame's half-width, where its mask fades out across it
        const float MinDensity = 0.25f; // however thin the air, it still slows the smoke as if at least this thick

        [Required] public FlameMeshModule flame;
        [Required] public Material material;

        [Space]
        public Color color = new(0.9f, 0.9f, 0.89f, 1);
        [Tooltip("Opacity (as optical depth) down the middle of the trail where it leaves the flame, at 150 m/s, sea level and full throttle")]
        [Min(0)] public float thickness = 3;
        [Tooltip("How much thinner the air has to get to hide the smoke: its opacity goes with density ^ this")]
        [Min(0)] public float densityFalloff = 0.3f;
        [Tooltip("Air density, relative to sea level, the smoke starts to fade away at as the craft climbs")]
        [Range(0, 1)] public float thinOutFrom = 0.25f;
        [Tooltip("Air density it's all gone at. From halfway there, it only shows as it puffs out after leaving the flame, rather than all at once")]
        [Range(0, 1)] public float thinOutTo = 0.02f;
        [Min(0)] public float lifetime = 60;
        [Tooltip("How much wider than the flame the smoke puffs out in its first seconds (2 = three times as wide)")]
        [Min(0)] public float swell = 2;
        [Tooltip("m the trail's radius has grown by a minute on, at sea level, per m of nozzle half-width; it spreads slowly at first and ever faster, and quicker in thinner air")]
        [Min(0)] public float expansion = 60;
        [Tooltip("How much of its opacity the smoke keeps as it spreads out: 0 thins it with its width, as its mass would, 1 keeps it as thick")]
        [Range(0, 1)] public float keepVisible = 0.5f;
        [Tooltip("m/s the smoke carries on down the flame's axis with once it leaves it, per m of nozzle half-width")]
        [Min(0)] public float jetSpeed = 60;
        [Tooltip("s it takes sea-level air to slow the smoke to about a third of its speed through it, so it carries on with the flame's momentum before settling; thinner air slows it less")]
        [Min(0.01f)] public float dragTime = 1.2f;
        [Tooltip("How dim the flame has got, relative to its nozzle, where the smoke starts to show")]
        [Range(0, 1)] public float fadeInFrom = 0.3f;
        [Tooltip("How dim it has got where the smoke is fully in")]
        [Range(0, 1)] public float fadeInTo = 0.03f;

        SmokeTrail ownTrail, groupTrail;
        Rigidbody2D body;
        float physicsTime = float.NaN; // Time.fixedTime last frame
        float physicsStep;             // s of physics run since then: the smoke in the flame moves on it, as the smoke let go does

        void LateUpdate()
        {
            physicsStep = float.IsNaN(physicsTime) ? 0 : Mathf.Max(0, Time.fixedTime - physicsTime);
            physicsTime = Time.fixedTime;

            if (!SmokeTrails.Enabled || !TryGetAtmosphere(out Planet planet, out float density, out float thinOut))
            {
                EndTrails();
                return;
            }

            // This jet's smoke hands over to the merged plume's as it merges with its neighbours, as the flame itself does, by how far
            // into the merge it is where its own smoke sets off. One flame of the group sends out the merged plume's smoke for all of them.
            FindFade(false, out float ownStart, out float ownEnd);
            float merged = flame.GetMergeAt(ownStart);
            Emit(ref ownTrail, false, ownStart, ownEnd, 1 - merged, planet, density, thinOut);

            if (flame.merge.amount > 0 && flame.merge.leadsSmoke)
            {
                FindFade(true, out float groupStart, out float groupEnd);
                Emit(ref groupTrail, true, groupStart, groupEnd, merged * flame.merge.smokeNozzles, planet, density, thinOut);
            }
            else
                EndTrail(ref groupTrail);
        }

        // Feeds a trail from this jet's own plume or the group's merged one, putting out the given share of a nozzle's smoke
        void Emit(ref SmokeTrail trail, bool group, float fadeStart, float fadeEnd, float share, Planet planet, float density, float thinOut)
        {
            if (share < 1e-3f)
            {
                EndTrail(ref trail);
                return;
            }

            // Takes over from the plume as drawn as it dims, moving with it
            float fadedOut = flame.GetFadedOutMeshY(group);
            flame.GetCrossSection(fadeStart, group, out Vector2 localStart, out float halfWidth, out float startDistance);
            flame.GetCrossSection(Mathf.Max(fadeStart - 0.05f * fadedOut, 0), group, out Vector2 upstream, out _, out _);
            flame.GetCrossSection(fadeEnd, group, out _, out _, out float endDistance);

            Double2 birth = WorldView.ToGlobalPosition(localStart);
            if (IsUnderwater(planet, birth))
            {
                EndTrail(ref trail);
                return;
            }

            float throttle = group ? flame.merge.throttle : flame.appliedThrottle;
            float scaleX = transform.TransformVector(Vector3.right).magnitude;
            float size = flame.GetNozzleRadius(group); // shared by a merged group, so its smoke moves and spreads as one

            Vector2 axis = localStart - upstream;
            Vector2 direction = axis.sqrMagnitude > 1e-8f ? axis.normalized : (Vector2)transform.TransformDirection(Vector3.down);
            float speed = jetSpeed * size;
            Vector2 velocity = GetFlameVelocity(localStart) + direction * speed;
            float radius = FlameEdge * halfWidth;
            float seaLevelRadius = FlameEdge * scaleX * FlameMeshModule.GetFlameWidth(0.5f, flame.exitPressure, 1, 1, flame.DiamondOffset);

            // Put out at the rate that gives a trail left at the reference speed its thickness
            float massRate = thickness * throttle * share * Mathf.Pow(density, densityFalloff) * thinOut * ReferenceSpeed * seaLevelRadius / 0.9375f;
            float expansionAfterAMinute = expansion * size / Mathf.Sqrt(Mathf.Max(density, 0.05f));
            float drag = Mathf.Max(density, MinDensity) / dragTime;

            if (trail == null || trail.Removed || trail.planet != planet)
            {
                trail?.End();
                trail = SmokeTrails.StartTrail(material, planet);
            }

            double now = WorldTime.main.worldTime;
            Carry(trail, group, direction, speed, endDistance, now);

            trail.color = color;
            trail.keepVisible = keepVisible;
            trail.fadeIn = Mathf.Max(endDistance - startDistance, 0.1f * radius);
            float hidden = Mathf.Clamp01(2 * (1 - thinOut));
            trail.Feed(birth, velocity, radius, startDistance, swell, expansionAfterAMinute, drag, lifetime, hidden, massRate, now);
        }

        // How much of the smoke is left as the air thins out: all of it below thinOutFrom, none past thinOutTo, and evenly with height in between
        float GetThinOut(float density)
        {
            float from = Mathf.Log(Mathf.Max(thinOutFrom, 1e-6f)), to = Mathf.Log(Mathf.Max(thinOutTo, 1e-6f));
            if (to >= from)
                return density >= thinOutFrom ? 1 : 0;

            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(to, from, Mathf.Log(Mathf.Max(density, 1e-9f))));
        }

        // Moves the smoke still in the flame on down it, as fixed to it as the flame is to the craft, and lets it go where the flame ends
        void Carry(SmokeTrail trail, bool group, Vector2 direction, float speed, float endDistance, double now)
        {
            float step = speed * physicsStep;
            List<SmokeTrail.Point> points = trail.points;

            for (int i = points.Count - 1; i >= 0 && points[i].held; i--)
            {
                SmokeTrail.Point point = points[i];
                point.along += step;
                flame.GetCrossSection(flame.GetMeshY(point.along), group, out Vector2 local, out float halfWidth, out _);

                Double2 position = WorldView.ToGlobalPosition(local);
                point.carried += position - point.position;
                point.position = position;
                point.velocity = GetFlameVelocity(local) + direction * speed;
                point.radius = Mathf.Max(point.radius, FlameEdge * halfWidth); // never narrows, though the flame pinches in at its diamonds
                point.time = now;
                point.held = point.along < endDistance;
                points[i] = point;
            }
        }

        // Where down the plume (as meshY) the flame has dimmed to fadeInFrom, and to fadeInTo, of its brightness at the nozzle
        void FindFade(bool group, out float from, out float to)
        {
            const int Samples = 24;
            float end = flame.GetFadedOutMeshY(group);
            from = to = end;
            bool foundFrom = false;

            float previousY = 0, previous = flame.GetBrightness(0, group);
            for (int i = 1; i <= Samples; i++)
            {
                float y = end * i / Samples;
                float brightness = flame.GetBrightness(y, group);

                if (!foundFrom && brightness <= fadeInFrom)
                {
                    from = Mathf.Lerp(previousY, y, Mathf.InverseLerp(previous, brightness, fadeInFrom));
                    foundFrom = true;
                }
                if (brightness <= fadeInTo)
                {
                    to = Mathf.Lerp(previousY, y, Mathf.InverseLerp(previous, brightness, fadeInTo));
                    break;
                }

                previousY = y;
                previous = brightness;
            }

            to = Mathf.Max(to, from);
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
            EndTrails();
        }

        void EndTrails()
        {
            EndTrail(ref ownTrail);
            EndTrail(ref groupTrail);
        }

        static void EndTrail(ref SmokeTrail trail)
        {
            trail?.End();
            trail = null;
        }
    }
}
