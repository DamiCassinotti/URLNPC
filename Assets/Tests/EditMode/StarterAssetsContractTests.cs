using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

// A canary for the one string-typed coupling in the codebase. CombatantRig
// silences the human driver by name because StarterAssets has no asmdef and
// lands in Assembly-CSharp, which auto-references URLNPC — the reference can't
// go the other way, and this test assembly can't name those types either.
//
// The failure mode that buys: upgrade or rename StarterAssets, or mistype the
// name in the rig, and the lookup quietly finds nothing — no exception, no log,
// just human input left live to fight the NavMeshAgent in agent mode.
public class StarterAssetsContractTests
{
    // Read from the rig itself, not a second copy: the point is to check the
    // names EnableAgentDriver actually looks up, so a typo or a half-finished
    // rename there fails here too.
    static string[] RequiredTypeNames => CombatantRig.HumanDriverTypeNames;

    [Test]
    public void TypesTheRigDisablesByName_StillExist([ValueSource(nameof(RequiredTypeNames))] string typeName)
    {
        Assert.That(FindTypes(typeName), Is.Not.Empty,
            $"CombatantRig disables '{typeName}' by name, but no such type is loaded. " +
            "If StarterAssets was upgraded or renamed, update DisableByTypeName to match.");
    }

    [Test]
    public void TypesTheRigDisablesByName_AreDisableableComponents([ValueSource(nameof(RequiredTypeNames))] string typeName)
    {
        // DisableByTypeName scans GetComponentsInChildren<MonoBehaviour> and
        // sets .enabled, so a match only does anything if it is a MonoBehaviour.
        foreach (Type type in FindTypes(typeName))
        {
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(type), Is.True,
                $"'{type.FullName}' is matched by name but is not a MonoBehaviour, so disabling it would silently no-op.");
        }
    }

    [Test]
    public void TypeNamesAreUnambiguous([ValueSource(nameof(RequiredTypeNames))] string typeName)
    {
        // Name matching disables *every* component whose type has this name.
        // A second type sharing it would be collateral damage.
        List<Type> matches = FindTypes(typeName);
        Assert.That(matches.Count, Is.EqualTo(1),
            "expected exactly one loaded type with this name, found: " +
            string.Join(", ", matches.Select(t => t.FullName)));
    }

    static List<Type> FindTypes(string typeName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(t => t.Name == typeName)
            .ToList();
    }

    // Some loaded assemblies fail to resolve every type; take what they do give.
    static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException e)
        {
            return e.Types.Where(t => t != null);
        }
    }
}
