// =============================================================================
// ObjectInspector.cs  —  RTSCL.World.Unity
//
// The bottom INFO panel (read-only). Display modes:
//   Building   — left-clicking a placed building: shows name + HP. Actions (train/
//                upgrade) live in the BuildMenu sidebar; this fires OnBuildingInspected.
//   Goblins    — own units selected (GoblinSelectionController.OnSelectionChanged):
//                single = full stat sheet, multi = brief summary.
//   Decoration — a resource tile: remaining amount, live-updated each frame.
//   UnitInfo   — an enemy/neutral unit clicked: read-only stat sheet.
//
// Building actions moved to BuildMenu: OnBuildingInspected(origin, def, isLocal) tells
// the sidebar which own building is selected; OnInspectionCleared closes it when the
// selection switches to units / a resource / nothing.
// =============================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace RTSCL.World.Unity
{
    public sealed class ObjectInspector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera _camera;
        [SerializeField] private Tilemap _decorationMap;
        [SerializeField] private Tilemap _terrainMap;
        [SerializeField] private BuildingPlacer _placer;
        [SerializeField] private GoblinSelectionController _selectionController;

        [Header("Popup UI")]
        [SerializeField] private GameObject _popupRoot;
        [SerializeField] private Text _nameLabel;
        [SerializeField] private Text _descriptionLabel;
        [SerializeField] private RectTransform _unitCardsContainer;
        [SerializeField] private Image _progressFill;
        [SerializeField] private Text _progressLabel;
        [SerializeField] private GameObject _progressRow;

        [Header("Rally")]
        [Tooltip("Flag icon shown at a building's rally point (GUI_33)")]
        [SerializeField] private Sprite _rallyIcon;

        // Which type of object is currently being inspected.
        private enum SelKind { None, Building, Decoration, Goblins, UnitInfo }
        private SelKind _selKind = SelKind.None;
        private Goblin _inspectedUnit;            // enemy/neutral unit shown in UnitInfo mode (read-only)
        // Cached state for the active display mode; updated each time Show*() is called.
        private Vector2Int _selOrigin;            // SW-corner of selected building (Building mode)
        private BuildingDefinition _selDef;       // definition of selected building
        private BuildingOwner _selBuildingOwner;  // component used to toggle the building selection ring
        private string _lastCarryLine = "";       // cached carry text to avoid redundant label updates
        private string _lastGoblinDescBase = "";  // base description without carry suffix (Goblins mode)
        private Vector3Int _selDecoCell;          // tile-space cell of selected decoration (Decoration mode)
        private bool _selDecoHarvestable;         // whether the selected tile yields a resource
        private string _selDecoBaseDesc = "";     // description without the live resource amount line
        private string _lastAmountLine = "";      // cached amount text to avoid redundant label updates

        // Extra line spacing applied to all info-panel text for a roomier, more readable layout.
        private const float LineSpacing = 1.45f;

        /// <summary>Fired when the player left-clicks a building: (origin, definition, isLocal). The
        /// BuildMenu sidebar listens and shows that building's train/upgrade actions. The inspector itself
        /// only shows the building's info (name + HP).</summary>
        public static event Action<Vector2Int, BuildingDefinition, bool> OnBuildingInspected;
        /// <summary>Fired when the info panel hides or switches away from a building, so the BuildMenu
        /// closes its actions panel.</summary>
        public static event Action OnInspectionCleared;

        private void Start()
        {
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_progressRow != null) _progressRow.SetActive(false);
            if (_nameLabel != null) _nameLabel.lineSpacing = LineSpacing;
            if (_descriptionLabel != null) _descriptionLabel.lineSpacing = LineSpacing;
            if (_selectionController != null)
            {
                _selectionController.OnSelectionChanged += OnGoblinSelectionChanged;
                _selectionController.OnInspectUnit += ShowUnitInfo;
            }
        }

        private void OnDestroy()
        {
            if (_selectionController != null)
            {
                _selectionController.OnSelectionChanged -= OnGoblinSelectionChanged;
                _selectionController.OnInspectUnit -= ShowUnitInfo;
            }
        }

        private void Update()
        {
            if (_camera == null || Mouse.current == null) return;
            // Don't intercept clicks during placement mode
            if (_placer != null && _placer.Selected != null) return;

            // Live stat updates for the currently-shown info panel (production/actions live in BuildMenu now).
            if (_selKind == SelKind.Goblins) UpdateCarryUI();
            if (_selKind == SelKind.Decoration) UpdateResourceAmount();
            if (_selKind == SelKind.UnitInfo) UpdateInspectedUnit();

            // Right-click while a unit-training building is selected → set/move its rally point.
            if (Mouse.current.rightButton.wasPressedThisFrame
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
                && BuildingTakesRally(out var bc))
            {
                Vector2 rmp = Mouse.current.position.ReadValue();
                Vector3 rworld = _camera.ScreenToWorldPoint(new Vector3(rmp.x, rmp.y, -_camera.transform.position.z));
                var rally = new Vector3(Mathf.FloorToInt(rworld.x) + 0.5f, Mathf.FloorToInt(rworld.y) + 0.5f, 0f);
                RallyPoints.Set(_selOrigin, rally);
                RallyVisual.Instance.Show(bc, rally, _rallyIcon);
                return;
            }

            if (!Mouse.current.leftButton.wasPressedThisFrame) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mp = Mouse.current.position.ReadValue();
            Vector3 world = _camera.ScreenToWorldPoint(
                new Vector3(mp.x, mp.y, -_camera.transform.position.z));
            Vector3Int cell = (_terrainMap != null ? _terrainMap : _decorationMap).WorldToCell(world);
            Vector2Int cell2 = new(cell.x, cell.y);

            if (_placer != null && _placer.TryGetBuildingAt(cell2, out var building) && building != null)
            {
                _placer.TryGetBuildingOrigin(cell2, out _selOrigin);
                ShowBuilding(building, _selOrigin);
                return;
            }
            if (_decorationMap != null)
            {
                var deco = _decorationMap.GetTile(cell);
                if (deco != null)
                {
                    _selKind = SelKind.Decoration;
                    _selDef = null;
                    ShowResourceNode(cell, deco.name);
                    return;
                }
            }
            // Click on empty terrain — if no goblin selection, hide popup. (UnitInfo panels are opened by
            // GoblinSelectionController on mouse-release, a later frame than this press-driven check, so
            // this won't clobber them; clicking empty ground here correctly dismisses a shown panel.)
            if (_selectionController == null || _selectionController.Selection.Count == 0)
                Hide();
        }

        private void OnGoblinSelectionChanged()
        {
            if (_selectionController == null) return;
            var sel = _selectionController.Selection;
            if (sel.Count == 0)
            {
                if (_selKind == SelKind.Goblins) Hide();
                return;
            }
            ShowGoblinSelection(sel);
        }

        // ---------- Display modes ----------

        private void ShowGoblinSelection(IReadOnlyList<Goblin> sel)
        {
            _selKind = SelKind.Goblins;
            _selDef = null;
            RallyVisual.Instance.Hide();
            if (_selBuildingOwner != null) { _selBuildingOwner.SetSelected(false); _selBuildingOwner = null; }

            bool hasFarmer = false;
            string firstKind = null;
            int farmers = 0;
            foreach (var g in sel)
            {
                if (g == null) continue;
                firstKind ??= g.Kind;
                if (g.Kind == "FarmerGoblin") { hasFarmer = true; farmers++; }
            }

            string name = sel.Count == 1 ? PrettyKindName(firstKind) : $"{PrettyKindName(firstKind)} × {sel.Count}";
            // Single unit → full stat sheet (HP / damage / attack speed / range). Multi → brief summary.
            string desc = (sel.Count == 1 && sel[0] != null)
                ? UnitStatsDesc(sel[0])
                : (hasFarmer ? "Workers" : "Warriors");
            _lastGoblinDescBase = desc;
            _lastCarryLine = "";
            SetHeader(name, desc);

            OnInspectionCleared?.Invoke();   // selecting units closes the BuildMenu actions panel

            _popupRoot?.SetActive(true);
        }

        // Read-only info for an enemy / neutral unit clicked on the map (no action cards, no commands).
        private void ShowUnitInfo(Goblin g)
        {
            if (g == null) return;
            _selKind = SelKind.UnitInfo;
            _selDef = null;
            _inspectedUnit = g;
            RallyVisual.Instance.Hide();
            if (_selBuildingOwner != null) { _selBuildingOwner.SetSelected(false); _selBuildingOwner = null; }
            SetHeader(UnitInfoTitle(g), UnitStatsDesc(g));
            _popupRoot?.SetActive(true);
            OnInspectionCleared?.Invoke();   // inspecting a unit closes the BuildMenu actions panel
        }

        // Live-refresh the inspected unit's HP; hide once it dies or is removed (e.g. boards a boat).
        private void UpdateInspectedUnit()
        {
            if (_inspectedUnit == null || _inspectedUnit.CurrentHp <= 0 || !_inspectedUnit.gameObject.activeInHierarchy)
            { Hide(); return; }
            if (_descriptionLabel != null) _descriptionLabel.text = UnitStatsDesc(_inspectedUnit);
        }

        private static string UnitInfoTitle(Goblin g)
        {
            string side = g.IsNeutral ? "Neutral" : "Enemy";
            return $"{side} {PrettyKindName(g.Kind)}";
        }

        // Shared stat sheet for a unit: type, HP, and (for combat units) damage / attack speed / range.
        private static string UnitStatsDesc(Goblin g)
        {
            string type = g.Kind == "FarmerGoblin" ? "Worker — chops trees, builds"
                        : (g.AttackDamage > 0 ? "Warrior" : "Unit");
            string s = $"{type}\nHP: {g.CurrentHp} / {g.MaxHp}";
            if (g.AttackDamage > 0)
            {
                float aps = g.AttackInterval > 0.001f ? 1f / g.AttackInterval : 0f;
                s += $"\nDamage: {g.AttackDamage}";
                s += $"\nAttack Speed: {aps:0.0}/s";
                s += $"\nRange: {g.AttackRange}";
            }
            return s;
        }

        private void ShowBuilding(BuildingDefinition def, Vector2Int origin)
        {
            _selKind = SelKind.Building;
            _selDef = def;
            _inspectedUnit = null;

            // Turn off previous building's ring, then turn on this one's.
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(false);
            _selBuildingOwner = FindBuildingOwnerAt(origin);
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(true);

            bool isLocal = true;
            if (_placer != null && _placer.TryGetBuildingOwner(origin, out ulong owner))
                isLocal = (owner == WorldStartContext.LocalPlayer || owner == 0UL);

            string desc = DescribeBuilding(def);
            if (BuildingHP.TryGet(origin, out int cur, out int max))
                desc += $"\nHP: {cur} / {max}";
            SetHeader(def.DisplayName, desc);

            _popupRoot?.SetActive(true);
            ShowRallyVisual();

            // Actions (train/upgrade) live in the BuildMenu sidebar — tell it which building is selected.
            OnBuildingInspected?.Invoke(origin, def, isLocal);
        }

        // True when the current selection is a local, unit-training building (eligible for a rally point).
        // Outputs the building's footprint center in world space (line start for the rally visual).
        private bool BuildingTakesRally(out Vector3 buildingCenter)
        {
            buildingCenter = default;
            if (_selKind != SelKind.Building || _selDef == null) return false;
            if (_selDef.TrainsUnits == null || _selDef.TrainsUnits.Length == 0) return false;
            if (_placer != null && _placer.TryGetBuildingOwner(_selOrigin, out ulong owner)
                && owner != WorldStartContext.LocalPlayer && owner != 0UL) return false;
            if (_terrainMap == null) return false;
            var w = _terrainMap.CellToWorld(new Vector3Int(_selOrigin.x, _selOrigin.y, 0));
            buildingCenter = w + new Vector3(_selDef.Footprint.x * 0.5f, _selDef.Footprint.y * 0.5f, 0f);
            return true;
        }

        // Show the rally flag + dashed line for the selected building (creating a default rally if none),
        // or hide the visual when the selection isn't a rally-eligible building.
        private void ShowRallyVisual()
        {
            if (!BuildingTakesRally(out var center)) { RallyVisual.Instance.Hide(); return; }
            if (!RallyPoints.TryGet(_selOrigin, out var rally))
            {
                rally = RallyPoints.Default(_selOrigin, _selDef.Footprint);
                RallyPoints.Set(_selOrigin, rally);
            }
            RallyVisual.Instance.Show(center, rally, _rallyIcon);
        }

        private void ShowSimple(string title, string description)
        {
            if (_selBuildingOwner != null) { _selBuildingOwner.SetSelected(false); _selBuildingOwner = null; }
            SetHeader(title, description);
            _popupRoot?.SetActive(true);
            OnInspectionCleared?.Invoke();   // not a building → close the BuildMenu actions panel
        }

        // Locate the BuildingOwner MonoBehaviour that sits at the world position corresponding
        // to the given origin cell. FindObjectsByType is expensive; called only when the
        // selection changes (not every frame), so the cost is acceptable.
        private BuildingOwner FindBuildingOwnerAt(Vector2Int origin)
        {
            if (_terrainMap == null) return null;
            Vector3 originWorld = _terrainMap.CellToWorld(new Vector3Int(origin.x, origin.y, 0));
            var all = UnityEngine.Object.FindObjectsByType<BuildingOwner>(FindObjectsSortMode.None);
            const float eps = 0.01f;
            foreach (var bo in all)
            {
                if (bo == null) continue;
                Vector3 p = bo.transform.position;
                if (Mathf.Abs(p.x - originWorld.x) < eps && Mathf.Abs(p.y - originWorld.y) < eps)
                    return bo;
            }
            return null;
        }

        private void SetHeader(string title, string description)
        {
            if (_nameLabel != null)        _nameLabel.text = title;
            if (_descriptionLabel != null) _descriptionLabel.text = description;
        }

        private void Hide()
        {
            RallyVisual.Instance.Hide();
            if (_popupRoot != null) _popupRoot.SetActive(false);
            if (_selBuildingOwner != null) _selBuildingOwner.SetSelected(false);
            _selBuildingOwner = null;
            _selKind = SelKind.None;
            _selDef = null;
            _inspectedUnit = null;
            OnInspectionCleared?.Invoke();
        }

        // Poll the selected farmer's carry slot each frame and append a carry line to the
        // description label when non-zero. Uses string equality to avoid redundant label writes.
        private void UpdateCarryUI()
        {
            if (_selectionController == null) return;
            var sel = _selectionController.Selection;
            if (sel.Count != 1 || sel[0] == null) return;   // only a single unit shows live stats
            var g = sel[0];
            string desc = UnitStatsDesc(g);
            if (g.Kind == "FarmerGoblin" && g.CarriedAmount > 0)
                desc += $"\nCarrying: {g.CarriedAmount} {g.CarriedKind.ToString().ToLowerInvariant()}";
            if (desc == _lastCarryLine) return;             // cache full text to skip redundant writes
            _lastCarryLine = desc;
            if (_descriptionLabel != null) _descriptionLabel.text = desc;
        }

        // Poll the resource node's remaining HP each frame and append an amount line.
        // Hides the popup if the tile has been removed since it was selected.
        private void UpdateResourceAmount()
        {
            if (!_selDecoHarvestable || _decorationMap == null) return;
            var tile = _decorationMap.GetTile(_selDecoCell);
            if (tile == null) { Hide(); return; } // node depleted/removed
            string newAmount = AmountLine(_selDecoCell, tile.name);
            if (newAmount == _lastAmountLine) return;
            _lastAmountLine = newAmount;
            if (_descriptionLabel != null)
                _descriptionLabel.text = _selDecoBaseDesc + "\n" + newAmount;
        }

        // ---------- Helpers ----------

        // Human-readable display name for goblin unit types. Extend when adding new kinds.
        private static string PrettyKindName(string kind) => kind switch
        {
            "FarmerGoblin" => "Farmer Goblin",
            "ClubGoblin"   => "Club Goblin",
            "ArcherGoblin" => "Archer Goblin",
            "SpearGoblin"  => "Spear Goblin",
            null           => "Goblin",
            _              => kind,
        };

        // Derive a brief material/size description from the asset name.
        // Convention: "Keep_0" → sheet="Keep"; "Barracks_3" → sheet="Barracks".
        private static string DescribeBuilding(BuildingDefinition def)
        {
            int us = def.name.IndexOf('_');
            string sheet = us > 0 ? def.name[..us] : def.name;
            return $"Wood / {sheet} • {def.Footprint.x}×{def.Footprint.y} cells";
        }

        // Enter Decoration mode: cache the tile info and show the initial popup.
        // After this, UpdateResourceAmount() keeps the remaining HP line current each frame.
        private void ShowResourceNode(Vector3Int cell, string tileName)
        {
            RallyVisual.Instance.Hide();
            _selDecoCell = cell;
            _selDecoHarvestable = Goblin.IsHarvestable(tileName);
            _selDecoBaseDesc = DecorationDesc(tileName);
            _lastAmountLine = "";
            string desc = _selDecoBaseDesc;
            if (_selDecoHarvestable) desc += "\n" + AmountLine(cell, tileName);
            ShowSimple(DecorationName(tileName), desc);
        }

        // Format the remaining resource HP as "Wood: 23 / 50". Uses TreeHP which tracks
        // per-tile hit-point state (shared across all clients on the owner's authority).
        private string AmountLine(Vector3Int cell, string tileName)
        {
            int max = Goblin.MaxHpForCell(cell, tileName);   // boosted wheat fields show 1000, not 500
            int cur = TreeHP.GetHP(cell, max);
            var kind = Goblin.KindOf(tileName);
            return $"{kind}: {cur} / {max}";
        }

        // Map tile-name prefix → human-readable display name for the popup title.
        private static string DecorationName(string tileName)
        {
            if (tileName.StartsWith("Trees_")) return "Oak Tree";
            if (tileName.StartsWith("PineTrees_") || tileName.StartsWith("WinterTrees_")) return "Pine Tree";
            if (tileName.StartsWith("CoconutTrees_")) return "Palm Tree";
            if (tileName.StartsWith("DeadTrees_") || tileName.StartsWith("WinterDeadTrees_")) return "Dead Tree";
            if (tileName.StartsWith("Wheatfield_")) return "Wheat Field";
            if (tileName.StartsWith("BerryBush")) return "Berry Bush";
            if (tileName.StartsWith("Rocks_")) return "Stone Deposit";
            if (tileName.StartsWith("GoldOre_")) return "Gold Deposit";
            if (tileName.StartsWith("IronOre_")) return "Iron Deposit";
            if (tileName.StartsWith("CrystalOre_")) return "Crystal Deposit";
            if (tileName.StartsWith("Cactus_")) return "Cactus";
            if (tileName.StartsWith("Tumbleweed_")) return "Tumbleweed";
            int us = tileName.IndexOf('_');
            return us > 0 ? tileName[..us] : tileName;
        }

        private static string DecorationDesc(string tileName)
        {
            if (Goblin.IsTreeTile(tileName)) return "Chop for Wood";
            if (tileName.StartsWith("Wheatfield_") || tileName.StartsWith("BerryBush")) return "Harvest for Food";
            if (tileName.StartsWith("Rocks_")) return "Mine for Stone";
            if (tileName.StartsWith("GoldOre_")) return "Mine for Gold";
            if (tileName.StartsWith("IronOre_")) return "Mine for Iron";
            if (tileName.StartsWith("CrystalOre_")) return "Mine for Crystal";
            return "Desert flora";
        }
    }
}
