using System;
using System.IO;
using System.Threading;
using System.Windows;
using TgProxyHoizze.UI;

namespace TgProxyHoizze {
    public static class Program {
        private const string MutexName = "Global\\TgProxyHo1zze_AppMutex_7788";

        [STAThread]
        public static void Main(string[] args) {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try {
                    string err = e.ExceptionObject != null ? e.ExceptionObject.ToString() : "Unknown error";
                    File.WriteAllText("crash.log", err);
                    System.Windows.MessageBox.Show("Произошла ошибка при запуске:\n\n" + err, "TG PROXY Ho1zze Error", MessageBoxButton.OK, MessageBoxImage.Error);
                } catch { }
            };

            bool isNewInstance;
            using (var mutex = new Mutex(true, MutexName, out isNewInstance)) {
                if (!isNewInstance) {
                    System.Windows.MessageBox.Show(
                        "TG PROXY Ho1zze уже запущен!",
                        "TG PROXY Ho1zze",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                // Check command line arguments (e.g. autostart --minimized)
                bool startMinimized = false;
                int initialTab = 0;
                if (args != null && args.Length > 0) {
                    for (int i = 0; i < args.Length; i++) {
                        if (args[i].Equals("--minimized", StringComparison.OrdinalIgnoreCase)) {
                            startMinimized = true;
                        } else if (args[i].StartsWith("--tab=", StringComparison.OrdinalIgnoreCase)) {
                            int.TryParse(args[i].Substring(6), out initialTab);
                        }
                    }
                }

                var settings = SettingsManager.Load();

                var app = new System.Windows.Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += (s, e) => {
                    try {
                        File.WriteAllText("crash.log", e.Exception.ToString());
                        System.Windows.MessageBox.Show("Ошибка диспетчера:\n\n" + e.Exception.Message, "TG PROXY Ho1zze Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        e.Handled = true;
                    } catch { }
                };

                var window = new MainWindow(settings, initialTab);

                if (!startMinimized) {
                    window.Show();
                } else {
                    window.WindowState = WindowState.Minimized;
                    window.Hide();
                }

                app.Run(window);
            }
        }
    }
}
