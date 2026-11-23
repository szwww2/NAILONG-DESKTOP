using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NailongDesktop
{
    internal enum PetAction { Idle, Laugh, Feed, Sleep, Walk, Turn }

    internal static class PetArt
    {
        public const double Width = 280;
        public const double Height = 310;
        public const double LaughSeconds = 4.3;
        public const double TurnSeconds = 1.35;
        private static readonly Brush Shadow = ShadowBrush();
        private static readonly Brush Cream = Solid(255, 255, 242, 198);
        private static readonly Brush Ink = Solid(255, 79, 50, 28);
        private static readonly Brush Gold = Solid(255, 246, 191, 60);
        private static readonly Brush Biscuit = Solid(255, 206, 142, 68);
        private static readonly Pen Outline = Stroke(1.4);
        private static readonly Typeface Typeface = new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"),
            FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        private static readonly Geometry Star = Geometry.Parse("M0,-8 Q1,-1 7,0 Q1,1 0,8 Q-1,1 -7,0 Q-1,-1 0,-8 Z");

        public static void Draw(DrawingContext dc, PetAction action, double time)
        {
            dc.DrawEllipse(Shadow, null, new Point(140, 284), 71, 8);
            if (action == PetAction.Feed)
            {
                dc.PushOpacity(Math.Sin(Math.PI * Clamp(time / 2.1)));
                Caption(dc, "好吃！", 145, 31, 5);
                Spark(dc, 211, 92 - time * 10);
                if (time < 0.72) DrawBiscuit(dc, 147 + time * 14, 141 - time * 48);
                dc.Pop();
            }
            if (action == PetAction.Sleep)
            {
                double drift = time * 0.55 % 1;
                dc.PushOpacity(Math.Sin(Math.PI * drift));
                Caption(dc, "Z z z", 188 + drift * 22, 49 - drift * 22, 10);
                dc.Pop();
            }
        }

        private static void DrawBiscuit(DrawingContext dc, double x, double y)
        {
            dc.DrawEllipse(Biscuit, Outline, new Point(x, y), 9, 8);
            dc.DrawEllipse(Ink, null, new Point(x - 3, y - 2), 1.4, 1.45);
            dc.DrawEllipse(Ink, null, new Point(x + 3, y + 2), 1.6, 1.5);
        }

        private static void Spark(DrawingContext dc, double x, double y)
        {
            dc.PushTransform(new TranslateTransform(x, y));
            dc.DrawGeometry(Gold, null, Star);
            dc.Pop();
        }

        private static void Caption(DrawingContext dc, string text, double x, double y, double tilt)
        {
            var label = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, Typeface, 19, Ink, 1);
            double width = label.Width + 21;
            dc.PushTransform(new RotateTransform(tilt, x, y + 15));
            dc.DrawRoundedRectangle(Cream, Outline, new Rect(x - width / 2, y, width, 30), 15, 15);
            dc.DrawText(label, new Point(x - label.Width / 2, y + (30 - label.Height) / 2));
            dc.Pop();
        }

        private static double Clamp(double value) { return Math.Max(0, Math.Min(value, 1)); }
        private static Brush ShadowBrush()
        {
            Color center = Color.FromArgb(75, 28, 24, 23);
            Color rim = Color.FromArgb(0, 28, 24, 23);
            var brush = new RadialGradientBrush(center, rim);
            brush.Freeze();
            return brush;
        }


        private static Brush Solid(byte alpha, byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
            brush.Freeze();
            return brush;
        }

        private static Pen Stroke(double width)
        {
            var pen = new Pen(Ink, width);
            pen.Freeze();
            return pen;
        }
    }
}
