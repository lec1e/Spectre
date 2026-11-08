using Avalonia.Media;

namespace Froststrap.Models
{
    public sealed class WorkbenchPalette
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public bool IsLight { get; init; }

        public Color Deep { get; init; }
        public Color Canvas { get; init; }
        public Color Surface { get; init; }
        public Color Raised { get; init; }
        public Color RaisedHover { get; init; }
        public Color Overlay { get; init; }
        public Color Line { get; init; }
        public Color LineStrong { get; init; }
        public Color Fg { get; init; }
        public Color FgMuted { get; init; }
        public Color FgFaint { get; init; }
        public Color FgBright { get; init; }
        public Color Accent { get; init; }
        public Color AccentHover { get; init; }
        public Color OnAccent { get; init; }

        public IBrush CanvasBrush => new SolidColorBrush(Canvas);
        public IBrush SurfaceBrush => new SolidColorBrush(Surface);
        public IBrush RaisedBrush => new SolidColorBrush(Raised);
        public IBrush LineBrush => new SolidColorBrush(LineStrong);
        public IBrush FgBrush => new SolidColorBrush(Fg);
        public IBrush FgMutedBrush => new SolidColorBrush(FgMuted);
        public IBrush AccentBrush => new SolidColorBrush(Accent);
        public IBrush OnAccentBrush => new SolidColorBrush(OnAccent);

        public override string ToString() => Name;
    }
}
