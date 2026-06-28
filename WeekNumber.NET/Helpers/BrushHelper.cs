﻿namespace WeekNumber.Helpers;

public static class BrushHelper
{
    public static SolidBrush GetBrushFromColor(Color color) => new(color);
}