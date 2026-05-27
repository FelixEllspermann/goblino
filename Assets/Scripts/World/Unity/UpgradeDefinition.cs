using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Upgrade Definition", fileName = "Upgrade")]
    public sealed class UpgradeDefinition : ScriptableObject
    {
        public string DisplayName;
        public Sprite Icon;
        public int WoodCost = 100;
        public UpgradeKind Kind;
    }
}
