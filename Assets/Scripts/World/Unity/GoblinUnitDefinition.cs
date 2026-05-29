// GoblinUnitDefinition.cs — ScriptableObject asset that defines a single trainable unit type.
// Assets live under Assets/Data/Units/ (or equivalent). Referenced by:
//   - BuildingDefinition.TrainsUnits (which buildings produce this unit)
//   - GoblinProduction (spawn timer, stat injection)
//   - NetworkCatalog (byte-index mapping for CmdTrainUnit wire messages)
//   - ResourceUI / CardRefs (cost display)
//
// To add a new unit type:
//   1. Create asset via RTSCL/Goblin Unit.
//   2. Set SpawnerKindName to match the kind string in GoblinSpawner (drives walk animation frames).
//   3. Add the asset to the relevant BuildingDefinition.TrainsUnits array.
//   4. Cost is deducted by GoblinProduction.TryStart via ResourceBank.
using UnityEngine;

namespace RTSCL.World.Unity
{
    /// <summary>Asset-driven definition for a trainable goblin unit.
    /// Values live in the .asset file; edit them in the Inspector.
    /// NetworkCatalog maps GoblinUnitDefinition ↔ byte index for CmdTrainUnit wire messages.</summary>
    [CreateAssetMenu(menuName = "RTSCL/Goblin Unit", fileName = "Goblin")]
    public sealed class GoblinUnitDefinition : ScriptableObject
    {
        /// <summary>Human-readable name shown in training card UI.</summary>
        public string DisplayName;

        /// <summary>Icon displayed in the training card and selection panel.</summary>
        public Sprite Icon;

        // --- Training costs (deducted from ResourceBank when training starts) ---
        public int WoodCost = 50;
        public int FoodCost = 0;

        /// <summary>Population slots consumed by one of this unit (vs. PopulationManager cap).
        /// Farmer = 1, Club = 3.</summary>
        public int PopulationCost = 1;

        // --- Combat stats (copied into Goblin MonoBehaviour on spawn) ---
        public int MaxHp = 20;
        /// <summary>Damage per hit. 0 = non-combat unit (Farmer won't auto-attack).</summary>
        public int AttackDamage = 0;
        /// <summary>Seconds between attacks. Tune here; Goblin.Update reads this.</summary>
        public float AttackInterval = 1.5f;
        /// <summary>Attack reach in world cells. Melee = 1; ranged units (e.g. Archer) use a larger value.</summary>
        public int AttackRange = 1;

        /// <summary>If set, this unit is RANGED: on each attack swing it fires this sprite as a
        /// homing projectile (see Arrow.cs) instead of a melee lunge. Null = melee.</summary>
        [Tooltip("Set for ranged units (e.g. Archer's arrow). Null = melee.")]
        public Sprite ProjectileSprite;

        [Tooltip("Visual size multiplier applied to the unit transform on spawn (monsters scale up).")]
        public float WorldScale = 1f;

        [Tooltip("If true, this unit moves only on WATER (boats). Inverts passability: water ok, land blocked.")]
        public bool WaterUnit;

        /// <summary>Seconds to produce one unit. Progress bar in BuildingPaletteUI uses this.</summary>
        [Tooltip("Seconds to produce one unit at a keep")]
        public float SpawnDuration = 3f;

        /// <summary>Must match the "kind" string key used in GoblinSpawner to select the correct
        /// walk-animation frame set for this unit type.</summary>
        [Tooltip("Name must match a GoblinSpawner kind so walk frames are applied")]
        public string SpawnerKindName;
    }
}
