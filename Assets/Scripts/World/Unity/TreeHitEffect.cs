// TreeHitEffect.cs  (MonoBehaviour — RTSCL.World.Unity)
// Two VFX modes for chopping/destroying a tree tile:
//   Spawn()      — single instance: squash-and-stretch + HDR brighten, plays on each axe hit.
//   SpawnBurst() — 5 copies offset radially: triggered on tree destruction.
//
// CenterOffset() handles the sprite pivot mismatch: CellToWorld returns the cell's
// bottom-left corner, but SpriteRenderer places the sprite pivot at the position.
// Adding the normalised pivot (pivot / rect.size) shifts the effect to the sprite centre.
//
// Where to adjust:
//   - Hit duration: Duration constant (0.28s).
//   - Squash amount: wobble * 0.35f (wide) and wobble * 0.25f (short) in Update().
//   - Flash brightness: HDR color starts at (3,3,3) = 3× overbright, lerps to (1,1,1).
//     Requires HDR on the camera / URP renderer to be visible.
//   - Burst radius/count: Radius / Count in SpawnBurst().
//   - Sorting order: 40 (above decorations/buildings/goblins, below click feedback).

using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    /// <summary>Short-lived squash-and-stretch + HDR flash placed at a tree cell.
    /// Spawn() for per-hit feedback; SpawnBurst() for destruction.</summary>
    public sealed class TreeHitEffect : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private float _t;
        private const float Duration = 0.28f;

        // Offset that centers a sprite of any pivot inside its cell: the SpriteRenderer
        // places the sprite's pivot at the GameObject position, so adding the normalized
        // pivot to the cell corner centers sprites whether their pivot is (0.5,0.5) or (0,0).
        private static Vector3 CenterOffset(Sprite sprite)
        {
            if (sprite == null || sprite.rect.width <= 0f || sprite.rect.height <= 0f)
                return new Vector3(0.5f, 0.5f, 0f);
            // Normalise pivot to [0,1] range, then use as world-unit offset (1 cell = 1 unit).
            return new Vector3(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height, 0f);
        }

        /// <summary>Spawns a single squash-and-flash effect centred on <paramref name="cell"/>.
        /// Uses the tree's own sprite so the flash visually matches the tile.</summary>
        public static void Spawn(Tilemap decorationMap, Vector3Int cell, Sprite sprite)
        {
            if (sprite == null) return;
            // CellToWorld gives the cell bottom-left; add the normalised pivot to centre.
            var pos = decorationMap.CellToWorld(cell) + CenterOffset(sprite);
            var go = new GameObject("TreeHitFx");
            go.transform.position = pos;
            var fx = go.AddComponent<TreeHitEffect>();
            fx._renderer = go.AddComponent<SpriteRenderer>();
            fx._renderer.sprite = sprite;
            fx._renderer.sortingOrder = 40;  // above decorations, buildings, goblins
            fx._renderer.color = new Color(3f, 3f, 3f, 1f);  // HDR brighten — flash white
        }

        /// <summary>Spawns 5 copies offset radially from the cell centre — used on tree destruction
        /// to produce a scatter-chip effect.</summary>
        public static void SpawnBurst(Tilemap decorationMap, Vector3Int cell, Sprite sprite)
        {
            if (sprite == null) return;
            var basePos = decorationMap.CellToWorld(cell) + CenterOffset(sprite);
            const int Count = 5;
            const float Radius = 0.35f;  // world-unit offset from centre per chip
            for (int i = 0; i < Count; i++)
            {
                float angle = (i / (float)Count) * Mathf.PI * 2f;
                Vector3 offset = new(Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * Radius, 0f);
                var go = new GameObject("TreeHitFx_Burst");
                go.transform.position = basePos + offset;
                var fx = go.AddComponent<TreeHitEffect>();
                fx._renderer = go.AddComponent<SpriteRenderer>();
                fx._renderer.sprite = sprite;
                fx._renderer.sortingOrder = 40;
                fx._renderer.color = new Color(3f, 3f, 3f, 1f);
            }
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float n = _t / Duration;
            if (n >= 1f) { Destroy(gameObject); return; }

            // Squash & stretch: starts wide+short, returns to neutral (1,1,1) as n→1
            float wobble = 1f - n;
            transform.localScale = new Vector3(
                1f + wobble * 0.35f,
                1f - wobble * 0.25f,
                1f);

            // Color: bright HDR white → back to normal (1,1,1,1)
            float bright = Mathf.Lerp(3f, 1f, n);
            _renderer.color = new Color(bright, bright, bright, 1f);
        }
    }
}
