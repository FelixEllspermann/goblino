using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Building Definition", fileName = "Building")]
    public sealed class BuildingDefinition : ScriptableObject
    {
        public string DisplayName;
        public Sprite Sprite;                          // full building sprite (one PNG)
        public Vector2Int Footprint = Vector2Int.one;  // in cells
        public int WoodCost = 50;
    }
}
