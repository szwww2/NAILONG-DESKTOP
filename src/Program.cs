using System;
using System.Reflection;
using System.Threading;
using System.Windows;

[assembly: AssemblyTitle("奶龙桌面宠物")]
[assembly: AssemblyProduct("Nailong Desktop")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace NailongDesktop
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\NailongDesktop.Show.v1"))
            using (var instance = new Mutex(true, @"Local\NailongDesktop.Instance.v1", out firstInstance))
            {
                if (!firstInstance)
                {
                    showRequest.Set();
                    return;
                }

                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                var window = new PetWindow(Preferences.Load());
                RegisteredWaitHandle listener = ThreadPool.RegisterWaitForSingleObject(showRequest,
                    delegate { app.Dispatcher.BeginInvoke(new Action(window.ShowPet)); }, null, Timeout.Infinite, false);
                try { app.Run(window); }
                finally
                {
                    listener.Unregister(null);
                    instance.ReleaseMutex();
                }
            }
        }
    }
}
