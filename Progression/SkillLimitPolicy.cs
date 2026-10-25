using System;

namespace RevampedSkills.Progression;

internal static class SkillLimitPolicy
{
    internal const int Capacity = 64;
    internal const int PageSize = 8;
    internal static bool ValidLimit(int limit) => limit >= 1 && limit <= Capacity;
    internal static bool CanBuy(int owned, int limit) => owned < limit;
    internal static bool AllowUnlock(bool server, bool loading, int owned, int limit)
        => loading || !server || CanBuy(owned, limit);
    internal static int PageCount(int extent) => Math.Max(1, (extent + PageSize - 1) / PageSize);
    internal static int MovePage(int page, int direction, int extent)
        => (page + direction + PageCount(extent)) % PageCount(extent);
    internal static bool Visible(int index, int page, int extent)
        => index < extent && index / PageSize == page;
}
