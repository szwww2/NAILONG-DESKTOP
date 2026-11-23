using System;
using System.IO;
using System.Media;
using System.Reflection;

namespace NailongDesktop
{
    internal sealed class LaughSound : IDisposable
    {
        private readonly SoundPlayer player;
        private readonly Stream wave;

        public LaughSound()
        {
            wave = Open();
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

        internal static Stream Open()
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Nailong.Laugh.wav");
            if (stream == null) throw new InvalidDataException("Missing laugh recording.");
            return stream;
        }
    }
}
