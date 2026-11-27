using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GamingMode.Services;

// Eternal Core: the pieces of Core's look that the boot sequence (BootSequenceVisual.cs) and
// the mode-switch transitions (ModeTransitionVisual.cs) share, so the three read as one
// family: the palette, the chrome gradient, the spaced capitals, the Core mark's geometry
// (docs/eternal/brand/core-logo-mark.svg, a 140-unit box) and the boot's star field.
// Everything is drawn on the 1280x720 canvas of docs/eternal/brand/preview/index.html and
// scaled to the screen by a Viewbox, so it is vector-sharp at 1080p and 4K. Only solid
// fills and the small chrome gradient: no large gradients that could band on HDR.
internal static class CoreMarkDrawing
{
	internal static readonly Color Crimson = Color.FromRgb(0xD7, 0x26, 0x3D);
	internal static readonly Color RingUnlit = Color.FromRgb(0x3A, 0x3F, 0x46);
	internal static readonly Color Steel = Color.FromRgb(0xC9, 0xD1, 0xD9);
	internal static readonly Color Glint = Color.FromRgb(0xFF, 0x8A, 0x96);

	// The mark, in core-logo-mark.svg units.
	internal const double RingRadius = 62, RingWidth = 9, TickInner = 38, TickOuter = 48, TickWidth = 5, CoreRadius = 22, CentreRadius = 9;

	internal static SolidColorBrush Solid(Color color)
	{
		var brush = new SolidColorBrush(color);
		brush.Freeze();
		return brush;
	}

	internal static LinearGradientBrush Chrome(bool absolute = false, double top = 0, double bottom = 1)
	{
		var brush = new LinearGradientBrush
		{
			StartPoint = new Point(0, top), EndPoint = new Point(0, bottom),
			MappingMode = absolute ? BrushMappingMode.Absolute : BrushMappingMode.RelativeToBoundingBox,
			GradientStops =
			{
				new GradientStop(Color.FromRgb(0xF4, 0xF7, 0xFA), 0),
				new GradientStop(Color.FromRgb(0xAE, 0xB8, 0xC2), 0.48),
				new GradientStop(Color.FromRgb(0x5B, 0x63, 0x6C), 0.52),
				new GradientStop(Color.FromRgb(0xC9, 0xD1, 0xD9), 1),
			}
		};
		brush.Freeze();
		return brush;
	}

	// Widely spaced capitals, centred on x, with the baseline at y.
	internal static FrameworkElement Spaced(string text, double size, double spacing, double centerX, double baseline)
	{
		var row = new StackPanel { Orientation = Orientation.Horizontal };
		var fill = Chrome();
		for (int i = 0; i < text.Length; i++)
		{
			row.Children.Add(new TextBlock
			{
				Text = text[i].ToString(), FontFamily = new FontFamily("Segoe UI"), FontSize = size, Foreground = fill,
				Margin = new Thickness(0, 0, i == text.Length - 1 ? 0 : spacing, 0)
			});
		}
		var holder = new Grid { Width = 1280, Children = { row } };
		row.HorizontalAlignment = HorizontalAlignment.Center;
		holder.RenderTransform = new TranslateTransform(centerX - 640, 0);
		Canvas.SetLeft(holder, 0);
		Canvas.SetTop(holder, baseline - size * 1.08); // Segoe UI ascent is about 1.08 em
		return holder;
	}

	internal static Line Stroke(double x1, double y1, double x2, double y2, Brush brush, double width) => new()
	{
		X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = width,
		StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
	};

	// Tick `index` of the eight, counted clockwise from nine o'clock (where the crimson charge
	// starts), as a segment from the inner to the outer radius in mark units.
	internal static (double X1, double Y1, double X2, double Y2) Tick(int index)
	{
		double angle = Math.PI + index * Math.PI / 4;
		double cos = Math.Round(Math.Cos(angle), 4), sin = Math.Round(Math.Sin(angle), 4);
		return (cos * TickInner, sin * TickInner, cos * TickOuter, sin * TickOuter);
	}

	// The boot's sky: fixed seed, the same stars every time. Steel and near-white, one in
	// twenty crimson. Returned at full opacity; the caller animates it.
	internal static Canvas StarField()
	{
		var random = new Random(7);
		var field = new Canvas { Width = 1280, Height = 720, IsHitTestVisible = false };
		var steel = Solid(Steel);
		var white = Solid(Color.FromRgb(0xE6, 0xEC, 0xF2));
		var crimson = Solid(Crimson);
		for (int i = 0; i < 220; i++)
		{
			double x = random.NextDouble() * 1280, y = random.NextDouble() * 720, size = 1 + random.NextDouble() * 2.2;
			double pick = random.NextDouble();
			var star = new Ellipse { Width = size, Height = size, Fill = pick < 0.05 ? crimson : pick < 0.3 ? white : steel, Opacity = 0.25 + random.NextDouble() * 0.6 };
			Canvas.SetLeft(star, x - size / 2);
			Canvas.SetTop(star, y - size / 2);
			field.Children.Add(star);
		}
		return field;
	}
}
