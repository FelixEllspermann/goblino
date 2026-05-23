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
        private Tilemap _decorationMap;
        private SpriteRenderer _renderer;
        private GameObject _selectionRing;

        private enum State { Idle, MovingToPoint, MovingToTree, Harvesting }
        private State _state = State.Idle;

        private Vector3 _moveTarget;
        private Vector3Int _treeCell;
        private float _harvestTimer;

        private float _moveSpeed = 2.0f;
        private float _frameTimer;
        private int _frameIndex;

        private const float FrameDuration = 0.18f;
        private const float TargetReachedEpsilon = 0.05f;
        private const float ChopDuration = 2.0f;
        private const int WoodPerTree = 5;
        private const int AutoFindRadius = 12;

        public void Init(string kind, Sprite[] walkFrames, Tilemap terrainMap, Tilemap decorationMap)
        {
            Kind = kind;
            _frames = walkFrames;
            _terrainMap = terrainMap;
            _decorationMap = decorationMap;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = walkFrames[0];
            _moveTarget = transform.position;
            BuildSelectionRing();
        }

        public void SetMoveCommand(Vector3 worldTarget)
        {
            _moveTarget = worldTarget;
            _state = State.MovingToPoint;
        }

        public void SetHarvestCommand(Vector3Int treeCell)
        {
            _treeCell = treeCell;
            _moveTarget = CellCenter(treeCell);
            _state = State.MovingToTree;
            _harvestTimer = 0f;
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

            switch (_state)
            {
                case State.Idle:
                    ShowIdleFrame();
                    break;

                case State.MovingToPoint:
                    if (StepToward(_moveTarget)) _state = State.Idle;
                    break;

                case State.MovingToTree:
                {
                    if (!IsTreeStillThere(_treeCell))
                    {
                        FindNextTreeOrIdle();
                        break;
                    }
                    if (StepToward(_moveTarget))
                    {
                        _state = State.Harvesting;
                        _harvestTimer = 0f;
                    }
                    break;
                }

                case State.Harvesting:
                {
                    ShowIdleFrame();
                    if (!IsTreeStillThere(_treeCell))
                    {
                        FindNextTreeOrIdle();
                        break;
                    }
                    _harvestTimer += Time.deltaTime;
                    if (_harvestTimer >= ChopDuration)
                    {
                        ChopTree(_treeCell);
                        FindNextTreeOrIdle();
                    }
                    break;
                }
            }
        }

        /// <summary>Returns true when target is reached.</summary>
        private bool StepToward(Vector3 target)
        {
            var delta = target - transform.position;
            if (delta.sqrMagnitude <= TargetReachedEpsilon * TargetReachedEpsilon)
                return true;

            _frameTimer += Time.deltaTime;
            if (_frameTimer >= FrameDuration)
            {
                _frameTimer = 0f;
                _frameIndex = (_frameIndex + 1) % _frames.Length;
                _renderer.sprite = _frames[_frameIndex];
            }

            float step = _moveSpeed * Time.deltaTime;
            if (delta.magnitude <= step) transform.position = target;
            else transform.position += delta.normalized * step;

            if (Mathf.Abs(delta.x) > 0.05f) _renderer.flipX = delta.x < 0f;
            return false;
        }

        private void ShowIdleFrame()
        {
            if (_frameIndex != 0)
            {
                _frameIndex = 0;
                _renderer.sprite = _frames[0];
            }
            _frameTimer = 0f;
        }

        private bool IsTreeStillThere(Vector3Int cell)
        {
            if (_decorationMap == null) return false;
            var t = _decorationMap.GetTile(cell);
            return t != null && IsTreeTile(t.name);
        }

        private void ChopTree(Vector3Int cell)
        {
            _decorationMap.SetTile(cell, null);
            ResourceBank.AddWood(WoodPerTree);
        }

        private void FindNextTreeOrIdle()
        {
            if (_decorationMap == null) { _state = State.Idle; return; }

            Vector3Int origin = _decorationMap.WorldToCell(transform.position);
            Vector3Int? best = null;
            int bestSq = int.MaxValue;
            for (int dy = -AutoFindRadius; dy <= AutoFindRadius; dy++)
            for (int dx = -AutoFindRadius; dx <= AutoFindRadius; dx++)
            {
                var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
                if (!IsTreeStillThere(c)) continue;
                int sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; best = c; }
            }

            if (best.HasValue) SetHarvestCommand(best.Value);
            else _state = State.Idle;
        }

        public static bool IsTreeTile(string tileName)
        {
            return tileName.StartsWith("Trees_")
                || tileName.StartsWith("PineTrees_")
                || tileName.StartsWith("WinterTrees_")
                || tileName.StartsWith("WinterDeadTrees_")
                || tileName.StartsWith("DeadTrees_")
                || tileName.StartsWith("CoconutTrees_");
        }

        private static Vector3 CellCenter(Vector3Int cell) =>
            new(cell.x + 0.5f, cell.y + 0.5f, 0f);

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
