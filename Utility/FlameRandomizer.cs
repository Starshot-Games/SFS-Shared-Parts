using System;
using Sirenix.OdinInspector;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SFS.Parts.Modules
{
    [ExecuteInEditMode]
    public class FlameRandomizer : MonoBehaviour
    {
        public Vector2 size = Vector2.one;
        [Space]
        public bool splitAxis;
        [Range(0, 1), HideIf(nameof(splitAxis))] public float randomizationStrength = 0.2f;
        [Range(0, 1), ShowIf(nameof(splitAxis))] public float randomizationStrengthX = 0.2f;
        [Range(0, 1), ShowIf(nameof(splitAxis))] public float randomizationStrengthY = 0.2f;
        

        void Reset() => size = transform.localScale;
        
        float mX, mY;
        void Start()
        {
            float x = splitAxis? randomizationStrengthX : randomizationStrength;
            float y = splitAxis? randomizationStrengthY : randomizationStrength;
            mX = 1 / (1 + x / 2);
            mY = 1 / (1 + y / 2);
        }
        
        void Update()
        {
            if (Time.timeScale > 0)
            {
                float x = splitAxis? randomizationStrengthX : randomizationStrength;
                float y = splitAxis? randomizationStrengthY : randomizationStrength;
                transform.localScale = Application.isPlaying ? new Vector3(size.x * (1.0f + Random.value * x) * mX, size.y * (1.0f + Random.value * y) * mY, 1) : size;
            }
        }
        
        
        // Legacy
        [HideInInspector] public Vector2 min, max;
        void OnValidate()
        {
            if (min == Vector2.zero)
                return;
            
            size = (max + min) / 2;
            randomizationStrength = Mathf.Round((max.y - min.y) / min.y * 20) / 20;
            min = max = Vector2.zero;
        }
    }
}