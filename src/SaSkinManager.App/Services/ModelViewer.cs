using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SaSkinManager.App.Services;

/// <summary>
/// Estado de giro compartilhado: visualizadores com o mesmo <see cref="ModelViewer.SyncGroup"/>
/// mostram sempre o mesmo ângulo e zoom (ex.: original × nova skin). O giro automático é calculado
/// pelo relógio, então todos os modelos que giram na mesma velocidade ficam alinhados.
/// </summary>
internal sealed class RotationState
{
    private static readonly Dictionary<string, RotationState> Groups = new();
    public static RotationState For(string? group)
    {
        if (string.IsNullOrEmpty(group)) return new RotationState();
        if (!Groups.TryGetValue(group, out var s)) Groups[group] = s = new RotationState();
        return s;
    }

    public static readonly Stopwatch Clock = Stopwatch.StartNew();

    public double Offset;      // giro manual (arrastar)
    public double Pitch;
    public double Zoom = 1;
    public bool Dragging;
    public double FrozenAngle = double.NaN; // ângulo enquanto arrasta
    public event Action? Changed;
    public void Notify() => Changed?.Invoke();
}

/// <summary>
/// Visualizador 3D: gira sozinho, arrastar com o mouse gira, roda do mouse aproxima,
/// clique duplo volta à posição inicial.
/// </summary>
public sealed class ModelViewer : Border
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(Model3D), typeof(ModelViewer), new PropertyMetadata(null, (d, _) => ((ModelViewer)d).OnModelChanged()));

    public static readonly DependencyProperty AutoRotateProperty = DependencyProperty.Register(
        nameof(AutoRotate), typeof(bool), typeof(ModelViewer), new PropertyMetadata(true, (d, _) => ((ModelViewer)d).UpdateTicking()));

    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
        nameof(Interactive), typeof(bool), typeof(ModelViewer), new PropertyMetadata(true));

    /// <summary>Graus por segundo do giro automático.</summary>
    public double Speed { get; set; } = 45;

    /// <summary>Visualizadores com o mesmo grupo giram juntos (mesmo ângulo, arrastar e zoom compartilhados).</summary>
    public string? SyncGroup
    {
        get => _syncGroup;
        set
        {
            _syncGroup = value;
            _state.Changed -= OnStateChanged;
            _state = RotationState.For(value);
            _state.Changed += OnStateChanged;
        }
    }

    public Model3D? Model { get => (Model3D?)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public bool AutoRotate { get => (bool)GetValue(AutoRotateProperty); set => SetValue(AutoRotateProperty, value); }
    public bool Interactive { get => (bool)GetValue(InteractiveProperty); set => SetValue(InteractiveProperty, value); }

    private const double StartAngle = 20;
    private readonly Viewport3D _viewport = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = ModelScene.Fov, LookDirection = new Vector3D(0, 0, -1), UpDirection = new Vector3D(0, 1, 0) };
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), StartAngle);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);
    private readonly ModelVisual3D _content = new();
    private string? _syncGroup;
    private RotationState _state = new();
    private Point _last;
    private bool _ticking;

    public ModelViewer()
    {
        Background = Brushes.Transparent; // recebe o mouse no quadro todo
        _viewport.Camera = _camera;
        _viewport.Children.Add(new ModelVisual3D { Content = ModelScene.Lights() });
        var t = new Transform3DGroup();
        t.Children.Add(new RotateTransform3D(_yaw));
        t.Children.Add(new RotateTransform3D(_pitch));
        _content.Transform = t;
        _viewport.Children.Add(_content);
        Child = _viewport;
        Loaded += (_, _) => { _state.Changed -= OnStateChanged; _state.Changed += OnStateChanged; UpdateTicking(); UpdateAngle(); };
        Unloaded += (_, _) => { _state.Changed -= OnStateChanged; StopTicking(); };
        IsVisibleChanged += (_, _) => UpdateTicking();
        SizeChanged += (_, _) => FitCamera();
    }

    private void OnModelChanged()
    {
        _content.Content = Model;
        FitCamera();
        UpdateAngle();
        UpdateTicking();
    }

    private void OnStateChanged()
    {
        FitCamera();
        UpdateAngle();
    }

    public void ResetView()
    {
        _state.Offset = 0;
        _state.Pitch = 0;
        _state.Zoom = 1;
        _state.FrozenAngle = double.NaN;
        _state.Notify();
    }

    /// <summary>Ângulo atual: relógio (giro automático) + o que o usuário arrastou.</summary>
    private double CurrentAngle()
    {
        if (_state.Dragging && !double.IsNaN(_state.FrozenAngle)) return _state.FrozenAngle;
        double spin = AutoRotate ? RotationState.Clock.Elapsed.TotalSeconds * Speed : 0;
        return (StartAngle + spin + _state.Offset) % 360;
    }

    private void UpdateAngle()
    {
        _yaw.Angle = CurrentAngle();
        _pitch.Angle = _state.Pitch;
    }

    private void FitCamera()
    {
        if (Model is null) return;
        var b = Model.Bounds;
        double aspect = ActualHeight > 0 ? ActualWidth / ActualHeight : 1;
        double dist = ModelScene.FitDistance(b, aspect, ModelScene.Fov) * _state.Zoom;
        _camera.Position = new Point3D(0, b.IsEmpty ? 0 : b.SizeY * 0.02, dist);
    }

    private void UpdateTicking()
    {
        bool want = AutoRotate && Model is not null && IsLoaded && IsVisible;
        if (want && !_ticking) { CompositionTarget.Rendering += OnRendering; _ticking = true; }
        else if (!want) StopTicking();
        UpdateAngle();
    }

    private void StopTicking()
    {
        if (!_ticking) return;
        CompositionTarget.Rendering -= OnRendering;
        _ticking = false;
    }

    private void OnRendering(object? sender, EventArgs e) => UpdateAngle();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!Interactive || Model is null) return;
        if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
        _state.FrozenAngle = CurrentAngle();
        _state.Dragging = true;
        _last = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_state.Dragging || !IsMouseCaptured) return;
        var p = e.GetPosition(this);
        _state.FrozenAngle += (p.X - _last.X) * 0.6;
        _state.Pitch = Math.Clamp(_state.Pitch + (p.Y - _last.Y) * 0.4, -35, 35);
        _last = p;
        _state.Notify();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_state.Dragging || !IsMouseCaptured) return;
        EndDrag();
        ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_state.Dragging) EndDrag();
    }

    /// <summary>Ao soltar, o giro automático continua de onde o usuário deixou.</summary>
    private void EndDrag()
    {
        double frozen = _state.FrozenAngle;
        _state.Dragging = false;
        _state.FrozenAngle = double.NaN;
        double spin = AutoRotate ? RotationState.Clock.Elapsed.TotalSeconds * Speed : 0;
        _state.Offset = ((frozen - StartAngle - spin) % 360 + 360) % 360;
        _state.Notify();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!Interactive || Model is null) return;
        _state.Zoom = Math.Clamp(_state.Zoom * (e.Delta > 0 ? 0.88 : 1.14), 0.35, 2.5);
        _state.Notify();
        e.Handled = true;
    }
}
