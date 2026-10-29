using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace TwinCatAssistant.Native
{
    // The native context bar is drawn in OpenChamber's live palette so the bar
    // and the embedded chat read as one surface. Only validated #rrggbb values
    // from the pinned adapter are applied; everything else keeps the fallback.
    internal static class AssistantTheme
    {
        // Same values as the TwinCAT chat themes (src/themes.mjs), so the bar and
        // the loading screen match the chat before its live palette is read.
        public static readonly ChatTheme Light = new ChatTheme { background = "#fbfcfe", foreground = "#1c2433", muted = "#566176",
            border = "#d8dee8", surface = "#ffffff", primary = "#1e63b8", primaryForeground = "#ffffff",
            error = "#c4314b", warning = "#9a6700", success = "#2e7d32" };
        public static readonly ChatTheme Dark = new ChatTheme { dark = true, background = "#1b1e24", foreground = "#e3e7ee", muted = "#9aa3b2",
            border = "#343a46", surface = "#242933", primary = "#3d8bfd", primaryForeground = "#071a33",
            error = "#f0707a", warning = "#e0b44c", success = "#6cc17a" };
        // TwinCAT's POU icon blue; constant so the badge stays recognizable.
        public static readonly Color PouBadge = Color.FromRgb(0x1f, 0x4f, 0x8f);

        public static ResourceDictionary CreateResources() { return (ResourceDictionary)XamlReader.Parse(Xaml); }

        public static string Key(ChatTheme theme)
        {
            return theme == null ? null : string.Join("|", theme.dark, theme.background, theme.foreground, theme.muted, theme.border,
                theme.surface, theme.primary, theme.primaryForeground, theme.error, theme.warning, theme.success);
        }

        public static bool Apply(ResourceDictionary resources, ChatTheme theme)
        {
            Color background, foreground, muted, border, surface, primary, primaryForeground, error, warning, success;
            if (theme == null || !Parse(theme.background, out background) || !Parse(theme.foreground, out foreground)
                || !Parse(theme.muted, out muted) || !Parse(theme.border, out border) || !Parse(theme.surface, out surface)
                || !Parse(theme.primary, out primary) || !Parse(theme.primaryForeground, out primaryForeground)
                || !Parse(theme.error, out error) || !Parse(theme.warning, out warning) || !Parse(theme.success, out success)) return false;
            resources["Tc.Background"] = Brush(background);
            resources["Tc.Foreground"] = Brush(foreground);
            resources["Tc.Muted"] = Brush(muted);
            resources["Tc.Border"] = Brush(border);
            resources["Tc.Surface"] = Brush(surface);
            resources["Tc.Primary"] = Brush(primary);
            resources["Tc.PrimaryForeground"] = Brush(primaryForeground);
            resources["Tc.HoverOverlay"] = Brush(Color.FromArgb(0x16, foreground.R, foreground.G, foreground.B));
            resources["Tc.Error"] = Brush(error);
            resources["Tc.Warning"] = Brush(warning);
            resources["Tc.Success"] = Brush(success);
            resources["Tc.Neutral"] = Brush(muted);
            resources["Tc.ErrorSurface"] = Brush(Mix(background, error, 0.12));
            resources["Tc.WarningSurface"] = Brush(Mix(background, warning, 0.13));
            resources["Tc.SuccessSurface"] = Brush(Mix(background, success, 0.12));
            resources["Tc.NeutralSurface"] = Brush(Mix(background, foreground, 0.07));
            resources["Tc.PouBadge"] = Brush(PouBadge);
            return true;
        }

        public static bool IsDark(Brush brush)
        {
            var color = (brush as SolidColorBrush)?.Color;
            return color.HasValue && 0.2126 * color.Value.R + 0.7152 * color.Value.G + 0.0722 * color.Value.B < 128;
        }

        private static bool Parse(string value, out Color color)
        {
            color = default(Color);
            if (value == null || !Regex.IsMatch(value, @"\A#[0-9a-fA-F]{6}\z")) return false;
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        private static Color Mix(Color a, Color b, double amount)
        {
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * amount), (byte)(a.G + (b.G - a.G) * amount), (byte)(a.B + (b.B - a.B) * amount));
        }
        private static SolidColorBrush Brush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

        // 16x16 stroke icons in the style of OpenChamber's remix icons.
        public static Geometry Icon(string name)
        {
            switch (name)
            {
                case "history": return Geometry.Parse("M2.5,8 A5.5,5.5 0 1 0 4.1,4.1 M4.1,1.6 L4.1,4.1 L1.6,4.1 M8,5 L8,8 L10.2,9.4");
                case "plus": return Geometry.Parse("M8,3 L8,13 M3,8 L13,8");
                case "more": return Geometry.Parse("M3.4,8 L3.6,8 M7.9,8 L8.1,8 M12.4,8 L12.6,8");
                case "chevron": return Geometry.Parse("M4.5,6.5 L8,10 L11.5,6.5");
                case "attach": return Geometry.Parse("M10.8,4.6 L5.9,9.5 A1.3,1.3 0 0 0 7.7,11.3 L12.5,6.5 A2.7,2.7 0 0 0 8.7,2.7 L3.8,7.6 A4.1,4.1 0 0 0 9.6,13.4 L13.4,9.6");
                case "refresh": return Geometry.Parse("M13,8.5 A5,5 0 1 1 11.6,4.6 M12,1.8 L12,4.9 L8.9,4.9");
                case "check": return Geometry.Parse("M3.5,8.5 L6.5,11.5 L12.5,4.8");
                case "close": return Geometry.Parse("M4.5,4.5 L11.5,11.5 M11.5,4.5 L4.5,11.5");
                case "chat": return Geometry.Parse("M4,2.5 L12,2.5 A1.5,1.5 0 0 1 13.5,4 L13.5,9.5 A1.5,1.5 0 0 1 12,11 L7,11 L4.5,13.5 L4.5,11 L4,11 A1.5,1.5 0 0 1 2.5,9.5 L2.5,4 A1.5,1.5 0 0 1 4,2.5 Z M8,4.8 L8,8.7 M6.05,6.75 L9.95,6.75");
                case "sliders": return Geometry.Parse("M2.5,5 L13.5,5 M2.5,11 L13.5,11 M10.5,3 L10.5,7 M5.5,9 L5.5,13");
                case "book": return Geometry.Parse("M8,4.2 C6.6,3.2 4.6,2.9 2.5,3.3 L2.5,12.6 C4.6,12.2 6.6,12.5 8,13.5 C9.4,12.5 11.4,12.2 13.5,12.6 L13.5,3.3 C11.4,2.9 9.4,3.2 8,4.2 Z M8,4.2 L8,13.5");
                case "pulse": return Geometry.Parse("M1.5,8.5 L4.5,8.5 L6.3,4 L9.4,12.5 L11.2,8.5 L14.5,8.5");
                default: return Geometry.Empty;
            }
        }

        private const string Xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <ControlTemplate x:Key='Tc.ButtonTemplate' TargetType='Button'>
    <Grid SnapsToDevicePixels='True'>
      <Border x:Name='Bd' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='7'/>
      <Border x:Name='Ov' Background='{DynamicResource Tc.HoverOverlay}' CornerRadius='7' Opacity='0'/>
      <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' RecognizesAccessKey='True'/>
    </Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Ov' Property='Opacity' Value='1'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Bd' Property='BorderBrush' Value='{DynamicResource Tc.Primary}'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers>
  </ControlTemplate>
  <Style x:Key='Tc.Button' TargetType='Button'>
    <Setter Property='Template' Value='{StaticResource Tc.ButtonTemplate}'/>
    <Setter Property='Foreground' Value='{DynamicResource Tc.Foreground}'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='BorderBrush' Value='Transparent'/>
    <Setter Property='BorderThickness' Value='1'/>
    <Setter Property='Padding' Value='8,4'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='HorizontalContentAlignment' Value='Center'/>
    <Setter Property='VerticalAlignment' Value='Center'/>
  </Style>
  <Style x:Key='Tc.IconButton' TargetType='Button' BasedOn='{StaticResource Tc.Button}'>
    <Setter Property='Width' Value='28'/>
    <Setter Property='Height' Value='28'/>
    <Setter Property='Padding' Value='0'/>
    <Setter Property='Foreground' Value='{DynamicResource Tc.Muted}'/>
  </Style>
  <Style x:Key='Tc.PrimaryButton' TargetType='Button' BasedOn='{StaticResource Tc.Button}'>
    <Setter Property='Background' Value='{DynamicResource Tc.Primary}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Tc.Primary}'/>
    <Setter Property='Foreground' Value='{DynamicResource Tc.PrimaryForeground}'/>
    <Setter Property='Padding' Value='10,4'/>
    <Setter Property='Height' Value='28'/>
  </Style>
  <Style x:Key='Tc.SecondaryButton' TargetType='Button' BasedOn='{StaticResource Tc.Button}'>
    <Setter Property='Background' Value='{DynamicResource Tc.Surface}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Tc.Border}'/>
    <Setter Property='Padding' Value='10,4'/>
    <Setter Property='Height' Value='28'/>
  </Style>
  <Style x:Key='Tc.MenuRow' TargetType='Button' BasedOn='{StaticResource Tc.Button}'>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Padding' Value='8,6'/>
    <Setter Property='Margin' Value='0'/>
  </Style>
  <Style x:Key='Tc.TextBox' TargetType='TextBox'>
    <Setter Property='Background' Value='{DynamicResource Tc.Surface}'/>
    <Setter Property='Foreground' Value='{DynamicResource Tc.Foreground}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Tc.Border}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Tc.Foreground}'/>
    <Setter Property='Padding' Value='4,2'/>
  </Style>
</ResourceDictionary>";
    }
}
