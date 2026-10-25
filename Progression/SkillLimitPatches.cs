using HarmonyLib;

namespace RevampedSkills.Progression;

[HarmonyPatch(typeof(Alta.Networking.Servers.ConnectToServerUtility), "SetupConnectionToServer")]
internal static class ResetServerSkillLimit
{
    private static void Prefix() => SkillLimitConfig.RemoteLimit = null;
}

[HarmonyPatch(typeof(ProgressionSettings), "get_MaxBoughtSkills")]
internal static class ConfiguredSkillLimit
{
    private static void Postfix(ref uint __result)
    {
        if (NetworkSceneManager.IsServer) __result = (uint)SkillLimitConfig.ServerLimit;
        else if (SkillLimitConfig.RemoteLimit.HasValue) __result = (uint)SkillLimitConfig.RemoteLimit.Value;
    }
}

[HarmonyPatch(typeof(PlayerProgressionManager), "IsSlotAvailable")]
internal static class RefreshSkillCapacity
{
    private static void Prefix(int ___currentlyOwnedSkillsCount, ref bool ___canBuyMoreSkills)
        => ___canBuyMoreSkills = SkillLimitPolicy.CanBuy(___currentlyOwnedSkillsCount,
            (int)ProgressionSettings.Instance.MaxBoughtSkills);
}

[HarmonyPatch(typeof(PlayerProgressionManager), "UnlockSlot")]
internal static class EnforceSkillCapacity
{
    private static bool Prefix(bool isFromLoad, int ___currentlyOwnedSkillsCount)
    {
        // Loading and client replication must preserve existing skills, including over-cap saves.
        bool server = NetworkSceneManager.IsServer;
        return SkillLimitPolicy.AllowUnlock(server, isFromLoad, ___currentlyOwnedSkillsCount,
            server ? SkillLimitConfig.ServerLimit : 8);
    }
}
