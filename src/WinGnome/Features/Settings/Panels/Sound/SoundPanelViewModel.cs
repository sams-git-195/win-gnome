using System.Collections.ObjectModel;
using System.Windows.Input;
using WinGnome.Core.ControlCenter;
using WinGnome.Infrastructure;
using WinGnome.Interop;

namespace WinGnome.Features.Settings.Panels.Sound;

/// <summary>
/// Sound: output and input device, their volumes, and per-app volumes. Choosing the default device uses the
/// undocumented IPolicyConfig; when it isn't available the device lists are read-only and link to Windows Settings.
/// </summary>
internal sealed class SoundPanelViewModel : SystemPanelViewModel
{
    private readonly SystemSettingWriter _writer;
    private AudioDevices? _audio;
    private IReadOnlyList<AudioDevice> _outputs = [];
    private IReadOnlyList<AudioDevice> _inputs = [];
    private AudioDevice? _output;
    private AudioDevice? _input;
    private double _outputVolume;
    private bool _outputMuted;
    private double _inputVolume;
    private bool _hasOutput;
    private bool _hasInput;
    private bool _canSetDefault;

    public SoundPanelViewModel(SystemPanelContext context)
        : base(context, PanelIds.Sound)
    {
        _writer = context.CreateWriter();
        VolumeMixerCommand = new RelayCommand(() => context.OpenLink("ms-settings:apps-volume"));
    }

    public IReadOnlyList<AudioDevice> Outputs
    {
        get => _outputs;
        private set => SetProperty(ref _outputs, value);
    }

    public IReadOnlyList<AudioDevice> Inputs
    {
        get => _inputs;
        private set => SetProperty(ref _inputs, value);
    }

    public AudioDevice? Output
    {
        get => _output;
        set => ChooseDefault(ref _output, value, "output");
    }

    public AudioDevice? Input
    {
        get => _input;
        set => ChooseDefault(ref _input, value, "input");
    }

    /// <summary>True when the default devices can be changed here (not in safe mode, and IPolicyConfig answered).</summary>
    public bool CanChooseDevice => CanEdit && _canSetDefault;

    public string DeviceNote => _canSetDefault
        ? "The device Windows plays sound through."
        : "This Windows doesn't let WinGnome change the device; use Windows Settings.";

    /// <summary>Output volume 0..100.</summary>
    public double OutputVolume
    {
        get => _outputVolume;
        set
        {
            if (CanEdit && SetProperty(ref _outputVolume, Math.Clamp(value, 0, 100)))
            {
                _audio?.SetVolume(EDataFlow.Render, _outputVolume / 100);
                if (_outputVolume > 0 && _outputMuted)
                {
                    _outputMuted = false;
                    OnPropertyChanged(nameof(OutputMuted));
                }
            }
        }
    }

    public bool OutputMuted
    {
        get => _outputMuted;
        set
        {
            if (CanEdit && SetProperty(ref _outputMuted, value))
            {
                _audio?.SetMuted(EDataFlow.Render, value);
            }
        }
    }

    /// <summary>Input (microphone) volume 0..100.</summary>
    public double InputVolume
    {
        get => _inputVolume;
        set
        {
            if (CanEdit && SetProperty(ref _inputVolume, Math.Clamp(value, 0, 100)))
            {
                _audio?.SetVolume(EDataFlow.Capture, _inputVolume / 100);
            }
        }
    }

    public bool HasOutput => _hasOutput;

    public bool HasInput => _hasInput;

    /// <summary>Apps with an audio session on the default output.</summary>
    public ObservableCollection<AppVolume> Apps { get; } = [];

    public bool HasApps => Apps.Count > 0;

    public ICommand VolumeMixerCommand { get; }

    protected override void Open()
    {
        _audio = new AudioDevices(Context.Dispatcher);
        _audio.DevicesChanged += OnDevicesChanged;
        _canSetDefault = AudioDevices.CanSetDefault();
        if (!_audio.IsAvailable)
        {
            Problem = "WinGnome couldn't reach the Windows audio service.";
        }

        Reload();
    }

    protected override void Close()
    {
        ClearApps();
        if (_audio is not null)
        {
            _audio.DevicesChanged -= OnDevicesChanged;
            _audio.Dispose();
            _audio = null;
        }
    }

    private void OnDevicesChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        if (_audio is null)
        {
            return;
        }

        Outputs = _audio.List(EDataFlow.Render);
        Inputs = _audio.List(EDataFlow.Capture);
        var outputId = _audio.DefaultId(EDataFlow.Render);
        var inputId = _audio.DefaultId(EDataFlow.Capture);
        _output = Outputs.FirstOrDefault(d => d.Id == outputId);
        _input = Inputs.FirstOrDefault(d => d.Id == inputId);

        var output = _audio.ReadVolume(EDataFlow.Render);
        _hasOutput = output is not null;
        _outputVolume = Math.Round((output?.Level ?? 0) * 100);
        _outputMuted = output?.Muted ?? false;
        var input = _audio.ReadVolume(EDataFlow.Capture);
        _hasInput = input is not null;
        _inputVolume = Math.Round((input?.Level ?? 0) * 100);

        ClearApps();
        foreach (var app in _audio.ListApps())
        {
            Apps.Add(app);
        }

        OnPropertyChanged(string.Empty);
    }

    private void ClearApps()
    {
        foreach (var app in Apps)
        {
            app.Dispose();
        }

        Apps.Clear();
    }

    /// <summary>Makes the chosen device the default. The lists refresh when Windows reports the new default.</summary>
    private void ChooseDefault(ref AudioDevice? field, AudioDevice? value, string direction)
    {
        if (value is null || value == field || !CanChooseDevice)
        {
            return;
        }

        field = value;
        OnPropertyChanged(direction == "output" ? nameof(Output) : nameof(Input));
        var id = value.Id;
        _writer.Run($"make \"{value.Name}\" the default {direction} device", () => AudioDevices.SetDefault(id), () =>
        {
            ReportWriteFailure($"the {direction} device");
            Reload();
        });
    }
}
