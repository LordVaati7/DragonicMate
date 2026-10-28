namespace OpenGameMate.Core;

// No catch-up queue. Manual requests, resume and completed attempts reset the
// next due time, so inactivity cannot create a burst of screenshots.
public sealed class PeriodicCaptureSchedule
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(2);
    public TimeSpan Interval { get; private set; } = DefaultInterval;
    public DateTimeOffset? NextDueAt { get; private set; }

    public void Reset(DateTimeOffset now, TimeSpan interval)
    {
        if (interval != TimeSpan.FromMinutes(1) && interval != TimeSpan.FromMinutes(2) &&
            interval != TimeSpan.FromMinutes(5) && interval != TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }
        Interval = interval;
        NextDueAt = now + interval;
    }

    public void Stop() => NextDueAt = null;

    public bool TryClaim(DateTimeOffset now)
    {
        if (NextDueAt is not { } due || now < due) return false;
        NextDueAt = now + Interval;
        return true;
    }
}
