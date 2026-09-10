using System;
using System.Globalization;
using System.IO;
using System.Windows.Media;

namespace SkyAPI.Desktop;

/// <summary>
/// Paleta da interface e preferências de aparência. O tema e o tamanho do texto
/// escolhidos ficam gravados em %LOCALAPPDATA%\Skynova\SkyAPI\preferencias.ini
/// e são reaplicados na abertura seguinte.
/// </summary>
public static class Theme {
    public static bool Dark { get; private set; }

    /// <summary>Fatores de ampliação oferecidos no ajuste de tamanho do texto.</summary>
    public static readonly double[] Scales = { 0.90, 1.00, 1.10, 1.25, 1.40 };
    public static double Scale { get; private set; } = 1.00;

    // Superfícies e traços
    public static Brush Bg = null!, Surface = null!, SurfaceAlt = null!, Hover = null!, Side = null!, Field = null!, Line = null!, LineSoft = null!;
    // Texto
    public static Brush Ink = null!, Muted = null!, Heading = null!;
    // Marca e destaques
    public static Brush Accent = null!, AccentHover = null!, AccentInk = null!, AccentSoft = null!, Cyan = null!;
    // Estados
    public static Brush Ok = null!, OkSoft = null!, Danger = null!, DangerSoft = null!, Warn = null!, WarnSoft = null!, Info = null!, InfoSoft = null!;

    public static event Action? Changed;

    public static void Apply(bool dark) {
        Dark = dark;
        if (dark) {
            Bg = B("#10141C"); Surface = B("#171E29"); SurfaceAlt = B("#1C2432"); Hover = B("#222B3A");
            Side = B("#141A24"); Field = B("#1B2330"); Line = B("#2B3446"); LineSoft = B("#232C3B");
            Ink = B("#E7ECF4"); Muted = B("#97A5BB"); Heading = B("#9CC6F2");
            Accent = B("#3F9BE0"); AccentHover = B("#5AAEEC"); AccentInk = B("#0B1017"); AccentSoft = B("#16283A");
            Cyan = B("#38C4F0");
            Ok = B("#57D69E"); OkSoft = B("#122E24");
            Danger = B("#FF8DA1"); DangerSoft = B("#331A21");
            Warn = B("#F0BE6A"); WarnSoft = B("#31281A");
            Info = B("#7CBDF0"); InfoSoft = B("#152736");
        } else {
            Bg = B("#F4F7FB"); Surface = B("#FFFFFF"); SurfaceAlt = B("#F7FAFD"); Hover = B("#EDF3FA");
            Side = B("#FFFFFF"); Field = B("#FFFFFF"); Line = B("#DDE5EF"); LineSoft = B("#EAF0F7");
            Ink = B("#25334D"); Muted = B("#6D7E96"); Heading = B("#263570");
            Accent = B("#0059A7"); AccentHover = B("#00468A"); AccentInk = B("#FFFFFF"); AccentSoft = B("#E8F2FB");
            Cyan = B("#009DDB");
            Ok = B("#1C7A55"); OkSoft = B("#E7F6EF");
            Danger = B("#AE4056"); DangerSoft = B("#FCEDF0");
            Warn = B("#8A6420"); WarnSoft = B("#FBF3E3");
            Info = B("#0059A7"); InfoSoft = B("#E8F4FC");
        }
        Changed?.Invoke();
    }

    public static void SetScale(double scale) {
        Scale = Math.Clamp(scale, Scales[0], Scales[^1]);
        Changed?.Invoke();
    }

    /// <summary>Muda para o próximo tamanho disponível. Devolve false quando já está no limite.</summary>
    public static bool StepScale(int direction) {
        int index = Array.FindIndex(Scales, s => Math.Abs(s - Scale) < 0.001);
        if (index < 0) index = 1;
        int next = Math.Clamp(index + direction, 0, Scales.Length - 1);
        if (next == index) return false;
        Scale = Scales[next];
        Changed?.Invoke();
        return true;
    }

    public static bool CanStepScale(int direction) {
        int index = Array.FindIndex(Scales, s => Math.Abs(s - Scale) < 0.001);
        if (index < 0) index = 1;
        return index + direction >= 0 && index + direction < Scales.Length;
    }

    public static string ScaleLabel => ((int)Math.Round(Scale * 100)) + "%";

    private static SolidColorBrush B(string hex) {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    // ---- preferências gravadas ----

    private static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Skynova", "SkyAPI");
    private static string File => Path.Combine(Folder, "preferencias.ini");

    /// <summary>Lê as preferências gravadas. Qualquer falha volta ao padrão claro em 100%.</summary>
    public static void Load() {
        bool dark = false;
        double scale = 1.00;
        try {
            if (System.IO.File.Exists(File)) {
                foreach (var line in System.IO.File.ReadAllLines(File)) {
                    var parts = line.Split('=', 2);
                    if (parts.Length != 2) continue;
                    var key = parts[0].Trim().ToLowerInvariant();
                    var value = parts[1].Trim();
                    if (key == "tema") dark = value.Equals("escuro", StringComparison.OrdinalIgnoreCase);
                    else if (key == "escala" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) scale = parsed;
                }
            }
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) {
            // Preferência é conveniência: se não puder ler, segue com o padrão.
        }
        Scale = Math.Clamp(scale, Scales[0], Scales[^1]);
        Apply(dark);
    }

    /// <summary>Grava as preferências. Falhas de disco são ignoradas de propósito.</summary>
    public static void Save() {
        try {
            Directory.CreateDirectory(Folder);
            System.IO.File.WriteAllText(File,
                "# Preferências de aparência do SkyAPI" + Environment.NewLine +
                "tema=" + (Dark ? "escuro" : "claro") + Environment.NewLine +
                "escala=" + Scale.ToString("0.00", CultureInfo.InvariantCulture) + Environment.NewLine);
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Sem preferência gravada o aplicativo continua funcionando normalmente.
        }
    }
}
