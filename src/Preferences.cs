using System;
using System.Globalization;
using System.IO;
using System.Xml;
using Microsoft.Win32;

namespace NailongDesktop
{
    internal sealed class Preferences
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupValue = "NailongDesktop";
        private static readonly string PathToSettings = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NailongDesktop", "settings.xml");

        public bool Sound = true;
        public bool Topmost = true;
        public bool Wander;
        public int Size = 100;
        public int? X;
        public int? Y;

        public static Preferences Load()
        {
            var preferences = new Preferences();
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                using (var reader = XmlReader.Create(PathToSettings, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 4096 }))
                    document.Load(reader);

                XmlElement root = document.DocumentElement;
                if (root == null || root.Name != "nailong") return preferences;
                preferences.Sound = GetBool(root, "sound", true);
                preferences.Topmost = GetBool(root, "topmost", true);
                preferences.Wander = GetBool(root, "wander", false);
                int? size = GetInt(root, "size");
                if (size == 75 || size == 100 || size == 125 || size == 150) preferences.Size = size.Value;
                preferences.X = GetInt(root, "x");
                preferences.Y = GetInt(root, "y");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (XmlException) { }
            return preferences;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathToSettings));
                string temp = PathToSettings + ".tmp";
                using (XmlWriter writer = XmlWriter.Create(temp, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartElement("nailong");
                    writer.WriteAttributeString("sound", XmlConvert.ToString(Sound));
                    writer.WriteAttributeString("topmost", XmlConvert.ToString(Topmost));
                    writer.WriteAttributeString("wander", XmlConvert.ToString(Wander));
                    writer.WriteAttributeString("size", XmlConvert.ToString(Size));
                    if (X.HasValue && Y.HasValue)
                    {
                        writer.WriteAttributeString("x", XmlConvert.ToString(X.Value));
                        writer.WriteAttributeString("y", XmlConvert.ToString(Y.Value));
                    }
                    writer.WriteEndElement();
                }
                if (File.Exists(PathToSettings)) File.Replace(temp, PathToSettings, null);
                else File.Move(temp, PathToSettings);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static bool StartsWithWindows
        {
            get
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(StartupValue) is string;
            }
            set
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) key.SetValue(StartupValue, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                    else key.DeleteValue(StartupValue, false);
                }
            }
        }

        private static bool GetBool(XmlElement node, string name, bool fallback)
        {
            bool value;
            return bool.TryParse(node.GetAttribute(name), out value) ? value : fallback;
        }

        private static int? GetInt(XmlElement node, string name)
        {
            int value;
            return int.TryParse(node.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? (int?)value : null;
        }
    }
}
