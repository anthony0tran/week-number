namespace WeekNumber.Helpers;

public static class BrushHelper
{
    public static Brush GetBrushFromColor(Color color)
    {
        // Iterate through the properties of the Brushes class
        foreach (var property in typeof(Brushes).GetProperties())
        {
            if (property.PropertyType != typeof(Brush))
            {
                continue;
            }
            
            var brush = (Brush)property.GetValue(null)!;
            
            if (brush is SolidBrush solidBrush && solidBrush.Color == color)
            {
                return brush; // Return the matching predefined brush
            }
        }

        // If no predefined brush matches, create a new SolidBrush
        return new SolidBrush(color);
    }
}