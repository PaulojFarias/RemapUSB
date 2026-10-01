using System.Windows;
using RemapUSB.Infrastructure;
using RemapUSB.Ui;

namespace RemapUSB;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private EventWaitHandle? _showSignal;
    private MainWindow? _window;
    private bool _exiting;
    private bool _hideNoticeShown;

    internal static Services Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Chamado pelo desinstalador: só desfaz as teclas neutralizadas e sai, sem janela nem bandeja.
        if (e.Args.Contains(Engine.NeutralCleanup.Argument, StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(Engine.NeutralCleanup.Run());
            return;
        }

        // Uma instância só: a segunda pede para a primeira mostrar a janela e sai.
        _singleInstance = new Mutex(true, @"Local\RemapUSB.SingleInstance", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\RemapUSB.Show");
        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("ERRO", args.Exception.ToString());
            args.Handled = true;
        };

        Services = new Services();
        Services.Tray.OpenRequested += ShowMainWindow;
        Services.Tray.PauseToggled += () => Services.Paused = !Services.Paused;
        Services.Tray.StartupToggled += () => Services.StartWithWindows = !Services.StartWithWindows;
        Services.Tray.ExitRequested += ExitApp;
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMainWindow)), null, -1, false);

        if (!e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
            ShowMainWindow();

#if DEBUG
        // Só para conferir telas sem o controle: --screen=device | editor:<botão> | settings | listen
        if (e.Args.FirstOrDefault(a => a.StartsWith("--screen=", StringComparison.OrdinalIgnoreCase)) is { } screen)
            _window?.DebugShow(screen["--screen=".Length..]);
#endif
    }

    public void ShowMainWindow()
    {
        if (_window is null)
        {
            _window = new MainWindow { Icon = Services.Tray.WindowIcon };
            _window.Closing += (_, args) =>
            {
                if (_exiting)
                    return;

                // O X só esconde a janela: o app continua na bandeja.
                args.Cancel = true;
                _window.Hide();
                if (!_hideNoticeShown)
                {
                    _hideNoticeShown = true;
                    Services.Tray.Notify("RemapUSB continua rodando", "O app fica na bandeja, perto do relógio. Para fechar de vez, use Sair no menu do ícone.");
                }
            };
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ExitApp()
    {
        _exiting = true;
        _window?.Close();
        Services.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showSignal?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
