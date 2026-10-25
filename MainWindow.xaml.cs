
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace FPSMonitor
{
    public partial class MainWindow : Window
    {
        private readonly SystemMonitor monitor = new();
        private readonly DispatcherTimer timer = new();

        private bool robloxAberto;

        public MainWindow()
        {
            InitializeComponent();

            // Configura o overlay
            Topmost = true;
            ShowInTaskbar = true;

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += AtualizarDados;
            timer.Start();
        }

        private void AtualizarDados(object? sender, EventArgs e)
        {
            try
            {
                CpuText.Text = $"{monitor.GetCpuUsage():0}%";
                RamText.Text = $"{monitor.GetRamUsage():0} MB";
            }
            catch
            {
                CpuText.Text = "N/D";
                RamText.Text = "N/D";
            }

            // Detecta se o Roblox está aberto
            bool aberto = Process.GetProcessesByName("RobloxPlayerBeta").Any();

            if (aberto != robloxAberto)
            {
                robloxAberto = aberto;

                if (robloxAberto)
                {
                    Show();
                    Topmost = true;
                }
                else
                {
                    Hide();
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            timer.Stop();
            monitor.Dispose();

            base.OnClosed(e);
        }
    }
}
