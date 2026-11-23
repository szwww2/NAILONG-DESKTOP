using System;
using System.IO;
using System.Media;

namespace NailongDesktop
{
    internal sealed class LaughSound : IDisposable
    {
        private readonly SoundPlayer player;
        private readonly MemoryStream wave;

        public LaughSound()
        {
            wave = Render();
            player = new SoundPlayer(wave);
            player.Load();
        }

        public void Play()
        {
            try { player.Play(); }
            catch (InvalidOperationException) { }
            catch (TimeoutException) { }
        }

        public void Stop() { player.Stop(); }

        public void Dispose()
        {
            player.Stop();
            player.Dispose();
            wave.Dispose();
        }

        internal static MemoryStream Render()
        {
            const int sampleRate = 22050;
            const double tau = Math.PI * 2;
            double[] starts = { 0.32, 0.57, 0.80, 1.05, 1.30, 1.56, 1.84, 2.11, 2.35, 2.64, 2.90, 3.17, 3.44, 3.69, 3.92 };
            double[] lengths = { 0.24, 0.22, 0.23, 0.24, 0.25, 0.24, 0.25, 0.22, 0.26, 0.24, 0.25, 0.26, 0.24, 0.22, 0.31 };
            double[] pitches = { 185, 171, 177, 166, 156, 197, 182, 161, 173, 177, 165, 174, 166, 154, 139 };
            int samples = (int)(sampleRate * PetArt.LaughSeconds);
            var stream = new MemoryStream(44 + samples * 2);
            var random = new Random(971);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
            {
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + samples * 2);
                writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(samples * 2);

                int burst = 0;
                double phase = 0;
                double previousNoise = 0;
                for (int i = 0; i < samples; i++)
                {
                    double time = (double)i / sampleRate;
                    if (burst < starts.Length - 1 && time >= starts[burst + 1]) burst++;
                    double u = (time - starts[burst]) / lengths[burst];
                    double signal = 0;
                    if (u >= 0 && u < 1)
                    {
                        double pitch = pitches[burst] * (1 - 0.23 * u + 0.115 * Math.Sin(Math.PI * u) + 0.027 * Math.Sin(tau * time * 22));
                        phase += tau * pitch / sampleRate;
                        double attack = Math.Min(1, u * 12);
                        double envelope = attack * Math.Pow(1 - u, 0.8) * (0.8 + 0.2 * Math.Sin(Math.PI * u));
                        double voiced = 0;
                        for (int harmonic = 1; harmonic <= 25; harmonic++)
                        {
                            double f = harmonic * pitch;
                            double resonance = 1.08 * Bell(f, 620 + u * 190, 190) + 0.60 * Bell(f, 1100 + u * 330, 245)
                                + 0.32 * Bell(f, 2530, 360) + 0.11 * Bell(f, 3380, 500);
                            voiced += Math.Sin(harmonic * phase + 0.36 * harmonic) * (0.08 + resonance) / Math.Pow(harmonic, 0.77);
                        }
                        double white = random.NextDouble() * 2 - 1;
                        double breath = white - previousNoise * 0.58;
                        previousNoise = white;
                        double growl = 0.82 + 0.18 * Math.Sin(phase / 2 + 0.7);
                        signal = 0.45 * growl * voiced * envelope * Math.Min(1, u * 8)
                            + 0.13 * breath * envelope * (0.36 + 0.64 * (1 - u));
                    }
                    writer.Write((short)(Math.Tanh(signal) * 27500));
                }
            }
            stream.Position = 0;
            return stream;
        }

        private static double Bell(double frequency, double center, double bandwidth)
        {
            double delta = (frequency - center) / bandwidth;
            return 1 / (1 + delta * delta * delta * delta);
        }
    }
}
