using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SitStand.Core;
using SitStand.Core.Model;

namespace SitStand.App.Tray;

/// <summary>
/// Draws the tray icon at runtime so it can encode state at a glance. Shape carries the activity
/// (so it still reads without colour) and colour carries urgency.
/// </summary>
public sealed class TrayIconRenderer
{
    private const int Size = 64;

    private static readonly Color Green = Color.Parse("#22C55E");
    private static readonly Color Amber = Color.Parse("#F59E0B");
    private static readonly Color Red = Color.Parse("#EF4444");
    private static readonly Color Blue = Color.Parse("#3B82F6");
    private static readonly Color Teal = Color.Parse("#14B8A6");
    private static readonly Color Gray = Color.Parse("#9CA3AF");

    private readonly Dictionary<(Shape, Color), WindowIcon> _cache = new();

    public enum Shape { Disc, Ring, Bar }

    public (Shape Shape, Color Color) Describe(TrackerSnapshot s)
    {
        if (s.IsPaused) return (ShapeFor(s.Activity), Gray);

        return s.Activity switch
        {
            Activity.Moving => (Shape.Ring, Blue),
            Activity.Standing => (Shape.Bar, UrgencyColor(s, Teal)),
            _ => (Shape.Disc, UrgencyColor(s, Green)),
        };
    }

    public WindowIcon IconFor(TrackerSnapshot snapshot)
    {
        var key = Describe(snapshot);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var icon = Render(key.Shape, key.Color);
        _cache[key] = icon;
        return icon;
    }

    private static Shape ShapeFor(Activity a) => a switch
    {
        Activity.Moving => Shape.Ring,
        Activity.Standing => Shape.Bar,
        _ => Shape.Disc,
    };

    private static Color UrgencyColor(TrackerSnapshot s, Color calm)
    {
        if (s.HasPendingReminder) return Red;
        return s.ReminderProgress switch
        {
            null => calm,
            >= 1.0 => Red,
            >= 0.75 => Amber,
            _ => calm,
        };
    }

    private static WindowIcon Render(Shape shape, Color color)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(Size, Size), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var brush = new SolidColorBrush(color);
            var center = new Point(Size / 2.0, Size / 2.0);
            const double radius = Size * 0.40;

            switch (shape)
            {
                case Shape.Disc:
                    ctx.DrawEllipse(brush, null, center, radius, radius);
                    break;

                case Shape.Ring:
                    var thickness = Size * 0.18;
                    ctx.DrawEllipse(null, new Pen(brush, thickness), center, radius - thickness / 2, radius - thickness / 2);
                    break;

                case Shape.Bar:
                    var rect = new Rect(Size * 0.32, Size * 0.10, Size * 0.36, Size * 0.80);
                    ctx.DrawRectangle(brush, null, new RoundedRect(rect, Size * 0.12));
                    break;
            }
        }

        return new WindowIcon(bitmap);
    }
}
