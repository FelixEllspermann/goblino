using NUnit.Framework;
using UnityEngine;
using RTSCL.World.Unity;

public class BuildRequirementsTests
{
    private static BuildingDefinition Def(string name, params BuildingDefinition[] requires)
    {
        var d = ScriptableObject.CreateInstance<BuildingDefinition>();
        d.name = name;
        d.Requires = requires;
        return d;
    }

    [Test]
    public void NoRequirements_AlwaysUnlocked()
    {
        var d = Def("Hut");
        Assert.IsTrue(BuildRequirements.IsUnlocked(d, _ => false));
    }

    [Test]
    public void MissingPrereq_Locked()
    {
        var barracks = Def("Barracks");
        var workshop = Def("Workshop", barracks);
        Assert.IsFalse(BuildRequirements.IsUnlocked(workshop, owned => owned != barracks));
    }

    [Test]
    public void AllPrereqsOwned_Unlocked()
    {
        var barracks = Def("Barracks");
        var workshop = Def("Workshop", barracks);
        Assert.IsTrue(BuildRequirements.IsUnlocked(workshop, owned => owned == barracks));
    }

    [Test]
    public void NullRequires_AlwaysUnlocked()
    {
        var d = Def("Hut");
        d.Requires = null;
        Assert.IsTrue(BuildRequirements.IsUnlocked(d, _ => false));
    }
}
