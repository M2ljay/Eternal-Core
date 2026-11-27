using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Automation;
using System.Windows.Shapes;
using System.Windows.Media.Effects;
using GamingMode.Models;

namespace GamingMode.Services;

internal static class ModeTransitionVisual
{
    internal static string ResolveLanguage(string? fallback)
    {
        // Playhub's language is authoritative, including changes made since agent startup.
        string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Playhub", "settings.json");
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(candidate));
                if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals("Language", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                        return Normalize(property.Value.GetString());
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
        }
        return Normalize(fallback);
    }

    private static string Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return language.Trim().ToLowerInvariant().Split('-', '_')[0];
    }

    internal static string Title(ModeKind mode, string language)
    {
        bool gaming = mode == ModeKind.Gaming;
        return Normalize(language) switch
        {
            "it" => gaming ? "Passaggio a Gaming Mode" : "Passaggio a Desktop Mode",
            "es" => gaming ? "Cambiando a Gaming Mode" : "Cambiando a Desktop Mode",
            "fr" => gaming ? "Passage en Gaming Mode" : "Passage en Desktop Mode",
            "de" => gaming ? "Wechsel zu Gaming Mode" : "Wechsel zu Desktop Mode",
            "pt" => gaming ? "Mudando para Gaming Mode" : "Mudando para Desktop Mode",
            "uk" or "ua" => gaming ? "Перехід до Gaming Mode" : "Перехід до Desktop Mode",
            "zh" => gaming ? "正在切换到游戏模式" : "正在切换到桌面模式",
            "ja" => gaming ? "ゲーミングモードに切り替え中" : "デスクトップモードに切り替え中",
            "ko" => gaming ? "게이밍 모드로 전환 중" : "데스크톱 모드로 전환 중",
            "hi" => gaming ? "Gaming Mode में जा रहे हैं" : "Desktop Mode में जा रहे हैं",
            "ru" => gaming ? "Переход в Gaming Mode" : "Переход в Desktop Mode",
            _ => gaming ? "Switching to Gaming Mode" : "Switching to Desktop Mode"
        };
    }

    // The line under "CORE": "Gaming Mode" or "Desktop", in the agent's 12 languages.
    internal static string Subtitle(ModeKind mode, string? language) =>
        CoreText.Get(language, mode == ModeKind.Gaming ? "transition.gaming" : "transition.desktop");

    // Eternal Core: Core's look (ModeTransitionTimeline.cs has the timing). The full sentence
    // ("Switching to Gaming Mode") stays as the screen-reader name of the surface.
    internal static FrameworkElement Create(ModeKind mode, string? language, GamingSplashSettings? settings = null)
    {
        string resolved = ResolveLanguage(language);
        var direction = mode == ModeKind.Gaming ? TransitionDirection.ToGaming : TransitionDirection.ToDesktop;
        var motion = ModeTransitionTimeline.MotionFor(settings?.AnimationEnabled != false, SystemParameters.ClientAreaAnimation);
        return new ModeTransitionScene(direction, motion, Subtitle(mode, resolved), Title(mode, resolved)).Root;
    }

    internal static DropShadowEffect TextShadow() => new() { Color = Colors.Black, ShadowDepth = 0, BlurRadius = 12, Opacity = 1 };

    internal static void AddGlows(Grid root, double width, double height, GamingSplashSettings? settings = null)
    {
        if (settings?.AnimationEnabled == false) return;
        Color color = Color.FromRgb(215, 38, 61);
        try { color = (Color)ColorConverter.ConvertFromString(settings?.AnimationColor ?? "#D7263D"); }
        catch (FormatException) { }
        catch (ArgumentException) { }
        double opacity = settings?.AnimationOpacity ?? 100;
        if (!double.IsFinite(opacity)) opacity = 100;
        root.Children.Add(new ReactiveCircleBackground(color) { IsHitTestVisible = false, Opacity = Math.Clamp(opacity, 0, 100) / 100 });
    }
}

// The scene: the boot's star field filling the screen, and the Core mark with "CORE" and the
// steel line on the boot's 1280x720 canvas (centre and scale of the boot's Core frame).
internal sealed class ModeTransitionScene
{
    private const double CX = 640, CY = 320, S = 1.7;
    private readonly Storyboard _storyboard = new();
    private readonly TransitionDirection _direction;

    internal Grid Root { get; }
    internal TransitionMotion Motion { get; }

    internal ModeTransitionScene(TransitionDirection direction, TransitionMotion motion, string subtitle, string title)
    {
        _direction = direction;
        Motion = motion;
        bool gaming = direction == TransitionDirection.ToGaming;
        bool full = motion == TransitionMotion.Full;

        var stars = CoreMarkDrawing.StarField();
        var canvas = new Canvas { Width = 1280, Height = 720, ClipToBounds = false };
        Root = new Grid
        {
            Background = Brushes.Black, ClipToBounds = true, IsHitTestVisible = false,
            Children =
            {
                // Stars cover the whole screen whatever its shape; the mark keeps its proportions.
                new Viewbox { Stretch = Stretch.UniformToFill, Child = stars },
                new Viewbox { Stretch = Stretch.Uniform, Child = canvas },
            }
        };
        AutomationProperties.SetName(Root, title);

        var unlitBrush = CoreMarkDrawing.Solid(CoreMarkDrawing.RingUnlit);
        var crimson = CoreMarkDrawing.Solid(CoreMarkDrawing.Crimson);
        var steel = CoreMarkDrawing.Solid(CoreMarkDrawing.Steel);

        var mark = new Canvas { Width = 1280, Height = 720 };
        canvas.Children.Add(mark);

        double radius = CoreMarkDrawing.RingRadius * S, ring = CoreMarkDrawing.RingWidth * S, size = radius * 2 + ring;
        Ellipse Ring(Brush brush)
        {
            var ellipse = new Ellipse { Width = size, Height = size, Stroke = brush, StrokeThickness = ring };
            Canvas.SetLeft(ellipse, CX - size / 2);
            Canvas.SetTop(ellipse, CY - size / 2);
            return ellipse;
        }
        mark.Children.Add(Ring(unlitBrush));
        // The charge, from nine o'clock, as in the boot (dash lengths are in stroke widths).
        double dash = 2 * Math.PI * radius / ring;
        var lit = Ring(crimson);
        lit.StrokeDashArray = new DoubleCollection { dash, dash };
        lit.StrokeDashCap = PenLineCap.Round;
        lit.RenderTransformOrigin = new Point(0.5, 0.5);
        lit.RenderTransform = new RotateTransform(180);
        mark.Children.Add(lit);

        var chrome = CoreMarkDrawing.Chrome(absolute: true, top: CY - CoreMarkDrawing.TickOuter * S, bottom: CY + CoreMarkDrawing.TickOuter * S);
        var ticks = new Line[ModeTransitionTimeline.TickCount];
        for (int i = 0; i < ticks.Length; i++)
        {
            var (x1, y1, x2, y2) = CoreMarkDrawing.Tick(i);
            ticks[i] = CoreMarkDrawing.Stroke(CX + x1 * S, CY + y1 * S, CX + x2 * S, CY + y2 * S, chrome, CoreMarkDrawing.TickWidth * S);
            mark.Children.Add(ticks[i]);
        }

        var core = new Canvas { Width = 1280, Height = 720, RenderTransform = new ScaleTransform(1, 1, CX, CY) };
        var outer = new Ellipse { Width = 2 * CoreMarkDrawing.CoreRadius * S, Height = 2 * CoreMarkDrawing.CoreRadius * S, Fill = CoreMarkDrawing.Chrome() };
        Canvas.SetLeft(outer, CX - CoreMarkDrawing.CoreRadius * S); Canvas.SetTop(outer, CY - CoreMarkDrawing.CoreRadius * S);
        var centre = new Ellipse { Width = 2 * CoreMarkDrawing.CentreRadius * S, Height = 2 * CoreMarkDrawing.CentreRadius * S, Fill = crimson };
        Canvas.SetLeft(centre, CX - CoreMarkDrawing.CentreRadius * S); Canvas.SetTop(centre, CY - CoreMarkDrawing.CentreRadius * S);
        core.Children.Add(outer);
        core.Children.Add(centre);
        mark.Children.Add(core);

        // "CORE" exactly as the boot draws it, and the steel line under it.
        var word = CoreMarkDrawing.Spaced("CORE", 40, 26, 646, 540);
        canvas.Children.Add(word);
        var line = new TextBlock
        {
            Text = subtitle, Foreground = steel, FontFamily = new FontFamily("Segoe UI Semilight"), FontSize = 22,
            TextAlignment = TextAlignment.Center, Width = 1280
        };
        Canvas.SetLeft(line, 0);
        Canvas.SetTop(line, 566);
        canvas.Children.Add(line);

        // Final frames first: these are what reduced motion shows, and what each animation ends on.
        stars.Opacity = gaming ? 1 : ModeTransitionTimeline.StarsRest;
        lit.StrokeDashOffset = gaming ? 0 : full ? dash : dash / 2; // still Desktop: the half-lit brand mark
        foreach (var tick in ticks) tick.Opacity = gaming || !full ? ModeTransitionTimeline.TickLit : ModeTransitionTimeline.TickUnlit;
        centre.Opacity = gaming || !full ? 1 : ModeTransitionTimeline.CentreDimmed;

        if (!full)
        {
            Root.Opacity = 0;
            Animate(Root, UIElement.OpacityProperty, 0, 1, new TransitionSpan(0, ModeTransitionTimeline.StaticFade.TotalSeconds), null);
        }
        else if (gaming) BuildPowerUp(stars, mark, lit, dash, ticks, core, word, line);
        else BuildPowerDown(stars, mark, lit, dash, ticks, centre);

        Root.Loaded += (_, _) => _storyboard.Begin(Root, true);
        Root.Unloaded += (_, _) => { try { _storyboard.Stop(Root); } catch (InvalidOperationException) { } };
    }

    private void BuildPowerUp(Canvas stars, Canvas mark, Ellipse lit, double dash, Line[] ticks, Canvas core, FrameworkElement word, TextBlock line)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var reveal = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        stars.Opacity = 0; mark.Opacity = 0; word.Opacity = 0; line.Opacity = 0;
        Animate(stars, UIElement.OpacityProperty, 0, 1, ModeTransitionTimeline.StarsIn, null);
        Animate(mark, UIElement.OpacityProperty, 0, 1, ModeTransitionTimeline.MarkIn, null);
        // An empty dash with round caps still draws a dot: the arc stays hidden until it moves (as in the boot).
        lit.StrokeDashOffset = dash;
        lit.Opacity = 0;
        Animate(lit, UIElement.OpacityProperty, 0, 1, new TransitionSpan(ModeTransitionTimeline.Charge.Begin, 0.01), null);
        Animate(lit, Shape.StrokeDashOffsetProperty, dash, 0, ModeTransitionTimeline.Charge, ease);
        for (int i = 0; i < ticks.Length; i++)
        {
            ticks[i].Opacity = ModeTransitionTimeline.TickUnlit;
            Animate(ticks[i], UIElement.OpacityProperty, ModeTransitionTimeline.TickUnlit, ModeTransitionTimeline.TickLit,
                new TransitionSpan(ModeTransitionTimeline.TickChange(TransitionDirection.ToGaming, i), ModeTransitionTimeline.TickLight), reveal);
        }
        // The lock: the core pulses (the boot's 1.15) with the screensaver's corner-flash glow.
        foreach (string axis in new[] { "ScaleX", "ScaleY" })
            KeyFrames(core, new PropertyPath($"(UIElement.RenderTransform).(ScaleTransform.{axis})"), ModeTransitionTimeline.Pulse.Begin,
                (0, 1), (ModeTransitionTimeline.Pulse.Length / 2, 1.15), (ModeTransitionTimeline.Pulse.Length, 1));
        var glow = new DropShadowEffect { Color = Colors.White, ShadowDepth = 0, BlurRadius = 0, Opacity = 0 };
        mark.Effect = glow;
        KeyFrames(mark, new PropertyPath("(UIElement.Effect).(DropShadowEffect.BlurRadius)"), ModeTransitionTimeline.Lock,
            (0, ModeTransitionTimeline.FlashBlur), (ModeTransitionTimeline.FlashLength, 0));
        var flash = KeyFrames(mark, new PropertyPath("(UIElement.Effect).(DropShadowEffect.Opacity)"), ModeTransitionTimeline.Lock,
            (0, ModeTransitionTimeline.FlashOpacity), (ModeTransitionTimeline.FlashLength, 0));
        // A spent glow still costs a shader pass every frame at 4K: drop it.
        flash.Completed += (_, _) => mark.Effect = null;
        Animate(word, UIElement.OpacityProperty, 0, 1, ModeTransitionTimeline.Wordmark, reveal);
        Animate(line, UIElement.OpacityProperty, 0, 1, ModeTransitionTimeline.Line, reveal);
        AddTickPulse(ticks, ModeTransitionTimeline.TickLit);
    }

    private void BuildPowerDown(Canvas stars, Canvas mark, Ellipse lit, double dash, Line[] ticks, Ellipse centre)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var fade = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        // Everything appears lit, then drains.
        Root.Opacity = 0;
        Animate(Root, UIElement.OpacityProperty, 0, 1, ModeTransitionTimeline.Appear, null);
        stars.Opacity = 1;
        Animate(stars, UIElement.OpacityProperty, 1, ModeTransitionTimeline.StarsRest, ModeTransitionTimeline.StarsOut, fade);
        lit.StrokeDashOffset = 0;
        Animate(lit, Shape.StrokeDashOffsetProperty, 0, dash, ModeTransitionTimeline.Drain, ease);
        // Drained, the round caps would leave a dot at nine o'clock: hide the arc.
        Animate(lit, UIElement.OpacityProperty, 1, 0, new TransitionSpan(ModeTransitionTimeline.Drain.End - 0.01, 0.01), null);
        for (int i = 0; i < ticks.Length; i++)
        {
            ticks[i].Opacity = ModeTransitionTimeline.TickLit;
            Animate(ticks[i], UIElement.OpacityProperty, ModeTransitionTimeline.TickLit, ModeTransitionTimeline.TickUnlit,
                new TransitionSpan(ModeTransitionTimeline.TickChange(TransitionDirection.ToDesktop, i), ModeTransitionTimeline.TickLight), fade);
        }
        centre.Opacity = 1;
        Animate(centre, UIElement.OpacityProperty, 1, ModeTransitionTimeline.CentreDimmed, ModeTransitionTimeline.CentreDim, fade);
        AddTickPulse(ticks, ModeTransitionTimeline.TickUnlit);
    }

    // The hold for a slow switch: each tick dips (or, unlit, glows a little) and returns in
    // turn, round the dial. It starts after the intro, so a quick switch never shows it.
    private void AddTickPulse(Line[] ticks, double rest)
    {
        double peak = rest >= ModeTransitionTimeline.TickLit ? rest * ModeTransitionTimeline.PulseDepth : rest * (1 + ModeTransitionTimeline.PulseDepth * 2);
        for (int i = 0; i < ticks.Length; i++)
        {
            var animation = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromSeconds(ModeTransitionTimeline.PulseBegins(_direction, i)),
                Duration = TimeSpan.FromSeconds(ModeTransitionTimeline.PulsePeriod),
                RepeatBehavior = RepeatBehavior.Forever, FillBehavior = FillBehavior.HoldEnd
            };
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(ModeTransitionTimeline.PulseLength / 2)), ease));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(rest, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(ModeTransitionTimeline.PulseLength)), ease));
            Storyboard.SetTarget(animation, ticks[i]);
            Storyboard.SetTargetProperty(animation, new PropertyPath(UIElement.OpacityProperty));
            _storyboard.Children.Add(animation);
        }
    }

    private void Animate(DependencyObject target, DependencyProperty property, double from, double to, TransitionSpan span, IEasingFunction? ease)
    {
        var animation = new DoubleAnimation
        {
            From = from, To = to, BeginTime = TimeSpan.FromSeconds(span.Begin), Duration = TimeSpan.FromSeconds(span.Length),
            EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        _storyboard.Children.Add(animation);
    }

    private DoubleAnimationUsingKeyFrames KeyFrames(DependencyObject target, PropertyPath path, double begin, params (double At, double Value)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromSeconds(begin), FillBehavior = FillBehavior.HoldEnd };
        foreach (var (at, value) in frames) animation.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at))));
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, path);
        _storyboard.Children.Add(animation);
        return animation;
    }
}
