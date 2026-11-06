using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SFS.Parts
{
    // A skin color: base RGB plus its material look. Serialized as "RRGGBB" with an optional "M" (metallic) and "Sxx" (smoothness percent)
    [Serializable]
    public struct SkinColor : IEquatable<SkinColor>
    {
        public Color32 rgb;
        public bool metallic;
        public float smoothness;

        public static readonly SkinColor White = new SkinColor(Color.white);
        public static readonly SkinColor DarkGray = new SkinColor(new Color32(64, 64, 64, 255));

        public SkinColor(Color color, bool metallic = false, float smoothness = 0)
        {
            color.a = 1;
            rgb = color;
            this.metallic = metallic;
            this.smoothness = Mathf.Clamp01(smoothness);
        }

        public Color Color => new Color32(rgb.r, rgb.g, rgb.b, 255);
        public string Hex => rgb.r.ToString("X2") + rgb.g.ToString("X2") + rgb.b.ToString("X2");
        public int SmoothnessPercent => Mathf.RoundToInt(Mathf.Clamp01(smoothness) * 100);

        public SkinColor WithColor(Color color) => new SkinColor(color, metallic, smoothness);
        public SkinColor WithMetallic(bool value) => new SkinColor(Color, value, smoothness);
        public SkinColor WithSmoothness(float value) => new SkinColor(Color, metallic, value);

        public string Serialize()
        {
            string output = Hex;

            if (metallic)
                output += "M";

            if (SmoothnessPercent > 0)
                output += "S" + SmoothnessPercent.ToString(CultureInfo.InvariantCulture);

            return output;
        }
        public static bool TryParse(string text, out SkinColor color)
        {
            color = White;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim().TrimStart('#').ToUpperInvariant();
            if (text.Length < 6 || !uint.TryParse(text.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
                return false;

            Color32 rgb = new Color32((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex, 255);
            bool metallic = false;
            float smoothness = 0;

            for (int i = 6; i < text.Length; i++)
            {
                if (text[i] == 'M')
                    metallic = true;
                else if (text[i] == 'S')
                {
                    int end = i + 1;
                    while (end < text.Length && char.IsDigit(text[end]))
                        end++;

                    if (end > i + 1 && int.TryParse(text.Substring(i + 1, end - i - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
                        smoothness = percent / 100f;

                    i = end - 1;
                }
            }

            color = new SkinColor(rgb, metallic, smoothness);
            return true;
        }
        public static SkinColor? ParseOptional(string text) => TryParse(text, out SkinColor color) ? color : null;

        public bool Equals(SkinColor other) => rgb.r == other.rgb.r && rgb.g == other.rgb.g && rgb.b == other.rgb.b && metallic == other.metallic && SmoothnessPercent == other.SmoothnessPercent;
        public override bool Equals(object obj) => obj is SkinColor other && Equals(other);
        public override int GetHashCode() => (rgb.r << 16 | rgb.g << 8 | rgb.b) ^ (metallic ? 1 << 24 : 0) ^ (SmoothnessPercent << 25);
        public override string ToString() => Serialize();
    }

    public static class SkinColorPresets
    {
        public static readonly SkinColor[] All =
        {
            new SkinColor(Color.white),
            new SkinColor(new Color32(214, 214, 214, 255)),
            new SkinColor(new Color32(110, 110, 110, 255)),
            new SkinColor(new Color32(64, 64, 64, 255)),
            new SkinColor(Color.white, true),
            new SkinColor(new Color32(214, 214, 214, 255), true),
            new SkinColor(new Color32(110, 110, 110, 255), true),
            new SkinColor(new Color32(64, 64, 64, 255), true),
            new SkinColor(new Color32(239, 172, 104, 255)),
            new SkinColor(new Color32(182, 0, 0, 255)),
            new SkinColor(new Color32(43, 79, 132, 255)),
            new SkinColor(new Color32(26, 26, 26, 255)),
        };
    }

    // Old solid color textures that are now the uniform pattern tinted with that color, so old saves can be recolored
    public static class SkinLegacy
    {
        // Tintable copies of old textures, so packs made before skin colors replace the old texture and not the pattern
        public const string TintSuffix = "_Tint";
        public const string UniformPattern = "Color_White" + TintSuffix;

        static readonly Dictionary<string, SkinColor> colors = new Dictionary<string, SkinColor>
        {
            { "Color_Black", new SkinColor(new Color32(64, 64, 64, 255)) },
            { "Color_Gray", new SkinColor(new Color32(110, 110, 110, 255)) },
            { "Metal", new SkinColor(Color.white, true) },
        };

        // The game's own versions; one replaced by a texture pack is not converted
        static readonly Dictionary<string, (PartTexture id, string pattern)> stock = new Dictionary<string, (PartTexture, string)>();

        public static void RegisterStock(Dictionary<string, ColorTexture> textures)
        {
            stock.Clear();

            foreach (ColorTexture texture in textures.Values)
                if (colors.ContainsKey(texture.name))
                    stock[texture.name] = (texture.GetTexID(), UniformPattern);
                else if (textures.ContainsKey(texture.name + TintSuffix))
                    stock[texture.name] = (texture.GetTexID(), texture.name + TintSuffix);
        }
        static bool IsStock(ColorTexture texture, out string pattern)
        {
            pattern = stock.TryGetValue(texture.name, out var entry) && texture.GetTexID() == entry.id? entry.pattern : null;
            return pattern != null;
        }

        public static bool IsLegacy(string textureName) => textureName != null && stock.ContainsKey(textureName);
        // The game's own solid colors, which skin colors replace in the pattern list
        public static bool IsStockLegacy(ColorTexture texture) => IsStock(texture, out _);
        public static bool HiddenFromPicker(ColorTexture texture) => DevSettings.NewSkinSystem? IsStockLegacy(texture) : texture.tintable;

        public static bool TryConvert(ColorTexture texture, out ColorTexture pattern, out SkinColor? color)
        {
            pattern = null;
            color = null;

            if (texture == null || texture.Tintable || !IsStock(texture, out string patternName))
                return false;

            if (Base.partsLoader == null || !Base.partsLoader.colorTextures.TryGetValue(patternName, out pattern) || !pattern.Tintable)
                return false;

            if (colors.TryGetValue(texture.name, out SkinColor solid))
                color = solid;

            return true;
        }

        // Resolves what a mesh actually renders: the pattern texture and both colors, filling in defaults and legacy conversions
        public static void Resolve(ColorTexture pattern, SkinColor? colorA, SkinColor? colorB, out ColorTexture resolved, out SkinColor a, out SkinColor b)
        {
            resolved = pattern;

            if (TryConvert(pattern, out ColorTexture converted, out SkinColor? legacyColor))
            {
                resolved = converted;
                colorA ??= legacyColor;
            }

            a = colorA ?? (resolved != null ? new SkinColor(resolved.defaultColorA) : SkinColor.White);
            b = colorB ?? (resolved != null ? new SkinColor(resolved.defaultColorB) : SkinColor.DarkGray);
        }
    }

    // Shader properties and CPU-side rendering of skin colors (swatches, slider gradients), kept in sync with Part.shader
    public static class SkinRendering
    {
        public static readonly int SkinColorA = Shader.PropertyToID("_SkinColorA");
        public static readonly int SkinColorB = Shader.PropertyToID("_SkinColorB");
        public static readonly int SkinParams = Shader.PropertyToID("_SkinParams");
        public static readonly int SkinTint = Shader.PropertyToID("_SkinTint");
        public static readonly int SkinLightFlip = Shader.PropertyToID("_SkinLightFlip");
        public static readonly int MetalGradient = Shader.PropertyToID("_MetalGradient");

        const float HighlightCenter = 0.4f;
        const float HighlightStrength = 0.4f;

        static float[] metalCurve;
        static Texture2D fallbackGradient;

        public static void SetMetalGradient(Texture2D gradient)
        {
            if (gradient == null)
                gradient = GetFallbackGradient();

            Shader.SetGlobalTexture(MetalGradient, gradient);

            Texture2D readable = gradient.isReadable ? gradient : gradient.GetReadableCopy();
            metalCurve = new float[readable.width];
            for (int x = 0; x < readable.width; x++)
                metalCurve[x] = readable.GetPixel(x, readable.height / 2).r;

            if (readable != gradient)
                UnityEngine.Object.Destroy(readable);
        }
        static Texture2D GetFallbackGradient()
        {
            if (fallbackGradient != null)
                return fallbackGradient;

            fallbackGradient = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int x = 0; x < 64; x++)
            {
                float t = x / 63f;
                float v = Mathf.Lerp(0.5f, 0.87f, Mathf.Exp(-Mathf.Pow((t - HighlightCenter) / 0.35f, 2)));
                fallbackGradient.SetPixel(x, 0, new Color(v, v, v, 1));
            }
            fallbackGradient.Apply();
            return fallbackGradient;
        }

        // Horizontal position across the part (0..1) -> brightness, same formula as the shader
        public static float Metal(float t)
        {
            if (metalCurve == null || metalCurve.Length == 0)
                return 1;

            float x = Mathf.Clamp01(t) * (metalCurve.Length - 1);
            int i = Mathf.FloorToInt(x);
            return Mathf.Lerp(metalCurve[i], metalCurve[Mathf.Min(i + 1, metalCurve.Length - 1)], x - i);
        }
        public static float Highlight(float t, float smoothness)
        {
            float d = (t - HighlightCenter) / Mathf.Lerp(0.5f, 0.1f, smoothness);
            return Mathf.Exp(-d * d) * smoothness * HighlightStrength;
        }
        public static Color Shade(SkinColor color, float t)
        {
            Color c = color.Color * Mathf.Lerp(1, Metal(t), color.metallic ? 1 : 0);
            float highlight = Highlight(t, color.smoothness);
            c.r = Mathf.Clamp01(c.r + highlight);
            c.g = Mathf.Clamp01(c.g + highlight);
            c.b = Mathf.Clamp01(c.b + highlight);
            c.a = 1;
            return c;
        }

        // Swatches
        static readonly Dictionary<SkinColor, Sprite> swatches = new Dictionary<SkinColor, Sprite>();
        public static Sprite GetSwatch(SkinColor color)
        {
            if (swatches.TryGetValue(color, out Sprite sprite) && sprite != null)
                return sprite;

            if (swatches.Count > 256)
                ClearSwatches();

            const int size = 48;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Swatch " + color.Serialize() };
            Color[] pixels = new Color[size * size];
            for (int x = 0; x < size; x++)
            {
                Color c = Shade(color, x / (size - 1f));
                for (int y = 0; y < size; y++)
                    pixels[y * size + x] = c;
            }
            texture.SetPixels(pixels);
            texture.Apply();

            sprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * 0.5f, 100);
            swatches[color] = sprite;
            return sprite;
        }
        static void ClearSwatches()
        {
            foreach (Sprite sprite in swatches.Values)
                if (sprite != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }

            swatches.Clear();
        }

        // Horizontal gradient texture for sliders, re-fillable
        public static Texture2D CreateGradient(int width) => new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        public static void FillGradient(Texture2D texture, Func<float, Color> getColor)
        {
            int width = texture.width;
            for (int x = 0; x < width; x++)
                texture.SetPixel(x, 0, getColor(x / (width - 1f)));
            texture.Apply();
        }
        public static Sprite CreateSprite(Texture2D texture) => Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f, 100);
    }
}
