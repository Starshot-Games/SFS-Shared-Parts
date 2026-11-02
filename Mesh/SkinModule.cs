using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using SFS.Variables;
using System.Linq;
using System;
using SFS.Builds;

#if !UNITY_STANDALONE
using SFS.Core;
#endif

namespace SFS.Parts.Modules
{
    [HideMonoScript]
    public class SkinModule : MonoBehaviour, I_InitializePartModule
    {
        // Variables // Field names are used by part mods and saves
        [BoxGroup("Ref", false), Required] public string skinTag;
        //
        public PipeMesh[] meshModules;
        ColorTexture[] defaultColors;
        ShapeTexture[] defaultShapes;
        //
        [BoxGroup("Tex", false)] public String_Reference colorTextureName; // The pattern; its texture is a mask when tintable
        [BoxGroup("Tex", false)] public String_Reference shapeTextureName;
        public bool disableColorSelect, disableShapeSelect;

        // Skin colors are string variables registered on the part at runtime, so prefabs, saves and part mods need no changes
        VariableList<string>.Variable colorA, colorB;

        int I_InitializePartModule.Priority => -10;

        void Awake() => RegisterColorVariables();

        void RegisterColorVariables()
        {
            if (colorA != null || !DevSettings.NewSkinSystem)
                return;

            VariablesModule variables = colorTextureName.referenceToVariables != null? colorTextureName.referenceToVariables : GetComponentInParent<VariablesModule>();
            if (variables == null)
                return;

            string prefix = colorTextureName.VariableName;
            if (prefix.EndsWith("_tex"))
                prefix = prefix.Substring(0, prefix.Length - 4);
            if (prefix.Length == 0)
                prefix = "color";

            colorA = GetOrAdd(prefix + "_a");
            colorB = GetOrAdd(prefix + "_b");

            VariableList<string>.Variable GetOrAdd(string variableName)
            {
                if (!variables.stringVariables.Has(variableName))
                    variables.stringVariables.saves.Add(new VariableSave(variableName));

                return variables.stringVariables.GetVariable(variableName);
            }
        }

        void I_InitializePartModule.Initialize()
        {
            RegisterColorVariables();

            // Default skin to return to
            defaultColors = meshModules.Select(meshModule => meshModule.textures.texture.colorTexture).ToArray();
            defaultShapes = meshModules.Select(meshModule => meshModule.textures.texture.shapeTexture).ToArray();

            ConvertLegacySkin();
            UpdateSaveFlags();

            // When color or shape texture changes, regenerates mesh
            colorTextureName.OnChange += OnColorTextureChange;
            shapeTextureName.OnChange += OnShapeTextureChange;

            if (colorA != null)
            {
                colorA.onValueChange += OnColorChange;
                colorB.onValueChange += OnColorChange;
            }

            // Purchasing
            #if !UNITY_STANDALONE
            Purchases.Main.HasSkins.OnChange += OnColorTextureChange;
            Purchases.Main.HasSkins.OnChange += OnShapeTextureChange;
            #endif
        }

        #if !UNITY_STANDALONE
        void OnDestroy()
        {
            if (Base.sceneLoader.isUnloading)
                return;

            Purchases.Main.HasSkins.OnChange -= OnColorTextureChange;
            Purchases.Main.HasSkins.OnChange -= OnShapeTextureChange;
        }
        #endif

        // Old saves store a solid color texture, it becomes the uniform pattern with that color so it can be recolored
        void ConvertLegacySkin()
        {
            if (colorA == null || HasValue(colorA) || HasValue(colorB))
                return;

            string key = colorTextureName.Value;
            if (key == null || Base.partsLoader == null || !Base.partsLoader.colorTextures.TryGetValue(key, out ColorTexture saved))
                return;

            if (!SkinLegacy.TryConvert(saved, out ColorTexture pattern, out SkinColor color))
                return;

            colorTextureName.Value = pattern.name;
            colorA.Value = color.Serialize();
        }

        // Empty colors are left out of saves
        void UpdateSaveFlags()
        {
            if (colorA == null)
                return;

            colorA.Save = HasValue(colorA);
            colorB.Save = HasValue(colorB);
        }
        static bool HasValue(VariableList<string>.Variable variable) => variable != null && !string.IsNullOrEmpty(variable.Value);

        static bool HasSkins
        {
            get
            {
                #if UNITY_STANDALONE
                return DevSettings.FullVersion;
                #else
                return Purchases.Main.HasSkins.Value;
                #endif
            }
        }
        static bool HasRedstone
        {
            get
            {
                #if UNITY_STANDALONE
                return DevSettings.FullVersion;
                #else
                return Purchases.Main.HasRedstoneAtlas.Value;
                #endif
            }
        }

        // Sets variable
        public void SetTexture(int channel, PartTexture texture)
        {
            int index = GetTextureOptions(channel).IndexOf(texture);
            if (index == -1)
                return;

            if (channel == 0)
                colorTextureName.Value = GetColorTextures()[index].name;
            if (channel == 1)
                shapeTextureName.Value = GetShapeTextures()[index].name;
        }
        public void SetPattern(ColorTexture pattern)
        {
            if (pattern != null)
                colorTextureName.Value = pattern.name;
        }
        public void SetColor(int slot, SkinColor color)
        {
            if (colorA == null)
                return;

            // The pattern is made explicit so the saved color always belongs to a tintable pattern
            ColorTexture current = GetSkinPattern();
            string key = colorTextureName.Value;
            Dictionary<string, ColorTexture> textures = Base.partsLoader.colorTextures;
            bool explicitPattern = key != null && textures.TryGetValue(key, out ColorTexture selected) && selected.Tintable;
            if (current != null && current.Tintable && !explicitPattern && textures.TryGetValue(current.name, out ColorTexture named) && named.Tintable)
                colorTextureName.Value = current.name;

            (slot == 0? colorA : colorB).Value = color.Serialize();
        }

        // On change (applies variable)
        void OnColorChange()
        {
            UpdateSaveFlags();
            OnColorTextureChange();
        }
        void OnColorTextureChange()
        {
            bool hasSkins = HasSkins;
            bool hasRedstone = HasRedstone;

            string key = colorTextureName.Value;
            ColorTexture selected = null;
            bool hasSelected = hasSkins && key != null && Base.partsLoader.colorTextures.TryGetValue(key, out selected) && (!selected.pack_Redstone_Atlas || hasRedstone);

            SkinColor? a = hasSkins && colorA != null? SkinColor.ParseOptional(colorA.Value) : null;
            SkinColor? b = hasSkins && colorB != null? SkinColor.ParseOptional(colorB.Value) : null;

            for (int i = 0; i < meshModules.Length; i++)
            {
                SkinLegacy.Resolve(hasSelected? selected : defaultColors[i], a, b, out ColorTexture pattern, out SkinColor resolvedA, out SkinColor resolvedB);
                meshModules[i].SetSkin(pattern, resolvedA, resolvedB);
            }
        }
        void OnShapeTextureChange()
        {
            bool hasSkins = HasSkins;
            bool hasRedstone = HasRedstone;

            for (int i = 0; i < meshModules.Length; i++)
            {
                string key = shapeTextureName.Value;
                Dictionary<string, ShapeTexture> tex = Base.partsLoader.shapeTextures;
                meshModules[i].SetShapeTexture(hasSkins && tex.TryGetValue(key, out ShapeTexture a) && (!a.pack_Redstone_Atlas || hasRedstone)? a : defaultShapes[i]);
            }
        }

        // Current Texture
        public PartTexture GetTexture(int channel)
        {
            Textures.TextureSelector tex = meshModules[0].textures.texture;

            if (channel == 0)
                return tex.colorTexture.GetTexID();
            if (channel == 1)
                return tex.shapeTexture.GetTexID();

            throw new Exception();
        }
        public ColorTexture GetSkinPattern() => meshModules.Length > 0? meshModules[0].textures.texture.colorTexture : null;
        public SkinColor GetColor(int slot)
        {
            if (meshModules.Length == 0)
                return SkinColor.White;

            return slot == 0? meshModules[0].SkinColorA : meshModules[0].SkinColorB;
        }
        public bool IsTintable => !disableColorSelect && GetSkinPattern() is ColorTexture pattern && pattern.Tintable;
        public bool IsTwoTone => IsTintable && GetSkinPattern().twoTone;

        // Available Textures
        public List<PartTexture> GetTextureOptions(int channel)
        {
            List<PartTexture> output = null;

            if (channel == 0)
                output = GetColorTextures().ConvertAll(colorTex => colorTex.GetTexID());
            if (channel == 1)
                output = GetShapeTextures().ConvertAll(shapeTex => shapeTex.GetTexID());

            return output;
        }
        public List<ColorTexture> GetPatternOptions() => GetColorTextures();
        List<ColorTexture> GetColorTextures()
        {
            List<ColorTexture> output = new List<ColorTexture>();

            if (!disableColorSelect)
                foreach (ColorTexture colorTexture in Base.partsLoader.colorTextures.Values)
                    if (colorTexture.tags.Contains(skinTag) && !(DevSettings.NewSkinSystem && SkinLegacy.IsStockLegacy(colorTexture)))
                        output.Add(colorTexture);

            #if !UNITY_STANDALONE
            output = output.Where(a => !a.pack_Redstone_Atlas || Purchases.Main.HasRedstoneAtlas.Value).ToList();
            #endif

            return output;
        }
        List<ShapeTexture> GetShapeTextures()
        {
            List<ShapeTexture> output = new List<ShapeTexture>();

            if (!disableShapeSelect)
                foreach (ShapeTexture shapeTexture in Base.partsLoader.shapeTextures.Values)
                    if (shapeTexture.tags.Contains(skinTag))
                        output.Add(shapeTexture);

            #if !UNITY_STANDALONE
            output = output.Where(a => !a.pack_Redstone_Atlas || Purchases.Main.HasRedstoneAtlas.Value).ToList();
            #endif

            return output;
        }

        void OnEnable()
        {
           if (BuildManager.main == null)
               return;
           BuildManager.main.buildMenus.DrawSkinButtons();
        }

        void OnDisable()
        {
            if (BuildManager.main == null)
                return;
            BuildManager.main.buildMenus.DrawSkinButtons();
        }
    }
}
