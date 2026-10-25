
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
        private bool cabecalhoEncontrado;
        private readonly object sync = new();

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
                aberto = Process.GetProcessesByName("RobloxPlayerBeta").Any();
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

            lock (sync)
            {
                colunaFrameTime = -1;
                cabecalhoEncontrado = false;
            }

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
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                presentMon = new Process
                {
                    StartInfo = inicio,
                    EnableRaisingEvents = true
                };

                presentMon.OutputDataReceived += LerSaidaPresentMon;
                presentMon.ErrorDataReceived += (_, _) => { };

                if (!presentMon.Start())
                {
                    FpsText.Text = "ERRO";
                    return;
                }

                presentMon.BeginOutputReadLine();
                presentMon.BeginErrorReadLine();
            }
            catch
            {
                FpsText.Text = "ERRO";
                PararPresentMon();
            }
        }

        private void LerSaidaPresentMon(
            object sender,
            DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            string linha = e.Data.Trim();

            if (linha.StartsWith("Application,", StringComparison.OrdinalIgnoreCase))
            {
                string[] cabecalho = linha.Split(',');

                int coluna = Array.FindIndex(
                    cabecalho,
                    nome => nome.Trim().Equals(
                        "MsBetweenPresents",
                        StringComparison.OrdinalIgnoreCase));

                lock (sync)
                {
                    colunaFrameTime = coluna;
                    cabecalhoEncontrado = true;
                }

                return;
            }

            int indice;

            lock (sync)
            {
                if (!cabecalhoEncontrado)
                    return;

                indice = colunaFrameTime;
            }

            if (indice < 0)
                return;

            string[] campos = linha.Split(',');

            if (campos.Length <= indice)
                return;

            if (!double.TryParse(
                campos[indice].Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double ms))
                return;

            if (ms <= 0 || ms > 1000)
                return;

            int fps = (int)Math.Round(1000.0 / ms);

            Dispatcher.BeginInvoke(() =>
            {
                if (robloxAberto && IsVisible)
                    FpsText.Text = fps.ToString();
            });
        }

        private void PararPresentMon()
        {
            Process? processo = presentMon;
            presentMon = null;

            if (processo == null)
                return;

            try
            {
                processo.OutputDataReceived -= LerSaidaPresentMon;

                if (!processo.HasExited)
                    processo.Kill(true);
            }
            catch
            {
                // O processo pode já ter encerrado.
            }
            finally
            {
                processo.Dispose();

                lock (sync)
                {
                    colunaFrameTime = -1;
                    cabecalhoEncontrado = false;
                }
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
