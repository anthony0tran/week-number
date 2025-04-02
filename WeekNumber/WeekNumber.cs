using System.Globalization;

namespace WeekNumber;

/// <summary>
/// Represents an ISO week number with its last update timestamp.
/// </summary>
public record WeekNumber
{
    public WeekNumber()
    {
        LastUpdated = DateTime.Now;
        Number = CalculateWeekNumber(LastUpdated);
    }
    
    /// <summary>
    /// Gets the timestamp of when the week number was last updated.
    /// </summary>
    public DateTime LastUpdated { get; private set; }

    /// <summary>
    /// Gets the current ISO week number.
    /// </summary>
    /// <remarks>
    /// The week number follows ISO 8601 standard where weeks start on Monday
    /// and the first week of the year contains January 4th.
    /// </remarks>
    public int Number { get; private set; }
    
    /// <summary>
    /// Updates the week number and timestamp to current date and time.
    /// </summary>
    public void UpdateNumber()
    {
        LastUpdated = DateTime.Now;
        Number = CalculateWeekNumber(LastUpdated);
    }

    /// <summary>
    /// Calculates the ISO week number for a given date.
    /// </summary>
    /// <param name="date">The date to calculate the week number for.</param>
    /// <returns>The ISO week number.</returns>
    private static int CalculateWeekNumber(DateTime date)
    {
        if (date.Year is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(date), "Year must be between 1 and 9999.");
        }
        
        return ISOWeek.GetWeekOfYear(date);
    }
}