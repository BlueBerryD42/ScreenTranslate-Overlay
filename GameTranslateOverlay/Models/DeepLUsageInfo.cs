namespace GameTranslateOverlay.Models;

public sealed class DeepLUsageInfo
{
    public DeepLUsageInfo(long used, long limit, bool limitReached)
    {
        Used = used;
        Limit = limit;
        LimitReached = limitReached;
    }

    public long Used { get; }
    public long Limit { get; }
    public bool LimitReached { get; }

    public long Remaining => Limit > Used ? Limit - Used : 0;

    public double UsedFraction => Limit > 0 ? (double)Used / Limit : 0;

    public bool HasQuota => Limit > 0;
}

public enum DeepLQuotaWarningLevel
{
    None,
    Low,
    Critical,
    Exceeded,
}
