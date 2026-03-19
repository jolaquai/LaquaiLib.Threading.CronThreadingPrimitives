namespace LaquaiLib.Threading.CronTimer;

internal static class CronHelpers
{
    internal static bool TryParse(string[] crons, out CronExpression[] parsedCrons)
    {
        // Allow both 5-field and 6-field cron expressions when not specifying a format
        return TryValidate(crons, out parsedCrons, CronFormat.Standard) || TryValidate(crons, out parsedCrons, CronFormat.IncludeSeconds);
    }
    internal static bool TryValidate(string[] crons, out CronExpression[] parsedCrons, CronFormat format)
    {
        if (crons is null)
            throw new ArgumentNullException(nameof(crons));
        if (crons.Length == 0)
        {
            parsedCrons = [];
            return true;
        }

        parsedCrons = new CronExpression[crons.Length];

        for (var i = 0; i < crons.Length; i++)
            if (!CronExpression.TryParse(crons[i], format, out parsedCrons[i]))
                return false;

        return true;
    }
}