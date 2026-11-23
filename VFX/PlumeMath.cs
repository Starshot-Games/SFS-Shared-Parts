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
        public Matrix4x4 toWorld; // the plume renderer's space, which the merge below is restated in
        public float scaleX, scaleY; // world length of one of its x and y units
        public FlameMerge merge;
        public int maskLength;
        public fixed float mask[PlumeMath.MaskSamples];
    }

    // The plume at a height down it, before it is cut into slices: this jet on its own, or the group's whole envelope
    public struct PlumeSample
    {
        public float along, centre, halfWidth, widthRatio;
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

        // coverMargin and joinFeather in the flame shader
        const float CoverMargin = 0.03f, JoinFeather = 0.02f;

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

        // GetBent in the shader
        public static float GetBent(float along, float bend)
        {
            along = Mathf.Max(along, 0);
            return bend <= 0 ? along : along < bend ? along - along * along / (2 * bend) : bend / 2;
        }

        // GetJetAxis in the shader
        public static float GetJetAxis(float along, in FlameMerge merge) => merge.amount > 0 ? merge.amount * merge.drift * (GetBent(along, merge.bend) - Mathf.Max(along, 0)) : 0;

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
        public static float GetFadeEnd(in PlumeState s, bool group) => Mathf.Lerp(0.01f, 1, group && s.merge.amount > 0 ? s.merge.throttle : s.throttle);

        // meshY it has faded out at, tail included
        public static float GetFadedOutMeshY(in PlumeState s, bool group) => GetFadeEnd(s, group) * (1 + GetTail(s.atmospherePressure));

        // How far down the plume the row at meshY is, in its local y
        public static float GetAlong(in PlumeState s, float meshY)
        {
            ref readonly FlameMerge m = ref s.merge;
            float ownLength = GetPlumeLength(s.exitPressure);
            float mergedLength = m.amount > 0 ? Mathf.Lerp(ownLength, Mathf.Max(m.alongOffset + m.groupLength, ownLength), m.amount) : ownLength;
            return meshY * (ownLength + (mergedLength - ownLength) * meshY);
        }

        // The plume at meshY: this jet on its own, or the group's whole envelope, which every flame in it agrees on
        public static PlumeSample Sample(in PlumeState s, float meshY, bool group)
        {
            ref readonly FlameMerge m = ref s.merge;
            float ownLength = GetPlumeLength(s.exitPressure);
            bool merging = m.amount > 0;

            PlumeSample sample;
            sample.along = GetAlong(s, meshY);
            float ownWidth = GetFlameWidth(sample.along / ownLength, s.exitPressure, s.throttle, s.airSpread, s.atmospherePressure, s.diamondOffset);
            sample.centre = merging ? GetJetAxis(sample.along, m) : 0;
            sample.halfWidth = sample.widthRatio = ownWidth;
            if (!merging || !group)
                return sample;

            float groupT = (sample.along - m.alongOffset) / Mathf.Max(m.groupLength, 1e-4f);
            float groupWidth = m.halfSpan + m.throat * (GetFlameWidth(groupT, m.exitPressure, m.throttle, m.airSpread, s.atmospherePressure, s.diamondOffset) - 1);

            sample.centre = m.center - m.drift * sample.along;
            sample.halfWidth = groupWidth;
            sample.widthRatio = groupWidth / Mathf.Max(m.throat, 1e-4f);
            return sample;
        }

        // A point of the plume's space in world space
        static Vector2 ToWorld(in PlumeState s, float x, float y)
        {
            ref readonly Matrix4x4 m = ref s.toWorld;
            return new Vector2(m.m00 * x + m.m01 * y + m.m03, m.m10 * x + m.m11 * y + m.m13);
        }

        // The plume at meshY in world space - this jet, or the group's merged plume - as its middle, half-width, and how far down it that is
        public static void GetCrossSection(in PlumeState s, float meshY, bool group, out Vector2 centre, out float halfWidth, out float distance)
        {
            PlumeSample sample = Sample(s, meshY, group);
            centre = ToWorld(s, sample.centre, -sample.along);
            halfWidth = sample.halfWidth * s.scaleX;
            distance = sample.along * s.scaleY;
        }

        // The meshY that lies the given distance down the plume, as GetCrossSection measures it
        public static float GetMeshY(in PlumeState s, float distance)
        {
            ref readonly FlameMerge m = ref s.merge;
            float ownLength = GetPlumeLength(s.exitPressure);
            float extra = m.amount > 0 ? Mathf.Max(m.alongOffset + m.groupLength - ownLength, 0) * m.amount : 0;
            float along = Mathf.Max(distance, 0) / Mathf.Max(s.scaleY, 1e-6f);

            // along = meshY · (ownLength + extra · meshY), solved for meshY
            return 2 * along / (ownLength + Mathf.Sqrt(ownLength * ownLength + 4 * extra * along));
        }

        // How far into the merge with its neighbours the flame is at meshY: 0 its own jet .. 1 wholly the group's plume, as the shader crossfades them
        public static float GetMergeAt(in PlumeState s, float meshY)
        {
            ref readonly FlameMerge m = ref s.merge;
            if (m.amount <= 0)
                return 0;

            float along = GetAlong(s, meshY);
            return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(m.alongOffset, m.alongOffset + m.fadeLength, along)) * m.amount;
        }

        // The half-width of the nozzle the plume comes out of, in world space: this flame's own, or the average across the merged group
        public static float GetNozzleRadius(in PlumeState s, bool group) => (group && s.merge.amount > 0 ? s.merge.nozzleRadius : 1) * s.scaleX;

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

        // How bright the plume is down its middle at meshY, relative to a nozzle, as GetFlameEmission in the shader has it (T^4 going as width^-1.6).
        // The group's merged plume comes out the same for every flame in it.
        public static float GetBrightness(ref PlumeState s, float meshY, bool group)
        {
            PlumeSample sample = Sample(s, meshY, group);
            float atmospherePressure = s.atmospherePressure;

            float end = GetFadeEnd(s, group);
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
            bool merged = group && s.merge.amount > 0;
            float mixedLight = GetMixedLight(meshY, end, 1, merged ? s.merge.coreLength : s.coreLength, merged ? s.merge.mixedLight : s.mixedLight, merged ? s.merge.edgeLight : s.edgeLight, atmospherePressure);
            return Mathf.Pow(Mathf.Max(sample.widthRatio, 1e-4f), -1.6f) * lengthFade * mask * breakup * mixedLight;
        }

        // ---------- Batches: Burst-compiled through PlumeBurst unless UseBurst is off ----------

        // The culling bounds of a flame mesh, walked down the plume as its shape goes: merged, the slice slides sideways as well as
        // widening and is no longer widest at the tip. merge is the merge restated in that mesh's space.
        public static void GetFlameBounds(ref PlumeState s, ref FlameMerge merge, bool machDiamondsMesh, float maxMeshY, float maxMeshZ, out Vector3 min, out Vector3 max)
        {
            if (UseBurst)
                fixed (PlumeState* plume = &s)
                fixed (FlameMerge* meshMerge = &merge)
                    PlumeBurst.GetFlameBounds(plume, meshMerge, machDiamondsMesh ? 1 : 0, maxMeshY, maxMeshZ, out min, out max);
            else
                FlameBounds(ref s, ref merge, machDiamondsMesh, maxMeshY, maxMeshZ, out min, out max);
        }

        // Where down the plume (as meshY) the flame has dimmed to fadeInFrom, and to fadeInTo, of its brightness at the nozzle, and where it's gone
        public static void FindFade(ref PlumeState s, bool group, float fadeInFrom, float fadeInTo, float unseen, out float from, out float to, out float gone)
        {
            if (UseBurst)
                fixed (PlumeState* plume = &s)
                    PlumeBurst.FindFade(plume, group ? 1 : 0, fadeInFrom, fadeInTo, unseen, out from, out to, out gone);
            else
                Fade(ref s, group, fadeInFrom, fadeInTo, unseen, out from, out to, out gone);
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

        // A jet of a cluster FlameMergeSolver is finding the touches in, on the cluster's axes
        public struct TouchJet
        {
            public float across, along, length, radius, travel, reach;
            public Envelope envelope;
        }

        // How firmly every two jets of a cluster touch, by how far BOTH drawn plumes run on past where they would first meet, and where that is,
        // as count x count tables; presence is each jet's firmest contact
        public static void FindTouches(TouchJet* jets, int count, float* contacts, float* touches, float* presence)
        {
            if (UseBurst)
                PlumeBurst.FindTouches(jets, count, contacts, touches, presence);
            else
                Touches(jets, count, contacts, touches, presence);
        }

        internal static void Touches(TouchJet* jets, int count, float* contacts, float* touches, float* presence)
        {
            for (int i = 0; i < count * count; i++)
            {
                contacts[i] = 0;
                touches[i] = float.PositiveInfinity;
            }
            for (int i = 0; i < count; i++)
                presence[i] = 0;

            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                {
                    if (!TryGetTouchDistance(jets[i], jets[j], out float touch))
                        continue;

                    float contact = Mathf.Min(GetPresence(jets[i], touch), GetPresence(jets[j], touch));
                    contacts[i * count + j] = contacts[j * count + i] = contact;
                    touches[i * count + j] = touches[j * count + i] = touch;

                    presence[i] = Mathf.Max(presence[i], contact);
                    presence[j] = Mathf.Max(presence[j], contact);
                }
        }

        // How far a jet's drawn plume runs on past a distance down the cluster: 0 = it has faded out by there .. 1 = well past
        static float GetPresence(in TouchJet jet, float distance)
            => Mathf.SmoothStep(0, 1, (jet.reach - distance) / (FlameMergeSolver.PresenceRamp * jet.length));

        static float GetAxis(in TouchJet jet, float distance) => jet.across + jet.travel * (distance - jet.along);

        // Off the smooth envelope, or the first touch would skip between diamond bulges
        static float GetMixingWidth(in TouchJet jet, float distance) => jet.radius * jet.envelope.GetWidth((distance - jet.along) / jet.length) * (1 + FlameMergeSolver.EntrainReach);

        // How far downstream two jets first touch, if they do within their length
        static bool TryGetTouchDistance(in TouchJet a, in TouchJet b, out float touch)
        {
            touch = 0;

            // Both jets only exist downstream of the lower of the two nozzles.
            float from = Mathf.Max(a.along, b.along);
            if (a.along + a.length <= from || b.along + b.length <= from)
                return false;

            float to = Mathf.Min(from + FlameMergeSolver.TouchSearchLength * Mathf.Max(a.length, b.length),
                                 Mathf.Min(a.along + a.length, b.along + b.length));

            // Too far apart to meet even where both are widest
            float closest = Mathf.Min(b.across - a.across, Mathf.Min(GetAxis(b, from) - GetAxis(a, from), GetAxis(b, to) - GetAxis(a, to)));
            if (GetMixingWidth(a, to) + GetMixingWidth(b, to) < closest)
                return false;

            const int samples = FlameMergeSolver.TouchSamples;
            float lastDistance = from, lastOverlap = 0;
            for (int i = 0; i <= samples; i++)
            {
                float distance = Mathf.Lerp(from, to, (float)i / samples);

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

        internal static void FlameBounds(ref PlumeState s, ref FlameMerge merge, bool machDiamondsMesh, float maxMeshY, float maxMeshZ, out Vector3 min, out Vector3 max)
        {
            const int samples = 8;
            float halfX = 0, halfZ = 0, reach = 0;

            for (int i = 0; i <= samples; i++)
            {
                GetPlumeShape(s, maxMeshY * i / samples, machDiamondsMesh, merge, out float left, out float right, out float along);

                halfX = Mathf.Max(halfX, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)));
                halfZ = Mathf.Max(halfZ, maxMeshZ * Mathf.Abs(right - left));
                reach = Mathf.Max(reach, along);
            }

            const float margin = 1.1f; // +10%
            halfX *= 1 + Mathf.Max(GetEdgeCover(s.edgeSoftness, s.edgeBalance), GetEdgeCover(merge.edgeSoftness, merge.edgeBalance));

            min = new Vector3(-halfX * margin, -reach * margin, -halfZ * margin);
            max = new Vector3(halfX * margin, reach * (margin - 1), halfZ * margin);
        }

        // Mirror of GetPlumeShape in the flame shader, for the culling bounds above
        static void GetPlumeShape(in PlumeState s, float meshY, bool machDiamondsMesh, in FlameMerge merge, out float left, out float right, out float along)
        {
            float throttle = s.throttle, atmospherePressure = s.atmospherePressure;
            float ownLength = GetPlumeLength(s.exitPressure);
            bool takesShare = merge.amount > 0 && !machDiamondsMesh;

            float mergedLength = takesShare ? Mathf.Max(merge.alongOffset + merge.groupLength, ownLength) : ownLength;
            float drawnLength = mergedLength;
            if (!machDiamondsMesh)
            {
                float reach = 1 + GetTail(atmospherePressure) + 0.1f;
                drawnLength = reach * (ownLength + (mergedLength - ownLength) * reach);
            }
            along = meshY * (ownLength + (drawnLength - ownLength) * meshY);

            float ownWidth = GetFlameWidth(along / ownLength, s.exitPressure, throttle, s.airSpread, atmospherePressure, s.diamondOffset);
            float jetAxis = GetJetAxis(along, merge);
            left = jetAxis - ownWidth;
            right = jetAxis + ownWidth;

            if (!takesShare)
                return;

            float m = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(merge.alongOffset, merge.alongOffset + merge.fadeLength, along)) * merge.amount;

            float groupT = (along - merge.alongOffset) / Mathf.Max(merge.groupLength, 1e-4f);
            float groupWidth = merge.halfSpan + merge.throat * (GetFlameWidth(groupT, merge.exitPressure, merge.throttle, merge.airSpread, atmospherePressure, s.diamondOffset) - 1);

            float mergeCentre = merge.center - merge.drift * along;
            float envelopeLeft = mergeCentre - groupWidth;
            float envelopeRight = mergeCentre + groupWidth;

            bool outerLeft = merge.share.x <= 1e-4f;
            bool outerRight = merge.share.y >= 1 - 1e-4f;

            // The joins hold the places they have at the merge plane; only the outline goes with the envelope
            Vector2 share = Vector2.Lerp(merge.share, merge.litShare, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(merge.alongOffset, merge.alongOffset + Mathf.Max(merge.shareFadeLength, 1e-4f), along)) * merge.contact);
            float sliceLeft = outerLeft ? envelopeLeft : Mathf.Clamp(mergeCentre - merge.halfSpan + 2 * merge.halfSpan * share.x, envelopeLeft, envelopeRight);
            float sliceRight = outerRight ? envelopeRight : Mathf.Clamp(mergeCentre - merge.halfSpan + 2 * merge.halfSpan * share.y, envelopeLeft, envelopeRight);

            // A pair is only split below the lower of its two nozzles
            float startLeft = merge.left.present ? Mathf.Max(merge.left.origin.y, 0) : 0;
            float startRight = merge.right.present ? Mathf.Max(merge.right.origin.y, 0) : 0;
            float approachLeft = merge.alongOffset > startLeft + 1e-4f ? Mathf.Clamp01((along - startLeft) / (merge.alongOffset - startLeft)) : 1;
            float approachRight = merge.alongOffset > startRight + 1e-4f ? Mathf.Clamp01((along - startRight) / (merge.alongOffset - startRight)) : 1;
            float planeLeft = mergeCentre - merge.halfSpan + 2 * merge.halfSpan * merge.share.x;
            float planeRight = mergeCentre - merge.halfSpan + 2 * merge.halfSpan * merge.share.y;
            Vector2 nozzleBounds = merge.nozzleBounds - Vector2.one * (merge.drift * along);
            bool merged = along >= merge.alongOffset;
            bool joinLeft = !outerLeft && along >= startLeft;
            bool joinRight = !outerRight && along >= startRight;

            Vector2 firm = new Vector2(joinLeft ? merge.joinWeight.x : 0, joinRight ? merge.joinWeight.y : 0);
            Vector2 outline = new Vector2(Mathf.Lerp(left, envelopeLeft, m), Mathf.Lerp(right, envelopeRight, m));
            Vector2 joins = new Vector2(
                joinLeft ? (merged ? sliceLeft : Mathf.Lerp(nozzleBounds.x, planeLeft, approachLeft)) : outline.x,
                joinRight ? (merged ? sliceRight : Mathf.Lerp(nozzleBounds.y, planeRight, approachRight)) : outline.y);
            left = Mathf.Lerp(outline.x, joins.x, firm.x);
            right = Mathf.Lerp(outline.y, joins.y, firm.y);

            // A covered outer slice closes up rather than inverting
            if (left > right)
            {
                if (firm.x <= firm.y)
                    left = right;
                else
                    right = left;
            }

            // A loose join draws out to the outline too
            if (joinLeft && firm.x < 1)
                left = Mathf.Min(joins.x, outline.x);
            if (joinRight && firm.y < 1)
                right = Mathf.Max(joins.y, outline.y);

            // Away from its joins a slice also draws its neighbours' light, as far as it reaches (GetReach in the shader)
            if (!joinLeft)
                left = Mathf.Min(left, Mathf.Min(GetNeighbourEdge(s, merge.left, along, -1), GetNeighbourEdge(s, merge.right, along, -1)));
            if (!joinRight)
                right = Mathf.Max(right, Mathf.Max(GetNeighbourEdge(s, merge.left, along, 1), GetNeighbourEdge(s, merge.right, along, 1)));

            // At a join with a neighbouring slice the geometry overshoots, and the join itself is
            // settled per fragment (GetSliceCover in the shader).
            float overshoot = (CoverMargin + JoinFeather) * merge.halfSpan;
            if (!outerLeft)
                left -= overshoot;
            if (!outerRight)
                right += overshoot;
        }

        // A neighbouring jet's edge on one side (-1 left, 1 right) at a distance down this flame; none upstream of its nozzle
        static float GetNeighbourEdge(in PlumeState s, in FlameNeighbour n, float along, float side)
        {
            float alongN = (along - n.origin.y) / Mathf.Max(n.scale.y, 1e-6f);
            if (!n.present || alongN < 0)
                return -side * float.MaxValue;

            float width = GetFlameWidth(alongN / GetPlumeLength(n.exitPressure), n.exitPressure, n.throttle, n.airSpread, s.atmospherePressure, s.diamondOffset);
            float bent = along - n.origin.y;
            return n.origin.x + n.slope * along + n.drift * n.amount * (GetBent(bent, n.bend) - bent) + side * width * Mathf.Abs(n.scale.x);
        }

        internal static void Fade(ref PlumeState s, bool group, float fadeInFrom, float fadeInTo, float unseen, out float from, out float to, out float gone)
        {
            const int Samples = 24;
            float end = GetFadedOutMeshY(s, group);
            from = to = gone = end;
            bool foundFrom = false, foundTo = false;

            float previousY = 0, previous = GetBrightness(ref s, 0, group);
            for (int i = 1; i <= Samples; i++)
            {
                float y = end * i / Samples;
                float brightness = GetBrightness(ref s, y, group);

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

        // Where the plume's middle is at meshY, in the glow's space: its own jet's, going over to the group's as it merges
        static Vector2 GetGlowCentre(in PlumeState s, in GlowSlabInput input, float meshY)
        {
            PlumeSample own = Sample(s, meshY, false);
            Vector2 centre = new Vector2(own.centre, -own.along);

            float merge = GetMergeAt(s, meshY);
            if (merge > 0)
            {
                PlumeSample group = Sample(s, meshY, true);
                centre = Vector2.Lerp(centre, new Vector2(group.centre, -group.along), merge);
            }

            ref readonly Matrix4x4 m = ref input.toGlow;
            return new Vector2(m.m00 * centre.x + m.m01 * centre.y + m.m03, m.m10 * centre.x + m.m11 * centre.y + m.m13);
        }

        // The plume across at meshY, in the glow's space: its half-width, and how bright it is over that.
        // Merged, this flame's share of the group's light, spread over the group's plume, so the group's glows add up to its plume's.
        static void GetGlowSlab(ref PlumeState s, in GlowSlabInput input, float meshY, out float halfWidth, out float light)
        {
            halfWidth = Sample(s, meshY, false).halfWidth * s.scaleX;
            light = GetBrightness(ref s, meshY, false) * s.exitPressure;

            float merge = GetMergeAt(s, meshY);
            if (merge > 0)
            {
                float groupHalfWidth = Sample(s, meshY, true).halfWidth * s.scaleX;
                float share = Mathf.Clamp01(s.merge.share.y - s.merge.share.x);
                float groupLight = GetBrightness(ref s, meshY, true) * s.merge.exitPressure * share;

                float mergedHalfWidth = Mathf.Lerp(halfWidth, groupHalfWidth, merge);
                light = Mathf.Lerp(light * halfWidth, groupLight * groupHalfWidth, merge) / Mathf.Max(mergedHalfWidth, 1e-6f);
                halfWidth = mergedHalfWidth;
            }

            halfWidth *= input.toLocal;
            light *= input.throatEmission;
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
        public static void GetFlameBounds(PlumeState* s, FlameMerge* merge, int machDiamondsMesh, float maxMeshY, float maxMeshZ, out Vector3 min, out Vector3 max)
            => PlumeMath.FlameBounds(ref *s, ref *merge, machDiamondsMesh != 0, maxMeshY, maxMeshZ, out min, out max);

        [BurstCompile(CompileSynchronously = true)]
        public static void FindTouches(PlumeMath.TouchJet* jets, int count, float* contacts, float* touches, float* presence)
            => PlumeMath.Touches(jets, count, contacts, touches, presence);

        [BurstCompile(CompileSynchronously = true)]
        public static void FindFade(PlumeState* s, int group, float fadeInFrom, float fadeInTo, float unseen, out float from, out float to, out float gone)
            => PlumeMath.Fade(ref *s, group != 0, fadeInFrom, fadeInTo, unseen, out from, out to, out gone);

        [BurstCompile(CompileSynchronously = true)]
        public static void GetGlowSlabs(PlumeState* s, ref GlowSlabInput input, float* eddies, Vector4* axis, Vector4* shape, out Vector2 min, out Vector2 max, out Vector2 nozzleCentre, out Vector2 nozzleDown)
            => PlumeMath.GlowSlabs(ref *s, ref input, eddies, axis, shape, out min, out max, out nozzleCentre, out nozzleDown);
    }
}
