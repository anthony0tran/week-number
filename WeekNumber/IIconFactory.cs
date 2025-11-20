namespace WeekNumber;

public interface IIconFactory
{
    Icon CreateNumberIcon(int number, Font font, Brush brush, int iconSize);
}