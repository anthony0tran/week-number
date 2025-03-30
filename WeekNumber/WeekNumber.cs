using System.Globalization;

namespace WeekNumber;

public record WeekNumber
{
    public DateTime LastUpdated { get; private set; } = DateTime.Now;

    public int Number { get; private set; } = ISOWeek.GetWeekOfYear(DateTime.Now);

    public void UpdateNumber()
    {
        LastUpdated = DateTime.Now;
        Number = ISOWeek.GetWeekOfYear(LastUpdated);
    }
}