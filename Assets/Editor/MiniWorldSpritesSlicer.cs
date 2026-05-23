using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.Editor
{
    public static class MiniWorldSpritesSlicer
    {
        private const int CellSize = 16;
        private const string GroundFolder = "Assets/MiniWorldSprites/Ground";
        private const string NatureFolder = "Assets/MiniWorldSprites/Nature";
        private const string OutFolder    = "Assets/Generated/Tiles";
        private const string DecoOutFolder = "Assets/Generated/Tiles/Decorations";

        // For V1 we hardcode which sprite index represents each biome's "base" tile.
        // These indices are into the 16x16 grid in reading order (top-to-bottom, left-to-right
        // as Unity slices). Adjust if a particular sheet's center looks bad.
        private static readonly Dictionary<string, int> GroundBaseIndex = new()
        {
            // Indices chosen to be in-bounds for the actual pack sheets (sizes vary 5-8 sprites).
            { "Grass",         2 },
            { "DeadGrass",     2 },
            { "TexturedGrass", 2 },
            { "Winter",        3 },
            { "Shore",         2 },
            { "Cliff",        12 },
            // Cliff-Water deferred to Phase 2 (transitions)
        };

        [MenuItem("Tools/RTSCL/Slice MiniWorldSprites")]
        public static void Run()
        {
            EnsureFolder(OutFolder);
            EnsureFolder(DecoOutFolder);

            EnsureDeepWaterTile();

            int sliced = 0, tilesMade = 1;  // 1 = DeepWater already counted

            foreach (var path in EnumeratePngs(GroundFolder))
            {
                if (SliceTo16(path)) sliced++;
                var name = Path.GetFileNameWithoutExtension(path);
                if (GroundBaseIndex.TryGetValue(name, out int idx))
                {
                    if (CreateBaseTile(path, name, idx)) tilesMade++;
                }
            }

            foreach (var path in EnumeratePngs(NatureFolder))
            {
                if (SliceTo16(path)) sliced++;
                tilesMade += CreateDecorationTiles(path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Slicer] Sliced {sliced} PNGs; generated {tilesMade} TileBase assets.");
        }

        private static void EnsureDeepWaterTile()
        {
            const string tileOut = OutFolder + "/DeepWater.asset";
            if (AssetDatabase.LoadAssetAtPath<Tile>(tileOut) != null) return;

            var pngPath = OutFolder + "/_DeepWaterTexture.png";
            if (!File.Exists(pngPath))
            {
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var color = new Color32(56, 88, 156, 255);
                var pixels = new Color32[16 * 16];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(pngPath, tex.EncodeToPNG());
                AssetDatabase.ImportAsset(pngPath);
                Object.DestroyImmediate(tex);
            }

            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType         = TextureImporterType.Sprite;
                importer.spriteImportMode    = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 16;
                importer.filterMode          = FilterMode.Point;
                importer.textureCompression  = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(pngPath);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            AssetDatabase.CreateAsset(tile, tileOut);
        }

        private static IEnumerable<string> EnumeratePngs(string folder)
        {
            if (!Directory.Exists(folder)) yield break;
            foreach (var p in Directory.GetFiles(folder, "*.png", SearchOption.TopDirectoryOnly))
                yield return p.Replace('\\', '/');
        }

        private static bool SliceTo16(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return false;
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 16;
            importer.filterMode          = FilterMode.Point;
            importer.textureCompression  = TextureImporterCompression.Uncompressed;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            // Read texture dims via temporary load
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null) { importer.SaveAndReimport(); return false; }
            int cols = tex.width  / CellSize;
            int rows = tex.height / CellSize;

            var rects = new List<SpriteRect>(cols * rows);
            string baseName = Path.GetFileNameWithoutExtension(assetPath);
            int i = 0;
            for (int row = rows - 1; row >= 0; row--)  // Unity origin is bottom-left;
                                                       // start from top row to match reading order
            for (int col = 0; col < cols; col++)
            {
                rects.Add(new SpriteRect
                {
                    name      = $"{baseName}_{i}",
                    rect      = new Rect(col * CellSize, row * CellSize, CellSize, CellSize),
                    pivot     = new Vector2(0.5f, 0.5f),
                    alignment = SpriteAlignment.Center,
                    spriteID  = GUID.Generate(),
                });
                i++;
            }
            provider.SetSpriteRects(rects.ToArray());

            // Sync name table (required for sprite lookup by name)
            var nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            var pairs = new List<SpriteNameFileIdPair>(rects.Count);
            foreach (var r in rects)
                pairs.Add(new SpriteNameFileIdPair(r.name, r.spriteID));
            nameProvider.SetNameFileIdPairs(pairs);

            provider.Apply();
            importer.SaveAndReimport();
            return true;
        }

        private static bool CreateBaseTile(string sheetAssetPath, string biomeName, int spriteIndex)
        {
            var sprites = LoadSlicedSprites(sheetAssetPath);
            if (spriteIndex < 0 || spriteIndex >= sprites.Count)
            {
                Debug.LogWarning($"[Slicer] '{biomeName}' index {spriteIndex} OOB " +
                                 $"(sheet has {sprites.Count}). Using 0.");
                spriteIndex = 0;
            }
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprites[spriteIndex];
            var outPath = $"{OutFolder}/{biomeName}.asset";
            AssetDatabase.CreateAsset(tile, outPath);
            return true;
        }

        private static int CreateDecorationTiles(string sheetAssetPath)
        {
            var sprites = LoadSlicedSprites(sheetAssetPath);
            string baseName = Path.GetFileNameWithoutExtension(sheetAssetPath);
            int made = 0;
            for (int i = 0; i < sprites.Count; i++)
            {
                // Skip fully-transparent sprites (heuristic: tiny rect won't help in V1; emit all)
                var tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sprites[i];
                var outPath = $"{DecoOutFolder}/{baseName}_{i}.asset";
                AssetDatabase.CreateAsset(tile, outPath);
                made++;
            }
            return made;
        }

        private static List<Sprite> LoadSlicedSprites(string sheetAssetPath)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(sheetAssetPath);
            var list = new List<Sprite>();
            foreach (var o in all)
                if (o is Sprite s) list.Add(s);
            return list;
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder)) return;
            var parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            var leaf   = Path.GetFileName(assetFolder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
