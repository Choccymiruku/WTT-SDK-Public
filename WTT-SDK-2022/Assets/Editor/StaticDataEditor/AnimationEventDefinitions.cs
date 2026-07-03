using System.Collections.Generic;

/// <summary>
/// Static lookup tables used to populate the dropdowns in <see cref="StaticDataEditor"/>.
/// </summary>
internal static class AnimationEventDefinitions
{
    public static readonly string[] ConditionTypeNames = { "Int", "Float", "Boolean" };

    public static readonly string[] ConditionModeNames =
    {
        "Equal", "NotEqual", "GreaterThan", "LessThan", "GreaterEqualThan", "LessEqualThan"
    };

    public static readonly string[] ParamTypeNames = { "None", "Int32", "Float", "String", "Boolean" };

    public static readonly string[] FunctionNames =
    {
        "None", "Sound", "ThirdAction", "UseProp", "AddAmmoInChamber", "AddAmmoInMag", "Arm", "Cook",
        "DelAmmoChamber", "DelAmmoFromMag", "Disarm", "FireEnd", "FiringBullet", "FoldOff", "FoldOn",
        "IdleStart", "LauncherAppeared", "LauncherDisappeared", "MagHide", "MagIn", "MagOut", "MagShow",
        "MalfunctionOff", "ModChanged", "OffBoltCatch", "OnBoltCatch", "RemoveShell", "StartUtilityOperation",
        "ShellEject", "WeapIn", "WeapOut", "OnBackpackDrop"
    };

    /// <summary>Function names that carry an extra <c>AnimationEventParameter</c> payload.</summary>
    public static readonly HashSet<string> FunctionsWithParameters = new HashSet<string>
    {
        "Sound", "ThirdAction", "UseProp"
    };

    public static readonly string[] BoolConditionNames =
    {
        "Active", "AddAmmoInChamber", "AddAmmoInMag", "AltFire", "Arm", "Armed", "BoltActionReload", "BoltCatch",
        "CanReload", "DelAmmoChamber", "DelAmmoFromMag", "Disarm", "Discharge", "FastHide", "Fire", "Grip", "Hvat",
        "Idle", "IncompatibleAmmo", "Inventory", "IsExternalMag", "LauncherReady", "LoadOne", "MagFull", "MagIn",
        "MagInWeapon", "MagOut", "MagSwap", "ModSet", "OffBoltCatch", "OnBoltCatch", "Patrol", "QuickFire",
        "SetFiremode0", "SetFiremode1", "ShellEject", "StockFolded", "UseLeftHand", "Rechamber",
        "MalfunctionRepair", "MisfireSlideUnknown", "RollCylinder"
    };

    public static readonly string[] FloatConditionNames =
    {
        "DoubleActionFireModeFloat", "Aim_angle", "AmmoInChamber", "AmmoInMag", "AmmoCountForRemove", "CharacterID",
        "Deflected", "EmptyLinksCount", "FireMode", "GestureIndex", "GripWeight", "IdlePosition", "IdleVar",
        "LauncherID", "LeftHandProgress", "RadioCommand", "RelTypeNew", "RelTypeOld", "ShellsInWeapon",
        "ShoulderReach", "SpeedDraw", "SpeedFix", "SpeedReload", "StockAnimationIndex", "SwingSpeed", "ThirdPerson",
        "UnderMod", "UseTimeMultiplier", "WeaponLevel", "CamoraIndex", "CamoraIndexForLoadAmmo",
        "CamoraIndexWithShellForRemove", "CamoraIndexForRemoveAmmo", "CamoraFireIndex", "ChamberIndex",
        "ChamberIndexWithShell"
    };

    public static readonly string[] IntConditionNames =
    {
        "AnimationVariant", "FireVar", "GrenadeAltFire", "GrenadeFire", "LActionIndex", "PlayerState",
        "ThirdAction", "Malfunction", "MalfType"
    };

    /// <summary>
    /// Returns the correct condition-name list for a given condition-type dropdown index.
    /// Index order matches <see cref="ConditionTypeNames"/> (0 = Int, 1 = Float, 2 = Boolean).
    /// </summary>
    public static string[] ConditionNamesForTypeIndex(int typeIndex)
    {
        switch (typeIndex)
        {
            case 0: return IntConditionNames;
            case 1: return FloatConditionNames;
            case 2: return BoolConditionNames;
            default: return System.Array.Empty<string>();
        }
    }
}
