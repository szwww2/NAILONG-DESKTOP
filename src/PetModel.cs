using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace NailongDesktop
{
    internal sealed class PetModel
    {
        private const int PoseRate = 12;
        private readonly MeshGeometry3D mesh = new MeshGeometry3D();
        private readonly Pose[][] actions;
        private readonly AxisAngleRotation3D rotation = new AxisAngleRotation3D(new Vector3D(0, 1, 0), -17);
        private PetAction previousAction = PetAction.Turn;
        private int previousStep = -1;
        private double facing = -17;

        public Model3D Model { get; private set; }

        public PetModel()
        {
            using (var stream = new DeflateStream(Resource("Nailong.Model"), CompressionMode.Decompress))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != 0x314D504E) throw new InvalidDataException("Unrecognized Nailong model format.");
                int points = reader.ReadUInt16();
                var uv = new PointCollection(points);
                for (int i = 0; i < points; i++) uv.Add(new Point(reader.ReadSingle(), reader.ReadSingle()));
                uv.Freeze();
                mesh.TextureCoordinates = uv;

                int indexCount = reader.ReadInt32();
                var triangles = new Int32Collection(indexCount);
                for (int i = 0; i < indexCount; i++) triangles.Add(reader.ReadUInt16());
                triangles.Freeze();
                mesh.TriangleIndices = triangles;

                actions = new Pose[(int)PetAction.Turn][];
                for (int action = 0; action < actions.Length; action++)
                {
                    var frames = new Pose[reader.ReadUInt16()];
                    for (int frame = 0; frame < frames.Length; frame++) frames[frame] = new Pose(reader, points);
                    actions[action] = frames;
                }
            }

            var paint = new ImageBrush(LoadTexture());
            paint.Freeze();
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(paint));
            material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(35, 29, 20)), 28));
            material.Freeze();
            Model = new GeometryModel3D(mesh, material)
            {
                BackMaterial = material,
                Transform = new RotateTransform3D(rotation, new Point3D(0, 1.05, 0))
            };
            SetPose(PetAction.Idle, 0, 1);
        }

        public void SetPose(PetAction action, double seconds, int direction)
        {
            double target = direction == 1 ? -17 : 21;
            if (action == PetAction.Laugh) target *= 1.65;
            facing += (target - facing) * 0.24;
            double turn = Math.Max(0, Math.Min(1, seconds / PetArt.TurnSeconds));
            rotation.Angle = facing + (action == PetAction.Turn ? 360 * turn * turn * (3 - 2 * turn) : 0);

            PetAction clip = action == PetAction.Turn ? PetAction.Idle : action;
            Pose[] frames = actions[(int)clip];
            double frameTime = seconds * PoseRate;
            if (clip == PetAction.Idle || clip == PetAction.Walk || clip == PetAction.Sleep)
                frameTime %= frames.Length;
            else
                frameTime = Math.Min(frameTime, frames.Length - 1);
            const int subdivisions = 4;
            int step = (int)(frameTime * subdivisions);
            if (previousStep == step && previousAction == clip) return;
            previousStep = step;
            previousAction = clip;
            int frame = step / subdivisions;
            int next = (frame + 1) % frames.Length;
            double blend = (step % subdivisions) / (double)subdivisions;
            Apply(frames[frame], frames[next], blend);
        }

        private void Apply(Pose a, Pose b, double blend)
        {
            int count = a.Positions.Length / 3;
            var points = new Point3DCollection(count);
            var normals = new Vector3DCollection(count);
            for (int i = 0; i < count * 3; i += 3)
            {
                points.Add(new Point3D(Interpolate(a.Positions[i], b.Positions[i], blend) / 8192,
                    Interpolate(a.Positions[i + 1], b.Positions[i + 1], blend) / 8192,
                    Interpolate(a.Positions[i + 2], b.Positions[i + 2], blend) / 8192));
                normals.Add(new Vector3D(Interpolate(a.Normals[i], b.Normals[i], blend) / 127,
                    Interpolate(a.Normals[i + 1], b.Normals[i + 1], blend) / 127,
                    Interpolate(a.Normals[i + 2], b.Normals[i + 2], blend) / 127));
            }
            points.Freeze();
            normals.Freeze();
            mesh.Positions = points;
            mesh.Normals = normals;
        }

        private static double Interpolate(int a, int b, double weight) { return a + (b - a) * weight; }

        private static Stream Resource(string name)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) throw new InvalidDataException("Missing resource " + name);
            return stream;
        }

        private static BitmapImage LoadTexture()
        {
            var image = new BitmapImage();
            using (Stream stream = Resource("Nailong.Texture"))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }

        private sealed class Pose
        {
            public readonly short[] Positions;
            public readonly sbyte[] Normals;

            public Pose(BinaryReader reader, int count)
            {
                Positions = new short[count * 3];
                Normals = new sbyte[count * 3];
                for (int i = 0; i < Positions.Length; i++) Positions[i] = reader.ReadInt16();
                for (int i = 0; i < Normals.Length; i++) Normals[i] = reader.ReadSByte();
            }
        }
    }
}
