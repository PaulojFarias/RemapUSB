using System.Globalization;
using System.Resources;
using System.Threading;

namespace RemapUSB.Resources;

/// <summary>
/// Acesso tipado aos recursos localizados (Strings.resx / Strings.pt-BR.resx).
/// A cultura é definida em <see cref="SetCulture"/> (chamado no startup do app).
/// </summary>
internal static class Loc
{
    private static readonly ResourceManager _rm = new("RemapUSB.Resources.Strings", typeof(Loc).Assembly);
    private static CultureInfo _culture = CultureInfo.CurrentUICulture;

    /// <summary>
    /// Define a cultura usada para buscar strings. Chame no startup antes de criar qualquer UI.
    /// </summary>
    public static void SetCulture(CultureInfo culture)
    {
        _culture = culture ?? CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = _culture;
        Thread.CurrentThread.CurrentCulture = _culture;
    }

    /// <summary>Cultura atual.</summary>
    public static CultureInfo Culture => _culture;

    /// <summary>Obtém uma string formatada (suporta {0}, {1}...).</summary>
    public static string Get(string name, params object[] args)
    {
        var value = _rm.GetString(name, _culture) ?? name;
        return args.Length > 0 ? string.Format(_culture, value, args) : value;
    }

    // ============ Navigation ============
    public static string NavDevices => Get(nameof(NavDevices));
    public static string NavSettings => Get(nameof(NavSettings));
    public static string Back => Get(nameof(Back));

    // ============ Devices View ============
    public static string DevicesTitle => Get(nameof(DevicesTitle));
    public static string NoDevicesSaved => Get(nameof(NoDevicesSaved));
    public static string NoDevicesSavedDescription => Get(nameof(NoDevicesSavedDescription));
    public static string AddDevice => Get(nameof(AddDevice));
    public static string SavedDevices => Get(nameof(SavedDevices));
    public static string DeviceSubtitle(string vid, string pid, int recorded, int remapped) => Get(nameof(DeviceSubtitle), vid, pid, recorded, remapped);
    public static string StatusConnected => Get(nameof(StatusConnected));
    public static string StatusDisconnected => Get(nameof(StatusDisconnected));
    public static string ActiveCheckbox => Get(nameof(ActiveCheckbox));
    public static string ActiveTooltip => Get(nameof(ActiveTooltip));
    public static string RemoveDevice => Get(nameof(RemoveDevice));

    // ============ Device View (Buttons) ============
    public static string RecordMoreButtons => Get(nameof(RecordMoreButtons));
    public static string RecordingTitle => Get(nameof(RecordingTitle));
    public static string RecordingDescription => Get(nameof(RecordingDescription));
    public static string FinishRecording => Get(nameof(FinishRecording));
    public static string NeutralPendingTitle => Get(nameof(NeutralPendingTitle));
    public static string NeutralPendingGoSettings => Get(nameof(NeutralPendingGoSettings));
    public static string NoButtonsRecorded => Get(nameof(NoButtonsRecorded));
    public static string NoButtonsRecordedDescription => Get(nameof(NoButtonsRecordedDescription));
    public static string RecordButtons => Get(nameof(RecordButtons));
    public static string ConnectDeviceToRecord => Get(nameof(ConnectDeviceToRecord));
    public static string ColumnButton => Get(nameof(ColumnButton));
    public static string ColumnPart => Get(nameof(ColumnPart));
    public static string ColumnAction => Get(nameof(ColumnAction));
    public static string PartMedia => Get(nameof(PartMedia));
    public static string PartKeyboard => Get(nameof(PartKeyboard));
    public static string DeleteButtonTooltip => Get(nameof(DeleteButtonTooltip));
    public static string ButtonsHint => Get(nameof(ButtonsHint));

    // ============ Settings View ============
    public static string SettingsTitle => Get(nameof(SettingsTitle));
    public static string StartupWithWindows => Get(nameof(StartupWithWindows));
    public static string StartupWithWindowsDescription => Get(nameof(StartupWithWindowsDescription));
    public static string LogToFile => Get(nameof(LogToFile));
    public static string LogToFileDescription => Get(nameof(LogToFileDescription));
    public static string OpenLogFolder => Get(nameof(OpenLogFolder));
    public static string NeutralizedKeysSection => Get(nameof(NeutralizedKeysSection));
    public static string ApplyNeutralButton => Get(nameof(ApplyNeutralButton));
    public static string UndoAllNeutralButton => Get(nameof(UndoAllNeutralButton));
    public static string NeutralKeysDescription => Get(nameof(NeutralKeysDescription));
    public static string NeutralInUse(int count) => Get(nameof(NeutralInUse), count);
    public static string NeutralKeysList(string keys) => Get(nameof(NeutralKeysList), keys);
    public static string NeutralWarningAdmin => Get(nameof(NeutralWarningAdmin));
    public static string NeutralWarningActive => Get(nameof(NeutralWarningActive));
    public static string AboutSection => Get(nameof(AboutSection));
    public static string AppName => Get(nameof(AppName));

    // ============ Action Editor ============
    public static string EditorTitle => Get(nameof(EditorTitle));
    public static string ActionLabel => Get(nameof(ActionLabel));
    public static string RemoveButton => Get(nameof(RemoveButton));
    public static string Cancel => Get(nameof(Cancel));
    public static string Save => Get(nameof(Save));

    // Action Types
    public static string ActionKeep => Get(nameof(ActionKeep));
    public static string ActionNone => Get(nameof(ActionNone));
    public static string ActionKey => Get(nameof(ActionKey));
    public static string ActionMedia => Get(nameof(ActionMedia));
    public static string ActionOpenApp => Get(nameof(ActionOpenApp));
    public static string ActionCloseApp => Get(nameof(ActionCloseApp));
    public static string ActionToggleApp => Get(nameof(ActionToggleApp));
    public static string ActionRestartApp => Get(nameof(ActionRestartApp));
    public static string ActionSite => Get(nameof(ActionSite));
    public static string ActionFile => Get(nameof(ActionFile));
    public static string ActionCommand => Get(nameof(ActionCommand));

    // App Panel
    public static string AppLabel => Get(nameof(AppLabel));
    public static string AppSearchPlaceholder => Get(nameof(AppSearchPlaceholder));
    public static string AppLoading => Get(nameof(AppLoading));
    public static string BrowseExe => Get(nameof(BrowseExe));
    public static string AppSelected(string name) => Get(nameof(AppSelected), name);
    public static string AppHintPackage => Get(nameof(AppHintPackage));
    public static string AppHintExe => Get(nameof(AppHintExe));
    public static string AppKindPackage => Get(nameof(AppKindPackage));
    public static string AppKindExe => Get(nameof(AppKindExe));

    // Key Panel
    public static string KeyOrShortcut => Get(nameof(KeyOrShortcut));
    public static string CaptureKey => Get(nameof(CaptureKey));
    public static string NoKeyCaptured => Get(nameof(NoKeyCaptured));
    public static string CaptureHint => Get(nameof(CaptureHint));

    // Media Panel
    public static string MediaKeyLabel => Get(nameof(MediaKeyLabel));

    // Site Panel
    public static string UrlLabel => Get(nameof(UrlLabel));
    public static string UrlHint => Get(nameof(UrlHint));

    // File Panel
    public static string FileOrFolderLabel => Get(nameof(FileOrFolderLabel));
    public static string BrowseFile => Get(nameof(BrowseFile));
    public static string FileHint => Get(nameof(FileHint));

    // Command Panel
    public static string CommandLabel => Get(nameof(CommandLabel));
    public static string CommandHint => Get(nameof(CommandHint));

    // Original Key Options (Keyboard Part)
    public static string OriginalKeySection => Get(nameof(OriginalKeySection));
    public static string OriginalIntro => Get(nameof(OriginalIntro));
    public static string PassText(string key) => Get(nameof(PassText), key);
    public static string NeutralText(string key, int inUse, int capacity) => Get(nameof(NeutralText), key, inUse, capacity);
    public static string PassRadio => Get(nameof(PassRadio));
    public static string PassDescription => Get(nameof(PassDescription));
    public static string NeutralizeRadio => Get(nameof(NeutralizeRadio));
    public static string NeutralizeDescription => Get(nameof(NeutralizeDescription));
    public static string OriginalWarning => Get(nameof(OriginalWarning));
    public static string NeutralLimitHit(int capacity) => Get(nameof(NeutralLimitHit), capacity);
    public static string NeutralWarnLowRisk(string key) => Get(nameof(NeutralWarnLowRisk), key);
    public static string NeutralWarnHighRisk(string key) => Get(nameof(NeutralWarnHighRisk), key);
    public static string NeutralWarnPass(string key) => Get(nameof(NeutralWarnPass), key);
    public static string NeutralWarnDelete => Get(nameof(NeutralWarnDelete));

    // Original Key Options (Media Part)
    public static string MediaOriginalInfo => Get(nameof(MediaOriginalInfo));
    public static string MediaOriginalNoKey => Get(nameof(MediaOriginalNoKey));
    public static string MediaOriginalBlocked => Get(nameof(MediaOriginalBlocked));
    public static string MediaOriginalBrowser => Get(nameof(MediaOriginalBrowser));

    // Simple Actions Hints
    public static string HintKeep => Get(nameof(HintKeep));
    public static string HintNone => Get(nameof(HintNone));

    // ============ Add Device Overlay ============
    public static string ListenTitle => Get(nameof(ListenTitle));
    public static string ListenDescription => Get(nameof(ListenDescription));
    public static string ListenSeconds(int seconds) => Get(nameof(ListenSeconds), seconds);
    public static string ListenSecondsLabel => Get(nameof(ListenSecondsLabel));
    public static string ListenCancel => Get(nameof(ListenCancel));
    public static string ListenRetry => Get(nameof(ListenRetry));
    public static string ListenSaveAndRecord => Get(nameof(ListenSaveAndRecord));
    public static string ListenNameLabel => Get(nameof(ListenNameLabel));
    public static string ListenPartsLabel => Get(nameof(ListenPartsLabel));
    public static string ListenPartsNote => Get(nameof(ListenPartsNote));

    // ============ Tray Menu ============
    public static string TrayOpen => Get(nameof(TrayOpen));
    public static string TrayPause => Get(nameof(TrayPause));
    public static string TrayResume => Get(nameof(TrayResume));
    public static string TrayStartup => Get(nameof(TrayStartup));
    public static string TrayExit => Get(nameof(TrayExit));
    public static string TrayStatusNoDevices => Get(nameof(TrayStatusNoDevices));
    public static string TrayStatusOneDevice(string name, string status) => Get(nameof(TrayStatusOneDevice), name, status);
    public static string TrayStatusMultipleDevices(int total, int connected) => Get(nameof(TrayStatusMultipleDevices), total, connected);
    public static string TrayConnected => Get(nameof(TrayConnected));
    public static string TrayDisconnected => Get(nameof(TrayDisconnected));
    public static string TrayPausedSuffix => Get(nameof(TrayPausedSuffix));
    public static string TrayNotifyDeviceConnected(string name) => Get(nameof(TrayNotifyDeviceConnected), name);
    public static string TrayNotifyDeviceConnectedBody => Get(nameof(TrayNotifyDeviceConnectedBody));
    public static string TrayNotifyDeviceDisconnected(string name) => Get(nameof(TrayNotifyDeviceDisconnected), name);
    public static string TrayNotifyDeviceDisconnectedBody => Get(nameof(TrayNotifyDeviceDisconnectedBody));
    public static string TrayNotifyRunning => Get(nameof(TrayNotifyRunning));
    public static string TrayNotifyRunningBody => Get(nameof(TrayNotifyRunningBody));

    // ============ Paused Bar ============
    public static string PausedTitle => Get(nameof(PausedTitle));
    public static string PausedDescription => Get(nameof(PausedDescription));
    public static string ResumeButton => Get(nameof(ResumeButton));

    // ============ Media Keys ============
    public static string MediaPlayPause => Get(nameof(MediaPlayPause));
    public static string MediaNext => Get(nameof(MediaNext));
    public static string MediaPrev => Get(nameof(MediaPrev));
    public static string MediaStop => Get(nameof(MediaStop));
    public static string MediaVolumeUp => Get(nameof(MediaVolumeUp));
    public static string MediaVolumeDown => Get(nameof(MediaVolumeDown));
    public static string MediaMute => Get(nameof(MediaMute));
    public static string MediaBassBoost => Get(nameof(MediaBassBoost));
    public static string MediaBassDown => Get(nameof(MediaBassDown));
    public static string MediaTrebleUp => Get(nameof(MediaTrebleUp));
    public static string MediaTrebleDown => Get(nameof(MediaTrebleDown));

    // ============ Key Names ============
    public static string KeyNone => Get(nameof(KeyNone));
    public static string KeyBackspace => Get(nameof(KeyBackspace));
    public static string KeyTab => Get(nameof(KeyTab));
    public static string KeyEnter => Get(nameof(KeyEnter));
    public static string KeyShift => Get(nameof(KeyShift));
    public static string KeyCtrl => Get(nameof(KeyCtrl));
    public static string KeyAlt => Get(nameof(KeyAlt));
    public static string KeyPause => Get(nameof(KeyPause));
    public static string KeyCapsLock => Get(nameof(KeyCapsLock));
    public static string KeyEscape => Get(nameof(KeyEscape));
    public static string KeySpace => Get(nameof(KeySpace));
    public static string KeyPageUp => Get(nameof(KeyPageUp));
    public static string KeyPageDown => Get(nameof(KeyPageDown));
    public static string KeyEnd => Get(nameof(KeyEnd));
    public static string KeyHome => Get(nameof(KeyHome));
    public static string KeyLeft => Get(nameof(KeyLeft));
    public static string KeyUp => Get(nameof(KeyUp));
    public static string KeyRight => Get(nameof(KeyRight));
    public static string KeyDown => Get(nameof(KeyDown));
    public static string KeyPrintScreen => Get(nameof(KeyPrintScreen));
    public static string KeyInsert => Get(nameof(KeyInsert));
    public static string KeyDelete => Get(nameof(KeyDelete));
    public static string Key0 => Get(nameof(Key0));
    public static string Key1 => Get(nameof(Key1));
    public static string Key2 => Get(nameof(Key2));
    public static string Key3 => Get(nameof(Key3));
    public static string Key4 => Get(nameof(Key4));
    public static string Key5 => Get(nameof(Key5));
    public static string Key6 => Get(nameof(Key6));
    public static string Key7 => Get(nameof(Key7));
    public static string Key8 => Get(nameof(Key8));
    public static string Key9 => Get(nameof(Key9));
    public static string KeyA => Get(nameof(KeyA));
    public static string KeyB => Get(nameof(KeyB));
    public static string KeyC => Get(nameof(KeyC));
    public static string KeyD => Get(nameof(KeyD));
    public static string KeyE => Get(nameof(KeyE));
    public static string KeyF => Get(nameof(KeyF));
    public static string KeyG => Get(nameof(KeyG));
    public static string KeyH => Get(nameof(KeyH));
    public static string KeyI => Get(nameof(KeyI));
    public static string KeyJ => Get(nameof(KeyJ));
    public static string KeyK => Get(nameof(KeyK));
    public static string KeyL => Get(nameof(KeyL));
    public static string KeyM => Get(nameof(KeyM));
    public static string KeyN => Get(nameof(KeyN));
    public static string KeyO => Get(nameof(KeyO));
    public static string KeyP => Get(nameof(KeyP));
    public static string KeyQ => Get(nameof(KeyQ));
    public static string KeyR => Get(nameof(KeyR));
    public static string KeyS => Get(nameof(KeyS));
    public static string KeyT => Get(nameof(KeyT));
    public static string KeyU => Get(nameof(KeyU));
    public static string KeyV => Get(nameof(KeyV));
    public static string KeyW => Get(nameof(KeyW));
    public static string KeyX => Get(nameof(KeyX));
    public static string KeyY => Get(nameof(KeyY));
    public static string KeyZ => Get(nameof(KeyZ));
    public static string KeyLWin => Get(nameof(KeyLWin));
    public static string KeyRWin => Get(nameof(KeyRWin));
    public static string KeyApps => Get(nameof(KeyApps));
    public static string KeySleep => Get(nameof(KeySleep));
    public static string KeyF1 => Get(nameof(KeyF1));
    public static string KeyF2 => Get(nameof(KeyF2));
    public static string KeyF3 => Get(nameof(KeyF3));
    public static string KeyF4 => Get(nameof(KeyF4));
    public static string KeyF5 => Get(nameof(KeyF5));
    public static string KeyF6 => Get(nameof(KeyF6));
    public static string KeyF7 => Get(nameof(KeyF7));
    public static string KeyF8 => Get(nameof(KeyF8));
    public static string KeyF9 => Get(nameof(KeyF9));
    public static string KeyF10 => Get(nameof(KeyF10));
    public static string KeyF11 => Get(nameof(KeyF11));
    public static string KeyF12 => Get(nameof(KeyF12));
    public static string KeyF13 => Get(nameof(KeyF13));
    public static string KeyF14 => Get(nameof(KeyF14));
    public static string KeyF15 => Get(nameof(KeyF15));
    public static string KeyF16 => Get(nameof(KeyF16));
    public static string KeyF17 => Get(nameof(KeyF17));
    public static string KeyF18 => Get(nameof(KeyF18));
    public static string KeyF19 => Get(nameof(KeyF19));
    public static string KeyF20 => Get(nameof(KeyF20));
    public static string KeyF21 => Get(nameof(KeyF21));
    public static string KeyF22 => Get(nameof(KeyF22));
    public static string KeyF23 => Get(nameof(KeyF23));
    public static string KeyF24 => Get(nameof(KeyF24));
    public static string KeyNumLock => Get(nameof(KeyNumLock));
    public static string KeyScrollLock => Get(nameof(KeyScrollLock));
    public static string KeyLShift => Get(nameof(KeyLShift));
    public static string KeyRShift => Get(nameof(KeyRShift));
    public static string KeyLCtrl => Get(nameof(KeyLCtrl));
    public static string KeyRCtrl => Get(nameof(KeyRCtrl));
    public static string KeyLAlt => Get(nameof(KeyLAlt));
    public static string KeyRAlt => Get(nameof(KeyRAlt));
    public static string KeyBrowserBack => Get(nameof(KeyBrowserBack));
    public static string KeyBrowserForward => Get(nameof(KeyBrowserForward));
    public static string KeyBrowserRefresh => Get(nameof(KeyBrowserRefresh));
    public static string KeyBrowserStop => Get(nameof(KeyBrowserStop));
    public static string KeyBrowserSearch => Get(nameof(KeyBrowserSearch));
    public static string KeyBrowserFavorites => Get(nameof(KeyBrowserFavorites));
    public static string KeyBrowserHome => Get(nameof(KeyBrowserHome));
    public static string KeyVolumeMute => Get(nameof(KeyVolumeMute));
    public static string KeyVolumeDown => Get(nameof(KeyVolumeDown));
    public static string KeyVolumeUp => Get(nameof(KeyVolumeUp));
    public static string KeyMediaNextTrack => Get(nameof(KeyMediaNextTrack));
    public static string KeyMediaPrevTrack => Get(nameof(KeyMediaPrevTrack));
    public static string KeyMediaStop => Get(nameof(KeyMediaStop));
    public static string KeyMediaPlayPause => Get(nameof(KeyMediaPlayPause));
    public static string KeyLaunchMail => Get(nameof(KeyLaunchMail));
    public static string KeyLaunchMedia => Get(nameof(KeyLaunchMedia));
    public static string KeyLaunchApp1 => Get(nameof(KeyLaunchApp1));
    public static string KeyLaunchApp2 => Get(nameof(KeyLaunchApp2));
    public static string KeyOemSemicolon => Get(nameof(KeyOemSemicolon));
    public static string KeyOemPlus => Get(nameof(KeyOemPlus));
    public static string KeyOemComma => Get(nameof(KeyOemComma));
    public static string KeyOemMinus => Get(nameof(KeyOemMinus));
    public static string KeyOemPeriod => Get(nameof(KeyOemPeriod));
    public static string KeyOemQuestion => Get(nameof(KeyOemQuestion));
    public static string KeyOemTilde => Get(nameof(KeyOemTilde));
    public static string KeyOemOpenBrackets => Get(nameof(KeyOemOpenBrackets));
    public static string KeyOemPipe => Get(nameof(KeyOemPipe));
    public static string KeyOemCloseBrackets => Get(nameof(KeyOemCloseBrackets));
    public static string KeyOemQuotes => Get(nameof(KeyOemQuotes));
    public static string KeyOemBackslash => Get(nameof(KeyOemBackslash));
    public static string KeyProcessKey => Get(nameof(KeyProcessKey));
    public static string KeyAttn => Get(nameof(KeyAttn));
    public static string KeyCrSel => Get(nameof(KeyCrSel));
    public static string KeyExSel => Get(nameof(KeyExSel));
    public static string KeyEraseEof => Get(nameof(KeyEraseEof));
    public static string KeyPlay => Get(nameof(KeyPlay));
    public static string KeyZoom => Get(nameof(KeyZoom));
    public static string KeyNoName => Get(nameof(KeyNoName));
    public static string KeyPa1 => Get(nameof(KeyPa1));
    public static string KeyOemClear => Get(nameof(KeyOemClear));
    public static string KeyUnknown(ushort vk) => Get(nameof(KeyUnknown), vk);

    // ============ NeutralCleanup / Uninstall ============
    public static string UndoKeysTitle => Get(nameof(UndoKeysTitle));
    public static string UndoKeysNothing => Get(nameof(UndoKeysNothing));
    public static string UndoKeysSuccess => Get(nameof(UndoKeysSuccess));
    public static string UndoKeysFailed => Get(nameof(UndoKeysFailed));
    public static string UninstallKeysStillSwapped => Get(nameof(UninstallKeysStillSwapped));
    public static string UninstallHowToUndo => Get(nameof(UninstallHowToUndo));

    // ============ Language Selection ============
    public static string LanguageLabel => Get(nameof(LanguageLabel));
    public static string LanguageSystem => Get(nameof(LanguageSystem));
    public static string LanguageEnglish => Get(nameof(LanguageEnglish));
    public static string LanguagePortuguese => Get(nameof(LanguagePortuguese));
    public static string LanguageRestartRequired => Get(nameof(LanguageRestartRequired));
    public static string LanguageRestartConfirm => Get(nameof(LanguageRestartConfirm));

    // ============ Engine Mode ============
    public static string EngineModeLabel => Get(nameof(EngineModeLabel));
    public static string EngineModeBasic => Get(nameof(EngineModeBasic));
    public static string EngineModeInterception => Get(nameof(EngineModeInterception));
    public static string EngineModeBasicDesc => Get(nameof(EngineModeBasicDesc));
    public static string EngineModeInterceptionDesc => Get(nameof(EngineModeInterceptionDesc));
    public static string EngineModeRestartRequired => Get(nameof(EngineModeRestartRequired));
    public static string EngineModeRestartConfirm => Get(nameof(EngineModeRestartConfirm));
    public static string InterceptionNotAvailable => Get(nameof(InterceptionNotAvailable));
    public static string InstallInterceptionDriver => Get(nameof(InstallInterceptionDriver));
}