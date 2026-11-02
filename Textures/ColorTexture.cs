using System;
using System.Collections.Generic;
using SFS.Parts.Modules;
using Sirenix.OdinInspector;
using UnityEngine;
using UV;

namespace SFS.Parts
{
    [CreateAssetMenu]
    public class ColorTexture : TextureAssetBase
    {
        [HideIf(nameof(multiple)), BoxGroup, InlineProperty, HideLabel] public PartTexture colorTex;
        [Space]
        public string[] tags;
        public bool pack_Redstone_Atlas;
        [Space]
        [Tooltip("The texture is a mask: white takes the primary skin color, black the secondary one")]
        public bool tintable;
        [ShowIf(nameof(tintable))] public bool twoTone = true;
        [ShowIf(nameof(tintable))] public Color defaultColorA = Color.white;
        [ShowIf("@tintable && twoTone")] public Color defaultColorB = new Color(0.25f, 0.25f, 0.25f, 1);
        
        // Only tinted with the new skin system, which then draws the textures' masks
        public bool Tintable => tintable && DevSettings.NewSkinSystem;
        [NonSerialized] PartTexture maskTex;
        [NonSerialized] Segment[] maskSegments;

        protected override PartTexture Texture => Tintable? maskTex ??= colorTex.WithMasks() : colorTex;
        protected override Segment[] DrawnSegments => Tintable? maskSegments ??= Array.ConvertAll(segments, s => new Segment { height = s.height, texture = s.texture.WithMasks() }) : segments;
    }

    [Serializable]
    public class Segment
    {
        [BoxGroup] public float height;
        [Space]
        [BoxGroup, InlineProperty, HideLabel] public PartTexture texture;
    }

    public abstract class TextureAssetBase : ScriptableObject
    {
        public bool multiple;
        [ShowIf(nameof(multiple)), Space] public Segment[] segments;
        
        public List<StartEnd_UV> Get_UV(Pipe shape, Line segment, float shapeWidth, Transform meshHolder, Vector2 lightDirection)
        {
            if (multiple)
            {
                List<StartEnd_UV> output = new List<StartEnd_UV>();

                float start = segment.start;
                foreach (Segment a in DrawnSegments)
                {
                    float end = a.height > 0? segment.start + a.height : segment.end + a.height;
                    Line localSegment = new Line(start, end);
                    start = end;
                    
                    output.AddRange(a.texture.Get_UV(shape, localSegment, shapeWidth, meshHolder, lightDirection));
                }
                
                return output;
            }

            return Texture.Get_UV(shape, segment, shapeWidth, meshHolder, lightDirection);
        }

        public PartTexture GetTexID() => multiple ? DrawnSegments[0].texture : Texture;
        protected abstract PartTexture Texture { get; }
        protected virtual Segment[] DrawnSegments => segments;
    }
}