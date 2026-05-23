using System.Collections.Generic;
using UnityEngine;

namespace RTSCL.World.Unity
{
    [CreateAssetMenu(menuName = "RTSCL/Building Catalog", fileName = "BuildingCatalog")]
    public sealed class BuildingCatalog : ScriptableObject
    {
        public List<BuildingDefinition> Buildings = new();
    }
}
