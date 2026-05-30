// =============================================================================
// Goblin.cs  —  RTSCL.World.Unity
//
// The core unit MonoBehaviour. Owns a finite state machine with 10 states
// (Idle → Move / Harvest / Build / Attack / Dying paths). Movement is A*
// via Pathfinder.FindPath + a cell-by-cell path list. Harvest fans out via
// HarvestReservations; damage authority lives on the attacker-owner client
// and is broadcast as EvDamage via NetCommandIssuer.IssueDamage.
//
// To add a new command type: (1) add a value to CommandType + GoblinCommand,
//   (2) add a SetXCommand entry point, (3) add the two State values for
//   moving and executing, (4) handle them in Update's switch, (5) add a case
//   to ActivateNextQueued. To tune movement/combat: see _moveSpeed,
//   AttackInterval, _chopTickDuration, BuildTickDuration, and their defaults.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using RTSCL.World;
using Unity.Mathematics;

namespace RTSCL.World.Unity
{
    public sealed class Goblin : MonoBehaviour
    {
        /// <summary>Global list of every live Goblin in the scene, updated via OnEnable/OnDisable.</summary>
        public static readonly List<Goblin> All = new();

        /// <summary>Asset name of this unit type (e.g. "FarmerGoblin", "ClubGoblin"). Set during Init.</summary>
        public string Kind { get; private set; } = "Goblin";
        public bool IsSelected { get; private set; }

        /// <summary>Network identity: (OwnerSteamId, per-owner sequential index). Assigned at spawn and
        /// registered in GoblinNetRegistry for cross-client lookup.</summary>
        public GoblinNetId NetId { get; private set; }

        /// <summary>Steam ID of the player that owns this unit. 0UL in solo play.</summary>
        public ulong Owner { get; private set; }
        /// <summary>Assign ownership after spawn (used by NetCommandApplier for remote units).
        /// Triggers faction tint + selection ring colour update.</summary>
        public void SetOwner(ulong ownerSteamId)
        {
            Owner = ownerSteamId;
            ApplyOwnerVisuals();
            RefreshSelectionRingColor();
        }

        public int  CurrentHp { get; private set; }
        public int  MaxHp { get; private set; } = 20;
        /// <summary>Population slots consumed when this unit is alive. Freed on death in EnterDying.</summary>
        public int  PopulationCost { get; private set; } = 1;
        /// <summary>Damage dealt per attack swing. 0 = non-combatant (Farmers ignore attack commands).</summary>
        public int  AttackDamage { get; private set; }
        /// <summary>Seconds between attack swings. Tune per-unit via GoblinUnitDefinition.</summary>
        public float AttackInterval { get; private set; } = 1.5f;
        /// <summary>Chebyshev-distance threshold to trigger/maintain Attacking state.</summary>
        public int  AttackRange { get; private set; } = 1;

        public void SetAttackDamage(int newDamage) => AttackDamage = Mathf.Max(0, newDamage);

        /// <summary>Override HP from network-received spawn data so all clients start in sync.</summary>
        public void SetMaxHp(int newMax, int newCurrent)
        {
            MaxHp = Mathf.Max(1, newMax);
            CurrentHp = Mathf.Clamp(newCurrent, 0, MaxHp);
        }

        public bool IsIdle => _state == State.Idle;

        /// <summary>Neutral creatures (monsters): no faction tint, not player-selectable, no population cost.</summary>
        public bool IsNeutral { get; private set; }
        /// <summary>True on the client that authoritatively controls this unit (its owner, or solo).
        /// Public mirror of the private IsLocalOwner so MonsterAI can gate its decision logic.</summary>
        public bool IsOwnedLocally => WorldStartContext.IsSolo || Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
        /// <summary>Mark this unit as a neutral monster (call right after spawn). Refreshes visuals.</summary>
        public void MarkNeutral() { IsNeutral = true; ApplyOwnerVisuals(); RefreshSelectionRingColor(); }

        /// <summary>True if <paramref name="other"/> is a valid hostile target (no friendly fire):
        /// monsters fight any non-monster; players fight only across different owners; same owner
        /// (and monster-vs-monster) is friendly.</summary>
        public bool IsHostileTo(Goblin other)
        {
            if (other == null || other == this) return false;
            if (IsNeutral && other.IsNeutral) return false;      // monsters don't fight each other
            if (IsNeutral || other.IsNeutral) return true;       // monster vs player = hostile
            return Owner != other.Owner;                          // players: only across factions
        }

        /// <summary>Discriminator for owner-local queued commands. Remotes never enqueue.</summary>
        public enum CommandType { Move, Harvest, BuildAssist, Attack }

        /// <summary>A single queued command. Only the field matching the CommandType is meaningful;
        /// others are default. Stored in _commandQueue and drained by ActivateNextQueued.</summary>
        public struct GoblinCommand
        {
            public CommandType Type;
            public Vector3 Point;       // Move
            public Vector3Int Cell;     // Harvest
            public Vector2Int Origin;   // BuildAssist
            public Goblin Target;       // Attack
        }

        // Owner-local command queue. Populated by GoblinSelectionController (Shift+right-click).
        // Remotes never touch this queue; they receive each step as a net command directly.
        private readonly List<GoblinCommand> _commandQueue = new();

        public void EnqueueCommand(GoblinCommand c) => _commandQueue.Add(c);
        public void ClearQueue() => _commandQueue.Clear();

        private Sprite[] _frames;
        private Tilemap _terrainMap;
        private Tilemap _decorationMap;
        private SpriteRenderer _renderer;
        private GameObject _selectionRing;

        // FSM state. Transitions are driven entirely inside Update; public Set*Command methods
        // only set up the parameters then change state — they do not tick logic themselves.
        private enum State { Idle, MovingToPoint, MovingToTree, Harvesting, WalkingToDeposit, MovingToBuild, Building, MovingToAttack, Attacking, MovingToAttackBuilding, AttackingBuilding, MovingToBoard, MovingToUnload, Dying }
        private State _state = State.Idle;

        private Vector2Int _buildingTarget;   // origin of the enemy building being attacked
        private bool _hasBuildingTarget;

        // Boat transport: boats carry passengers; land units board a boat.
        private const int BoatCapacity = 6;
        private readonly List<Goblin> _passengers = new();   // boats only
        private Goblin _boardBoat;                            // land unit's target boat while boarding
        private Vector3 _unloadTarget;                        // boat's requested unload land point

        /// <summary>True if this unit is a boat (water transport).</summary>
        public bool IsBoat => _waterMode;
        /// <summary>World-space AABB of the rendered sprite, padded a little, for click-selection.
        /// Centered on the actual sprite (correct even when the pivot is at the feet) and inflated so
        /// the unit is easy to click — large/offset sprites (boats, monsters) are fully pickable.</summary>
        public Bounds SelectionBounds
        {
            get
            {
                var b = _renderer != null ? _renderer.bounds
                                          : new Bounds(transform.position, new Vector3(0.5f, 0.5f, 0f));
                b.Expand(0.4f);   // ~0.2 world units of slack on each side
                return b;
            }
        }
        /// <summary>True if a boat still has room for more passengers.</summary>
        public bool BoatHasRoom => _waterMode && _passengers.Count < BoatCapacity;
        /// <summary>Number of units currently aboard this boat.</summary>
        public int PassengerCount => _passengers.Count;

        private Vector3 _moveTarget;
        private readonly List<Vector3> _path = new();   // A* waypoints in world space
        private int _pathIndex;
        // Cached cell of the attack target's position; used to detect when target has moved
        // enough to warrant re-pathing (avoids per-frame RepathTo calls).
        private Vector2Int _lastAttackGoalCell = new Vector2Int(int.MinValue, int.MinValue);
        private Vector3Int _treeCell;
        private ResourceKind _harvestKind;   // kind of the node currently/last harvested — auto-find sticks to it
        private Vector2Int _buildOrigin;
        private float _harvestTimer;
        private float _buildTimer;
        private Goblin _attackTarget;
        private float _attackTimer;
        private Sprite _projectileSprite;   // non-null = ranged unit (shoots an Arrow instead of a melee lunge)
        private HitFeedback _hitFeedback;    // white flash + wobble + red spritz when hit
        private bool _waterMode;             // true = boat: moves on water only (inverted passability)
        /// <summary>Seconds between build progress ticks. Reduce to make builders work faster.</summary>
        private const float BuildTickDuration = 1.0f;
        /// <summary>HP progress added to a building per build tick. Tune alongside BuildTickDuration.</summary>
        private const int BuildProgressPerTick = 10;

        // Headbutt anim state: a short lunge-and-tilt played for chop/build/attack.
        private float _hitAnimT = -1f;       // -1 = not animating; 0–1 = progress through lunge
        private Vector3 _hitHomePos;         // world position at lunge start; restored on reset
        private Vector3 _hitDir;             // normalised direction toward target cell

        // Death hop-arc state: parabolic trajectory played in the Dying state until landing.
        private Vector3 _dieStartPos;
        private Vector3 _dieVelocity;
        /// <summary>Downward acceleration during the death hop-arc (world units/s²). More negative = faster fall.</summary>
        private const float DeathGravity = -20f;
        /// <summary>Initial upward velocity of the death hop. Increase to make goblins arc higher on death.</summary>
        private const float DeathHopVelocity = 10f;

        private float _moveSpeed = 2.0f;
        private float _frameTimer;
        private int _frameIndex;

        /// <summary>Seconds per walk animation frame. Lower = faster leg cycle.</summary>
        private const float FrameDuration = 0.18f;
        /// <summary>Squared-distance threshold below which a waypoint is considered reached.</summary>
        private const float TargetReachedEpsilon = 0.05f;
        // Base chop duration; overridden by HarvestSpeedMul (e.g. Farmer Upgrade).
        private float _chopTickDuration = 2.0f;
        public float HarvestSpeedMul { get; private set; } = 1f;
        /// <summary>Scale the harvest tick rate. Kept in sync with _chopTickDuration = 2.0 / mul.</summary>
        public void SetHarvestSpeedMul(float mul)
        {
            HarvestSpeedMul = Mathf.Max(0.01f, mul);
            _chopTickDuration = 2.0f / HarvestSpeedMul;
        }
        /// <summary>Maximum resource units a goblin can carry before returning to deposit.</summary>
        private const int MaxCarried = 10;
        /// <summary>What resource kind is currently in the carry slot (Wood, Food, Stone, …).</summary>
        public ResourceKind CarriedKind { get; private set; }
        /// <summary>Current amount carried. Deposited in full to ResourceBank on reaching the Keep.</summary>
        public int CarriedAmount { get; private set; }
        /// <summary>Tile search radius used by FindNextTreeOrIdle when current node is depleted.</summary>
        private const int AutoFindRadius = 12;
        private const float HitAnimDuration = 0.32f;
        private const float HitLungeAmount = 0.30f;    // world units lurch forward
        private const float HitTiltDegrees = 18f;

        /// <summary>One-shot initialiser called by GoblinSpawner.SpawnAt immediately after AddComponent.
        /// Registers the unit with GoblinNetRegistry, wires references, and reads stats from the
        /// GoblinUnitDefinition asset (if provided). Must be called before any state is accessed.</summary>
        /// <param name="netId">Network identity; unique across all clients.</param>
        /// <param name="kind">Asset name string identifying the unit type.</param>
        /// <param name="walkFrames">Sprite sheet walk cycle frames.</param>
        /// <param name="def">Optional SO with HP/damage/range/pop stats. Null → uses field defaults.</param>
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
                _projectileSprite = def.ProjectileSprite;
                _waterMode = def.WaterUnit;
                transform.localScale = Vector3.one * Mathf.Max(0.1f, def.WorldScale);
            }
            CurrentHp = MaxHp;

            BuildSelectionRing();
            GoblinHealthBar.AttachTo(this);
            _hitFeedback = gameObject.AddComponent<HitFeedback>();
        }

        /// <summary>Command this unit to walk to a world position. Cancels any harvest reservation.
        /// Called by NetCommandApplier.ApplyMove on every client.</summary>
        public void SetMoveCommand(Vector3 worldTarget)
        {
            if (_state == State.Dying) return;
            HarvestReservations.Release(this);
            ResetHitAnim();
            RepathTo(worldTarget);
            // No path to a non-trivial target → can't go there: flag it and stay put.
            if (_path.Count == 0 && (worldTarget - transform.position).sqrMagnitude > 1f)
                UnitAlert.Show(this);
            _state = State.MovingToPoint;
        }

        /// <summary>Command this unit to harvest the decoration tile at treeCell.
        /// If already carrying a different resource kind, deposits first then resumes.
        /// Reserves a standing cell via HarvestReservations so multiple farmers fan out.</summary>
        public void SetHarvestCommand(Vector3Int treeCell)
        {
            if (_state == State.Dying) return;
            ResetHitAnim();

            // Mutual exclusion: if carrying a different resource kind, deposit first then resume to this cell.
            var tile = _decorationMap != null ? _decorationMap.GetTile(treeCell) : null;
            if (tile != null && CarriedAmount > 0 && KindOf(tile.name) != CarriedKind)
            {
                HarvestReservations.Release(this);
                _treeCell = treeCell;
                TryStartDepositRun();    // sets _state = WalkingToDeposit and broadcasts move; resume targets _treeCell
                return;
            }

            TryBeginHarvest(treeCell);
        }

        /// <summary>Reserve a standing cell on <paramref name="node"/> and start walking to harvest it.
        /// If every adjacent cell on that node is already taken (no stacking allowed), look within
        /// <see cref="HarvestOverflowRadius"/> for the nearest node of the SAME resource kind that
        /// still has a free spot and harvest there instead. If nothing is available, the unit stays
        /// put (Idle) and shows a red "!" alert. Returns true if a harvest target was started.</summary>
        private bool TryBeginHarvest(Vector3Int node)
        {
            HarvestReservations.Release(this);

            var tile = _decorationMap != null ? _decorationMap.GetTile(node) : null;
            if (tile == null || !IsHarvestable(tile.name)) { _state = State.Idle; UnitAlert.Show(this); return false; }
            // Remember the kind so that after this node depletes, auto-find only seeks the SAME resource.
            _harvestKind = KindOf(tile.name);

            // If this node has no free standing cell, find an alternative of the same kind nearby.
            if (!HarvestReservations.HasFreeSpot(node, IsCellPassable))
            {
                if (!TryFindAlternateNode(node, KindOf(tile.name), HarvestOverflowRadius, out node))
                {
                    _state = State.Idle;     // can't harvest anywhere reachable — don't stack, just stop
                    UnitAlert.Show(this);
                    return false;
                }
            }

            _treeCell = node;
            var spot = HarvestReservations.Reserve(node, this, IsCellPassable, transform.position);
            RepathTo(new Vector3(spot.x + 0.5f, spot.y + 0.5f, 0f));
            _state = State.MovingToTree;
            _harvestTimer = 0f;
            return true;
        }

        /// <summary>Radius (in cells) searched for an alternative node of the same kind when the
        /// requested node is full. Beyond this, the unit gives up rather than stacking.</summary>
        private const int HarvestOverflowRadius = 10;

        /// <summary>Find the nearest harvestable cell within <paramref name="radius"/> of
        /// <paramref name="origin"/> that is the same <paramref name="kind"/> AND still has a free
        /// standing cell (so no stacking). Returns false if none qualifies.</summary>
        private bool TryFindAlternateNode(Vector3Int origin, ResourceKind kind, int radius, out Vector3Int found)
        {
            found = default;
            if (_decorationMap == null) return false;
            int bestSq = int.MaxValue;
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
                var t = _decorationMap.GetTile(c);
                if (t == null || !IsHarvestable(t.name) || KindOf(t.name) != kind) continue;
                if (!HarvestReservations.HasFreeSpot(c, IsCellPassable)) continue;
                int sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; found = c; }
            }
            return bestSq != int.MaxValue;
        }

        /// <summary>True if this unit is currently walking to or constructing the building at origin.
        /// Used by the bot AI to avoid double-assigning builders.</summary>
        public bool IsBuildingAt(Vector2Int origin) =>
            (_state == State.MovingToBuild || _state == State.Building) && _buildOrigin == origin;

        /// <summary>Command this unit to walk to a building under construction and hammer on it.
        /// Releases any harvest reservation. Called by NetCommandApplier.ApplyBuildAssist.</summary>
        public void SetBuildCommand(Vector2Int buildingOrigin)
        {
            if (_state == State.Dying) return;
            HarvestReservations.Release(this);
            ResetHitAnim();
            _buildOrigin = buildingOrigin;
            // Walk to the origin cell center — close enough to "build" the structure
            RepathTo(new Vector3(buildingOrigin.x + 0.5f, buildingOrigin.y + 0.5f, 0f));
            _state = State.MovingToBuild;
            _buildTimer = 0f;
        }

        /// <summary>Command this unit to pursue and attack a target goblin.
        /// Silently ignored if AttackDamage == 0 (non-combatants like Farmers) or target is already dead.
        /// Called by NetCommandApplier.ApplyAttack on every client.</summary>
        public void SetAttackCommand(Goblin target)
        {
            if (_state == State.Dying) return;
            if (target == null || target == this) return;
            if (AttackDamage <= 0) return;   // non-combatants (Farmers) ignore
            if (target.CurrentHp <= 0) return;
            if (!IsHostileTo(target)) return;   // no friendly fire
            // Re-issuing an attack on the SAME target we're already engaging is a no-op: it must
            // not reset the attack cooldown, otherwise right-click spam fires with no cooldown.
            if (target == _attackTarget && (_state == State.MovingToAttack || _state == State.Attacking)) return;
            HarvestReservations.Release(this);
            ResetHitAnim();
            _attackTarget = target;
            _attackTimer = 0f;
            _state = State.MovingToAttack;
            _hasBuildingTarget = false;
            _lastAttackGoalCell = CellOfPos(target.transform.position);
            RepathTo(target.transform.position);
        }

        /// <summary>Command this unit to march to an (enemy) building and attack it until destroyed.
        /// Ignored for non-combatants or if no building is registered at the origin.</summary>
        public void SetAttackBuildingCommand(Vector2Int origin)
        {
            if (_state == State.Dying || AttackDamage <= 0) return;
            if (!BuildingHP.TryGet(origin, out int cur, out _) || cur <= 0) return;
            // Re-issuing on the same building we're already attacking is a no-op (preserve the cooldown).
            if (_hasBuildingTarget && _buildingTarget == origin
                && (_state == State.MovingToAttackBuilding || _state == State.AttackingBuilding)) return;
            HarvestReservations.Release(this);
            ResetHitAnim();
            _buildingTarget = origin;
            _hasBuildingTarget = true;
            _attackTimer = 0f;
            _state = State.MovingToAttackBuilding;
            RepathTo(NearestStandToFootprint(origin));
        }

        /// <summary>Land unit: walk to <paramref name="boat"/> and board it (if there's room).</summary>
        public void SetBoardCommand(Goblin boat)
        {
            if (_state == State.Dying || _waterMode) return;       // boats can't board boats
            if (boat == null || !boat._waterMode || !boat.BoatHasRoom) return;
            HarvestReservations.Release(this);
            ResetHitAnim();
            _hasBuildingTarget = false;
            _boardBoat = boat;
            _state = State.MovingToBoard;
            RepathTo(boat.transform.position);   // water target → snaps to nearest land cell next to the boat
        }

        /// <summary>Boat: travel to the shore nearest <paramref name="landTarget"/> and drop passengers.</summary>
        public void SetUnloadCommand(Vector3 landTarget)
        {
            if (_state == State.Dying || !_waterMode) return;
            ResetHitAnim();
            _unloadTarget = landTarget;
            _state = State.MovingToUnload;
            RepathTo(landTarget);   // boat is a water unit → snaps to the nearest water cell by the shore
        }

        // Eject all passengers onto nearby passable land cells, then clear the manifest.
        private void EjectPassengers()
        {
            var bc = CellOfPos(transform.position);
            foreach (var p in _passengers)
            {
                if (p == null) continue;
                Vector2Int cell = TryFindLandNear(bc, out var c) ? c : bc;
                p.transform.position = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
                p.gameObject.SetActive(true);   // re-enters Goblin.All via OnEnable
            }
            _passengers.Clear();
        }

        // Nearest land cell (terrain walkable, not occupied) within a few rings of center, for unloading.
        private bool TryFindLandNear(Vector2Int center, out Vector2Int found)
        {
            for (int r = 1; r <= 6; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                int x = center.x + dx, y = center.y + dy;
                var t = _terrainMap != null ? _terrainMap.GetTile(new Vector3Int(x, y, 0)) : null;
                if (t == null) continue;
                if (t.name == "DeepWater" || t.name == "Shore" || t.name == "Cliff") continue; // must be land
                var center2 = new Vector3(x + 0.5f, y + 0.5f, 0f);
                bool occupied = false;
                foreach (var g in All) { if (g != null && (g.transform.position - center2).sqrMagnitude < 0.36f) { occupied = true; break; } }
                if (!occupied) { found = new Vector2Int(x, y); return true; }
            }
            found = default;
            return false;
        }

        private static bool BuildingAlive(Vector2Int origin) =>
            BuildingHP.TryGet(origin, out int cur, out _) && cur > 0;

        // Footprint of the targeted building (via the placer); defaults to 1×1 if unknown.
        private static Vector2Int FootprintOf(Vector2Int origin) =>
            NetCommandApplier.Placer != null && NetCommandApplier.Placer.TryGetFootprint(origin, out var fp) ? fp : Vector2Int.one;

        // Chebyshev distance from the unit's cell to the building footprint rectangle (0 = adjacent/on it).
        private int ChebyshevToFootprint(Vector2Int origin, Vector2Int fp)
        {
            var u = CellOfPos(transform.position);
            int maxX = origin.x + fp.x - 1, maxY = origin.y + fp.y - 1;
            int dx = Mathf.Max(0, Mathf.Max(origin.x - u.x, u.x - maxX));
            int dy = Mathf.Max(0, Mathf.Max(origin.y - u.y, u.y - maxY));
            return Mathf.Max(dx, dy);
        }

        // Nearest passable cell on the ring just outside the footprint — where the unit stands to attack.
        private Vector3 NearestStandToFootprint(Vector2Int origin)
        {
            var fp = FootprintOf(origin);
            Vector3 pos = transform.position;
            Vector3 best = new(origin.x + 0.5f, origin.y + 0.5f, 0f);
            float bestD = float.MaxValue; bool found = false;
            for (int dy = -1; dy <= fp.y; dy++)
            for (int dx = -1; dx <= fp.x; dx++)
            {
                bool border = dx == -1 || dy == -1 || dx == fp.x || dy == fp.y;
                if (!border) continue;
                int x = origin.x + dx, y = origin.y + dy;
                if (!IsCellPassable(x, y)) continue;
                Vector3 c = new(x + 0.5f, y + 0.5f, 0f);
                float d = (c - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; found = true; }
            }
            return found ? best : new Vector3(origin.x + 0.5f, origin.y + 0.5f, 0f);
        }

        // True on the client that authoritatively drives this unit. In solo the single client owns
        // everything (player + bots); in MP it's the owning client. Gates harvest ticks, deposit
        // processing, and damage issuance. Mirrors the public IsOwnedLocally.
        private bool IsLocalOwner => IsOwnedLocally;

        // Chebyshev distance (8-directional grid distance) used for attack-range checks.
        // Cheaper than Euclidean and matches a square grid's natural notion of adjacency.
        private static int ChebyshevDistance(Vector3 a, Vector3 b)
        {
            int dx = Mathf.Abs(Mathf.FloorToInt(a.x) - Mathf.FloorToInt(b.x));
            int dy = Mathf.Abs(Mathf.FloorToInt(a.y) - Mathf.FloorToInt(b.y));
            return Mathf.Max(dx, dy);
        }

        /// <summary>Apply incoming damage. Called locally by NetCommandApplier.ApplyDamage
        /// (from EvDamage) on every client. Auto-retaliates if idle and capable.</summary>
        public void TakeDamage(int damage, Goblin attacker)
        {
            if (CurrentHp <= 0) return;
            CurrentHp = Mathf.Max(0, CurrentHp - damage);
            if (_hitFeedback != null) _hitFeedback.Play();   // flash + wobble + red spritz (all clients)
            if (CurrentHp <= 0) { EnterDying(); return; }

            // Auto-retaliate: only when idle and capable
            if (IsIdle && AttackDamage > 0 && attacker != null && attacker.CurrentHp > 0)
                SetAttackCommand(attacker);
        }

        /// <summary>Toggle the selection ring and IsSelected flag. Called by GoblinSelectionController.</summary>
        public void SetSelected(bool sel)
        {
            IsSelected = sel;
            if (_selectionRing != null) _selectionRing.SetActive(sel);
        }

        // OnEnable/OnDisable keep the global All list and GoblinNetRegistry consistent as
        // goblins are created and destroyed (including DestroyImmediate during world reset).
        private void OnEnable()  => All.Add(this);
        private void OnDisable()
        {
            All.Remove(this);
            GoblinNetRegistry.Unregister(NetId);
        }

        private void Update()
        {
            if (_frames == null || _frames.Length == 0) return;

            // Boats animate continuously (water bob / paddles) — even while idle. Other units only
            // animate while walking (handled in StepToward) and snap to frame 0 when idle.
            if (_waterMode && _state != State.Dying) AdvanceFrame();

            switch (_state)
            {
                case State.Idle:
                    ShowIdleFrame();
                    break;

                case State.MovingToPoint:
                    if (MoveAlongPath()) _state = State.Idle;
                    break;

                case State.MovingToTree:
                {
                    // If the node was felled by another goblin while we were walking, re-plan.
                    if (!IsHarvestableStillThere(_treeCell))
                    {
                        FindNextTreeOrIdle();
                        break;
                    }
                    if (MoveAlongPath())
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
                        if (CarriedAmount > 0) TryStartDepositRun();
                        else FindNextTreeOrIdle();
                        break;
                    }
                    _harvestTimer += Time.deltaTime;
                    if (_harvestTimer >= _chopTickDuration)
                    {
                        _harvestTimer = 0f;
                        bool destroyed = HitHarvestable(_treeCell);
                        if (CarriedAmount >= MaxCarried || destroyed)
                            TryStartDepositRun();
                    }
                    break;
                }

                case State.WalkingToDeposit:
                {
                    if (!IsLocalOwner) break;     // remotes are in MovingToPoint via ApplyMove; their walk handles itself
                    if (MoveAlongPath())
                    {
                        // Arrived at keep — deposit the full carry slot at once. Bot units (solo,
                        // non-player owner) credit their own BotEconomy; the player uses ResourceBank.
                        if (CarriedAmount > 0)
                        {
                            if (WorldStartContext.IsSolo && Owner != 0UL) BotEconomy.Add(Owner, CarriedKind, CarriedAmount);
                            else ResourceBank.Add(CarriedKind, CarriedAmount);
                        }
                        CarriedAmount = 0;

                        // If commands are queued, let them take over instead of auto-resuming.
                        if (_commandQueue.Count > 0) { _state = State.Idle; break; }

                        // Resume: walk back to last node if still alive (re-routing to an alternative of
                        // the same kind if its spots are now full), else find nearest, else idle.
                        if (IsHarvestableStillThere(_treeCell))
                        {
                            if (TryBeginHarvest(_treeCell)) SendMoveWireOnly(_moveTarget);
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
                    if (MoveAlongPath())
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
                        _attackTimer = AttackInterval; // pre-charge the timer so the first swing fires immediately
                        break;
                    }
                    // Only re-path when the target has moved to a different cell, not every frame.
                    var goalCell = CellOfPos(_attackTarget.transform.position);
                    if (goalCell != _lastAttackGoalCell)
                    {
                        _lastAttackGoalCell = goalCell;
                        RepathTo(_attackTarget.transform.position);
                    }
                    MoveAlongPath();
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
                        if (_projectileSprite != null)
                        {
                            // Ranged: face the target and shoot an arrow. The arrow applies damage
                            // on impact (owner client only); remotes spawn a visual-only arrow.
                            if (_renderer != null)
                                _renderer.flipX = _attackTarget.transform.position.x < transform.position.x;
                            Arrow.Spawn(transform.position, _attackTarget, AttackDamage, this, IsLocalOwner, _projectileSprite);
                        }
                        else
                        {
                            // Melee: lunge animation runs on every client (deterministic). Damage only
                            // fires on the attacker's owner client, which broadcasts EvDamage to remotes.
                            StartHitAnim(new Vector3Int(
                                Mathf.FloorToInt(_attackTarget.transform.position.x),
                                Mathf.FloorToInt(_attackTarget.transform.position.y), 0));
                            if (IsLocalOwner)
                                NetCommandIssuer.IssueDamage(_attackTarget, AttackDamage, this);
                        }
                    }
                    break;
                }

                case State.MovingToAttackBuilding:
                {
                    if (!BuildingAlive(_buildingTarget)) { _state = State.Idle; _hasBuildingTarget = false; break; }
                    if (ChebyshevToFootprint(_buildingTarget, FootprintOf(_buildingTarget)) <= AttackRange)
                    {
                        _state = State.AttackingBuilding;
                        _attackTimer = AttackInterval; // first hit promptly
                        break;
                    }
                    if (MoveAlongPath())   // arrived but still out of range → can't reach, give up
                    {
                        if (ChebyshevToFootprint(_buildingTarget, FootprintOf(_buildingTarget)) > AttackRange)
                        { _state = State.Idle; _hasBuildingTarget = false; }
                    }
                    break;
                }

                case State.AttackingBuilding:
                {
                    if (!BuildingAlive(_buildingTarget)) { _state = State.Idle; _hasBuildingTarget = false; break; }
                    if (ChebyshevToFootprint(_buildingTarget, FootprintOf(_buildingTarget)) > AttackRange)
                    {
                        _state = State.MovingToAttackBuilding;
                        RepathTo(NearestStandToFootprint(_buildingTarget));
                        break;
                    }
                    _attackTimer += Time.deltaTime;
                    if (_attackTimer >= AttackInterval)
                    {
                        _attackTimer = 0f;
                        var fp = FootprintOf(_buildingTarget);
                        Vector3 center = new(_buildingTarget.x + fp.x * 0.5f, _buildingTarget.y + fp.y * 0.5f, 0f);
                        if (_projectileSprite != null)
                        {
                            // Ranged: face + shoot a visual arrow that flashes the building on impact.
                            if (_renderer != null) _renderer.flipX = center.x < transform.position.x;
                            Arrow.SpawnToBuilding(transform.position, _buildingTarget, center, _projectileSprite);
                        }
                        else
                        {
                            // Melee: lunge toward the building center + immediate hit effect.
                            StartHitAnim(new Vector3Int(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y), 0));
                            BuildingHitFeedback.Play(_buildingTarget);
                        }
                        if (IsLocalOwner)
                        {
                            BuildingHP.Damage(_buildingTarget, AttackDamage);
                            if (!BuildingAlive(_buildingTarget))
                            {
                                NetCommandApplier.Placer?.RemoveBuilding(_buildingTarget);
                                _state = State.Idle; _hasBuildingTarget = false;
                            }
                        }
                    }
                    break;
                }

                case State.MovingToBoard:
                {
                    if (_boardBoat == null || _boardBoat.CurrentHp <= 0 || !_boardBoat.gameObject.activeInHierarchy)
                    { _boardBoat = null; _state = State.Idle; break; }
                    if (ChebyshevDistance(transform.position, _boardBoat.transform.position) <= 1)
                    {
                        // Board: hop into the boat (deactivate → leaves Goblin.All) if there's room.
                        if (_boardBoat.BoatHasRoom) { _boardBoat._passengers.Add(this); _state = State.Idle; gameObject.SetActive(false); }
                        else { _boardBoat = null; _state = State.Idle; }
                        break;
                    }
                    RepathTo(_boardBoat.transform.position);   // re-target the boat as it moves
                    MoveAlongPath();
                    break;
                }

                case State.MovingToUnload:
                {
                    if (MoveAlongPath()) { EjectPassengers(); _state = State.Idle; }
                    break;
                }

                case State.Dying:
                    // Simple Euler integration for the hop arc. Gravity is applied until
                    // the goblin returns to its starting Y, at which point it lands.
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

            // Drain the command queue: whenever idle with queued commands, start the next.
            // Queue is owner-local (remotes never enqueue), so this is a no-op on remotes.
            if (_state == State.Idle && _commandQueue.Count > 0) ActivateNextQueued();

            // Hit animations (chop / build / attack) keep playing across state transitions
            // so the killing blow's lunge finishes after the target dies. Dying state owns
            // the transform itself, so we skip there.
            if (_state != State.Dying) UpdateHitAnim();

            // Gentle anti-stacking: nudge apart from units sharing this spot, but ONLY while stationary
            // (idle / fighting / harvesting / building). Never while walking a path — pushing moving units
            // sideways makes them block each other at chokepoints. Cosmetic.
            if (IsStationaryForSeparation()) ApplySeparation();
        }

        private const float SepRadius = 0.5f;     // units want at least this much clear space around them
        private const float SepSpeed = 2.2f;      // max nudge speed (world units / second)

        // Only resolve overlap when the unit isn't navigating — walking units must be free to path.
        private bool IsStationaryForSeparation() => _state switch
        {
            State.Idle or State.Harvesting or State.Building
                or State.Attacking or State.AttackingBuilding => true,
            _ => false,
        };

        // Push slightly away from any nearby units, clamped and only onto passable cells. Runs on all
        // clients (purely cosmetic position fix-up; never affects HP, harvest totals, or pathing goals).
        private void ApplySeparation()
        {
            Vector3 push = Vector3.zero;
            foreach (var o in All)
            {
                if (o == null || o == this) continue;
                if (!o.IsStationaryForSeparation()) continue;   // only stationary units push each other
                Vector3 d = transform.position - o.transform.position;
                float sq = d.sqrMagnitude;
                if (sq >= SepRadius * SepRadius) continue;
                if (sq < 1e-5f) { push += new Vector3((NetId.LocalIndex % 2 == 0) ? 0.01f : -0.01f, 0.013f, 0f); continue; }
                float dist = Mathf.Sqrt(sq);
                push += d / dist * (SepRadius - dist);   // stronger the closer they are
            }
            if (push == Vector3.zero) return;

            float maxStep = SepSpeed * Time.deltaTime;
            if (push.magnitude > maxStep) push = push.normalized * maxStep;
            Vector3 next = transform.position + push;
            // Don't let separation shove a unit into impassable terrain (water/cliff for land units, etc.).
            if (IsCellPassable(Mathf.FloorToInt(next.x), Mathf.FloorToInt(next.y)))
                transform.position = next;
        }

        /// <summary>Pop and run the next queued command (owner-local). Invalid commands
        /// (depleted node, dead target, finished construction) are skipped. Each activated
        /// command fires the normal single-unit net command so remotes mirror the step.</summary>
        private void ActivateNextQueued()
        {
            while (_commandQueue.Count > 0)
            {
                var cmd = _commandQueue[0];
                _commandQueue.RemoveAt(0);
                var solo = new List<Goblin> { this };
                switch (cmd.Type)
                {
                    case CommandType.Move:
                        NetCommandIssuer.IssueMove(solo, cmd.Point);
                        return;
                    case CommandType.Harvest:
                        if (IsHarvestableStillThere(cmd.Cell))
                        {
                            NetCommandIssuer.IssueHarvest(solo, cmd.Cell);
                            return;
                        }
                        break; // depleted — try next
                    case CommandType.BuildAssist:
                        if (BuildingConstruction.IsUnderConstruction(cmd.Origin))
                        {
                            NetCommandIssuer.IssueBuildAssist(solo, cmd.Origin);
                            return;
                        }
                        break; // already built — try next
                    case CommandType.Attack:
                        if (cmd.Target != null && cmd.Target.CurrentHp > 0 && AttackDamage > 0)
                        {
                            NetCommandIssuer.IssueAttack(this, cmd.Target);
                            return;
                        }
                        break; // dead target — try next
                }
            }
        }

        /// <summary>Apply one chop to the given tree cell. Returns true if the tree was destroyed by this hit.</summary>
        private bool HitHarvestable(Vector3Int cell)
        {
            var tile = _decorationMap.GetTile(cell) as UnityEngine.Tilemaps.Tile;
            var sprite = tile != null ? tile.sprite : null;
            string tileName = tile != null ? tile.name : "";

            int remaining = TreeHP.Hit(cell, 1, MaxHpFor(tileName));
            CarriedKind = KindOf(tileName);
            CarriedAmount = Mathf.Min(MaxCarried, CarriedAmount + 1);

            StartHitAnim(cell);
            TreeHitEffect.Spawn(_decorationMap, cell, sprite);

            if (remaining <= 0)
            {
                _decorationMap.SetTile(cell, null);
                TreeHitEffect.SpawnBurst(_decorationMap, cell, sprite);
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
            RepathTo(target);
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

        // Begin a lunge-and-tilt animation toward the center of `cell`.
        // Called for harvest chop, build tick, and attack swing.
        private void StartHitAnim(Vector3Int cell)
        {
            _hitAnimT = 0f;
            _hitHomePos = transform.position;
            Vector3 treeWorld = CellCenter(cell);
            _hitDir = (treeWorld - _hitHomePos).sqrMagnitude > 0.0001f
                ? (treeWorld - _hitHomePos).normalized
                : Vector3.up;
        }

        // Drive the lunge using a sin curve over [0,π] for smooth in-out motion.
        // Rotation tilts the goblin toward the target and rights itself on the back half.
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

        // Snap position/rotation back to home before clearing the animation flag.
        // Called whenever a new command interrupts a running lunge.
        private void ResetHitAnim()
        {
            if (_hitAnimT >= 0f)
            {
                transform.position = _hitHomePos;
                transform.rotation = Quaternion.identity;
            }
            _hitAnimT = -1f;
        }

        // Tint enemy units with their faction colour; local/solo units stay white.
        private void ApplyOwnerVisuals()
        {
            if (_renderer == null) return;
            if (IsNeutral) { _renderer.color = Color.white; return; } // monsters keep their natural sprite
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            _renderer.color = isLocal ? Color.white : WorldStartContext.GetPlayerColor(Owner);
        }

        // Transition to Dying: release pop-cap slot, remove from global list and selection,
        // clear the lunge anim, free any harvest reservation, then launch the hop arc.
        // Idempotent guard prevents double-dying if TakeDamage is called twice in one frame.
        private void EnterDying()
        {
            if (_state == State.Dying) return;
            _state = State.Dying;
            // Release population: bot units (solo, non-player owner) from BotEconomy; the player from
            // PopulationManager; monsters never consumed any.
            if (!IsNeutral)
            {
                if (WorldStartContext.IsSolo && Owner != 0UL) BotEconomy.RemoveUsed(Owner, PopulationCost);
                else PopulationManager.RemoveUsed(PopulationCost);
            }
            Goblin.All.Remove(this);
            SetSelected(false);
            // Clear any in-flight lunge so it doesn't fight the hop-arc transform writes.
            ResetHitAnim();
            HarvestReservations.Release(this);

            // A sinking boat takes its passengers down with it.
            if (_waterMode && _passengers.Count > 0)
            {
                foreach (var p in _passengers) if (p != null) Destroy(p.gameObject);
                _passengers.Clear();
            }

            _dieStartPos = transform.position;
            _dieVelocity = new Vector3(0f, DeathHopVelocity, 0f);
        }

        private static Vector2Int CellOfPos(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

        // Passability predicate passed to Pathfinder and HarvestReservations.
        // Treats water/cliff/shore as walls and treats harvestable tiles as obstacles
        // (so units path around trees rather than through them).
        private bool IsCellPassable(int x, int y)
        {
            if (_terrainMap == null) return false;
            var t = _terrainMap.GetTile(new Vector3Int(x, y, 0));
            if (t == null) return false;
            bool water = t.name == "DeepWater" || t.name == "Shore";
            // Boats (water units) navigate ONLY water — land and cliffs block them, decorations don't matter.
            if (_waterMode) return water;
            // Land units: water, shore and cliffs block; harvestable decorations also block.
            if (water || t.name == "Cliff") return false;
            if (_decorationMap != null)
            {
                var d = _decorationMap.GetTile(new Vector3Int(x, y, 0));
                if (d != null && IsHarvestable(d.name)) return false;
            }
            return true;
        }

        // Compute an A* path to worldTarget and store it in _path.
        // If the target cell is impassable (e.g. water), snaps to the nearest passable cell
        // within an 8-cell search radius before running A*. No path found → _path stays
        // empty so the unit stands still rather than beelining through obstacles.
        private void RepathTo(Vector3 worldTarget)
        {
            _path.Clear();
            _pathIndex = 0;

            var goal = CellOfPos(worldTarget);
            // If the destination cell is impassable (e.g. clicked on water), snap to the
            // nearest passable cell so we never walk onto water/cliff/resources.
            if (!IsCellPassable(goal.x, goal.y))
            {
                if (TryFindNearestPassable(goal, out var snapped))
                {
                    goal = snapped;
                    worldTarget = new Vector3(snapped.x + 0.5f, snapped.y + 0.5f, 0f);
                }
                else
                {
                    _moveTarget = transform.position; // nowhere reachable — stay put
                    return;
                }
            }

            _moveTarget = worldTarget;
            var start = CellOfPos(transform.position);
            var cells = Pathfinder.FindPath(new int2(start.x, start.y), new int2(goal.x, goal.y),
                                            WorldGrid.Width, WorldGrid.Height, IsCellPassable);
            if (cells != null && cells.Count > 0)
            {
                for (int i = 0; i < cells.Count; i++)
                    _path.Add(new Vector3(cells[i].x + 0.5f, cells[i].y + 0.5f, 0f));
                _path[_path.Count - 1] = worldTarget; // final waypoint = exact destination
            }
            // No path found → leave _path empty; the unit stays put rather than beelining
            // straight through impassable terrain.
        }

        // Expand outward in square rings until a passable cell is found (BFS-ring, max radius 8).
        private bool TryFindNearestPassable(Vector2Int c, out Vector2Int found)
        {
            for (int r = 0; r <= 8; r++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue; // ring only
                int x = c.x + dx, y = c.y + dy;
                if (IsCellPassable(x, y)) { found = new Vector2Int(x, y); return true; }
            }
            found = default;
            return false;
        }

        // Advance one step along the cached A* path. Returns true when the path is exhausted
        // (unit has arrived at its destination). An empty path also returns true so state
        // transitions fire immediately rather than hanging.
        private bool MoveAlongPath()
        {
            if (_path.Count == 0) return true; // no path → treat as arrived (no straight-line onto blocked terrain)
            if (_pathIndex >= _path.Count) return true;
            if (StepToward(_path[_pathIndex]))
            {
                _pathIndex++;
                if (_pathIndex >= _path.Count) return true;
            }
            return false;
        }

        /// <summary>Move one frame's worth of distance toward target. Advances the walk
        /// animation, flips the sprite horizontally to face the direction of travel.
        /// Returns true when target is reached.</summary>
        private bool StepToward(Vector3 target)
        {
            var delta = target - transform.position;
            if (delta.sqrMagnitude <= TargetReachedEpsilon * TargetReachedEpsilon)
                return true;

            if (!_waterMode) AdvanceFrame();   // boats animate in Update() instead (idle + moving)

            float step = _moveSpeed * Time.deltaTime;
            if (delta.magnitude <= step) transform.position = target;
            else transform.position += delta.normalized * step;

            // Face the direction of travel. Boat art points the opposite way from goblins, so invert it.
            if (Mathf.Abs(delta.x) > 0.05f)
                _renderer.flipX = _waterMode ? delta.x > 0f : delta.x < 0f;
            return false;
        }

        // Advance the walk-cycle by one frame when the per-frame timer elapses.
        private void AdvanceFrame()
        {
            _frameTimer += Time.deltaTime;
            if (_frameTimer >= FrameDuration)
            {
                _frameTimer = 0f;
                _frameIndex = (_frameIndex + 1) % _frames.Length;
                _renderer.sprite = _frames[_frameIndex];
            }
        }

        private void ShowIdleFrame()
        {
            if (_waterMode) return;   // boats keep their continuous animation (driven from Update)
            if (_frameIndex != 0)
            {
                _frameIndex = 0;
                _renderer.sprite = _frames[0];
            }
            _frameTimer = 0f;
        }

        // Tile presence check used by both the FSM (detect depletion mid-harvest)
        // and ActivateNextQueued (skip already-gone nodes from the command queue).
        private bool IsHarvestableStillThere(Vector3Int cell)
        {
            if (_decorationMap == null) return false;
            var t = _decorationMap.GetTile(cell);
            return t != null && IsHarvestable(t.name);
        }

        // Auto-find fallback: scan AutoFindRadius cells for the nearest harvestable tile.
        // If the command queue has pending items, yields to it instead (Idle → queue drains).
        // Called when the current node is depleted and no explicit command was queued.
        private void FindNextTreeOrIdle()
        {
            HarvestReservations.Release(this);
            if (_commandQueue.Count > 0) { _state = State.Idle; return; } // let the queue advance
            if (_decorationMap == null) { _state = State.Idle; return; }

            Vector3Int origin = _decorationMap.WorldToCell(transform.position);
            Vector3Int? best = null;
            int bestSq = int.MaxValue;
            for (int dy = -AutoFindRadius; dy <= AutoFindRadius; dy++)
            for (int dx = -AutoFindRadius; dx <= AutoFindRadius; dx++)
            {
                var c = new Vector3Int(origin.x + dx, origin.y + dy, 0);
                if (!IsHarvestableStillThere(c)) continue;
                if (KindOf(_decorationMap.GetTile(c).name) != _harvestKind) continue; // same resource only
                if (!HarvestReservations.HasFreeSpot(c, IsCellPassable)) continue; // skip full nodes — no stacking
                int sq = dx * dx + dy * dy;
                if (sq < bestSq) { bestSq = sq; best = c; }
            }

            if (best.HasValue) SetHarvestCommand(best.Value);
            else _state = State.Idle;
        }

        // ---------- Tile classification helpers ----------
        // Used by IsCellPassable, ObjectInspector, GoblinSelectionController, and HarvestReservations
        // to identify which decoration tiles are harvestable and what resource kind they yield.

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

        public static bool IsBerryTile(string tileName) =>
            tileName.StartsWith("BerryBush");

        public static bool IsRockTile(string tileName) =>
            tileName.StartsWith("Rocks_");

        public static bool IsOreTile(string tileName) =>
            tileName.StartsWith("GoldOre_") || tileName.StartsWith("IronOre_") || tileName.StartsWith("CrystalOre_");

        public static bool IsHarvestable(string tileName) =>
            IsTreeTile(tileName) || IsWheatfieldTile(tileName) || IsBerryTile(tileName) || IsRockTile(tileName) || IsOreTile(tileName);

        public static ResourceKind KindOf(string tileName)
        {
            if (IsWheatfieldTile(tileName) || IsBerryTile(tileName)) return ResourceKind.Food;
            if (IsRockTile(tileName)) return ResourceKind.Stone;
            if (tileName.StartsWith("GoldOre_")) return ResourceKind.Gold;
            if (tileName.StartsWith("IronOre_")) return ResourceKind.Iron;
            if (tileName.StartsWith("CrystalOre_")) return ResourceKind.Crystal;
            return ResourceKind.Wood; // trees + default
        }

        public static int MaxHpFor(string tileName)
        {
            if (IsWheatfieldTile(tileName)) return 500;   // built field → bigger food yield
            if (IsBerryTile(tileName)) return 100;        // wild berry bush → 100 food
            if (IsRockTile(tileName)) return 100;
            if (IsOreTile(tileName)) return 200;
            return TreeHP.MaxHP; // trees = 50
        }

        private static Vector3 CellCenter(Vector3Int cell) =>
            new(cell.x + 0.5f, cell.y + 0.5f, 0f);

        // ---------- Selection ring (procedural sprite) ----------

        // Build a child GameObject with a SpriteRenderer displaying a thin circle.
        // Sorted just below the unit sprite. Scale (1.1, 0.5, 1) gives an isometric-ish oval.
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

        // Update the ring tint after SetOwner is called (e.g. when owner data arrives over the network).
        private void RefreshSelectionRingColor()
        {
            if (_selectionRing == null) return;
            var sr = _selectionRing.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            if (IsNeutral) { sr.color = new Color(1f, 0.5f, 0.5f, 1f); return; } // neutral ring tint
            bool isLocal = Owner == WorldStartContext.LocalPlayer || Owner == 0UL;
            sr.color = isLocal
                ? new Color(0.95f, 0.95f, 0.95f, 1f)
                : WorldStartContext.GetPlayerColor(Owner);
        }

        // Lazily generated 16×16 ring sprite shared across all goblin instances.
        // Generated at runtime to avoid a sprite asset dependency.
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
            float innerR = outerR - 2f;     // 2-pixel ring band
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
