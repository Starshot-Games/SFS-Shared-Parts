using System;
using System.Collections.Generic;
using System.Linq;
using Beebyte.Obfuscator;
using SFS.Parsers.Json;
using UnityEngine;

namespace SFS.Parts
{
    // Custom colors the player created, saved per world
    public static class SkinColorLibrary
    {
        [Serializable, Skip]
        class SaveData
        {
            public List<string> colors = new List<string>();
        }

        const string FileName = "CustomColors.txt";

        static string loadedPath;
        static List<SkinColor> colors = new List<SkinColor>();

        public static event Action onChange;

        public static IReadOnlyList<SkinColor> Colors
        {
            get
            {
                Load();
                return colors;
            }
        }

        public static int IndexOf(SkinColor color)
        {
            Load();
            return colors.IndexOf(color);
        }
        public static void Add(SkinColor color)
        {
            Load();
            if (colors.Contains(color))
                return;

            colors.Add(color);
            Save();
        }
        public static void Replace(int index, SkinColor color)
        {
            Load();
            if (index < 0 || index >= colors.Count)
                return;

            colors[index] = color;
            Save();
        }
        public static void Remove(int index)
        {
            Load();
            if (index < 0 || index >= colors.Count)
                return;

            colors.RemoveAt(index);
            Save();
        }

        static IFile GetFile()
        {
            if (Base.worldBase == null || Base.worldBase.paths == null)
                return null;

            return Base.worldBase.paths.path.GetFile(FileName);
        }
        static void Load()
        {
            IFile file = GetFile();
            string path = file != null ? file.Path : "";
            if (path == loadedPath)
                return;

            loadedPath = path;
            colors = new List<SkinColor>();

            if (file != null && file.Exists() && JsonWrapper.TryLoadJson(file, out SaveData data) && data.colors != null)
                foreach (string entry in data.colors)
                    if (SkinColor.TryParse(entry, out SkinColor color) && !colors.Contains(color))
                        colors.Add(color);
        }
        static void Save()
        {
            IFile file = GetFile();
            if (file != null)
            {
                try
                {
                    JsonWrapper.SaveAsJson(file, new SaveData { colors = colors.Select(c => c.Serialize()).ToList() }, true);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            onChange?.Invoke();
        }
    }
}
