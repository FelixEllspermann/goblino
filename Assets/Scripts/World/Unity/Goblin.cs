using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RTSCL.World.Unity
{
    public sealed class Goblin : MonoBehaviour
    {
        public static readonly List<Goblin> All = new();

        public string Kind { get; private set; } = "Goblin";
        public bool IsSelected { get; private set; }

        private Sprite[] _frames;
        private Tilemap _terrainMap;
        private SpriteRenderer _renderer;
        private GameObject _selectionRing;

        private Vector3 _target;
        private float _moveSpeed = 2.0f;
        private float _frameTimer;
        private int _frameIndex;

        private const float FrameDuration = 0.18f;
        private const float TargetReachedEpsilon = 0.05f;

        public void Init(string kind, Sprite[] walkFrames, Tilemap terrainMap)
        {
            Kind = kind;
            _frames = walkFrames;
            _terrainMap = terrainMap;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = walkFrames[0];
            _target = transform.position;  // start idle

            BuildSelectionRing();
        }

        public void SetCommand(Vector3 worldTarget)
        {
            _target = worldTarget;
        }

        public void SetSelected(bool sel)
        {
            IsSelected = sel;
            if (_selectionRing != null) _selectionRing.SetActive(sel);
        }

        private void OnEnable()  => All.Add(this);
        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            if (_frames == null || _frames.Length == 0) return;

            var delta = _target - transform.position;
            bool moving = delta.sqrMagnitude > TargetReachedEpsilon * TargetReachedEpsilon;

            if (moving)
            {
                _frameTimer += Time.deltaTime;
                if (_frameTimer >= FrameDuration)
                {
                    _frameTimer = 0f;
                    _frameIndex = (_frameIndex + 1) % _frames.Length;
                    _renderer.sprite = _frames[_frameIndex];
                }

                var step = _moveSpeed * Time.deltaTime;
                if (delta.magnitude <= step)
                    transform.position = _target;
                else
                    transform.position += delta.normalized * step;

                if (Mathf.Abs(delta.x) > 0.05f)
                    _renderer.flipX = delta.x < 0f;
            }
            else
            {
                if (_frameIndex != 0)
                {
                    _frameIndex = 0;
                    _renderer.sprite = _frames[0];
                }
                _frameTimer = 0f;
            }
        }

        private void BuildSelectionRing()
        {
            _selectionRing = new GameObject("SelectionRing");
            _selectionRing.transform.SetParent(transform, false);
            _selectionRing.transform.localPosition = new Vector3(0, 0.05f, 0);
            var sr = _selectionRing.AddComponent<SpriteRenderer>();
            sr.sprite = SelectionRingSprite();
            sr.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            sr.sortingOrder = (_renderer != null ? _renderer.sortingOrder : 25) - 1;
            _selectionRing.transform.localScale = new Vector3(1.1f, 0.5f, 1f);
            _selectionRing.SetActive(false);
        }

        private static Sprite s_ring;
        private static Sprite SelectionRingSprite()
        {
            if (s_ring != null) return s_ring;
            const int size = 16;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            var pixels = new Color32[size * size];
            Vector2 center = new(size / 2f - 0.5f, size / 2f - 0.5f);
            float outerR = size / 2f;
            float innerR = outerR - 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), center);
                bool ring = d < outerR && d >= innerR;
                pixels[y * size + x] = ring ? new Color32(255, 255, 255, 255)
                                            : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            s_ring = Sprite.Create(tex, new Rect(0, 0, size, size),
                                   new Vector2(0.5f, 0.5f), 16);
            return s_ring;
        }
    }
}
