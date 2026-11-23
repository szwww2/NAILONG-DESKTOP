using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NailongDesktop
{
    internal sealed class PetView : Grid
    {
        private readonly PetModel model = new PetModel();
        private readonly Effects effects = new Effects();

        public PetView()
        {
            var viewport = new Viewport3D
            {
                IsHitTestVisible = false,
                ClipToBounds = false,
                Camera = new OrthographicCamera(new Point3D(0, 1.69, 8), new Vector3D(0, -0.26, -8), new Vector3D(0, 1, 0), 3.10)
            };
            var stage = new Model3DGroup();
            stage.Children.Add(new AmbientLight(Color.FromRgb(111, 111, 114)));
            stage.Children.Add(new DirectionalLight(Color.FromRgb(206, 198, 183), new Vector3D(0.55, -0.68, -0.95)));
            stage.Children.Add(new DirectionalLight(Color.FromRgb(100, 111, 135), new Vector3D(-0.91, -0.26, -0.46)));
            stage.Children.Add(new DirectionalLight(Color.FromRgb(69, 67, 60), new Vector3D(0.12, -0.29, 1)));
            stage.Children.Add(model.Model);
            viewport.Children.Add(new ModelVisual3D { Content = stage });
            Children.Add(effects);
            Children.Add(viewport);
            Background = Brushes.Transparent;
        }

        public void Frame(PetAction action, double time, int direction)
        {
            model.SetPose(action, time, direction);
            effects.Update(action, time);
        }

        private sealed class Effects : FrameworkElement
        {
            private PetAction action;
            private double elapsed;

            public void Update(PetAction next, double time)
            {
                if (next == action && next != PetAction.Sleep && next != PetAction.Feed) return;
                action = next;
                elapsed = time;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                drawingContext.PushTransform(new ScaleTransform(ActualWidth / PetArt.Width, ActualHeight / PetArt.Height));
                PetArt.Draw(drawingContext, action, elapsed);
                drawingContext.Pop();
            }
        }
    }
}
