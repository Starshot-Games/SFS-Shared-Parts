using Sirenix.OdinInspector;
using System.Collections.Generic;
using UnityEngine;
using SFS.Variables;
using TriggerPoint = SFS.Parts.Modules.AdaptTriggerPoint;
using System;
using SFS.Builds;

namespace SFS.Parts.Modules
{
    public class AdaptModule : MonoBehaviour, I_InitializePartModule
    {
        const float Range = 0.05f;
        const float AreaMargin = 0.05f; // Must match the 'extra' margin used in InPositionRange's InArea call

        // Reused scratch buffers, cleared at the start of each UpdateAdaptation, to avoid per-call GC allocations.
        // Safe because UpdateAdaptation runs on the main thread and is not re-entrant (nothing it writes triggers another call into it).
        static readonly List<TriggerPoint> s_allTriggerPoints = new List<TriggerPoint>();
        static readonly Dictionary<Vector2Int, List<TriggerPoint>> s_triggers = new Dictionary<Vector2Int, List<TriggerPoint>>();
        static readonly List<List<TriggerPoint>> s_cellPool = new List<List<TriggerPoint>>(); // Pool of grid-cell lists referenced by s_triggers
        static readonly List<AdaptModule> s_adaptModules = new List<AdaptModule>();
        static readonly List<AdaptModule> s_adaptModulesReverse = new List<AdaptModule>();
        static readonly List<TriggerPoint> s_acceptedTriggers = new List<TriggerPoint>();
        static readonly List<TriggerPoint> s_bestCurrent = new List<TriggerPoint>(); // Ping-pong buffers for GetBestPoint
        static readonly List<TriggerPoint> s_bestNext = new List<TriggerPoint>();

        // Data
        public Point[] adaptPoints;

        public bool ignoreOccupied;
        [Space]
        public bool applySeparatorFix;
        public bool applyFairingConeFix;
        [ShowIf("applyFairingConeFix")] public Float_Reference original, width;


        public int Priority => 8;
        public void Initialize()
        {
            if (applyFairingConeFix && original.Value == -1)
                original.Value = width.Value;
        }

        bool initialized;
        void Start()
        {
            if (BuildManager.main != null)
                foreach (Point point in adaptPoints)
                foreach (ExtraType extraType in point.extraTypes)
                    extraType.apply.OnChange += () => OnCanAdaptChange(transform, initialized);

            initialized = true;
        }
        public static void OnCanAdaptChange(Transform owner, bool initialized)
        {
            if (initialized)
                UpdateAdaptation(owner.GetComponentInParentTree<PartHolder>().GetArray());
        }

        // Adapt to triggers
        public static void UpdateAdaptation(params Part[] parts)
        {
            // Reset reused scratch buffers (see field declarations)
            List<TriggerPoint> allTriggerPoints = s_allTriggerPoints;
            Dictionary<Vector2Int, List<TriggerPoint>> triggers = s_triggers;
            allTriggerPoints.Clear();
            triggers.Clear();

            // Gathers all trigger points once and caches their world data (part.GetModules returns a cached array - no allocation)
            foreach (Part part in parts)
                foreach (AdaptTriggerModule trigger in part.GetModules<AdaptTriggerModule>())
                {
                    Transform triggerTransform = trigger.transform;
                    foreach (AdaptTriggerPoint point in trigger.points)
                    {
                        point.worldPosition = triggerTransform.TransformPoint(point.position.Value);
                        point.worldNormal = triggerTransform.TransformVectorUnscaled(point.normal);
                        allTriggerPoints.Add(point);
                    }
                }

            // Builds the spatial grid, renting cell lists from a pool instead of allocating one per cell
            int cellPoolIndex = 0;
            foreach (TriggerPoint point in allTriggerPoints)
            {
                Vector2 pointWorld = point.worldPosition;
                Vector2Int pointPosition = new Vector2Int(Mathf.FloorToInt(pointWorld.x / Range), Mathf.FloorToInt(pointWorld.y / Range));

                if (triggers.TryGetValue(pointPosition, out List<TriggerPoint> cell))
                    cell.Add(point);
                else
                {
                    List<TriggerPoint> rented;
                    if (cellPoolIndex < s_cellPool.Count)
                    {
                        rented = s_cellPool[cellPoolIndex];
                        rented.Clear();
                    }
                    else
                    {
                        rented = new List<TriggerPoint>();
                        s_cellPool.Add(rented);
                    }
                    cellPoolIndex++;

                    rented.Add(point);
                    triggers[pointPosition] = rented;
                }
            }

            // Cache adapt-module world data (build the module list manually to avoid LINQ's iterator/array allocations)
            List<AdaptModule> adaptModules = s_adaptModules;
            adaptModules.Clear();
            foreach (Part part in parts)
                adaptModules.AddRange(part.GetModules<AdaptModule>());

            foreach (AdaptModule adaptModule in adaptModules)
            {
                Transform adaptTransform = adaptModule.transform;
                foreach (Point point in adaptModule.adaptPoints)
                {
                    point.worldPosition = adaptTransform.TransformPoint(point.position.Value);
                    point.worldNormal = adaptTransform.TransformVectorUnscaled(point.normal);

                    // Precompute the world-space AABB of the (margin-expanded) input area so the Area scan can reject far triggers without a matrix inverse
                    if (point.reciverType == ReceiverType.Area)
                    {
                        Rect area = point.inputArea.Value;
                        Vector2 min = area.min - Vector2.one * AreaMargin;
                        Vector2 max = area.max + Vector2.one * AreaMargin;
                        Vector2 c0 = adaptTransform.TransformPoint(new Vector2(min.x, min.y));
                        Vector2 c1 = adaptTransform.TransformPoint(new Vector2(max.x, min.y));
                        Vector2 c2 = adaptTransform.TransformPoint(new Vector2(min.x, max.y));
                        Vector2 c3 = adaptTransform.TransformPoint(new Vector2(max.x, max.y));
                        point.worldAreaMin = Vector2.Min(Vector2.Min(c0, c1), Vector2.Min(c2, c3));
                        point.worldAreaMax = Vector2.Max(Vector2.Max(c0, c1), Vector2.Max(c2, c3));
                    }
                }
            }

            // Reverse order (loops in reverse so new adapts to old) - invariant across passes, so build it once.
            // Equivalent to GetModules(parts.Reverse()): parts walked back-to-front, each part's modules kept in order.
            List<AdaptModule> adaptModules_Reverse = s_adaptModulesReverse;
            adaptModules_Reverse.Clear();
            for (int i = parts.Length - 1; i >= 0; i--)
                adaptModules_Reverse.AddRange(parts[i].GetModules<AdaptModule>());

            // Precomputes the invariant (geometry/type) candidate set for every adapt point once, reused by both passes below
            foreach (AdaptModule adaptModule in adaptModules)
                adaptModule.CacheCandidates(triggers);

            Adapt();
            Adapt();
            void Adapt()
            {
                // Resets occupied
                foreach (TriggerPoint point in allTriggerPoints)
                    point.Occupied = null;

                // Marks triggers as occupied
                foreach (AdaptModule adaptModule in adaptModules)
                    adaptModule.Adapt(true);

                // Adapts
                foreach (AdaptModule adaptModule in adaptModules_Reverse)
                    adaptModule.Adapt(false);
            }
        }

        // Builds the invariant candidate set for each adapt point once. The spatial/type checks (MatchingType, MatchingNormals,
        // InPositionRange) don't change between passes - only Occupied state and trigger outputs do - so they're computed here
        // instead of being redone on every one of the 4 Adapt calls.
        void CacheCandidates(Dictionary<Vector2Int, List<TriggerPoint>> triggers)
        {
            foreach (Point point in adaptPoints)
            {
                List<TriggerPoint> candidates = point.candidates;
                candidates.Clear();

                // Checking is it area or point, to not make full iteration if it is point
                switch (point.reciverType)
                {
                    case ReceiverType.Point:
                    {
                        // Inlines MagnetModule.GetDictionaryKeys to avoid its per-call Vector2Int[4] allocation
                        Vector2 wp = point.worldPosition;
                        int roofX = Mathf.RoundToInt(wp.x / Range);
                        int roofY = Mathf.RoundToInt(wp.y / Range);
                        int floorX = roofX - 1;
                        int floorY = roofY - 1;

                        AddCandidatesFromCell(point, candidates, new Vector2Int(floorX, floorY));
                        AddCandidatesFromCell(point, candidates, new Vector2Int(roofX, floorY));
                        AddCandidatesFromCell(point, candidates, new Vector2Int(floorX, roofY));
                        AddCandidatesFromCell(point, candidates, new Vector2Int(roofX, roofY));

                        void AddCandidatesFromCell(Point receiver, List<TriggerPoint> into, Vector2Int key)
                        {
                            if (triggers.TryGetValue(key, out List<TriggerPoint> triggerPoints))
                                foreach (TriggerPoint triggerPoint in triggerPoints)
                                    if (SpatiallyMatches(receiver, triggerPoint))
                                        if (!into.Contains(triggerPoint))
                                            into.Add(triggerPoint);
                        }

                        break;
                    }

                    case ReceiverType.Area:

                        Vector2 areaMin = point.worldAreaMin;
                        Vector2 areaMax = point.worldAreaMax;
                        foreach (List<TriggerPoint> triggersList in triggers.Values)
                        foreach (TriggerPoint triggerPoint in triggersList)
                        {
                            // Cheap world-space AABB reject: anything outside the area's bounds can't pass InArea, so skip the matrix inverse
                            Vector2 tw = triggerPoint.worldPosition;
                            if (tw.x < areaMin.x || tw.y < areaMin.y || tw.x > areaMax.x || tw.y > areaMax.y)
                                continue;

                            if (SpatiallyMatches(point, triggerPoint))
                                candidates.Add(triggerPoint);
                        }

                        break;
                }
            }
        }

        void Adapt(bool justMarkTriggerAsOccupied)
        {
            List<TriggerPoint> acceptedTriggers = s_acceptedTriggers;
            foreach (Point point in adaptPoints)
            {
                // Filters the precomputed candidates by the per-pass dynamic checks (Occupied state + adapt range)
                acceptedTriggers.Clear();
                foreach (TriggerPoint triggerPoint in point.candidates)
                    if (AcceptedDynamic(point, triggerPoint))
                        acceptedTriggers.Add(triggerPoint);

                bool hasTrigger = acceptedTriggers.Count > 0;
                TriggerPoint bestTrigger = hasTrigger ? GetBestPoint(point, acceptedTriggers) : null;

                float output = hasTrigger ? bestTrigger.output.Value + GetOutputOffset() + bestTrigger.outputOffset : point.defaultOutput.Value;

                // Computes the local difference once (InverseTransformPoint is a matrix inverse) instead of once per axis
                Vector2 difference = point.reciverType == ReceiverType.Area && hasTrigger ? GetPositionDifference_Local() : Vector2.zero;
                float differenceX = difference.x;
                float differenceY = difference.y;

                Vector2 GetPositionDifference_Local() => (Vector2)transform.InverseTransformPoint(bestTrigger.worldPosition) - point.position.Value;

                if (justMarkTriggerAsOccupied)
                {
                    if (hasTrigger && !ignoreOccupied)
                        if (point.output.Value == output && point.differenceX.Value == differenceX && point.differenceY.Value == differenceY) // Points current state is already target state
                            bestTrigger.Occupied = point;
                }
                else
                {
                    point.isOccupied.Value = hasTrigger;
                    point.output.Value = output;
                    point.differenceX.Value = differenceX;
                    point.differenceY.Value = differenceY;

                    if (hasTrigger && !ignoreOccupied)
                        bestTrigger.Occupied = point;
                }

                float GetOutputOffset()
                {
                    if (bestTrigger.type == point.type)
                        return point.outputOffset;

                    foreach (ExtraType extraType in point.extraTypes)
                        if (extraType.apply.Value && extraType.type == bestTrigger.type)
                            return extraType.outputOffset;

                    return 0;
                }
            }
        }

        // Invariant part of acceptance: type + geometry. Independent of Occupied state and trigger outputs, so it's cached in CacheCandidates.
        bool SpatiallyMatches(Point receiver, TriggerPoint trigger)
        {
            // MatchingNormals before InPositionRange: for Area receivers InPositionRange does a matrix inverse, so let the cheap check short-circuit first
            return MatchingType(receiver, trigger) && MatchingNormals(receiver, trigger) && InPositionRange(receiver, trigger);
        }
        // Dynamic part of acceptance: re-evaluated every pass because Occupied state and trigger.output change between passes.
        bool AcceptedDynamic(Point receiver, TriggerPoint trigger)
        {
            bool free = trigger.Occupied == null || trigger.Occupied == receiver;
            bool occupied = ignoreOccupied || free;

            if (applySeparatorFix && !free && trigger.outputOffset == 0.4f)
                return false;

            return occupied && (!trigger.toggle || trigger.enabled.Value) && InAdaptRange(receiver, trigger);
        }
        //
        static bool MatchingType(Point receiver, TriggerPoint trigger)
        {
            if (receiver.type == trigger.type)
                return true;

            foreach (ExtraType extraType in receiver.extraTypes)
                if (extraType.apply.Value && extraType.type == trigger.type)
                    return true;

            return false;
        }
        bool InAdaptRange(Point receiver, TriggerPoint trigger)
        {
            if (applySeparatorFix && trigger.outputOffset == 0.4f)
            {
                float differenceY = ((Vector2)transform.InverseTransformPoint(trigger.worldPosition) - receiver.position.Value).y;
                if (differenceY < 0.001f)
                    return false;
            }

            return trigger.output.Value > receiver.adaptRange.min.Value - 0.001f && trigger.output.Value < receiver.adaptRange.max.Value + 0.001f;
        }
        bool InPositionRange(Point receiver, TriggerPoint trigger)
        {
            if (receiver.reciverType == ReceiverType.Area)
            {
                Vector2 triggerPoint = transform.InverseTransformPoint(trigger.worldPosition);
                return Math_Utility.InArea(receiver.inputArea.Value, triggerPoint, AreaMargin);
            }

            return (trigger.worldPosition - receiver.worldPosition).sqrMagnitude < 0.05f * 0.05f;
        }
        bool MatchingNormals(Point receiver, TriggerPoint trigger)
        {
            return (receiver.worldNormal + trigger.worldNormal).sqrMagnitude < 0.01f; // Max difference of 0.1
        }

        // Get
        TriggerPoint GetBestPoint(Point adaptPoint, List<TriggerPoint> points)
        {
            // Ping-pongs between two reused buffers, narrowing to the best-scoring subset per priority, to avoid the per-level array allocations
            List<TriggerPoint> current = s_bestCurrent;
            List<TriggerPoint> next = s_bestNext;
            current.Clear();
            current.AddRange(points);

            foreach (PriorityType priorityType in adaptPoint.priority)
            {
                next.Clear();
                float bestScore = float.NegativeInfinity;

                foreach (TriggerPoint trigger in current)
                {
                    float score = 0;
                    switch (priorityType)
                    {
                        case PriorityType.MinDistance: score = -GetPositionDifference(adaptPoint, trigger).sqrMagnitude; break;
                        case PriorityType.MaxDistance: score = GetPositionDifference(adaptPoint, trigger).sqrMagnitude; break;
                        case PriorityType.MinValue: score = -trigger.output.Value; break;
                        case PriorityType.MaxValue: score = trigger.output.Value; break;
                        case PriorityType.ClosestValue: score = -Mathf.Abs(adaptPoint.defaultOutput.Value - (trigger.output.Value + trigger.outputOffset)); break;
                    }

                    if (score > bestScore)
                        next.Clear();

                    if (score >= bestScore)
                    {
                        next.Add(trigger);
                        bestScore = score;
                    }
                }

                // Swap: 'next' becomes the working set for the following priority
                List<TriggerPoint> temp = current;
                current = next;
                next = temp;

                if (current.Count == 1)
                    break;
            }

            return current[0];
        }
        Vector2 GetPositionDifference(Point adaptPoint, TriggerPoint trigger)
        {
            return trigger.worldPosition - adaptPoint.worldPosition;
        }


        [Serializable]
        public class Point
        {
            [BoxGroup("A", false)] public ReceiverType reciverType;
            [BoxGroup("A/Input", false)] public Composed_Vector2 position;
            [BoxGroup("A/Input", false)] public Vector2 normal;
            [BoxGroup("A/Input", false)] public MinMaxRange adaptRange;
            [BoxGroup("A/Input", false)] public TriggerType type;
            [BoxGroup("A/Input", false)] public ExtraType[] extraTypes;
            [BoxGroup("A/Input", false)] public PriorityType[] priority;
            //
            [BoxGroup("A/Area", false), ShowIf("reciverType", ReceiverType.Area), HideLabel] public Composed_Rect inputArea;
            //
            [BoxGroup("A/Output", false)] public Composed_Float defaultOutput;
            [BoxGroup("A/Output", false)] public float outputOffset;
            [BoxGroup("A/Output", false)] public Float_Reference output;
            [BoxGroup("A/Output", false)] public Bool_Reference isOccupied;
            [Space]
            [BoxGroup("A/Output", false), ShowIf("reciverType", ReceiverType.Area)] public Float_Reference differenceX;
            [BoxGroup("A/Output", false), ShowIf("reciverType", ReceiverType.Area)] public Float_Reference differenceY;

            // Temp
            [NonSerialized] public Vector2 worldPosition, worldNormal;
            // World-space AABB of the input area (Area receivers only) for cheaply rejecting far triggers before the precise InArea test
            [NonSerialized] public Vector2 worldAreaMin, worldAreaMax;
            // Triggers passing the invariant spatial/type checks - computed once per UpdateAdaptation, reused across all adaptation passes
            [NonSerialized] public readonly List<TriggerPoint> candidates = new List<TriggerPoint>();
        }


        public enum TriggerType
        {
            Fuselage,
            Fairing,
        }
        public enum ReceiverType
        {
            Point,
            Area,
        }
        public enum PriorityType
        {
            MinDistance,
            MaxDistance,
            MinValue,
            MaxValue,
            ClosestValue,
        }

        [Serializable]
        public class ExtraType
        {
            public Bool_Reference apply;
            public TriggerType type;
            public float outputOffset;
        }
    }
}
