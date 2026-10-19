namespace AltevaCrawler.Shared.Enums;

public enum JobStatus
{
    Pending = 'P',
    Running = 'R',
    Completed = 'C',
    Failed = 'F',
    Canceled = 'X'
}

public static class JobStatusExtensions
{
    public static char ToChar(this JobStatus status) => (char)status;

    public static JobStatus FromChar(char statusChar) => statusChar switch
    {
        'P' => JobStatus.Pending,
        'R' => JobStatus.Running,
        'C' => JobStatus.Completed,
        'F' => JobStatus.Failed,
        'X' => JobStatus.Canceled,
        _ => throw new ArgumentException($"Unknown JobStatus character: '{statusChar}'", nameof(statusChar))
    };

    public static JobStatus FromString(string? statusStr)
    {
        if (string.IsNullOrWhiteSpace(statusStr))
            return JobStatus.Pending;

        if (statusStr.Length == 1)
            return FromChar(statusStr[0]);

        if (Enum.TryParse<JobStatus>(statusStr, true, out var parsed))
            return parsed;

        return JobStatus.Pending;
    }
}
