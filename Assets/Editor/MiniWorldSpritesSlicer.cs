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

        // Biome → ground sheet name. The "best" sprite within each sheet is auto-picked
        // by max(variance + saturation) — that avoids blank pure-white corner cells that
        // would render as missing tiles.
        private static readonly string[] GroundSheets =
        {
            "Grass", "DeadGrass", "TexturedGrass", "Winter", "Shore", "Cliff",
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
            }
            // Second pass: now that all slicing is done and sprites are stable,
            // pick the most "interesting" sprite per ground sheet for the base tile.
            foreach (var name in GroundSheets)
            {
                var path = $"{GroundFolder}/{name}.png";
                if (!File.Exists(path)) continue;
                if (CreateBaseTileAutoPick(path, name)) tilesMade++;
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

        private static bool CreateBaseTileAutoPick(string sheetAssetPath, string biomeName)
        {
            var sprites = LoadSlicedSprites(sheetAssetPath);
            if (sprites.Count == 0)
            {
                Debug.LogWarning($"[Slicer] '{biomeName}' sheet has no sprites.");
                return false;
            }

            int bestIdx = 0;
            float bestScore = -1f;
            for (int i = 0; i < sprites.Count; i++)
            {
                float score = SpriteInterestingness(sprites[i]);
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprites[bestIdx];
            var outPath = $"{OutFolder}/{biomeName}.asset";
            AssetDatabase.CreateAsset(tile, outPath);
            return true;
        }

        // Score = within-sprite color variance + average saturation. Pure-white blank
        // corner cells have variance=0 and saturation=0, so they always lose.
        private static float SpriteInterestingness(Sprite sprite)
        {
            var tex = ReadableCopy(sprite.texture);
            try
            {
                var r = sprite.textureRect;
                int x0 = Mathf.FloorToInt(r.x), y0 = Mathf.FloorToInt(r.y);
                int w = Mathf.FloorToInt(r.width), h = Mathf.FloorToInt(r.height);
                var pixels = tex.GetPixels(x0, y0, w, h);
                float n = pixels.Length;
                float sumR = 0, sumG = 0, sumB = 0;
                foreach (var p in pixels) { sumR += p.r; sumG += p.g; sumB += p.b; }
                float meanR = sumR / n, meanG = sumG / n, meanB = sumB / n;
                float varSum = 0, satSum = 0;
                foreach (var p in pixels)
                {
                    varSum += (p.r - meanR) * (p.r - meanR)
                            + (p.g - meanG) * (p.g - meanG)
                            + (p.b - meanB) * (p.b - meanB);
                    float mx = Mathf.Max(p.r, Mathf.Max(p.g, p.b));
                    float mn = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                    satSum += mx > 0 ? (mx - mn) / mx : 0;
                }
                return (varSum / (3f * n)) + (satSum / n);
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        private static Texture2D ReadableCopy(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
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
