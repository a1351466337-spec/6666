using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware.Display;
using Android.Media;
using Android.Media.Projection;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;

[assembly: UsesPermission(Name = "android.permission.SYSTEM_ALERT_WINDOW")]
[assembly: UsesPermission(Name = "android.permission.FOREGROUND_SERVICE")]
[assembly: UsesPermission(Name = "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION")]
[assembly: UsesPermission(Name = "android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission(Name = "android.permission.VIBRATE")]

namespace TartarusAndroid;

// ------------------------------------------------------------------------------------------------
// Main page: start / stop the floating ball; a "设置" bar at the bottom opens the error-log page.
// ------------------------------------------------------------------------------------------------
[Activity(Label = "塔罗斯助手", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    const int ReqProjection = 1001;
    TextView _status = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AppLog.Init(FilesDir!.AbsolutePath);
        float d = Resources!.DisplayMetrics!.Density;

        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetFitsSystemWindows(true);
        root.SetPadding((int)(16 * d), (int)(16 * d), (int)(16 * d), 0);

        _status = new TextView(this) { Text = "点「开启悬浮球」，授权截屏后，悬浮球会出现在屏幕中央。\n单击：截屏分析；双击：隐藏；长按：拖动。\n隐藏后可从通知栏的「显示悬浮球」找回。" };
        _status.TextSize = 15;
        root.AddView(_status);

        var start = new Button(this) { Text = "开启悬浮球" };
        start.Click += (_, _) => StartBall();
        root.AddView(start);

        var stop = new Button(this) { Text = "关闭悬浮球" };
        stop.Click += (_, _) => StopService(new Intent(this, typeof(FloatingService)));
        root.AddView(stop);

        root.AddView(new View(this), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var settings = new Button(this) { Text = "⚙  设置" };
        settings.Click += (_, _) => StartActivity(new Intent(this, typeof(LogActivity)));
        root.AddView(settings);

        SetContentView(root);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
            && CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
            RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, 1);
    }

    void StartBall()
    {
        if (!Settings.CanDrawOverlays(this))
        {
            Toast.MakeText(this, "请先允许「显示在其他应用上层」，然后回来再点一次", ToastLength.Long)!.Show();
            StartActivity(new Intent(Settings.ActionManageOverlayPermission, Android.Net.Uri.Parse("package:" + PackageName)));
            return;
        }
        var mpm = GetSystemService(MediaProjectionService)!.JavaCast<MediaProjectionManager>();
        StartActivityForResult(mpm.CreateScreenCaptureIntent(), ReqProjection);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != ReqProjection) return;
        if (resultCode == Result.Ok && data != null)
        {
            var i = new Intent(this, typeof(FloatingService));
            i.SetAction(FloatingService.ActionStart);
            i.PutExtra("code", (int)resultCode);
            i.PutExtra("data", data);
            StartForegroundService(i);
            _status.Text = "悬浮球已开启（首次启动要解压模型，约几十秒）。切到游戏里点悬浮球即可。";
        }
        else
        {
            _status.Text = "没有授权截屏，悬浮球无法使用。";
        }
    }
}

// ------------------------------------------------------------------------------------------------
// The settings page: only the error log.
// ------------------------------------------------------------------------------------------------
[Activity(Label = "设置")]
public class LogActivity : Activity
{
    TextView _text = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        float d = Resources!.DisplayMetrics!.Density;
        var root = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Vertical };
        root.SetFitsSystemWindows(true);
        root.SetPadding((int)(12 * d), (int)(12 * d), (int)(12 * d), (int)(12 * d));

        var bar = new LinearLayout(this) { Orientation = Android.Widget.Orientation.Horizontal };
        var title = new TextView(this) { Text = "报错日志" };
        title.TextSize = 18;
        bar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var refresh = new Button(this) { Text = "刷新" };
        refresh.Click += (_, _) => Refresh();
        bar.AddView(refresh);
        var clear = new Button(this) { Text = "清空" };
        clear.Click += (_, _) => { AppLog.ClearError(); Refresh(); };
        bar.AddView(clear);
        root.AddView(bar);

        _text = new TextView(this) { Typeface = Typeface.Monospace };
        _text.TextSize = 11;
        _text.SetTextIsSelectable(true);
        var scroll = new ScrollView(this);
        scroll.AddView(_text);
        root.AddView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));
        SetContentView(root);
    }

    protected override void OnResume()
    {
        base.OnResume();
        Refresh();
    }

    void Refresh() => _text.Text = AppLog.ReadError();
}

// ------------------------------------------------------------------------------------------------
// The models travel in the APK's assets; the original code wants them as files.
// ------------------------------------------------------------------------------------------------
static class AssetInstaller
{
    public static string Install(Context ctx)
    {
        string root = System.IO.Path.Combine(ctx.FilesDir!.AbsolutePath, "tartarus");
        string stamp = System.IO.Path.Combine(root, ".version");
        string version = System.IO.File.GetLastWriteTimeUtc(ctx.PackageCodePath!).Ticks.ToString();
        string models = System.IO.Path.Combine(root, "models");
        if (System.IO.File.Exists(stamp) && System.IO.File.ReadAllText(stamp) == version && Directory.Exists(models)) return root;
        if (Directory.Exists(models)) Directory.Delete(models, true);
        Copy(ctx.Assets!, "models", models);
        Directory.CreateDirectory(root);
        System.IO.File.WriteAllText(stamp, version);
        return root;
    }

    static void Copy(Android.Content.Res.AssetManager am, string path, string dest)
    {
        string[]? names = am.List(path);
        if (names == null || names.Length == 0)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            using var input = am.Open(path);
            using var output = System.IO.File.Create(dest);
            input.CopyTo(output);
            return;
        }
        Directory.CreateDirectory(dest);
        foreach (string n in names) Copy(am, path + "/" + n, System.IO.Path.Combine(dest, n));
    }
}

// ------------------------------------------------------------------------------------------------
// Screen capture by MediaProjection: the newest frame is always at hand.
// ------------------------------------------------------------------------------------------------
sealed class ScreenGrabber : IDisposable
{
    sealed class StopCallback(Action onStop) : MediaProjection.Callback
    {
        public override void OnStop() => onStop();
    }

    sealed class Listener(ScreenGrabber owner) : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader? reader) => owner.OnImage(reader);
    }

    readonly Context _ctx;
    readonly MediaProjection _projection;
    readonly Handler _handler = new(Looper.MainLooper!);
    readonly object _gate = new();
    VirtualDisplay? _display;
    ImageReader? _reader;
    Image? _latest;
    long _seq;
    int _w, _h, _dpi;

    public event Action? Stopped;

    public ScreenGrabber(Context ctx, MediaProjection projection)
    {
        _ctx = ctx;
        _projection = projection;
        // Android 14 wants the callback registered before the first virtual display.
        _projection.RegisterCallback(new StopCallback(() => Stopped?.Invoke()), _handler);
        (_w, _h, _dpi) = Metrics(ctx);
        _reader = NewReader(_w, _h);
        // 16 = VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR
        _display = _projection.CreateVirtualDisplay("tartarus", _w, _h, _dpi, (DisplayFlags)16, _reader.Surface, null, null);
    }

    public long Seq { get { lock (_gate) return _seq; } }

    public static (int W, int H, int Dpi) Metrics(Context ctx)
    {
#pragma warning disable CA1422, CS0618
        var wm = ctx.GetSystemService(Context.WindowService)!.JavaCast<IWindowManager>();
        var dm = new DisplayMetrics();
        wm.DefaultDisplay!.GetRealMetrics(dm);
#pragma warning restore CA1422, CS0618
        return (dm.WidthPixels, dm.HeightPixels, (int)dm.DensityDpi);
    }

    ImageReader NewReader(int w, int h)
    {
        // 1 = RGBA_8888
        var reader = ImageReader.NewInstance(w, h, (ImageFormatType)1, 2)!;
        reader.SetOnImageAvailableListener(new Listener(this), _handler);
        return reader;
    }

    internal void OnImage(ImageReader? reader)
    {
        Image? img = null;
        try { img = reader?.AcquireLatestImage(); } catch { }
        if (img == null) return;
        lock (_gate)
        {
            _latest?.Close();
            _latest = img;
            _seq++;
        }
    }

    /// <summary>The screen turned (portrait menu, landscape game): the display and reader follow.</summary>
    public void FollowScreenSize()
    {
        var (w, h, dpi) = Metrics(_ctx);
        if (w == _w && h == _h) return;
        lock (_gate)
        {
            _latest?.Close();
            _latest = null;
        }
        var old = _reader;
        (_w, _h, _dpi) = (w, h, dpi);
        _reader = NewReader(w, h);
        _display!.Resize(w, h, dpi);
        _display.Surface = _reader.Surface;
        old?.Close();
    }

    /// <summary>The newest frame as tightly packed RGBA, or null if none came yet.</summary>
    public Tartarus.Vision.Frame? Grab()
    {
        lock (_gate)
        {
            if (_latest == null) return null;
            var plane = _latest.GetPlanes()![0];
            var buf = plane.Buffer!;
            int w = _latest.Width, h = _latest.Height, rowStride = plane.RowStride;
            var pixels = new byte[w * h * 4];
            if (rowStride == w * 4)
            {
                buf.Rewind();
                buf.Get(pixels, 0, pixels.Length);
            }
            else
            {
                for (int y = 0; y < h; y++)
                {
                    buf.Position(y * rowStride);
                    buf.Get(pixels, y * w * 4, w * 4);
                }
            }
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            return AnswerEngine.MakeFrame(w, h, pixels);
        }
    }

    public void Dispose()
    {
        try { _display?.Release(); } catch { }
        lock (_gate) { _latest?.Close(); _latest = null; }
        try { _reader?.Close(); } catch { }
        try { _projection.Stop(); } catch { }
    }
}

// ------------------------------------------------------------------------------------------------
// The floating ball and answer window.
//   tap: screenshot + answer | double tap: hide the ball | long press, then drag: move it
//   the ball starts in the middle of the screen every time the service starts
// ------------------------------------------------------------------------------------------------
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaProjection)]
public class FloatingService : Service
{
    public const string ActionStart = "tartarus.START", ActionShow = "tartarus.SHOW", ActionQuit = "tartarus.QUIT";
    const string ChannelId = "tartarus_ball";

    readonly Handler _handler = new(Looper.MainLooper!);
    IWindowManager _wm = null!;
    TextView? _ball, _answer;
    WindowManagerLayoutParams? _ballLp, _answerLp;
    bool _ballAdded, _answerAdded, _busy, _dragging, _moved, _tapPending, _userMoved;
    float _density, _downX, _downY;
    int _size, _slop, _startX, _startY;
    Java.Lang.Runnable _longPress = null!, _singleTap = null!, _hideAnswer = null!;
    ScreenGrabber? _grabber;
    AnswerEngine? _engine;
    volatile bool _ready;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        AppLog.Init(FilesDir!.AbsolutePath);
        _wm = GetSystemService(WindowService)!.JavaCast<IWindowManager>();
        _density = Resources!.DisplayMetrics!.Density;
        _size = (int)(56 * _density);
        _slop = (int)(8 * _density);
        _longPress = new Java.Lang.Runnable(BeginDrag);
        _singleTap = new Java.Lang.Runnable(() => { _tapPending = false; OnSingleTap(); });
        _hideAnswer = new Java.Lang.Runnable(() => { if (_answer != null) _answer.Visibility = ViewStates.Gone; });
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        switch (intent?.Action)
        {
            case ActionQuit:
                StopSelf();
                break;
            case ActionShow:
                ShowBall(center: false);
                break;
            case ActionStart:
                StartAsForeground();
                try
                {
                    int code = intent.GetIntExtra("code", 0);
#pragma warning disable CA1422, CS0618
                    var data = intent.GetParcelableExtra("data") as Intent;
#pragma warning restore CA1422, CS0618
                    var mpm = GetSystemService(MediaProjectionService)!.JavaCast<MediaProjectionManager>();
                    var projection = mpm.GetMediaProjection(code, data!)!;
                    _grabber?.Dispose();
                    _grabber = new ScreenGrabber(this, projection);
                    _grabber.Stopped += () => _handler.Post(StopSelf);
                }
                catch (Exception ex)
                {
                    AppLog.Error("截屏授权/启动失败", ex);
                }
                ShowBall(center: true);
                Task.Run(InitEngine);
                break;
        }
        return StartCommandResult.NotSticky;
    }

    void InitEngine()
    {
        try
        {
            string home = AssetInstaller.Install(this);
            System.Environment.SetEnvironmentVariable("TARTARUS_HOME", home);
            var engine = new AnswerEngine();
            _engine = engine;
            engine.WarmUpAsync().Wait();
            _ready = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("模型准备失败", ex);
        }
    }

    // ---------- notification (keeps the service alive; brings the ball back) ----------
    void StartAsForeground()
    {
        var nm = GetSystemService(NotificationService)!.JavaCast<NotificationManager>();
        nm.CreateNotificationChannel(new NotificationChannel(ChannelId, "悬浮球", NotificationImportance.Low));
        var show = PendingIntent.GetService(this, 1, new Intent(this, typeof(FloatingService)).SetAction(ActionShow),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        var quit = PendingIntent.GetService(this, 2, new Intent(this, typeof(FloatingService)).SetAction(ActionQuit),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        var icon = Android.Graphics.Drawables.Icon.CreateWithResource(this, Android.Resource.Drawable.IcMenuView)!;
        var n = new Notification.Builder(this, ChannelId)
            .SetContentTitle("塔罗斯助手运行中")
            .SetContentText("悬浮球被隐藏时，点下面的「显示悬浮球」")
            .SetSmallIcon(Android.Resource.Drawable.IcMenuView)!
            .AddAction(new Notification.Action.Builder(icon, "显示悬浮球", show).Build())!
            .AddAction(new Notification.Action.Builder(icon, "退出", quit).Build())!
            .Build()!;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) StartForeground(1, n, ForegroundService.TypeMediaProjection);
        else StartForeground(1, n);
    }

    // ---------- the ball ----------
    void CreateBall()
    {
        _ball = new TextView(this) { Text = "答", Gravity = GravityFlags.Center };
        _ball.TextSize = 18;
        _ball.SetTextColor(Color.White);
        var bg = new GradientDrawable();
        bg.SetShape(ShapeType.Oval);
        bg.SetColor(Color.Argb(210, 30, 30, 30));
        bg.SetStroke((int)(2 * _density), Color.Argb(255, 212, 175, 55));
        _ball.Background = bg;
        _ball.Touch += (_, e) => e.Handled = OnBallTouch(e.Event!);
        _ballLp = new WindowManagerLayoutParams(_size, _size, WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutNoLimits, Format.Translucent)
        { Gravity = GravityFlags.Top | GravityFlags.Left };
    }

    void ShowBall(bool center)
    {
        if (!Settings.CanDrawOverlays(this))
        {
            AppLog.Error("没有「显示在其他应用上层」权限，悬浮球无法显示");
            return;
        }
        if (_ball == null) CreateBall();
        if (center) CenterBall();
        _ball!.Visibility = ViewStates.Visible;
        if (!_ballAdded) { _wm.AddView(_ball, _ballLp); _ballAdded = true; }
        else _wm.UpdateViewLayout(_ball, _ballLp);
    }

    void CenterBall()
    {
        var (w, h, _) = ScreenGrabber.Metrics(this);
        _ballLp!.X = (w - _size) / 2;
        _ballLp.Y = (h - _size) / 2;
        _userMoved = false;
    }

    public override void OnConfigurationChanged(Android.Content.Res.Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig!);
        if (_ball == null || !_ballAdded) return;
        var (w, h, _) = ScreenGrabber.Metrics(this);
        if (!_userMoved) CenterBall();
        _ballLp!.X = Math.Clamp(_ballLp.X, 0, Math.Max(0, w - _size));
        _ballLp.Y = Math.Clamp(_ballLp.Y, 0, Math.Max(0, h - _size));
        _wm.UpdateViewLayout(_ball, _ballLp);
    }

    bool OnBallTouch(MotionEvent ev)
    {
        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = ev.RawX; _downY = ev.RawY;
                _startX = _ballLp!.X; _startY = _ballLp.Y;
                _dragging = false; _moved = false;
                _handler.PostDelayed(_longPress, 450);
                return true;
            case MotionEventActions.Move:
                float dx = ev.RawX - _downX, dy = ev.RawY - _downY;
                if (!_dragging)
                {
                    if (Math.Abs(dx) > _slop || Math.Abs(dy) > _slop)
                    {
                        _moved = true;
                        _handler.RemoveCallbacks(_longPress);
                    }
                    return true;
                }
                _ballLp!.X = (int)(_startX + dx);
                _ballLp.Y = (int)(_startY + dy);
                _userMoved = true;
                _wm.UpdateViewLayout(_ball, _ballLp);
                return true;
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _handler.RemoveCallbacks(_longPress);
                if (_dragging)
                {
                    _dragging = false;
                    _ball!.Alpha = 1f;
                    return true;
                }
                if (ev.ActionMasked == MotionEventActions.Up && !_moved) OnTap();
                return true;
        }
        return false;
    }

    void BeginDrag()
    {
        _dragging = true;
        _ball!.Alpha = 0.7f;
        try
        {
            var vib = GetSystemService(VibratorService)!.JavaCast<Vibrator>();
            vib.Vibrate(VibrationEffect.CreateOneShot(30, VibrationEffect.DefaultAmplitude));
        }
        catch { }
    }

    void OnTap()
    {
        if (_tapPending)
        {
            // a second tap: hide the ball (the notification brings it back)
            _handler.RemoveCallbacks(_singleTap);
            _tapPending = false;
            if (_ball != null) _ball.Visibility = ViewStates.Gone;
            if (_answer != null) _answer.Visibility = ViewStates.Gone;
            return;
        }
        _tapPending = true;
        _handler.PostDelayed(_singleTap, 300);
    }

    // ---------- tap: screenshot, solve, log, show ----------
    async void OnSingleTap()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_grabber == null) { AppLog.Error("没有截屏权限，请回到应用重新开启悬浮球"); ShowAnswer("截屏未授权"); return; }
            if (!_ready || _engine == null) { ShowAnswer("模型准备中，稍等"); return; }

            _ball!.Text = "…";
            Cloak(true);                       // the overlay would be in the screenshot (and in the OCR)
            _grabber.FollowScreenSize();
            long seq = _grabber.Seq;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(200);
            while (_grabber.Seq < seq + 2 && sw.ElapsedMilliseconds < 1500) await Task.Delay(30);
            var frame = _grabber.Grab();
            Cloak(false);

            if (frame == null) { AppLog.Error("截屏失败：没有拿到画面"); ShowAnswer("截屏失败"); return; }

            string answer = await _engine.SolveAsync(frame);
            AppLog.Answer(answer);
            ShowAnswer(AppLog.LastAnswer() ?? answer);   // what the window shows is read from the log
        }
        catch (Exception ex)
        {
            AppLog.Error("分析出错", ex);
            Cloak(false);
            ShowAnswer("出错了，详见 设置→报错日志");
        }
        finally
        {
            if (_ball != null) _ball.Text = "答";
            _busy = false;
        }
    }

    void Cloak(bool on)
    {
        if (_ball != null && _ball.Visibility != ViewStates.Gone)
            _ball.Visibility = on ? ViewStates.Invisible : ViewStates.Visible;
        if (_answer != null && _answer.Visibility != ViewStates.Gone)
            _answer.Visibility = on ? ViewStates.Invisible : ViewStates.Visible;
    }

    void ShowAnswer(string text)
    {
        if (_answer == null)
        {
            _answer = new TextView(this) { Gravity = GravityFlags.Center };
            _answer.TextSize = 20;
            _answer.SetTextColor(Color.White);
            _answer.SetPadding((int)(18 * _density), (int)(10 * _density), (int)(18 * _density), (int)(10 * _density));
            var bg = new GradientDrawable();
            bg.SetColor(Color.Argb(225, 20, 20, 20));
            bg.SetCornerRadius(14 * _density);
            bg.SetStroke((int)(2 * _density), Color.Argb(255, 212, 175, 55));
            _answer.Background = bg;
            _answer.Click += (_, _) => _answer.Visibility = ViewStates.Gone;   // tap to dismiss
            _answerLp = new WindowManagerLayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
                WindowManagerTypes.ApplicationOverlay, WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutNoLimits, Format.Translucent)
            { Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal, Y = (int)(24 * _density) };
        }
        _answer.Text = text;
        _answer.Visibility = ViewStates.Visible;
        if (!_answerAdded) { _wm.AddView(_answer, _answerLp); _answerAdded = true; }
        else _wm.UpdateViewLayout(_answer, _answerLp);
        _handler.RemoveCallbacks(_hideAnswer);
        _handler.PostDelayed(_hideAnswer, 10000);
    }

    public override void OnDestroy()
    {
        try { if (_ballAdded) _wm.RemoveView(_ball); } catch { }
        try { if (_answerAdded) _wm.RemoveView(_answer); } catch { }
        _ballAdded = _answerAdded = false;
        _grabber?.Dispose();
        _engine?.Dispose();
        base.OnDestroy();
    }
}
