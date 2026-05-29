using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
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
            return new Vector3(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height, 0f);
        }

        public static void Spawn(Tilemap decorationMap, Vector3Int cell, Sprite sprite)
        {
            if (sprite == null) return;
            var pos = decorationMap.CellToWorld(cell) + CenterOffset(sprite);
            var go = new GameObject("TreeHitFx");
            go.transform.position = pos;
            var fx = go.AddComponent<TreeHitEffect>();
            fx._renderer = go.AddComponent<SpriteRenderer>();
            fx._renderer.sprite = sprite;
            fx._renderer.sortingOrder = 40;  // above decorations, buildings, goblins
            fx._renderer.color = new Color(3f, 3f, 3f, 1f);  // HDR brighten — flash
        }

        public static void SpawnBurst(Tilemap decorationMap, Vector3Int cell, Sprite sprite)
        {
            if (sprite == null) return;
            var basePos = decorationMap.CellToWorld(cell) + CenterOffset(sprite);
            const int Count = 5;
            const float Radius = 0.35f;
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

            // Squash & stretch: starts wide+short, returns to neutral
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
