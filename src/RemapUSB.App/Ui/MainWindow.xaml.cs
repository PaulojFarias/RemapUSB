using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RemapUSB.Actions;
using RemapUSB.Engine;
using RemapUSB.Model;

namespace RemapUSB.Ui;

public partial class MainWindow : Window
{
    private enum View { Devices, Device, Settings }

    private static Services S => App.Services;

    private View _view = View.Devices;
    private DeviceConfig? _device;
    private readonly ObservableCollection<ButtonVm> _buttons = [];
    private bool _recording;

    // Editor
    private ButtonConfig? _editing;
    private ActionConfig _draft = new();
    private OriginalMode _draftOriginal;
    private bool _capturing;
    private bool _suppress;
    private bool _neutralLimitHit;
    private List<AppItemVm> _apps = [];

    // Escuta
    private readonly DispatcherTimer _listenTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _listenSeconds;
    private string? _foundKey;

    public MainWindow()
    {
        InitializeComponent();
        ButtonItems.ItemsSource = _buttons;
        MediaCombo.ItemsSource = KeyNames.MediaKeys.Select(m => m.Name).ToList();
        ActionCombo.ItemsSource = ActionText.All.Select(a => a.Label).ToList();

        _listenTimer.Tick += (_, _) => OnListenTick();
        S.DeviceConnected += OnListenConnected;
        S.Recorded += OnRecorded;
        S.StateChanged += () => Dispatcher.BeginInvoke(Refresh);

        SourceInitialized += (_, _) =>
            System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle)?.AddHook(OnWindowMessage);

        AboutText.Text =$"{Infrastructure.BuildInfo.Describe()}\nConfiguração: {ConfigStore.FilePath}\nLog: {Infrastructure.Log.Folder}";
        Show(View.Devices);
    }

    /// <summary>
    /// Com esta janela em foco, o Windows pode mandar o botão de mídia como WM_APPCOMMAND em vez de
    /// passá-lo pelo hook. Sem tratar, a janela repassa o comando e o Windows executa (ex.: abre o
    /// navegador no Home). Se o botão está remapeado, o comando é descartado; a ação vem do motor.
    /// </summary>
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Infrastructure.Native.WM_APPCOMMAND || S.Paused)
            return IntPtr.Zero;

        var command = (int)(((long)lParam >> 16) & 0x0FFF);
        if (KeyNames.VkForAppCommand(command) is not { } vk)
            return IntPtr.Zero;

        var remapped = S.Config.Devices.Where(d => d.Active).SelectMany(d => d.Buttons)
            .Any(b => b.Part == ButtonPart.Consumer && b.HookVk == vk && b.Action.Type != ActionType.Keep);
        Infrastructure.Log.Write("APPCOMMAND", $"janela do RemapUSB recebeu {KeyNames.VkName(vk)} (comando {command})"
            + (remapped ? ", descartado porque o botão está remapeado" : ", repassado ao Windows"));
        if (!remapped)
            return IntPtr.Zero;

        handled = true;
        return new IntPtr(1);
    }

    // ================= Navegação =================

    private void Show(View view)
    {
        if (_recording && view != View.Device)
            StopRecording();

        _view = view;
        DevicesView.Visibility = view == View.Devices ? Visibility.Visible : Visibility.Collapsed;
        DeviceView.Visibility = view == View.Device ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = view == View.Settings ? Visibility.Visible : Visibility.Collapsed;
        NavDevices.Tag = view == View.Settings ? null : "selected";
        NavSettings.Tag = view == View.Settings ? "selected" : null;
        Refresh();
    }

    private void Refresh()
    {
        PausedBar.Visibility = S.Paused ? Visibility.Visible : Visibility.Collapsed;
        switch (_view)
        {
            case View.Devices: RefreshDevices(); break;
            case View.Device: RefreshDevice(); break;
            case View.Settings: RefreshSettings(); break;
        }
    }

    private void OnNavDevices(object sender, RoutedEventArgs e) => Show(View.Devices);

    /// <summary>Botão "voltar" lateral do mouse: fecha o editor ou volta para a lista de dispositivos.</summary>
    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.XButton1 || ListenOverlay.Visibility == Visibility.Visible)
            return;

        e.Handled = true;
        if (EditorOverlay.Visibility == Visibility.Visible)
            CloseEditor();
        else if (_view == View.Device)
            Show(View.Devices);
    }

    private void OnNavSettings(object sender, RoutedEventArgs e) => Show(View.Settings);

    private void OnResume(object sender, RoutedEventArgs e) => S.Paused = false;

    // ================= Dispositivos =================

    private void RefreshDevices()
    {
        var hasDevices = S.Config.Devices.Count > 0;
        DevicesEmpty.Visibility = hasDevices ? Visibility.Collapsed : Visibility.Visible;
        DevicesList.Visibility = hasDevices ? Visibility.Visible : Visibility.Collapsed;
        DeviceItems.ItemsSource = S.Config.Devices.Select(d => new DeviceVm(d, S.IsConnected)).ToList();
    }

    private void OnDeviceClick(object sender, MouseButtonEventArgs e)
    {
        if (IsInside<CheckBox>(e.OriginalSource) || sender is not FrameworkElement { Tag: DeviceVm vm })
            return;
        OpenDevice(vm.Config);
    }

    private void OpenDevice(DeviceConfig device)
    {
        _device = device;
        _buttons.Clear();
        foreach (var button in device.Buttons)
            _buttons.Add(new ButtonVm(button));
        Show(View.Device);
    }

    private void OnRemoveDevice(object sender, RoutedEventArgs e)
    {
        if (_device is null)
            return;
        var answer = MessageBox.Show(this, $"Remover {_device.Name} e todos os botões gravados?", "Remover dispositivo",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        S.Config.Devices.Remove(_device);
        _device = null;
        S.SaveAndApply();
        Show(View.Devices);
    }

    // ================= Botões do dispositivo =================

    private void RefreshDevice()
    {
        if (_device is null)
        {
            Show(View.Devices);
            return;
        }

        var connected = S.IsConnected(_device.Key);
        DeviceTitle.Text = _device.Name;
        DeviceStatusDot.Fill = connected ? UiBrushes.Success : UiBrushes.Critical;
        DeviceStatusText.Text = $"{(connected ? "Conectado" : "Desconectado")} · VID_{_device.Vid} · PID_{_device.Pid}";

        var hasButtons = _buttons.Count > 0;
        ButtonsEmpty.Visibility = hasButtons ? Visibility.Collapsed : Visibility.Visible;
        ButtonsList.Visibility = hasButtons ? Visibility.Visible : Visibility.Collapsed;
        ButtonsHint.Visibility = hasButtons && !_recording ? Visibility.Visible : Visibility.Collapsed;
        RecordingBar.Visibility = _recording ? Visibility.Visible : Visibility.Collapsed;
        RecordMoreButton.Visibility = hasButtons && !_recording ? Visibility.Visible : Visibility.Collapsed;
        RecordFirstButton.Visibility = _recording ? Visibility.Collapsed : Visibility.Visible;
        RecordMoreButton.IsEnabled = RecordFirstButton.IsEnabled = connected;
        RecordMoreButton.ToolTip = RecordFirstButton.ToolTip = connected ? null : "Conecte o controle para gravar";

        var status = NeutralStatus();
        NeutralPendingBar.Visibility = status.Pending ? Visibility.Visible : Visibility.Collapsed;
        NeutralPendingText.Text = status.Warning;

        foreach (var vm in _buttons)
        {
            vm.CanDelete = !_recording;
            vm.Refresh();
        }
    }

    private void OnStartRecording(object sender, RoutedEventArgs e)
    {
        if (_device is null || !S.IsConnected(_device.Key))
            return;
        _recording = true;
        S.StartRecording(_device.Key);
        RefreshDevice();
    }

    private void OnStopRecording(object sender, RoutedEventArgs e) => StopRecording();

    private void StopRecording()
    {
        if (!_recording)
            return;
        _recording = false;
        S.StopRecording();
        S.SaveAndApply();
        if (_view == View.Device)
            RefreshDevice();
    }

    private void OnRecorded(RecordedButton recorded)
    {
        if (!_recording || _device is null)
            return;

        var existing = _buttons.FirstOrDefault(vm => vm.Config.Part == recorded.Part && (recorded.Part == ButtonPart.Consumer
            ? vm.Config.Code == recorded.Code
            : vm.Config.ScanCode == recorded.ScanCode));

        if (existing is not null)
        {
            if (existing.Config.HookVk is null && recorded.HookVk is not null)
                existing.Config.HookVk = recorded.HookVk;
            Flash(existing);
            return;
        }

        var button = new ButtonConfig
        {
            Name = recorded.KeyName,
            Part = recorded.Part,
            Code = recorded.Code,
            ScanCode = recorded.ScanCode,
            HookVk = recorded.HookVk,
            KeyName = recorded.KeyName,
        };
        _device.Buttons.Add(button);
        var vm = new ButtonVm(button);
        _buttons.Add(vm);
        RefreshDevice();
        Flash(vm);
    }

    private void Flash(ButtonVm vm)
    {
        vm.Flash = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        timer.Tick += (_, _) =>
        {
            vm.Flash = false;
            timer.Stop();
        };
        timer.Start();
    }

    private void OnButtonRowClick(object sender, MouseButtonEventArgs e)
    {
        if (_recording || IsInside<TextBox>(e.OriginalSource) || IsInside<Button>(e.OriginalSource)
            || sender is not FrameworkElement { Tag: ButtonVm vm })
            return;
        OpenEditor(vm.Config);
    }

    /// <summary>Lixeira da linha: tira da lista um botão gravado por engano, sem regravar os outros.</summary>
    private void OnDeleteButtonClick(object sender, RoutedEventArgs e)
    {
        if (_recording || _device is null || sender is not FrameworkElement { Tag: ButtonVm vm })
            return;

        var answer = MessageBox.Show(this, $"Remover o botão {vm.Name} da lista?", "Remover botão",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        _device.Buttons.Remove(vm.Config);
        _buttons.Remove(vm);
        S.SaveAndApply();
        RefreshDevice();
    }

    private void OnButtonNameLostFocus(object sender, RoutedEventArgs e) => S.SaveAndApply();

    // ================= Editor de ação =================

    private void OpenEditor(ButtonConfig button)
    {
        _editing = button;
        _draft = button.Action.Clone();
        _draftOriginal = button.Original;
        _neutralLimitHit = false;
        _capturing = false;

        EditorTitle.Text = button.Name;
        EditorBadgeText.Text = button.Part == ButtonPart.Consumer ? "Mídia" : "Teclado";
        EditorBadge.Background = button.Part == ButtonPart.Consumer ? UiBrushes.MediaBadge : UiBrushes.KeyboardBadge;
        EditorCode.Text = new ButtonVm(button).CodeText;

        _suppress = true;
        ActionCombo.SelectedIndex = Array.FindIndex(ActionText.All, a => a.Type == _draft.Type);
        MediaCombo.SelectedIndex = Math.Max(0, Array.FindIndex(KeyNames.MediaKeys, m => m.Vk == _draft.MediaVk));
        UrlBox.Text = _draft.Url ?? "";
        PathBox.Text = _draft.Path ?? "";
        CmdBox.Text = _draft.Command ?? "";
        AppSearch.Text = "";
        _suppress = false;

        UpdateEditor();
        EditorOverlay.Visibility = Visibility.Visible;
        _ = LoadAppsAsync();
    }

    private async Task LoadAppsAsync()
    {
        if (_apps.Count == 0)
        {
            AppLoading.Visibility = Visibility.Visible;
            _apps = (await AppCatalog.LoadAsync()).Select(a => new AppItemVm(a)).ToList();
        }
        AppLoading.Visibility = Visibility.Collapsed;
        FilterApps();
    }

    private void FilterApps()
    {
        var query = AppSearch.Text.Trim();
        var items = _apps.Where(a => query.Length == 0 || a.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Take(300).ToList();

        // O app escolhido antes aparece selecionado, mesmo que tenha vindo do "Procurar .exe".
        if (_draft.App is { } selected && items.All(i => !i.App.SameAs(selected)) && query.Length == 0)
            items.Insert(0, new AppItemVm(selected));

        _suppress = true;
        AppList.ItemsSource = items;
        AppList.SelectedItem = _draft.App is null ? null : items.FirstOrDefault(i => i.App.SameAs(_draft.App));
        if (AppList.SelectedItem is not null)
            AppList.ScrollIntoView(AppList.SelectedItem);
        _suppress = false;
    }

    private void UpdateEditor()
    {
        if (_editing is null)
            return;

        var type = _draft.Type;
        var isApp = type is ActionType.OpenApp or ActionType.CloseApp or ActionType.ToggleApp or ActionType.RestartApp;
        AppPanel.Visibility = Vis(isApp);
        KeyPanel.Visibility = Vis(type == ActionType.Key);
        MediaPanel.Visibility = Vis(type == ActionType.Media);
        SitePanel.Visibility = Vis(type == ActionType.Site);
        FilePanel.Visibility = Vis(type == ActionType.File);
        CmdPanel.Visibility = Vis(type == ActionType.Command);

        AppSelected.Text = _draft.App is null ? "Nenhum app escolhido" : $"Escolhido: {_draft.App.Name}";
        AppHint.Text = type switch
        {
            ActionType.OpenApp => "Se o app já estiver aberto, traz a janela para a frente.",
            ActionType.CloseApp => "Pede para o app fechar e, se ele não fechar em 3 segundos, encerra à força. Se não estiver aberto, não faz nada.",
            ActionType.ToggleApp => "Abre se estiver fechado; fecha se estiver aberto.",
            ActionType.RestartApp => "Fecha (se estiver aberto) e abre de novo.",
            _ => "",
        } + " Apps da Store e MSIX são abertos pelo pacote, então continuam funcionando depois de atualizados.";

        KeysText.Text = _draft.Keys is { Count: > 0 } ? ActionText.Keys(_draft.Keys) : "nenhuma";
        CaptureButton.Content = _capturing ? "Pressione no teclado…" : "Capturar";

        var keyboard = _editing.Part == ButtonPart.Keyboard;
        SimpleHint.Foreground = UiBrushes.Secondary;
        SimpleHint.Text = type switch
        {
            ActionType.Keep => "O botão continua fazendo o que já fazia, como se o app não existisse.",
            ActionType.None when keyboard => "O botão fica sem função. Para que a tecla original também não chegue aos programas, escolha Neutralizar abaixo.",
            ActionType.None => "O botão fica sem função. A tecla original é bloqueada.",
            _ => "",
        };
        SimpleHint.Visibility = Vis(SimpleHint.Text.Length > 0);

        // Tecla original
        OriginalPanel.Visibility = Vis(keyboard && type != ActionType.Keep);
        MediaOriginalPanel.Visibility = Vis(!keyboard && type != ActionType.Keep);

        var key = _editing.KeyName;
        if (keyboard)
        {
            _suppress = true;
            PassRadio.IsChecked = _draftOriginal == OriginalMode.Pass;
            NeutralRadio.IsChecked = _draftOriginal == OriginalMode.Neutralize;
            _suppress = false;

            OriginalIntro.Text = "Este botão é da parte de teclado do controle. O Windows não diz de qual teclado veio a tecla a tempo de bloqueá-la, então é preciso escolher:";
            PassText.Text = $"A nova ação acontece e a tecla {key} também chega ao programa em foco.";
            var (inUse, _) = NeutralSlots();
            NeutralText.Text = $"A tecla {key} vira uma tecla sem uso em todos os teclados; o app devolve {key} aos outros teclados. Pede administrador e reinício. "
                + $"{inUse}/{ScancodeMap.Capacity} teclas neutralizadas.";

            var lowRisk = KeyNames.IsLowRisk(_editing.Code);
            OriginalWarnText.Text = _neutralLimitHit
                ? $"Apenas {ScancodeMap.Capacity} teclas podem ser neutralizadas. Para neutralizar esta, mude outra tecla neutralizada para Deixar passar."
                : _draftOriginal == OriginalMode.Neutralize
                ? lowRisk
                    ? $"Custo baixo: a tecla {key} é pouco usada. Com o RemapUSB fechado, ela fica sem função em todos os teclados."
                    : $"Custo alto: {key} é uma tecla de uso diário. Com o RemapUSB fechado, ela para de funcionar em todos os teclados."
                : $"Se outro programa estiver em foco, ele recebe {key}."
                  + (_editing.Code == 0x2E ? " No Explorer, com um arquivo selecionado, o arquivo vai para a lixeira." : "");
        }
        else
        {
            MediaOriginalText.Text = _editing.HookVk is null
                ? "Este botão não gera tecla no Windows: só a nova ação acontece."
                : "Bloqueada automaticamente. Botão de mídia: só a nova ação acontece"
                  + (_editing.HookVk == 0xAC ? " (o navegador não abre mais)." : ".");
        }
    }

    private void OnActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || ActionCombo.SelectedIndex < 0)
            return;
        _draft.Type = ActionText.All[ActionCombo.SelectedIndex].Type;
        _capturing = false;
        UpdateEditor();
    }

    private void OnAppSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppress)
            FilterApps();
    }

    private void OnAppSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || AppList.SelectedItem is not AppItemVm vm)
            return;
        _draft.App = vm.App;
        UpdateEditor();
    }

    private void OnBrowseExe(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Programas (*.exe)|*.exe", Title = "Escolher programa" };
        if (dialog.ShowDialog(this) != true)
            return;

        var info = FileVersionInfo.GetVersionInfo(dialog.FileName);
        var name = string.IsNullOrWhiteSpace(info.FileDescription) ? System.IO.Path.GetFileNameWithoutExtension(dialog.FileName) : info.FileDescription;
        _draft.App = new AppRef { Kind = AppKind.Exe, Name = name, ExePath = dialog.FileName };
        FilterApps();
        UpdateEditor();
    }

    private void OnBrowseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Escolher arquivo" };
        if (dialog.ShowDialog(this) == true)
            PathBox.Text = dialog.FileName;
    }

    private void OnCaptureClick(object sender, RoutedEventArgs e)
    {
        _capturing = !_capturing;
        UpdateEditor();
    }

    private void OnOriginalChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress)
            return;

        _neutralLimitHit = false;
        _draftOriginal = NeutralRadio.IsChecked == true ? OriginalMode.Neutralize : OriginalMode.Pass;
        if (_draftOriginal == OriginalMode.Neutralize && !NeutralSlots().Fits)
        {
            // Sem tecla sem uso livre: volta para "deixar passar" e explica por quê.
            _draftOriginal = OriginalMode.Pass;
            _neutralLimitHit = true;
        }
        UpdateEditor();
    }

    /// <summary>
    /// Teclas neutralizadas contando a deste botão como está no editor, e se a deste botão cabe.
    /// A mesma tecla já neutralizada em outro botão não ocupa vaga nova.
    /// </summary>
    private (int InUse, bool Fits) NeutralSlots()
    {
        var others = ScancodeMap.NeutralizedScans(S.Config, except: _editing);
        var takesNewSlot = _editing is not null && !others.Contains(_editing.ScanCode);
        var fits = !takesNewSlot || others.Count < ScancodeMap.Capacity;
        var mine = _draftOriginal == OriginalMode.Neutralize && _draft.Type != ActionType.Keep && takesNewSlot;
        return (others.Count + (mine ? 1 : 0), fits);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing && EditorOverlay.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
                return;

            var keys = new List<ushort>();
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Control)) keys.Add(0x11);
            if (mods.HasFlag(ModifierKeys.Shift)) keys.Add(0x10);
            if (mods.HasFlag(ModifierKeys.Alt)) keys.Add(0x12);
            if (mods.HasFlag(ModifierKeys.Windows)) keys.Add(0x5B);
            keys.Add((ushort)KeyInterop.VirtualKeyFromKey(key));

            _draft.Keys = keys;
            _capturing = false;
            UpdateEditor();
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (EditorOverlay.Visibility == Visibility.Visible)
                CloseEditor();
            else if (ListenOverlay.Visibility == Visibility.Visible)
                StopListen();
        }
    }

    private void OnEditorSave(object sender, RoutedEventArgs e)
    {
        if (_editing is null)
            return;

        _draft.Url = UrlBox.Text.Trim();
        _draft.Path = PathBox.Text.Trim();
        _draft.Command = CmdBox.Text.Trim();
        if (_draft.Type == ActionType.Media)
            _draft.MediaVk = KeyNames.MediaKeys[Math.Max(0, MediaCombo.SelectedIndex)].Vk;
        if (_draft.Type == ActionType.Site && _draft.Url.Length > 0 && !_draft.Url.Contains("://"))
            _draft.Url = "https://" + _draft.Url;

        var error = _draft.Type switch
        {
            ActionType.OpenApp or ActionType.CloseApp or ActionType.ToggleApp or ActionType.RestartApp when _draft.App is null => "Escolha o app antes de salvar.",
            ActionType.Key when _draft.Keys is not { Count: > 0 } => "Capture a tecla ou o atalho antes de salvar.",
            ActionType.Site when _draft.Url.Length == 0 => "Informe o endereço do site.",
            ActionType.File when _draft.Path.Length == 0 => "Escolha o arquivo ou a pasta.",
            ActionType.Command when _draft.Command.Length == 0 => "Informe o comando.",
            _ => null,
        };
        if (error is not null)
        {
            SimpleHint.Text = error;
            SimpleHint.Foreground = UiBrushes.Critical;
            SimpleHint.Visibility = Visibility.Visible;
            return;
        }

        _editing.Action = _draft;
        if (_editing.Part == ButtonPart.Keyboard)
            _editing.Original = _draftOriginal;
        CloseEditor();
        S.SaveAndApply();
    }

    private void OnRemoveButton(object sender, RoutedEventArgs e)
    {
        if (_editing is null || _device is null)
            return;
        _device.Buttons.Remove(_editing);
        var vm = _buttons.FirstOrDefault(b => b.Config == _editing);
        if (vm is not null)
            _buttons.Remove(vm);
        CloseEditor();
        S.SaveAndApply();
    }

    private void OnEditorCancel(object sender, RoutedEventArgs e) => CloseEditor();

    private void OnEditorScrimClick(object sender, MouseButtonEventArgs e) => CloseEditor();

    private void CloseEditor()
    {
        _editing = null;
        _capturing = false;
        EditorOverlay.Visibility = Visibility.Collapsed;
    }

    // ================= Configurações =================

    private (bool Pending, string Title, string Warning, bool CanUndo) NeutralStatus()
    {
        var desired = ScancodeMap.Desired(S.Config);
        var applied = ScancodeMap.ReadApplied();
        var active = applied.Count > 0 && ScancodeMap.IsActive();

        var missing = desired.Where(d => !applied.Contains(d)).ToList();
        var unused = applied.Where(a => !desired.Contains(a)).ToList();
        var pending = missing.Count > 0 || unused.Count > 0;

        var title = applied.Count == 0
            ? "nenhuma aplicada no Windows"
            : $"aplicadas no Windows: {string.Join(", ", applied.Select(a => a.Describe()))}{(active ? "" : " (reinicie o Windows para valer)")}";

        var parts = new List<string>();
        if (missing.Count > 0)
            parts.Add($"Falta aplicar: {string.Join(", ", missing.Select(m => KeyNames.VkName(m.OriginalVk)))}.");
        if (unused.Count > 0)
            parts.Add($"Não é mais usada: {string.Join(", ", unused.Select(u => KeyNames.VkName(u.OriginalVk)))}.");
        if (pending)
            parts.Add("Aplicar pede permissão de administrador e reinício.");

        return (pending, title, string.Join(" ", parts), applied.Count > 0);
    }

    private void RefreshSettings()
    {
        StartupCheck.IsChecked = S.StartWithWindows;
        LogCheck.IsChecked = S.Config.LogToFile;

        var status = NeutralStatus();
        NeutralTitle.Text = $"{ScancodeMap.NeutralizedScans(S.Config).Count}/{ScancodeMap.Capacity} teclas marcadas para neutralizar · {status.Title}";
        NeutralWarn.Visibility = Vis(status.Pending);
        NeutralWarnText.Text = status.Warning;
        ApplyNeutralButton.IsEnabled = status.Pending;
        UndoNeutralButton.IsEnabled = status.CanUndo;
    }

    private void OnStartupClick(object sender, RoutedEventArgs e) => S.StartWithWindows = StartupCheck.IsChecked == true;

    private void OnLogClick(object sender, RoutedEventArgs e)
    {
        S.Config.LogToFile = LogCheck.IsChecked == true;
        Infrastructure.Log.SetEnabled(S.Config.LogToFile);
        S.SaveAndApply();
    }

    private void OnOpenLogFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Infrastructure.Log.Folder);
        Process.Start(new ProcessStartInfo(Infrastructure.Log.Folder) { UseShellExecute = true });
    }

    private async void OnApplyNeutral(object sender, RoutedEventArgs e) => await WriteNeutral(ScancodeMap.Desired(S.Config));

    private async void OnUndoNeutral(object sender, RoutedEventArgs e) => await WriteNeutral([]);

    private async Task WriteNeutral(List<NeutralKey> keys)
    {
        ApplyNeutralButton.IsEnabled = UndoNeutralButton.IsEnabled = false;
        if (await ScancodeMap.WriteAsync(keys))
            S.Tray.Notify("Reinicie o Windows", "A configuração das teclas foi gravada e entra em vigor depois de reiniciar.");
        S.SaveAndApply();
    }

    // ================= Escuta =================

    private void OnAddDevice(object sender, RoutedEventArgs e)
    {
        _foundKey = null;
        _listenSeconds = 30;
        ListenOverlay.Visibility = Visibility.Visible;
        UpdateListen();
        _listenTimer.Start();
    }

    private void OnListenTick()
    {
        _listenSeconds--;
        if (_listenSeconds <= 0)
            _listenTimer.Stop();
        UpdateListen();
    }

    private void OnListenConnected(string key)
    {
        if (ListenOverlay.Visibility != Visibility.Visible || _foundKey is not null || _listenSeconds <= 0)
            return;

        if (S.FindDevice(key) is { } saved)
        {
            ListenText.Text = $"{saved.Name} já está salvo. Conecte outro dispositivo, ou cancele.";
            return;
        }

        _foundKey = key;
        _listenTimer.Stop();
        ListenName.Text = S.ProductName(key) ?? $"Dispositivo {key}";
        ListenParts.ItemsSource = S.DescribeParts(key).Select(p => p switch
        {
            "Teclado" => "Teclado · setas, OK, Menu… · remapeável",
            "Mídia" => "Mídia · Home, Voltar, volume… · remapeável",
            _ => $"{p} · ignorado",
        }).ToList();
        UpdateListen();
    }

    private void UpdateListen()
    {
        var found = _foundKey is not null;
        var timeout = !found && _listenSeconds <= 0;

        ListenTitle.Text = found ? "Dispositivo encontrado" : timeout ? "Nenhum dispositivo conectado" : "Conecte o dispositivo agora";
        if (found)
            ListenText.Text = $"VID_{_foundKey![..4]} · PID_{_foundKey[5..]}";
        else if (timeout)
            ListenText.Text = "Nenhum dispositivo foi conectado nos 30 segundos. Você pode tentar de novo.";
        else if (_listenSeconds == 30)
            ListenText.Text = "O primeiro dispositivo USB conectado nos próximos 30 segundos será salvo. Se ele já estiver plugado, tire e coloque de novo.";

        ListenSeconds.Text = Math.Max(0, _listenSeconds).ToString();
        ListenProgress.Value = Math.Max(0, _listenSeconds);
        ListenCountdown.Visibility = Vis(!found && !timeout);
        ListenFound.Visibility = Vis(found);
        ListenSave.Visibility = Vis(found);
        ListenRetry.Visibility = Vis(timeout);
        ListenCancel.Content = found ? "Descartar" : "Cancelar";
    }

    private void OnListenSave(object sender, RoutedEventArgs e)
    {
        if (_foundKey is null)
            return;

        var device = new DeviceConfig
        {
            Name = string.IsNullOrWhiteSpace(ListenName.Text) ? $"Dispositivo {_foundKey}" : ListenName.Text.Trim(),
            Vid = _foundKey[..4],
            Pid = _foundKey[5..],
        };
        S.Config.Devices.Add(device);
        StopListen();
        S.SaveAndApply();
        Infrastructure.Log.Write("DONGLE", $"{device.Name} salvo ({device.Key})");
        OpenDevice(device);
    }

    private void OnListenCancel(object sender, RoutedEventArgs e) => StopListen();

    private void StopListen()
    {
        _listenTimer.Stop();
        _foundKey = null;
        ListenOverlay.Visibility = Visibility.Collapsed;
    }

#if DEBUG
    internal void DebugShow(string screen)
    {
        var device = S.Config.Devices.FirstOrDefault();
        var parts = screen.Split(':', 2);
        switch (parts[0])
        {
            case "device" when device is not null:
                OpenDevice(device);
                break;
            case "editor" when device is not null:
                OpenDevice(device);
                var button = device.Buttons.FirstOrDefault(b => parts.Length > 1 && b.Name == parts[1]) ?? device.Buttons.FirstOrDefault();
                if (button is not null)
                    OpenEditor(button);
                break;
            case "settings":
                Show(View.Settings);
                break;
            case "listen":
                OnAddDevice(this, new RoutedEventArgs());
                break;
        }
    }
#endif

    // ================= Utilitários =================

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static bool IsInside<T>(object source) where T : DependencyObject
    {
        for (var current = source as DependencyObject; current is not null;
             current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is T)
                return true;
        }
        return false;
    }
}
