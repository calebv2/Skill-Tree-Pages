using MelonLoader;

[assembly: MelonInfo(typeof(RevampedSkills.Core), "Revamped Skills - Shrine Only", "0.3.1", "ATT", null)]
[assembly: MelonGame("Alta", "A Township Tale")]

namespace RevampedSkills;

public sealed class Core : MelonMod
{
    internal static MelonLogger.Instance Logger { get; private set; }

    public override void OnInitializeMelon()
    {
        Logger = LoggerInstance;
        new HarmonyLib.Harmony("ATT.RevampedSkills").PatchAll(typeof(Core).Assembly);
        LoggerInstance.Msg("Revamped Skills 0.3.1 initialized: shrine-only build, server JSON skill cap and paged shrine holders. Matching server/client versions required.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Progression.SkillLimitConfig.RemoteLimit = null;
    }
}



