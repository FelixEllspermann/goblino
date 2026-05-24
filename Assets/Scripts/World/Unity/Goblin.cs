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

        public int  CurrentHp { get; private set; }
        public int  MaxHp { get; private set; } = 20;
        public int  PopulationCost { get; private set; } = 1;
        public int  AttackDamage { get; private set; }
        public float AttackInterval { get; private set; } = 1.5f;
        public int  AttackRange { get; private set; } = 1;

        public bool IsIdle => _state == State.Idle;

        private Sprite[] _frames;
        private Tilemap _terrainMap;
        private Tilemap _decorationMap;
        private SpriteRenderer _renderer;
        private GameObject _selectionRing;

        private enum State { Idle, MovingToPoint, MovingToTree, Harvesting, MovingToBuild, Building, MovingToAttack, Attacking, Dying }
        private State _state = State.Idle;

        private Vector3 _moveTarget;
        private Vector3Int _treeCell;
        private Vector2Int _buildOrigin;
        private float _harvestTimer;
        private float _buildTimer;
        private Goblin _attackTarget;
        private float _attackTimer;
        private const float BuildTickDuration = 1.0f;
        private const int BuildProgressPerTick = 10;

        // Headbutt anim state
        private float _hitAnimT = -1f;       // -1 = not animating
        private Vector3 _hitHomePos;
        private Vector3 _hitDir;

        // Death hop-arc state
        private Vector3 _dieStartPos;
        private Vector3 _dieVelocity;
        private const float DeathGravity = -10f;
        private const float DeathHopVelocity = 3f;

        private float _moveSpeed = 2.0f;
        private float _frameTimer;
        private int _frameIndex;

        private const float FrameDuration = 0.18f;
        private const float TargetReachedEpsilon = 0.05f;
        private const float ChopTickDuration = 2.0f;   // hit every 2s
        private const int WoodPerHit = 1;
        private const int AutoFindRadius = 12;
        private const float HitAnimDuration = 0.32f;
        private const float HitLungeAmount = 0.30f;    // world units lurch forward
        private const float HitTiltDegrees = 18f;

        public void Init(string kind, Sprite[] walkFrames, Tilemap terrainMap, Tilemap decorationMap,
                         GoblinUnitDefinition def = null)
        {
            Kind = kind;
            _frames = walkFrames;
            _terrainMap = terrainMap;
            _decorationMap = decorationMap;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = walkFrames[0];
            _moveTarget = transform.position;

            if (def != null)
            {
                MaxHp = def.MaxHp;
                PopulationCost = def.PopulationCost;
                AttackDamage = def.AttackDamage;
                AttackInterval = def.AttackInterval;
                AttackRange = def.AttackRange;
            }
            CurrentHp = MaxHp;

            BuildSelectionRing();
        }

        public void SetMoveCommand(Vector3 worldTarget)
        {
            ResetHitAnim();
            _moveTarget = worldTarget;
            _state = State.MovingToPoint;
        }

        public void SetHarvestCommand(Vector3Int treeCell)
        {
            ResetHitAnim();
            _treeCell = treeCell;
            _moveTarget = FindAdjacentStandingSpot(treeCell);
            _state = State.MovingToTree;
            _harvestTimer = 0f;
        }

        public void SetBuildCommand(Vector2Int buildingOrigin)
        {
            ResetHitAnim();
            _buildOrigin = buildingOrigin;
            // Walk to the origin cell center — close enough to "build" the structure
            _moveTarget = new Vector3(buildingOrigin.x + 0.5f, buildingOrigin.y + 0.5f, 0f);
            _state = State.MovingToBuild;
            _buildTimer = 0f;
        }

        public void SetAttackCommand(Goblin target)
        {
            if (target == null || target == this) return;
            if (AttackDamage <= 0) return;   // non-combatants (Farmers) ignore
            if (target.CurrentHp <= 0) return;
            ResetHitAnim();
            _attackTarget = target;
            _attackTimer = 0f;
            _state = State.MovingToAttack;
        }

        private static int ChebyshevDistance(Vector3 a, Vector3 b)
        {
            int dx = Mathf.Abs(Mathf.FloorToInt(a.x) - Mathf.FloorToInt(b.x));
            int dy = Mathf.Abs(Mathf.FloorToInt(a.y) - Mathf.FloorToInt(b.y));
            return Mathf.Max(dx, dy);
        }

        public void TakeDamage(int damage, Goblin attacker)
        {
            if (CurrentHp <= 0) return;
            CurrentHp = Mathf.Max(0, CurrentHp - damage);
            if (CurrentHp <= 0) { EnterDying(); return; }

            // Auto-retaliate: only when idle and capable
            if (IsIdle && AttackDamage > 0 && attacker != null && attacker.CurrentHp > 0)
                SetAttackCommand(attacker);
        }

        // Returns the world position of the passable cell adjacent to the tree closest to the goblin.
        // Falls back to the tree cell center if no adjacent cell is passable.
        private Vector3 FindAdjacentStandingSpot(Vector3Int treeCell)
        {
            Vector3 goblinPos = transform.position;
            Vector3Int[] offsets = {
                new( 1, 0, 0), new(-1, 0, 0), new( 0, 1, 0), new( 0,-1, 0),
                new( 1, 1, 0), new( 1,-1, 0), new(-1, 1, 0), new(-1,-1, 0),
            };
            Vector3 best = CellCenter(treeCell);
            float bestDist = float.MaxValue;
            bool found = false;
            foreach (var off in offsets)
            {
                var nc = treeCell + off;
                if (!IsTerrainPassable(nc)) continue;
                Vector3 p = CellCenter(nc);
                float d = (p - goblinPos).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = p; found = true; }
            }
            return found ? best : CellCenter(treeCell);
        }

        private bool IsTerrainPassable(Vector3Int cell)
        {
            if (_terrainMap == null) return true;
            var t = _terrainMap.GetTile(cell);
            if (t == null) return false;
            var n = t.name;
            return n != "DeepWater" && n != "Cliff";
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
                        ResetHitAnim();
                        FindNextTreeOrIdle();
                        break;
                    }
                    _harvestTimer += Time.deltaTime;
                    if (_harvestTimer >= ChopTickDuration)
                    {
                        _harvestTimer = 0f;
                        HitTree(_treeCell);
                    }
                    UpdateHitAnim();
                    break;
                }

                case State.MovingToBuild:
                {
                    if (!BuildingConstruction.IsUnderConstruction(_buildOrigin))
                    {
                        _state = State.Idle;
                        break;
                    }
                    if (StepToward(_moveTarget))
                    {
                        _state = State.Building;
                        _buildTimer = 0f;
                    }
                    break;
                }

                case State.Building:
                {
                    ShowIdleFrame();
                    if (!BuildingConstruction.IsUnderConstruction(_buildOrigin))
                    {
                        ResetHitAnim();
                        _state = State.Idle;
                        break;
                    }
                    _buildTimer += Time.deltaTime;
                    if (_buildTimer >= BuildTickDuration)
                    {
                        _buildTimer = 0f;
                        BuildingConstruction.AddProgress(_buildOrigin, BuildProgressPerTick, out _);
                        StartHitAnim(new Vector3Int(_buildOrigin.x, _buildOrigin.y, 0));
                    }
                    UpdateHitAnim();
                    break;
                }

                case State.MovingToAttack:
                {
                    if (_attackTarget == null || _attackTarget.CurrentHp <= 0) { _state = State.Idle; break; }
                    if (ChebyshevDistance(transform.position, _attackTarget.transform.position) <= AttackRange)
                    {
                        _state = State.Attacking;
                        _attackTimer = AttackInterval; // first hit immediately
                        break;
                    }
                    StepToward(_attackTarget.transform.position);
                    break;
                }

                case State.Attacking:
                {
                    if (_attackTarget == null || _attackTarget.CurrentHp <= 0) { _state = State.Idle; break; }
                    if (ChebyshevDistance(transform.position, _attackTarget.transform.position) > AttackRange)
                    {
                        _state = State.MovingToAttack;
                        break;
                    }
                    _attackTimer += Time.deltaTime;
                    if (_attackTimer >= AttackInterval)
                    {
                        _attackTimer = 0f;
                        _attackTarget.TakeDamage(AttackDamage, this);
                        StartHitAnim(new Vector3Int(
                            Mathf.FloorToInt(_attackTarget.transform.position.x),
                            Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
                    }
                    UpdateHitAnim();
                    break;
                }

                case State.Dying:
                    _dieVelocity.y += DeathGravity * Time.deltaTime;
                    transform.position += _dieVelocity * Time.deltaTime;
                    if (transform.position.y <= _dieStartPos.y && _dieVelocity.y <= 0f)
                    {
                        // Snap to ground, destroy. Burst comes in Task 9.
                        transform.position = _dieStartPos;
                        Destroy(gameObject);
                    }
                    break;
            }
        }

        private void HitTree(Vector3Int cell)
        {
            var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
            var sprite = tile != null ? tile.sprite : null;

            int remaining = TreeHP.Hit(cell, 1);
            ResourceBank.AddWood(WoodPerHit);
            StartHitAnim(cell);
            TreeHitEffect.Spawn(_decorationMap, cell, sprite);

            if (remaining <= 0)
            {
                _decorationMap.SetTile(cell, null);
                // FindNext on next frame via the IsTreeStillThere check
            }
        }

        private void StartHitAnim(Vector3Int cell)
        {
            _hitAnimT = 0f;
            _hitHomePos = transform.position;
            Vector3 treeWorld = CellCenter(cell);
            _hitDir = (treeWorld - _hitHomePos).sqrMagnitude > 0.0001f
                ? (treeWorld - _hitHomePos).normalized
                : Vector3.up;
        }

        private void UpdateHitAnim()
        {
            if (_hitAnimT < 0f) return;
            _hitAnimT += Time.deltaTime / HitAnimDuration;
            if (_hitAnimT >= 1f)
            {
                ResetHitAnim();
                return;
            }
            float sin = Mathf.Sin(_hitAnimT * Mathf.PI);
            transform.position = _hitHomePos + _hitDir * (HitLungeAmount * sin);
            float tilt = sin * HitTiltDegrees * (_hitDir.x >= 0f ? -1f : 1f);
            transform.rotation = Quaternion.Euler(0f, 0f, tilt);
        }

        private void ResetHitAnim()
        {
            if (_hitAnimT >= 0f)
            {
                transform.position = _hitHomePos;
                transform.rotation = Quaternion.identity;
            }
            _hitAnimT = -1f;
        }

        private void EnterDying()
        {
            if (_state == State.Dying) return;
            _state = State.Dying;
            PopulationManager.RemoveUsed(PopulationCost);
            Goblin.All.Remove(this);
            SetSelected(false);

            _dieStartPos = transform.position;
            _dieVelocity = new Vector3(0f, DeathHopVelocity, 0f);
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
