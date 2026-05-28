using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using RTSCL.World;

namespace RTSCL.World.Unity
{
    public sealed class Goblin : MonoBehaviour
    {
        public static readonly List<Goblin> All = new();

        public string Kind { get; private set; } = "Goblin";
        public bool IsSelected { get; private set; }

        public GoblinNetId NetId { get; private set; }

        public ulong Owner { get; private set; }
        public void SetOwner(ulong ownerSteamId)
        {
            Owner = ownerSteamId;
            ApplyOwnerVisuals();
            RefreshSelectionRingColor();
        }

        public int  CurrentHp { get; private set; }
        public int  MaxHp { get; private set; } = 20;
        public int  PopulationCost { get; private set; } = 1;
        public int  AttackDamage { get; private set; }
        public float AttackInterval { get; private set; } = 1.5f;
        public int  AttackRange { get; private set; } = 1;

        public void SetAttackDamage(int newDamage) => AttackDamage = Mathf.Max(0, newDamage);

        public void SetMaxHp(int newMax, int newCurrent)
        {
            MaxHp = Mathf.Max(1, newMax);
            CurrentHp = Mathf.Clamp(newCurrent, 0, MaxHp);
        }

        public bool IsIdle => _state == State.Idle;

        private Sprite[] _frames;
        private Tilemap _terrainMap;
        private Tilemap _decorationMap;
        private SpriteRenderer _renderer;
        private GameObject _selectionRing;

        private enum State { Idle, MovingToPoint, MovingToTree, Harvesting, WalkingToDeposit, MovingToBuild, Building, MovingToAttack, Attacking, Dying }
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
        private const float DeathGravity = -20f;
        private const float DeathHopVelocity = 10f;

        private float _moveSpeed = 2.0f;
        private float _frameTimer;
        private int _frameIndex;

        private const float FrameDuration = 0.18f;
        private const float TargetReachedEpsilon = 0.05f;
        private float _chopTickDuration = 2.0f;
        public float HarvestSpeedMul { get; private set; } = 1f;
        public void SetHarvestSpeedMul(float mul)
        {
            HarvestSpeedMul = Mathf.Max(0.01f, mul);
            _chopTickDuration = 2.0f / HarvestSpeedMul;
        }
        private const int WoodPerHit = 1;
        private const int MaxCarriedWood = 10;
        public int CarriedWood { get; private set; }
        private const int AutoFindRadius = 12;
        private const float HitAnimDuration = 0.32f;
        private const float HitLungeAmount = 0.30f;    // world units lurch forward
        private const float HitTiltDegrees = 18f;

        public void Init(GoblinNetId netId, string kind, Sprite[] walkFrames, Tilemap terrainMap, Tilemap decorationMap,
                         GoblinUnitDefinition def = null)
        {
            NetId = netId;
            GoblinNetRegistry.Register(netId, this);

            Kind = kind;
            _frames = walkFrames;
            _terrainMap = terrainMap;
            _decorationMap = decorationMap;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.sprite = walkFrames[0];
            ApplyOwnerVisuals();
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
            GoblinHealthBar.AttachTo(this);
            GoblinCarryText.AttachTo(this);
        }

        public void SetMoveCommand(Vector3 worldTarget)
        {
            if (_state == State.Dying) return;
            ResetHitAnim();
            _moveTarget = worldTarget;
            _state = State.MovingToPoint;
        }

        public void SetHarvestCommand(Vector3Int treeCell)
        {
            if (_state == State.Dying) return;
            ResetHitAnim();
            _treeCell = treeCell;
            _moveTarget = FindAdjacentStandingSpot(treeCell);
            _state = State.MovingToTree;
            _harvestTimer = 0f;
        }

        public void SetBuildCommand(Vector2Int buildingOrigin)
        {
            if (_state == State.Dying) return;
            ResetHitAnim();
            _buildOrigin = buildingOrigin;
            // Walk to the origin cell center — close enough to "build" the structure
            _moveTarget = new Vector3(buildingOrigin.x + 0.5f, buildingOrigin.y + 0.5f, 0f);
            _state = State.MovingToBuild;
            _buildTimer = 0f;
        }

        public void SetAttackCommand(Goblin target)
        {
            if (_state == State.Dying) return;
            if (target == null || target == this) return;
            if (AttackDamage <= 0) return;   // non-combatants (Farmers) ignore
            if (target.CurrentHp <= 0) return;
            ResetHitAnim();
            _attackTarget = target;
            _attackTimer = 0f;
            _state = State.MovingToAttack;
        }

        private bool IsLocalOwner =>
            Owner == WorldStartContext.LocalPlayer || Owner == 0UL;

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
        private void OnDisable()
        {
            All.Remove(this);
            GoblinNetRegistry.Unregister(NetId);
        }

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
                    if (!IsHarvestableStillThere(_treeCell))
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
                    if (!IsLocalOwner) break;     // remote clients skip the chop tick — owner authorities harvest

                    if (!IsHarvestableStillThere(_treeCell))
                    {
                        ResetHitAnim();
                        if (CarriedWood > 0) TryStartDepositRun();
                        else FindNextTreeOrIdle();
                        break;
                    }
                    _harvestTimer += Time.deltaTime;
                    if (_harvestTimer >= _chopTickDuration)
                    {
                        _harvestTimer = 0f;
                        bool destroyed = HitTree(_treeCell);
                        if (CarriedWood >= MaxCarriedWood || destroyed)
                            TryStartDepositRun();
                    }
                    break;
                }

                case State.WalkingToDeposit:
                {
                    if (!IsLocalOwner) break;     // remotes are in MovingToPoint via ApplyMove; their walk handles itself
                    if (StepToward(_moveTarget))
                    {
                        // Arrived at keep — deposit.
                        ResourceBank.AddWood(CarriedWood);
                        CarriedWood = 0;

                        // Resume: walk back to last tree if still alive, else find nearest, else idle.
                        if (IsHarvestableStillThere(_treeCell))
                        {
                            _moveTarget = FindAdjacentStandingSpot(_treeCell);
                            _state = State.MovingToTree;
                            SendMoveWireOnly(_moveTarget);
                        }
                        else
                        {
                            FindNextTreeOrIdle();
                            // If FindNextTreeOrIdle picked a tree (state changed to MovingToTree), mirror the move.
                            if (_state == State.MovingToTree) SendMoveWireOnly(_moveTarget);
                        }
                    }
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
                        // Hit animation runs on every client (deterministic). Damage only fires
                        // on the attacker's owner client, which broadcasts EvDamage to remotes.
                        StartHitAnim(new Vector3Int(
                            Mathf.FloorToInt(_attackTarget.transform.position.x),
                            Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
                        if (IsLocalOwner)
                            NetCommandIssuer.IssueDamage(_attackTarget, AttackDamage, this);
                    }
                    break;
                }

                case State.Dying:
                    _dieVelocity.y += DeathGravity * Time.deltaTime;
                    transform.position += _dieVelocity * Time.deltaTime;
                    if (transform.position.y <= _dieStartPos.y && _dieVelocity.y <= 0f)
                    {
                        // Snap to ground, spawn burst, destroy.
                        transform.position = _dieStartPos;
                        DeathBurst.Spawn(transform.position);
                        Destroy(gameObject);
                    }
                    break;
            }

            // Hit animations (chop / build / attack) keep playing across state transitions
            // so the killing blow's lunge finishes after the target dies. Dying state owns
            // the transform itself, so we skip there.
            if (_state != State.Dying) UpdateHitAnim();
        }

        /// <summary>Apply one chop to the given tree cell. Returns true if the tree was destroyed by this hit.</summary>
        private bool HitTree(Vector3Int cell)
        {
            var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
            var sprite = tile != null ? tile.sprite : null;

            int remaining = TreeHP.Hit(cell, 1);
            CarriedWood = Mathf.Min(MaxCarriedWood, CarriedWood + WoodPerHit);
            StartHitAnim(cell);
            TreeHitEffect.Spawn(_decorationMap, cell, sprite);

            if (remaining <= 0)
            {
                _decorationMap.SetTile(cell, null);
                return true;
            }
            return false;
        }

        /// <summary>Find the owner's nearest Keep, set move target to its edge, transition to WalkingToDeposit,
        /// and broadcast a CmdMove so remotes mirror the walk. Falls back to Idle (inventory preserved) if no
        /// Keep exists.</summary>
        private void TryStartDepositRun()
        {
            if (NetCommandApplier.Placer == null)
            {
                _state = State.Idle;
                return;
            }
            var here = new Vector2Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y));
            if (!NetCommandApplier.Placer.TryFindNearestBuildingByName("Keep_0", Owner, here, out var keepOrigin))
            {
                _state = State.Idle;
                return;
            }

            // Stand one cell to the SW of the keep origin (Keep is 2x2; origin is the SW corner).
            Vector3 target = new Vector3(keepOrigin.x - 0.5f, keepOrigin.y + 0.5f, 0f);

            ResetHitAnim();
            _moveTarget = target;
            _state = State.WalkingToDeposit;
            SendMoveWireOnly(target);
        }

        /// <summary>Broadcast a single-unit CmdMove without touching local state (the owner's FSM state
        /// is already set; remotes apply via the stock ApplyMove → SetMoveCommand path).</summary>
        private void SendMoveWireOnly(Vector3 target)
        {
            var wire = new System.Collections.Generic.List<NetWireFormat.WireNetIdLocal>(1)
            {
                new NetWireFormat.WireNetIdLocal(NetId.Owner, NetId.LocalIndex)
            };
            NetCommandBridge.Send(NetWireFormat.PackCmdMove(wire, target.x, target.y));
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

        private void ApplyOwnerVisuals()
        {
            if (_renderer == null) return;
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            _renderer.color = isLocal ? Color.white : WorldStartContext.GetPlayerColor(Owner);
        }

        private void EnterDying()
        {
            if (_state == State.Dying) return;
            _state = State.Dying;
            PopulationManager.RemoveUsed(PopulationCost);
            Goblin.All.Remove(this);
            SetSelected(false);
            // Clear any in-flight lunge so it doesn't fight the hop-arc transform writes.
            ResetHitAnim();

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

        private bool IsHarvestableStillThere(Vector3Int cell)
        {
            if (_decorationMap == null) return false;
            var t = _decorationMap.GetTile(cell);
            return t != null && IsHarvestable(t.name);
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
                if (!IsHarvestableStillThere(c)) continue;
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

        public static bool IsWheatfieldTile(string tileName) =>
            tileName.StartsWith("Wheatfield_");

        public static bool IsHarvestable(string tileName) =>
            IsTreeTile(tileName) || IsWheatfieldTile(tileName);

        private static Vector3 CellCenter(Vector3Int cell) =>
            new(cell.x + 0.5f, cell.y + 0.5f, 0f);

        private void BuildSelectionRing()
        {
            _selectionRing = new GameObject("SelectionRing");
            _selectionRing.transform.SetParent(transform, false);
            _selectionRing.transform.localPosition = new Vector3(0, 0.05f, 0);
            var sr = _selectionRing.AddComponent<SpriteRenderer>();
            sr.sprite = SelectionRingSprite();
            sr.sortingOrder = (_renderer != null ? _renderer.sortingOrder : 25) - 1;
            _selectionRing.transform.localScale = new Vector3(1.1f, 0.5f, 1f);
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            sr.color = isLocal
                ? new Color(0.95f, 0.95f, 0.95f, 1f)
                : WorldStartContext.GetPlayerColor(Owner);
            _selectionRing.SetActive(false);
        }

        private void RefreshSelectionRingColor()
        {
            if (_selectionRing == null) return;
            var sr = _selectionRing.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            sr.color = isLocal
                ? new Color(0.95f, 0.95f, 0.95f, 1f)
                : WorldStartContext.GetPlayerColor(Owner);
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
