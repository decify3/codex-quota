using MediaColor = System.Windows.Media.Color;

namespace CodexGuardian.App;

internal readonly record struct GuardianThemePalette(
    MediaColor Canvas,
    MediaColor Surface,
    MediaColor Raised,
    MediaColor Hairline,
    MediaColor Accent);

internal static class GuardianAppearanceCatalog
{
    public static GuardianThemePalette Resolve(string themeMode) => themeMode switch
    {
        "Glass" => Palette(224, 14, 17, 25, 24, 29, 41, 31, 37, 52, 78, 88, 108, 165, 151, 255),
        "Cyber" => Palette(247, 3, 12, 20, 7, 23, 34, 9, 31, 44, 21, 89, 112, 42, 226, 215),
        "Ember" => Palette(244, 31, 24, 20, 45, 36, 30, 57, 46, 39, 84, 69, 59, 242, 166, 90),
        "Aurora" => Palette(244, 9, 10, 24, 21, 21, 43, 33, 30, 59, 56, 52, 87, 181, 156, 255),
        "Moss" => Palette(244, 11, 20, 18, 19, 34, 30, 26, 46, 40, 46, 74, 65, 128, 214, 168),
        "Sakura" => Palette(244, 24, 12, 22, 40, 22, 36, 52, 29, 47, 83, 49, 72, 242, 151, 187),
        "Frost" => Palette(244, 7, 16, 27, 15, 31, 46, 21, 42, 60, 48, 77, 98, 148, 211, 255),
        "Tidal" => Palette(244, 5, 20, 23, 13, 37, 41, 20, 49, 54, 42, 79, 84, 87, 211, 200),
        "Solar" => Palette(244, 22, 18, 12, 38, 31, 20, 51, 41, 26, 83, 67, 39, 245, 196, 90),
        _ => Palette(242, 7, 9, 15, 17, 20, 27, 23, 27, 36, 41, 46, 58, 124, 131, 255),
    };

    private static GuardianThemePalette Palette(
        byte alpha,
        byte canvasRed, byte canvasGreen, byte canvasBlue,
        byte surfaceRed, byte surfaceGreen, byte surfaceBlue,
        byte raisedRed, byte raisedGreen, byte raisedBlue,
        byte hairlineRed, byte hairlineGreen, byte hairlineBlue,
        byte accentRed, byte accentGreen, byte accentBlue) => new(
            MediaColor.FromArgb(alpha, canvasRed, canvasGreen, canvasBlue),
            MediaColor.FromRgb(surfaceRed, surfaceGreen, surfaceBlue),
            MediaColor.FromRgb(raisedRed, raisedGreen, raisedBlue),
            MediaColor.FromRgb(hairlineRed, hairlineGreen, hairlineBlue),
            MediaColor.FromRgb(accentRed, accentGreen, accentBlue));
}
