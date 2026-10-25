
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace FPSMonitor
{
    public partial class MainWindow : Window
    {
        private readonly SystemMonitor monitor = new();
        private readonly DispatcherTimer timer = new();

        private Process? presentMon;
        private bool robloxAberto;
        private int colunaFrameTime = -1;

        public MainWindow()
        {
            InitializeComponent();

            WindowStartupLocation = WindowStartupLocation.Manual;
            PosicionarOverlay();
            Topmost = true;

            Hide();

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += AtualizarDados;
            timer.Start();
        }

        private void PosicionarOverlay()
        {
            Left = SystemParameters.WorkArea.Right - Width - 12;
            Top = SystemParameters.WorkArea.Top + 12;
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

            bool aberto;

            try
            {
                aberto = Process.GetProcessesByName(
                    "RobloxPlayerBeta").Any();
            }
            catch
            {
                aberto = false;
            }

            if (aberto && !robloxAberto)
            {
                robloxAberto = true;
                FpsText.Text = "--";
                PosicionarOverlay();
                Show();
                Topmost = true;
                IniciarPresentMon();
            }
            else if (!aberto && robloxAberto)
            {
                robloxAberto = false;
                PararPresentMon();
                Hide();
                FpsText.Text = "--";
            }
        }

        private void IniciarPresentMon()
        {
            PararPresentMon();

            string caminho = Path.Combine(
                AppContext.BaseDirectory,
                "PresentMon.exe");

            if (!File.Exists(caminho))
            {
                FpsText.Text = "SEM PM";
                return;
            }

            try
            {
                var inicio = new ProcessStartInfo
                {
                    FileName = caminho,
                    Arguments =
                        "--process_name RobloxPlayerBeta " +
                        "--output_stdout --v1_metrics --no_console_stats",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                presentMon = new Process
                {
                    StartInfo = inicio,
                    EnableRaisingEvents = true
                };

                presentMon.OutputDataReceived += LerSaidaPresentMon;
                presentMon.Start();
                presentMon.BeginOutputReadLine();
            }
            catch
            {
                FpsText.Text = "ERRO";
            }
        }

        private void LerSaidaPresentMon(
            object sender,
            DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            string linha = e.Data;

            if (linha.StartsWith("Application,", StringComparison.OrdinalIgnoreCase))
            {
                string[] cabecalho = linha.Split(',');

                colunaFrameTime = Array.FindIndex(
                    cabecalho,
                    coluna => coluna.Trim().Equals(
                        "MsBetweenPresents",
                        StringComparison.OrdinalIgnoreCase));

                return;
            }

            if (colunaFrameTime < 0)
                return;

            string[] campos = linha.Split(',');

            if (campos.Length <= colunaFrameTime)
                return;

            if (!double.TryParse(
                    campos[colunaFrameTime].Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double milissegundos))
                return;

            if (milissegundos <= 0 || milissegundos > 1000)
                return;

            int fps = (int)Math.Round(1000.0 / milissegundos);

            Dispatcher.BeginInvoke(() =>
            {
                if (robloxAberto)
                    FpsText.Text = fps.ToString();
            });
        }

        private void PararPresentMon()
        {
            if (presentMon == null)
                return;

            try
            {
                presentMon.OutputDataReceived -= LerSaidaPresentMon;

                if (!presentMon.HasExited)
                    presentMon.Kill(true);
            }
            catch
            {
                // O processo pode já ter encerrado.
            }
            finally
            {
                presentMon.Dispose();
                presentMon = null;
                colunaFrameTime = -1;
            }
        }

        private void FecharOverlay_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            timer.Stop();
            PararPresentMon();
            monitor.Dispose();
            base.OnClosed(e);
        }
    }
}
