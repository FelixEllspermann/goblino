using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Goblin Unit", fileName = "Goblin")]
    public sealed class GoblinUnitDefinition : ScriptableObject
    {
        public string DisplayName;
        public Sprite Icon;
        public int WoodCost = 50;
        public int PopulationCost = 1;
        public int MaxHp = 20;
        public int AttackDamage = 0;
        public float AttackInterval = 1.5f;
        public int AttackRange = 1;
        [Tooltip("Seconds to produce one unit at a keep")]
        public float SpawnDuration = 3f;
        [Tooltip("Name must match a GoblinSpawner kind so walk frames are applied")]
        public string SpawnerKindName;
    }
}
