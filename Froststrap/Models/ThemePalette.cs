namespace Froststrap.Models
{
    public class ThemePalette
    {
        public string Accent { get; set; } = "#E11D48";
        public string GradientStart { get; set; } = "#7F1D1D";
        public string GradientEnd { get; set; } = "#BE123C";
        public string Purple { get; set; } = "#FB7185";
        public string Background { get; set; } = "#0A0406";
        public string Surface { get; set; } = "#140808";
        public string Hairline { get; set; } = "#4A1518";
        public string Glow { get; set; } = "#C41E3A";

        public ThemePalette Clone() => (ThemePalette)MemberwiseClone();
    }
}
