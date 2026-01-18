﻿namespace WeekNumber.Helpers;

public static class BrushHelper
{
    public static Brush GetBrushFromColor(Color color)
    {
        // Try to match a predefined brush from Brushes class
        foreach (var property in typeof(Brushes).GetProperties())
        {
            if (property.PropertyType != typeof(Brush)) continue;
            var brush = (Brush)property.GetValue(null)!;
            if (brush is SolidBrush solidBrush && solidBrush.Color == color)
                return brush;
        }
        // Fallback: create a new SolidBrush if no match
        return new SolidBrush(color);
    }
}