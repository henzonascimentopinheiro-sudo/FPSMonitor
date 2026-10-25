
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
        private readonly object sync = new();

        private string CaminhoLog =>
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "FPSMonitor-debug.txt");

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
            Left = SystemParameters.WorkArea.Right - Width - 8;
            Top = SystemParameters.WorkArea.Top + 8;
        }

        private void RegistrarLog(string mensagem)
        {
            try
            {
                File.AppendAllText(
                    CaminhoLog,
                    $"[{DateTime.Now:HH:mm:ss}] {mensagem}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch
            {
                // O log é opcional.
            }
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

            lock (sync)
            {
                colunaFrameTime = -1;
            }

            string caminho = Path.Combine(
                AppContext.BaseDirectory,
                "PresentMon.exe");

            RegistrarLog("Procurando PresentMon: " + caminho);

            if (!File.Exists(caminho))
            {
                FpsText.Text = "SEM PM";
                RegistrarLog("PresentMon.exe não encontrado.");
                return;
            }

            try
            {
                var inicio = new ProcessStartInfo
                {
                    FileName = caminho,
                    Arguments =
                        "--process_name RobloxPlayerBeta " +
                        "--output_stdout --v2_metrics --no_console_stats",
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
                presentMon.ErrorDataReceived += LerErroPresentMon;

                if (!presentMon.Start())
                {
                    FpsText.Text = "ERRO";
                    RegistrarLog("Não foi possível iniciar PresentMon.");
                    return;
                }

                presentMon.BeginOutputReadLine();
                presentMon.BeginErrorReadLine();

                RegistrarLog("PresentMon iniciado.");
            }
            catch (Exception ex)
            {
                FpsText.Text = "ERRO";
                RegistrarLog("Erro ao iniciar: " + ex.Message);
                PararPresentMon();
            }
        }

        private void LerErroPresentMon(
            object sender,
            DataReceivedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
            {
                RegistrarLog("PresentMon stderr: " + e.Data);
            }
        }

        private void LerSaidaPresentMon(
            object sender,
            DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            string linha = e.Data.Trim();

            // Salva o cabeçalho e algumas informações úteis
            // para diagnosticar incompatibilidades de versão.
            if (linha.Contains("Application", StringComparison.OrdinalIgnoreCase))
            {
                RegistrarLog("CSV: " + linha);
            }

            string[] campos = linha.Split(',');

            // O cabeçalho pode variar conforme a versão/métrica.
            if (campos.Any(c =>
                c.Trim().Trim('"').Equals(
                    "Application",
                    StringComparison.OrdinalIgnoreCase)))
            {
                int indice = Array.FindIndex(
                    campos,
                    c =>
                    {
                        string nome = c.Trim().Trim('"');

                        return nome.Equals(
                                   "FrameTime",
                                   StringComparison.OrdinalIgnoreCase)
                               || nome.Equals(
                                   "MsBetweenPresents",
                                   StringComparison.OrdinalIgnoreCase);
                    });

                lock (sync)
                {
                    colunaFrameTime = indice;
                }

                RegistrarLog(
                    indice >= 0
                        ? $"Coluna de tempo de frame encontrada: {campos[indice]}"
                        : "Cabeçalho recebido, mas não encontrei FrameTime ou MsBetweenPresents.");

                return;
            }

            int coluna;

            lock (sync)
            {
                coluna = colunaFrameTime;
            }

            if (coluna < 0 || campos.Length <= coluna)
                return;

            if (!double.TryParse(
                campos[coluna].Trim().Trim('"'),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double milissegundos))
            {
                return;
            }

            if (milissegundos <= 0 || milissegundos > 1000)
                return;

            int fps = (int)Math.Round(1000.0 / milissegundos);

            Dispatcher.BeginInvoke(() =>
            {
                if (robloxAberto && IsVisible)
                {
                    FpsText.Text = fps.ToString(
                        CultureInfo.InvariantCulture);
                }
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
                processo.ErrorDataReceived -= LerErroPresentMon;

                if (!processo.HasExited)
                {
                    processo.Kill(true);
                    processo.WaitForExit(1500);
                }
            }
            catch (Exception ex)
            {
                RegistrarLog("Erro ao encerrar PresentMon: " + ex.Message);
            }
            finally
            {
                processo.Dispose();

                lock (sync)
                {
                    colunaFrameTime = -1;
                }

                RegistrarLog("PresentMon encerrado.");
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
