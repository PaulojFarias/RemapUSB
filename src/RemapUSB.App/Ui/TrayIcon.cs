using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using RemapUSB.Infrastructure;

namespace RemapUSB.Ui;

/// <summary>Ícone e menu da bandeja (área perto do relógio).</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _pause = new();
    private readonly ToolStripMenuItem _startup = new("Iniciar com o Windows");
    private Icon? _active;
    private Icon? _paused;

    public event Action? OpenRequested;
    public event Action? PauseToggled;
    public event Action? StartupToggled;
    public event Action? ExitRequested;

    public TrayIcon()
    {
        _active = DrawIcon(Color.FromArgb(0x00, 0x78, 0xD4));
        _paused = DrawIcon(Color.FromArgb(0x8A, 0x8A, 0x8A));

        _menu.Items.Add(_status);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Abrir RemapUSB", null, (_, _) => OpenRequested?.Invoke()) { Font = new Font(_menu.Font, System.Drawing.FontStyle.Bold) });
        _menu.Items.Add(_pause);
        _menu.Items.Add(_startup);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Sair", null, (_, _) => ExitRequested?.Invoke()));
        _pause.Click += (_, _) => PauseToggled?.Invoke();
        _startup.Click += (_, _) => StartupToggled?.Invoke();

        _icon.ContextMenuStrip = _menu;
        _icon.Icon = _active;
        _icon.Text = "RemapUSB";
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenRequested?.Invoke();
        };
        _icon.Visible = true;
    }

    public System.Windows.Media.ImageSource WindowIcon =>
        System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(_active!.Handle, System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

    public void Update(string status, bool paused, bool startWithWindows)
    {
        _status.Text = status;
        _pause.Text = paused ? "Retomar remapeamento" : "Pausar remapeamento";
        _startup.Checked = startWithWindows;
        _icon.Icon = paused ? _paused : _active;

        // O texto do ícone aceita no máximo 127 caracteres.
        var tip = $"RemapUSB · {status}{(paused ? " · pausado" : "")}";
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    public void Notify(string title, string text) => _icon.ShowBalloonTip(4000, title, text, ToolTipIcon.None);

    /// <summary>Desenha um controle remoto simples, para não depender de arquivo .ico.</summary>
    private static Icon DrawIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new GraphicsPath();
            var rect = new RectangleF(9, 2, 14, 28);
            const float r = 7;
            body.AddArc(rect.X, rect.Y, r * 2, r * 2, 180, 90);
            body.AddArc(rect.Right - r * 2, rect.Y, r * 2, r * 2, 270, 90);
            body.AddArc(rect.Right - r * 2, rect.Bottom - r * 2, r * 2, r * 2, 0, 90);
            body.AddArc(rect.X, rect.Bottom - r * 2, r * 2, r * 2, 90, 90);
            body.CloseFigure();
            using var fill = new SolidBrush(color);
            g.FillPath(fill, body);
            using var white = new SolidBrush(Color.White);
            g.FillEllipse(white, 12.5f, 10, 7, 7);
            g.FillRectangle(white, 13, 21, 6, 2);
            g.FillRectangle(white, 13, 25, 6, 2);
            g.FillEllipse(white, 15, 5, 2, 2);
        }

        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _active?.Dispose();
        _paused?.Dispose();
    }
}
