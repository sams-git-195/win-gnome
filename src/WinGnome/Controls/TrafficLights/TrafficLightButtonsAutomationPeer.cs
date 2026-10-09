using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using WinGnome.Core.Windows;

namespace WinGnome.Controls.TrafficLights;

/// <summary>
/// Exposes the traffic-light group to UI Automation as a group of three buttons. The view draws every circle
/// itself, so each circle gets a peer of its own. WPF creates peers only when a UI Automation client (Narrator,
/// an accessibility checker, a test script) asks, so this costs nothing otherwise.
/// </summary>
internal sealed class TrafficLightButtonsAutomationPeer : FrameworkElementAutomationPeer
{
    private readonly Dictionary<CaptionButtonKind, TrafficLightButtonAutomationPeer> _buttons = [];

    public TrafficLightButtonsAutomationPeer(TrafficLightButtonsView owner)
        : base(owner)
    {
    }

    private TrafficLightButtonsView View => (TrafficLightButtonsView)Owner;

    protected override string GetClassNameCore() => nameof(TrafficLightButtonsView);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetNameCore() => "Window buttons";

    protected override List<AutomationPeer>? GetChildrenCore()
    {
        if (View.Layout is not { } layout)
        {
            return null;
        }

        // Reuse peers by kind so clients keep stable elements across relayouts.
        var children = new List<AutomationPeer>(layout.Buttons.Count);
        foreach (var slot in layout.Buttons)
        {
            if (!_buttons.TryGetValue(slot.Kind, out var peer))
            {
                peer = new TrafficLightButtonAutomationPeer(View, slot.Kind);
                _buttons[slot.Kind] = peer;
            }

            children.Add(peer);
        }

        return children;
    }
}

/// <summary>One circle as a UI Automation button whose Invoke runs the same action as a click.</summary>
internal sealed class TrafficLightButtonAutomationPeer : AutomationPeer, IInvokeProvider
{
    private readonly TrafficLightButtonsView _view;
    private readonly CaptionButtonKind _kind;

    public TrafficLightButtonAutomationPeer(TrafficLightButtonsView view, CaptionButtonKind kind)
    {
        _view = view;
        _kind = kind;
    }

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke ? this : null;

    void IInvokeProvider.Invoke()
    {
        if (!_view.IsAvailable(_kind))
        {
            throw new ElementNotEnabledException();
        }

        // Asynchronous, as WPF's own buttons do: closing or minimising a window must not run inside the client's
        // cross-process UI Automation call.
        _view.Dispatcher.BeginInvoke(() => _view.Invoke(_kind));
    }

    protected override string GetNameCore() => CaptionButtonAccessibility.Name(_kind, _view.IsTargetMaximized);

    protected override string GetAutomationIdCore() => CaptionButtonAccessibility.AutomationId(_kind);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

    protected override string GetLocalizedControlTypeCore() => "button";

    protected override string GetClassNameCore() => "TrafficLightButton";

    protected override Rect GetBoundingRectangleCore() =>
        Slot() is { } slot ? _view.ScreenBoundsOf(slot) : Rect.Empty;

    protected override Point GetClickablePointCore()
    {
        var bounds = GetBoundingRectangleCore();
        return bounds.IsEmpty ? new Point(double.NaN, double.NaN) : new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    protected override bool IsOffscreenCore() => !_view.IsVisible || GetBoundingRectangleCore().IsEmpty;

    protected override bool IsEnabledCore() => _view.IsAvailable(_kind);

    protected override bool IsKeyboardFocusableCore() => _view.IsKeyboardNavigable && _view.IsAvailable(_kind);

    protected override bool HasKeyboardFocusCore() => _view.KeyboardButton == _kind;

    protected override void SetFocusCore()
    {
        if (!IsKeyboardFocusableCore())
        {
            throw new InvalidOperationException($"The {GetNameCore()} button can't take keyboard focus.");
        }

        _view.FocusButton(_kind);
    }

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override List<AutomationPeer>? GetChildrenCore() => null;

    protected override string GetAcceleratorKeyCore() => "";

    protected override string GetAccessKeyCore() => "";

    protected override string GetHelpTextCore() => "";

    protected override string GetItemStatusCore() => "";

    protected override string GetItemTypeCore() => "";

    protected override AutomationPeer? GetLabeledByCore() => null;

    protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.None;

    protected override bool IsPasswordCore() => false;

    protected override bool IsRequiredForFormCore() => false;

    private CaptionButtonSlot? Slot() => _view.Layout?.Buttons.FirstOrDefault(slot => slot.Kind == _kind);
}
