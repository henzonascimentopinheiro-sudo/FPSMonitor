using System;
using System.Collections.Generic;
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
        private bool encerrando;
        private bool erroCaptura;

        private int colunaFrameTime = -1;
        private int colunaAplicacao = -1;

        private readonly object sync = new();
        private readonly Queue<double> temposFrames = new();

        private double somaTemposFrames;
        private DateTime ultimaAtualizacaoFps = DateTime.MinValue;

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

        private void MostrarEstado(string estado)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (robloxAberto && IsVisible)
                    FpsText.Text = estado;
            });
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

        private void LimparDadosFps()
        {
            lock (sync)
            {
                temposFrames.Clear();
                somaTemposFrames = 0;
                ultimaAtualizacaoFps = DateTime.MinValue;
                colunaFrameTime = -1;
                colunaAplicacao = -1;
            }
        }

        private void IniciarPresentMon()
        {
            PararPresentMon();
            LimparDadosFps();

            erroCaptura = false;
            encerrando = false;

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
                // Cada instância recebe uma sessão ETW exclusiva.
                string nomeSessao = $"FPSMonitor_{Environment.ProcessId}";

                var inicio = new ProcessStartInfo
                {
                    FileName = caminho,
                    Arguments =
                        "--process_name RobloxPlayerBeta " +
                        "--output_stdout --v2_metrics --no_console_stats " +
                        $"--session_name {nomeSessao}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var processo = new Process
                {
                    StartInfo = inicio,
                    EnableRaisingEvents = true
                };

                processo.OutputDataReceived += LerSaidaPresentMon;
                processo.ErrorDataReceived += LerErroPresentMon;
                processo.Exited += PresentMon_Exited;

                if (!processo.Start())
                {
                    processo.Dispose();
                    FpsText.Text = "ERRO PM";
                    RegistrarLog("Não foi possível iniciar PresentMon.");
                    return;
                }

                presentMon = processo;

                processo.BeginOutputReadLine();
                processo.BeginErrorReadLine();

                RegistrarLog(
                    $"PresentMon iniciado. Sessão: {nomeSessao}");
            }
            catch (Exception ex)
            {
                FpsText.Text = "ERRO PM";
                RegistrarLog("Erro ao iniciar PresentMon: " + ex);
                PararPresentMon();
            }
        }

        private void PresentMon_Exited(object? sender, EventArgs e)
        {
            if (encerrando)
                return;

            int codigo = -1;

            try
            {
                if (sender is Process p)
                    codigo = p.ExitCode;
            }
            catch
            {
                // O processo pode já ter sido descartado.
            }

            RegistrarLog(
                $"PresentMon terminou inesperadamente. Código: {codigo}");

            MostrarEstado(erroCaptura ? "SEM PERM." : "ERRO PM");
        }

        private void LerErroPresentMon(
            object sender,
            DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            string mensagem = e.Data.Trim();
            RegistrarLog("PresentMon stderr: " + mensagem);

            if (mensagem.Contains(
                    "access denied",
                    StringComparison.OrdinalIgnoreCase) ||
                mensagem.Contains(
                    "elevated privilege",
                    StringComparison.OrdinalIgnoreCase) ||
                mensagem.Contains(
                    "administrative privileges",
                    StringComparison.OrdinalIgnoreCase) ||
                mensagem.Contains(
                    "Performance Log Users",
                    StringComparison.OrdinalIgnoreCase))
            {
                erroCaptura = true;
                MostrarEstado("SEM PERM.");
            }
            else if (mensagem.Contains(
                         "already running",
                         StringComparison.OrdinalIgnoreCase))
            {
                MostrarEstado("SESSÃO");
            }
        }

        private void LerSaidaPresentMon(
            object sender,
            DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            string linha = e.Data.Trim();
            string[] campos = linha.Split(',');

            // Identifica o cabeçalho CSV da versão instalada.
            if (campos.Any(c =>
                c.Trim().Trim('"').Equals(
                    "Application",
                    StringComparison.OrdinalIgnoreCase)))
            {
                int indiceFrameTime = Array.FindIndex(
                    campos,
                    c =>
                    {
                        string nome = c.Trim().Trim('"');

                        return nome.Equals(
                                   "MsBetweenPresents",
                                   StringComparison.OrdinalIgnoreCase)
                               || nome.Equals(
                                   "FrameTime",
                                   StringComparison.OrdinalIgnoreCase);
                    });

                int indiceAplicacao = Array.FindIndex(
                    campos,
                    c => c.Trim().Trim('"').Equals(
                        "Application",
                        StringComparison.OrdinalIgnoreCase));

                lock (sync)
                {
                    colunaFrameTime = indiceFrameTime;
                    colunaAplicacao = indiceAplicacao;
                    temposFrames.Clear();
                    somaTemposFrames = 0;
                    ultimaAtualizacaoFps = DateTime.MinValue;
                }

                RegistrarLog("CSV: " + linha);

                if (indiceFrameTime < 0)
                {
                    RegistrarLog(
                        "Nenhuma coluna FrameTime ou MsBetweenPresents encontrada.");
                    MostrarEstado("SEM DADO");
                }
                else
                {
                    RegistrarLog(
                        $"Coluna de tempo encontrada: {campos[indiceFrameTime]}");
                }

                return;
            }

            int colunaTempo;
            int colunaApp;

            lock (sync)
            {
                colunaTempo = colunaFrameTime;
                colunaApp = colunaAplicacao;
            }

            if (colunaTempo < 0 || campos.Length <= colunaTempo)
                return;

            if (colunaApp >= 0 && campos.Length > colunaApp)
            {
                string aplicacao = campos[colunaApp].Trim().Trim('"');

                if (!aplicacao.Equals(
                        "RobloxPlayerBeta.exe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            if (!double.TryParse(
                    campos[colunaTempo].Trim().Trim('"'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double milissegundos))
            {
                return;
            }

            if (!double.IsFinite(milissegundos) ||
                milissegundos <= 0 ||
                milissegundos > 1000)
            {
                return;
            }

            AdicionarFrame(milissegundos);
        }

        private void AdicionarFrame(double milissegundos)
        {
            int? fpsCalculado = null;

            lock (sync)
            {
                temposFrames.Enqueue(milissegundos);
                somaTemposFrames += milissegundos;

                // Mantém aproximadamente um segundo de amostras.
                while (temposFrames.Count > 1 &&
                       somaTemposFrames - temposFrames.Peek() >= 1000.0)
                {
                    somaTemposFrames -= temposFrames.Dequeue();
                }

                DateTime agora = DateTime.UtcNow;

                if ((agora - ultimaAtualizacaoFps).TotalMilliseconds < 250)
                    return;

                ultimaAtualizacaoFps = agora;

                if (somaTemposFrames > 0 && temposFrames.Count > 0)
                {
                    double fps =
                        temposFrames.Count * 1000.0 / somaTemposFrames;

                    if (double.IsFinite(fps) && fps >= 1 && fps <= 1000)
                    {
                        fpsCalculado = (int)Math.Round(fps);
                    }
                }
            }

            if (fpsCalculado.HasValue)
            {
                int valor = fpsCalculado.Value;

                Dispatcher.BeginInvoke(() =>
                {
                    if (robloxAberto && IsVisible && !erroCaptura)
                    {
                        FpsText.Text = valor.ToString(
                            CultureInfo.InvariantCulture);
                    }
                });
            }
        }

        private void PararPresentMon()
        {
            Process? processo = presentMon;
            presentMon = null;

            if (processo == null)
                return;

            encerrando = true;

            try
            {
                processo.OutputDataReceived -= LerSaidaPresentMon;
                processo.ErrorDataReceived -= LerErroPresentMon;
                processo.Exited -= PresentMon_Exited;

                if (!processo.HasExited)
                {
                    processo.Kill(true);
                    processo.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                RegistrarLog("Erro ao encerrar PresentMon: " + ex.Message);
            }
            finally
            {
                processo.Dispose();
                LimparDadosFps();
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
