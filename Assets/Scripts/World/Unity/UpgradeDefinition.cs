using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Upgrade Definition", fileName = "Upgrade")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        public string DisplayName;
        public Sprite Icon;
        public int WoodCost = 0;
        public int IronCost = 0;
        public int GoldCost = 0;
        public int CrystalCost = 0;
        public UpgradeKind Kind;
    }
}
