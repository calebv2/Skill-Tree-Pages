using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace RevampedSkills.Progression;

internal static class SkillLimitConfig
{
    internal static int? RemoteLimit;
    private static int? serverLimit;
    // Read once on first server use. Restart is deliberate: no live rebuild of held orbs.
    internal static int ServerLimit
    {
        get
        {
            if (serverLimit.HasValue) return serverLimit.Value;
            string path = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "RevampedSkills.json");
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, "{\n  \"MaxOwnedSkills\": 16\n}\n");
                }
                JObject json = JObject.Parse(File.ReadAllText(path));
                JToken? value = json["MaxOwnedSkills"];
                if (value == null || value.Type != JTokenType.Integer ||
                    !SkillLimitPolicy.ValidLimit(value.Value<int>()))
                    throw new InvalidDataException("MaxOwnedSkills must be an integer from 1 to 64.");
                serverLimit = value.Value<int>();
                Core.Logger.Msg($"[SkillLimit] Server limit: {serverLimit}; configuration: {path}");
            }
            catch (Exception ex)
            {
                serverLimit = 8;
                Core.Logger.Error($"[SkillLimit] {ex.Message} Using fallback limit 8; existing skills are retained. Fix {path} and restart.");
            }
            return serverLimit.Value;
        }
    }
}
