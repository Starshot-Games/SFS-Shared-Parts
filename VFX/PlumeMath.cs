using Unity.Burst;
using UnityEngine;

namespace SFS.Parts.Modules
{
    // Everything a plume is sampled from, gathered off its FlameMeshModule once (FlameMeshModule.GetPlume), so a batch of samples
    // needn't go back to the transforms and materials for each
    public unsafe struct PlumeState
    {
        public float exitPressure, airSpread, throttle, atmospherePressure, diamondOffset;
        public float coreLength, mixedLight, edgeLight, edgeSoftness, edgeBalance;
        public Matrix4x4 toWorld; // the plume renderer's space
        public float scaleX, scaleY; // world length of one of its x and y units
        public int maskLength;
        public fixed float mask[PlumeMath.MaskSamples];
    }

    // The plume at a height down it
    public struct PlumeSample
    {
        public float along, halfWidth;
    }

    // What the glow's slabs are laid out with, besides the plume
    public struct GlowSlabInput
    {
        public Matrix4x4 toGlow; // the plume's space to the glow's
        public float toLocal, end, fadedOut, rolloff, afterglow, afterglowHold, throatEmission;
    }

    // The plume's shape and light as FlameMeshModule's shader draws it, for the CPU. Pure maths over a PlumeState, so it compiles with Burst
    public static unsafe class PlumeMath
    {
        public const int MaskSamples = 32;

        // Runs the batches below Burst-compiled; off, the same code runs as plain C#
        public static bool UseBurst = true;

        // The tail the plume breaks up into in air (plumeTail etc. in the shader)
        const float PlumeTail = 0.6f, TailStart = 0.55f, TailThinStart = 0.35f;
        const float TailSeaChurn = 1.7f, TailThinChurn = 2.5f, TailThinAir = 0.01f;
        const float TailContrast = 0.5f;

        public static float GetTailPresence(float atmospherePressure) => Mathf.SmoothStep(0, 1, atmospherePressure / TailThinAir);

        // how much further the tail carries the plume, of its drawn length
        public static float GetTail(float atmospherePressure) => PlumeTail * GetTailPresence(atmospherePressure);

        public static float GetTailStart(float atmospherePressure) => Mathf.Lerp(TailThinStart, TailStart, Mathf.Sqrt(Mathf.Clamp01(atmospherePressure)));

        public static float GetTailChurn(float atmospherePressure)
            => Mathf.Lerp(TailThinChurn, TailSeaChurn, Mathf.Sqrt(Mathf.Clamp01(atmospherePressure))) * GetTailPresence(atmospherePressure);

        // The plume's drawn length in local units (matches the y scale in VertCore).
        public static float GetPlumeLength(float exitPressure) => 12 * exitPressure * 3 * 2;

        static float GetDiamondsStrength(float flamePressure, float ambientPressure)
            => Mathf.Clamp01(Mathf.Max(ambientPressure - flamePressure, 0f) * 2f);

        public static float GetFlamePressure(float exitPressure, float throttle) => exitPressure * Mathf.Lerp(0.1f, 1f, throttle);
        public static float GetAmbientPressure(float atmospherePressure) => Mathf.Max(atmospherePressure, 1e-3f);

        // Mirrors the shader; without the ripple it is the smooth envelope. y is how far down the plume, of its length; past 1 only the spread in air goes on.
        public static float GetFlameWidth(float y, float exitPressure, float throttle, float spread, float atmospherePressure, float diamondOffset, bool ripple = true)
        {
            const int diamondCount = 5;

            float width = new Envelope(exitPressure, throttle, spread, atmospherePressure).GetWidth(y);

            // Mach diamonds
            if (ripple)
            {
                y = Mathf.Clamp01(y);
                float diamonds = Mathf.Abs(Mathf.Sin((y * diamondCount - diamondOffset) * Mathf.PI))
                                 - Mathf.Abs(Mathf.Sin(-diamondOffset * Mathf.PI));
                width += diamonds * GetDiamondsStrength(GetFlamePressure(exitPressure, throttle), GetAmbientPressure(atmospherePressure)) * 0.3f * exitPressure * 3f;
            }

            return width;
        }

        // GetFlameWidth without the ripple, with what doesn't change down the plume worked out once
        public readonly struct Envelope
        {
            readonly float scale, billow, billowStart, spread;

            public Envelope(float exitPressure, float throttle, float spread, float atmospherePressure)
            {
                const float gamma = 1.2f;

                // Vac / underexpanded billow
                float pressureRatio = GetFlamePressure(exitPressure, throttle) / GetAmbientPressure(atmospherePressure);
                billow = (Mathf.Pow(Mathf.Max(pressureRatio, 1f), 1f / (2f * gamma)) - 1f) * 1.3f;
                billowStart = billow * Mathf.Sqrt(0.05f);

                // Scale (based on pressure)
                scale = exitPressure * 3f;

                // Spread in air
                this.spread = spread * GetTailPresence(atmospherePressure);
            }

            public float GetWidth(float y)
            {
                float spreadY = Mathf.Max(y, 0);
                y = Mathf.Clamp01(y);

                // Standard expansion
                float width = y * 0.5f + billow * Mathf.Sqrt(y + 0.05f) - billowStart;

                return 1f + width * scale + spread * (spreadY < 1 ? 0.5f * spreadY * spreadY : spreadY - 0.5f);
            }
        }

        // Mirror of GetEdgeCover in the flame shader: how far past its outline a softened edge's mesh reaches, of its half-width
        public static float GetEdgeCover(float softness, float balance) => softness * balance + 0.1f * Mathf.Clamp01(softness * 10);

        // GetMixedLight in the shader
        public static float GetMixedLight(float meshY, float end, float core, float coreLength, float mixed, float edge, float atmospherePressure)
        {
            float air = Mathf.Sqrt(Mathf.Clamp01(atmospherePressure));
            float t = Mathf.Clamp01((meshY / Mathf.Max(end, 1e-4f) - coreLength * 0.5f) / Mathf.Max(coreLength, 1e-4f));
            return Mathf.Lerp(1, mixed, t * t * (3 - 2 * t) * air) * Mathf.Lerp(1, edge, (1 - core) * air);
        }

        // meshY the plume would fade out at without its tail
        public static float GetFadeEnd(in PlumeState s) => Mathf.Lerp(0.01f, 1, s.throttle);

        // meshY it has faded out at, tail included
        public static float GetFadedOutMeshY(in PlumeState s) => GetFadeEnd(s) * (1 + GetTail(s.atmospherePressure));

        // How far down the plume the row at meshY is, in its local y
        public static float GetAlong(in PlumeState s, float meshY) => meshY * GetPlumeLength(s.exitPressure);

        // The plume at meshY
        public static PlumeSample Sample(in PlumeState s, float meshY)
        {
            float ownLength = GetPlumeLength(s.exitPressure);

            PlumeSample sample;
            sample.along = GetAlong(s, meshY);
            sample.halfWidth = GetFlameWidth(sample.along / ownLength, s.exitPressure, s.throttle, s.airSpread, s.atmospherePressure, s.diamondOffset);
            return sample;
        }

        // A point of the plume's space in world space
        static Vector2 ToWorld(in PlumeState s, float x, float y)
        {
            ref readonly Matrix4x4 m = ref s.toWorld;
            return new Vector2(m.m00 * x + m.m01 * y + m.m03, m.m10 * x + m.m11 * y + m.m13);
        }

        // The plume at meshY in world space, as its middle, half-width, and how far down it that is
        public static void GetCrossSection(in PlumeState s, float meshY, out Vector2 centre, out float halfWidth, out float distance)
        {
            PlumeSample sample = Sample(s, meshY);
            centre = ToWorld(s, 0, -sample.along);
            halfWidth = sample.halfWidth * s.scaleX;
            distance = sample.along * s.scaleY;
        }

        // The meshY that lies the given distance down the plume, as GetCrossSection measures it
        public static float GetMeshY(in PlumeState s, float distance) => Mathf.Max(distance, 0) / Mathf.Max(s.scaleY, 1e-6f) / GetPlumeLength(s.exitPressure);

        // The half-width of the nozzle the plume comes out of, in world space
        public static float GetNozzleRadius(in PlumeState s) => s.scaleX;

        // The flame mask's alpha down the plume's middle, which the shader samples at 1 - v
        static float GetMaskAlong(ref PlumeState s, float meshY)
        {
            int count = s.maskLength;
            float x = Mathf.Clamp((1 - meshY) * count - 0.5f, 0, count - 1);
            int i = Mathf.Min((int)x, count - 2);
            return Mathf.Lerp(s.mask[i], s.mask[i + 1], x - i);
        }

        // One of the tail's puffs, averaged over the eddies at its quartile
        static float GetPuff(float eddy, float bias, float churn) => Mathf.Clamp01(0.5f + (eddy + bias) * TailContrast * Mathf.Max(churn, 1)) / 4;

        // How bright the plume is down its middle at meshY, relative to a nozzle, as the shader has it (T^4 going as width^-1.6)
        public static float GetBrightness(ref PlumeState s, float meshY)
        {
            PlumeSample sample = Sample(s, meshY);
            float atmospherePressure = s.atmospherePressure;

            float end = GetFadeEnd(s);
            float tail = GetTail(atmospherePressure);
            float start = GetTailStart(atmospherePressure);
            float churn = GetTailChurn(atmospherePressure);
            float t = meshY / Mathf.Max(end, 1e-4f);
            float tailY = (t > start ? start + (t - start) * (1 - start) / (1 + tail - start) : t) * end;
            float mask = GetMaskAlong(ref s, tailY);

            // The tail's puffs, averaged over the eddies
            float alongTail = Mathf.Clamp01((t - start) / (1 + tail - start));
            float bias = 1.5f - 3.5f * alongTail - 1.5f * (1 - mask);
            float puffs = 0;
            puffs += GetPuff(-1.15f, bias, churn);
            puffs += GetPuff(-0.32f, bias, churn);
            puffs += GetPuff(0.32f, bias, churn);
            puffs += GetPuff(1.15f, bias, churn);
            float breakup = Mathf.Lerp(1, puffs, Mathf.SmoothStep(0, 1, alongTail / 0.35f) * Mathf.Clamp01(churn));

            float lengthFade = Mathf.Clamp01(1 - tailY / end);
            float mixedLight = GetMixedLight(meshY, end, 1, s.coreLength, s.mixedLight, s.edgeLight, atmospherePressure);
            return Mathf.Pow(Mathf.Max(sample.halfWidth, 1e-4f), -1.6f) * lengthFade * mask * breakup * mixedLight;
        }

        // ---------- Batches: Burst-compiled through PlumeBurst unless UseBurst is off ----------

        // The culling bounds of a flame mesh, walked down the plume as its shape goes: it isn't always widest at the tip
        public static void GetFlameBounds(ref PlumeState s, bool machDiamondsMesh, float maxMeshY, float maxMeshZ, out Vector3 min, out Vector3 max)
        {
            if (UseBurst)
                fixed (PlumeState* plume = &s)
                    PlumeBurst.GetFlameBounds(plume, machDiamondsMesh ? 1 : 0, maxMeshY, maxMeshZ, out min, out max);
            else
                FlameBounds(ref s, machDiamondsMesh, maxMeshY, maxMeshZ, out min, out max);
        }

        // Where down the plume (as meshY) the flame has dimmed to fadeInFrom, and to fadeInTo, of its brightness at the nozzle, and where it's gone
        public static void FindFade(ref PlumeState s, float fadeInFrom, float fadeInTo, float unseen, out float from, out float to, out float gone)
        {
            if (UseBurst)
                fixed (PlumeState* plume = &s)
                    PlumeBurst.FindFade(plume, fadeInFrom, fadeInTo, unseen, out from, out to, out gone);
            else
                Fade(ref s, fadeInFrom, fadeInTo, unseen, out from, out to, out gone);
        }

        // The glow's slabs down the plume, packed closer at the nozzle, in the glow's space; eddies holds how much brighter each is
        public static void GetGlowSlabs(ref PlumeState s, ref GlowSlabInput input, float* eddies, Vector4* axis, Vector4* shape, out Vector2 min, out Vector2 max, out Vector2 nozzleCentre, out Vector2 nozzleDown)
        {
            if (UseBurst)
                fixed (PlumeState* plume = &s)
                    PlumeBurst.GetGlowSlabs(plume, ref input, eddies, axis, shape, out min, out max, out nozzleCentre, out nozzleDown);
            else
                GlowSlabs(ref s, ref input, eddies, axis, shape, out min, out max, out nozzleCentre, out nozzleDown);
        }

        internal static void FlameBounds(ref PlumeState s, bool machDiamondsMesh, float maxMeshY, float maxMeshZ, out Vector3 min, out Vector3 max)
        {
            const int samples = 8;
            float ownLength = GetPlumeLength(s.exitPressure);
            float drawnLength = machDiamondsMesh ? ownLength : (1 + GetTail(s.atmospherePressure) + 0.1f) * ownLength; // GetDrawnLength in the shader
            float halfX = 0, halfZ = 0, reach = 0;

            for (int i = 0; i <= samples; i++)
            {
                float meshY = maxMeshY * i / samples;
                float along = meshY * (ownLength + (drawnLength - ownLength) * meshY);
                float width = GetFlameWidth(along / ownLength, s.exitPressure, s.throttle, s.airSpread, s.atmospherePressure, s.diamondOffset);

                halfX = Mathf.Max(halfX, width);
                halfZ = Mathf.Max(halfZ, maxMeshZ * 2 * width);
                reach = Mathf.Max(reach, along);
            }

            const float margin = 1.1f; // +10%
            halfX *= 1 + GetEdgeCover(s.edgeSoftness, s.edgeBalance);

            min = new Vector3(-halfX * margin, -reach * margin, -halfZ * margin);
            max = new Vector3(halfX * margin, reach * (margin - 1), halfZ * margin);
        }

        internal static void Fade(ref PlumeState s, float fadeInFrom, float fadeInTo, float unseen, out float from, out float to, out float gone)
        {
            const int Samples = 24;
            float end = GetFadedOutMeshY(s);
            from = to = gone = end;
            bool foundFrom = false, foundTo = false;

            float previousY = 0, previous = GetBrightness(ref s, 0);
            for (int i = 1; i <= Samples; i++)
            {
                float y = end * i / Samples;
                float brightness = GetBrightness(ref s, y);

                if (!foundFrom && brightness <= fadeInFrom)
                {
                    from = Mathf.Lerp(previousY, y, Mathf.InverseLerp(previous, brightness, fadeInFrom));
                    foundFrom = true;
                }
                if (!foundTo && brightness <= fadeInTo)
                {
                    to = Mathf.Lerp(previousY, y, Mathf.InverseLerp(previous, brightness, fadeInTo));
                    foundTo = true;
                }
                if (brightness <= unseen)
                {
                    gone = Mathf.Lerp(previousY, y, Mathf.InverseLerp(previous, brightness, unseen));
                    break;
                }

                previousY = y;
                previous = brightness;
            }

            to = Mathf.Max(to, from);
            gone = Mathf.Max(gone, to);
        }

        // Where the plume's middle is at meshY, in the glow's space
        static Vector2 GetGlowCentre(in PlumeState s, in GlowSlabInput input, float meshY)
        {
            float along = GetAlong(s, meshY);
            ref readonly Matrix4x4 m = ref input.toGlow;
            return new Vector2(m.m01 * -along + m.m03, m.m11 * -along + m.m13);
        }

        // The plume across at meshY, in the glow's space: its half-width, and how bright it is over that
        static void GetGlowSlab(ref PlumeState s, in GlowSlabInput input, float meshY, out float halfWidth, out float light)
        {
            halfWidth = Sample(s, meshY).halfWidth * s.scaleX * input.toLocal;
            light = GetBrightness(ref s, meshY) * s.exitPressure * input.throatEmission;
        }

        internal static void GlowSlabs(ref PlumeState s, ref GlowSlabInput input, float* eddies, Vector4* axis, Vector4* shape, out Vector2 min, out Vector2 max, out Vector2 nozzleCentre, out Vector2 nozzleDown)
        {
            const int slabs = FlameGlowModule.Slabs;
            float end = input.end;
            min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            Vector2 start = GetGlowCentre(s, input, 0);
            nozzleCentre = start;
            nozzleDown = Vector2.down;
            float nozzleLight = 0;

            for (int i = 0; i < slabs; i++)
            {
                float t = (i + 1f) / slabs;
                Vector2 stop = GetGlowCentre(s, input, end * t * t);
                Vector2 down = stop - start;
                float length = down.magnitude;
                down = length > 1e-6f ? down / length : Vector2.down;

                t = (i + 0.5f) / slabs;
                GetGlowSlab(ref s, input, end * t * t, out float halfWidth, out float light);
                Vector2 middle = (start + stop) * 0.5f;

                // The glow goes by how bright the flame looks, which saturates as its light climbs, so the white-hot core doesn't outshine the rest
                if (input.rolloff > 0)
                    light = (1 - Mathf.Exp(-input.rolloff * light)) / input.rolloff;

                // The hot exhaust glows on down the flame and a little past it, lit by it, after the flame's own light has faded
                if (i == 0)
                {
                    nozzleDown = down;
                    nozzleLight = light;
                }
                float holdFrom = input.fadedOut * input.afterglowHold;
                float cooled = 1 - Mathf.SmoothStep(0, 1, (end * t * t - holdFrom) / Mathf.Max(end - holdFrom, 1e-4f));
                light = Mathf.Max(light, input.afterglow * nozzleLight * cooled);
                light *= eddies[i];

                axis[i] = new Vector4(middle.x, middle.y, down.x, down.y);
                shape[i] = new Vector4(halfWidth, length, light);

                Vector2 extent = new Vector2(Mathf.Abs(down.y), Mathf.Abs(down.x)) * halfWidth + new Vector2(Mathf.Abs(down.x), Mathf.Abs(down.y)) * (length * 0.5f);
                min = Vector2.Min(min, middle - extent);
                max = Vector2.Max(max, middle + extent);
                start = stop;
            }
        }
    }

    // PlumeMath's batches compiled with Burst
    [BurstCompile]
    public static unsafe class PlumeBurst
    {
        [BurstCompile(CompileSynchronously = true)]
        public static void GetFlameBounds(PlumeState* s, int machDiamondsMesh, float maxMeshY, float maxMeshZ, out Vector3 min, out Vector3 max)
            => PlumeMath.FlameBounds(ref *s, machDiamondsMesh != 0, maxMeshY, maxMeshZ, out min, out max);

        [BurstCompile(CompileSynchronously = true)]
        public static void FindFade(PlumeState* s, float fadeInFrom, float fadeInTo, float unseen, out float from, out float to, out float gone)
            => PlumeMath.Fade(ref *s, fadeInFrom, fadeInTo, unseen, out from, out to, out gone);

        [BurstCompile(CompileSynchronously = true)]
        public static void GetGlowSlabs(PlumeState* s, ref GlowSlabInput input, float* eddies, Vector4* axis, Vector4* shape, out Vector2 min, out Vector2 max, out Vector2 nozzleCentre, out Vector2 nozzleDown)
            => PlumeMath.GlowSlabs(ref *s, ref input, eddies, axis, shape, out min, out max, out nozzleCentre, out nozzleDown);
    }
}
