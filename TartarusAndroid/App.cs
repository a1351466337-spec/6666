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

        _status = new TextView(this) { Text = "点「开启悬浮球」，授权截屏后，悬浮球会出现在屏幕中央。\n单击：马上分析；双击：隐藏；长按后拖动。\n打开「实时分析」后，画面一变就自动分析（球变绿，显示“实”）；也可以在通知栏里开关。\n隐藏后可从通知栏的「显示悬浮球」找回。" };
        _status.TextSize = 15;
        root.AddView(_status);

        var start = new Button(this) { Text = "开启悬浮球" };
        start.Click += (_, _) => StartBall();
        root.AddView(start);

        var stop = new Button(this) { Text = "关闭悬浮球" };
        stop.Click += (_, _) => StopService(new Intent(this, typeof(FloatingService)));
        root.AddView(stop);

        var live = new Switch(this) { Text = "实时分析（画面变化后自动分析）" };
        live.Checked = Prefs.Live(this);
        live.CheckedChange += (_, e) =>
        {
            Prefs.SetLive(this, e.IsChecked);
            if (FloatingService.Running)
            {
                var i = new Intent(this, typeof(FloatingService));
                i.SetAction(FloatingService.ActionLive);
                i.PutExtra("on", e.IsChecked);
                StartService(i);
            }
        };
        root.AddView(live);

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

    public const int SigW = 64, SigH = 28;

    /// <summary>
    /// A 64x28 grey thumbnail of the newest frame (a few rows read, not the whole frame), to tell whether the screen
    /// changed. The rectangles (the ball, the answer window) are left out.
    /// </summary>
    public byte[]? Signature(List<(int X0, int Y0, int X1, int Y1)> masks)
    {
        lock (_gate)
        {
            if (_latest == null) return null;
            var plane = _latest.GetPlanes()![0];
            var buf = plane.Buffer!;
            int w = _latest.Width, h = _latest.Height, rowStride = plane.RowStride, px = plane.PixelStride;
            var sig = new byte[SigW * SigH];
            var row = new byte[w * px];
            for (int sy = 0; sy < SigH; sy++)
            {
                int y = (sy * 2 + 1) * h / (SigH * 2);
                buf.Position(y * rowStride);
                buf.Get(row, 0, Math.Min(row.Length, buf.Remaining()));
                for (int sx = 0; sx < SigW; sx++)
                {
                    int x = (sx * 2 + 1) * w / (SigW * 2);
                    bool masked = false;
                    foreach (var m in masks)
                        if (x >= m.X0 && x < m.X1 && y >= m.Y0 && y < m.Y1) { masked = true; break; }
                    if (masked) continue;
                    int i = x * px;
                    if (i + 2 < row.Length) sig[sy * SigW + sx] = (byte)((row[i] + row[i + 1] * 2 + row[i + 2]) / 4);
                }
            }
            return sig;
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
static class Prefs
{
    static ISharedPreferences P(Context c) => c.GetSharedPreferences("tartarus", FileCreationMode.Private)!;
    public static bool Live(Context c) => P(c).GetBoolean("live", false);
    public static void SetLive(Context c, bool on) => P(c).Edit()!.PutBoolean("live", on)!.Apply();
}

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaProjection)]
public class FloatingService : Service
{
    public const string ActionStart = "tartarus.START", ActionShow = "tartarus.SHOW", ActionQuit = "tartarus.QUIT", ActionLive = "tartarus.LIVE";
    public static volatile bool Running;
    const string ChannelId = "tartarus_ball";
    const int ChangeCount = 22;   // of the 64x28 samples, this many changing strongly = the screen changed
    const int SettleCount = 8;    // fewer than this changing between two looks = the screen has settled

    readonly Handler _handler = new(Looper.MainLooper!);
    IWindowManager _wm = null!;
    TextView? _ball, _answer;
    GradientDrawable? _ballBg;
    WindowManagerLayoutParams? _ballLp, _answerLp;
    bool _ballAdded, _answerAdded, _busy, _dragging, _moved, _tapPending, _userMoved, _live;
    float _density, _downX, _downY;
    int _size, _slop, _startX, _startY, _noBoardStreak;
    string _lastAnswer = "";
    DateTime _lastShot = DateTime.MinValue;
    CancellationTokenSource? _liveCts;
    Java.Lang.Runnable _longPress = null!, _singleTap = null!, _hideAnswer = null!;
    ScreenGrabber? _grabber;
    AnswerEngine? _engine;
    volatile bool _ready;

    public override IBinder? OnBind(Intent? intent) => null;

    public override void OnCreate()
    {
        base.OnCreate();
        Running = true;
        AppLog.Init(FilesDir!.AbsolutePath);
        _live = Prefs.Live(this);
        _wm = GetSystemService(WindowService)!.JavaCast<IWindowManager>();
        _density = Resources!.DisplayMetrics!.Density;
        _size = (int)(56 * _density);
        _slop = (int)(8 * _density);
        _longPress = new Java.Lang.Runnable(BeginDrag);
        _singleTap = new Java.Lang.Runnable(() => { _tapPending = false; OnSingleTap(); });
        _hideAnswer = new Java.Lang.Runnable(HideAnswer);
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
            case ActionLive:
                SetLive(intent.HasExtra("on") ? intent.GetBooleanExtra("on", false) : !_live);
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
                if (_live) StartLive();
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

    // ---------- notification (keeps the service alive; brings the ball back; switches the live mode) ----------
    Notification BuildNotification()
    {
        var icon = Android.Graphics.Drawables.Icon.CreateWithResource(this, Android.Resource.Drawable.IcMenuView)!;
        PendingIntent Act(int code, string action) => PendingIntent.GetService(this, code,
            new Intent(this, typeof(FloatingService)).SetAction(action),
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        return new Notification.Builder(this, ChannelId)
            .SetContentTitle(_live ? "塔罗斯助手：实时分析中" : "塔罗斯助手运行中")
            .SetContentText("悬浮球被隐藏时，点「显示悬浮球」")
            .SetSmallIcon(Android.Resource.Drawable.IcMenuView)!
            .AddAction(new Notification.Action.Builder(icon, "显示悬浮球", Act(1, ActionShow)).Build())!
            .AddAction(new Notification.Action.Builder(icon, _live ? "关闭实时分析" : "开启实时分析", Act(3, ActionLive)).Build())!
            .AddAction(new Notification.Action.Builder(icon, "退出", Act(2, ActionQuit)).Build())!
            .Build()!;
    }

    void StartAsForeground()
    {
        var nm = GetSystemService(NotificationService)!.JavaCast<NotificationManager>();
        nm.CreateNotificationChannel(new NotificationChannel(ChannelId, "悬浮球", NotificationImportance.Low));
        var n = BuildNotification();
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q) StartForeground(1, n, ForegroundService.TypeMediaProjection);
        else StartForeground(1, n);
    }

    void RefreshNotification()
    {
        try
        {
            var nm = GetSystemService(NotificationService)!.JavaCast<NotificationManager>();
            nm.Notify(1, BuildNotification());
        }
        catch { }
    }

    // ---------- live mode ----------
    void SetLive(bool on)
    {
        _live = on;
        Prefs.SetLive(this, on);
        UpdateBallLook();
        RefreshNotification();
        if (on) StartLive(); else StopLive();
        Toast.MakeText(this, on ? "实时分析：开" : "实时分析：关", ToastLength.Short)!.Show();
    }

    void StartLive()
    {
        StopLive();
        _liveCts = new CancellationTokenSource();
        _ = LiveLoop(_liveCts.Token);
    }

    void StopLive()
    {
        _liveCts?.Cancel();
        _liveCts = null;
    }

    static int CountDiff(byte[] a, byte[] b)
    {
        int n = 0;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != 0 && b[i] != 0 && Math.Abs(a[i] - b[i]) > 36) n++;
        return n;
    }

    /// <summary>
    /// Looks at the screen every 0.3 s (a thumbnail, cheap). When it has changed since the last analysed screen and has
    /// settled (the game's own animations are small changes), it analyses it. Other apps' screens back off.
    /// </summary>
    async Task LiveLoop(CancellationToken ct)
    {
        byte[]? analyzed = null, prev = null;
        bool pending = true;
        int stable = 0, waited = 0;
        var lastRun = DateTime.MinValue;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(300, ct);
                if (_busy || !_ready || _grabber == null || _ball == null || _ball.Visibility == ViewStates.Gone) continue;
                var sig = _grabber.Signature(MaskRects());
                if (sig == null) continue;
                if (analyzed != null && CountDiff(sig, analyzed) > ChangeCount) pending = true;
                int moving = prev == null ? 99 : CountDiff(sig, prev);
                prev = sig;
                if (!pending) continue;
                if (moving < SettleCount) stable++; else stable = 0;
                waited += 300;
                if (stable < 2 && waited < 2500) continue;
                double minGap = _noBoardStreak < 3 ? 1.2 : Math.Min(_noBoardStreak, 8);
                if ((DateTime.UtcNow - lastRun).TotalSeconds < minGap) continue;
                lastRun = DateTime.UtcNow;
                pending = false; stable = 0; waited = 0;
                var done = await AnalyzeOnce(manual: false);
                if (done != null) analyzed = done; else pending = true;
                prev = null;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Error("实时分析出错", ex); }
    }

    List<(int X0, int Y0, int X1, int Y1)> MaskRects()
    {
        var list = new List<(int X0, int Y0, int X1, int Y1)>();
        int m = (int)(8 * _density);
        if (_ballLp != null) list.Add((_ballLp.X - m, _ballLp.Y - m, _ballLp.X + _size + m, _ballLp.Y + _size + m));
        if (_answer != null && _answerLp != null)
        {
            var (w, _, _) = ScreenGrabber.Metrics(this);
            int aw = _answer.Width > 0 ? _answer.Width : (int)(w * 0.45);
            int ah = _answer.Height > 0 ? _answer.Height : (int)(90 * _density);
            int ax = (w - aw) / 2, ay = _answerLp.Y;
            list.Add((ax - m, ay - m, ax + aw + m, ay + ah + m));
        }
        return list;
    }

    // ---------- the ball ----------
    void CreateBall()
    {
        _ball = new TextView(this) { Text = "答", Gravity = GravityFlags.Center };
        _ball.TextSize = 18;
        _ball.SetTextColor(Color.White);
        _ballBg = new GradientDrawable();
        _ballBg.SetShape(ShapeType.Oval);
        _ballBg.SetColor(Color.Argb(210, 30, 30, 30));
        _ball.Background = _ballBg;
        _ball.Touch += (_, e) => e.Handled = OnBallTouch(e.Event!);
        _ballLp = new WindowManagerLayoutParams(_size, _size, WindowManagerTypes.ApplicationOverlay,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutNoLimits, Format.Translucent)
        { Gravity = GravityFlags.Top | GravityFlags.Left };
        UpdateBallLook();
    }

    /// <summary>Gold ring "答" when idle, green ring "实" in the live mode, "…" while analysing.</summary>
    void UpdateBallLook()
    {
        if (_ball == null || _ballBg == null) return;
        _ballBg.SetStroke((int)(2 * _density), _live ? Color.Argb(255, 80, 200, 120) : Color.Argb(255, 212, 175, 55));
        _ball.Text = _busy ? "…" : _live ? "实" : "答";
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
            // a second tap: hide the ball (the notification brings it back); the live mode waits meanwhile
            _handler.RemoveCallbacks(_singleTap);
            _tapPending = false;
            if (_ball != null) _ball.Visibility = ViewStates.Gone;
            HideAnswer();
            return;
        }
        _tapPending = true;
        _handler.PostDelayed(_singleTap, 300);
    }

    // ---------- analyse: screenshot, solve, log, show ----------
    async void OnSingleTap() => await AnalyzeOnce(manual: true);

    /// <summary>Returns the thumbnail of the analysed screen, or null when nothing was analysed.</summary>
    async Task<byte[]?> AnalyzeOnce(bool manual)
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            if (_grabber == null)
            {
                if (manual) { AppLog.Error("没有截屏权限，请回到应用重新开启悬浮球"); ShowAnswer("截屏未授权"); }
                return null;
            }
            if (!_ready || _engine == null)
            {
                if (manual) ShowAnswer("模型准备中，稍等");
                return null;
            }

            UpdateBallLook();
            Cloak(true);                       // the overlay would be in the screenshot (and in the OCR)
            _grabber.FollowScreenSize();
            long seq = _grabber.Seq;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Task.Delay(200);
            while (_grabber.Seq < seq + 2 && sw.ElapsedMilliseconds < 1500) await Task.Delay(30);
            var frame = _grabber.Grab();
            var sig = _grabber.Signature(MaskRects());
            Cloak(false);

            if (frame == null)
            {
                if (manual) { AppLog.Error("截屏失败：没有拿到画面"); ShowAnswer("截屏失败"); }
                return null;
            }

            string answer = await _engine.SolveAsync(frame, manual);
            bool noBoard = answer == AnswerEngine.NoBoard;
            _noBoardStreak = noBoard && !manual ? _noBoardStreak + 1 : 0;
            if (noBoard)
            {
                if (manual || (DateTime.UtcNow - _lastShot).TotalSeconds > 60)
                {
                    _lastShot = DateTime.UtcNow;
                    int fw = frame.Width, fh = frame.Height;
                    byte[] fp = frame.Pixels;
                    await Task.Run(() => SaveDebugImage(fw, fh, fp));   // to see what the scanner saw
                }
                if (manual) ShowAnswer(answer); else HideAnswer();
            }
            else
            {
                if (manual || answer != _lastAnswer) AppLog.Answer(answer);
                _lastAnswer = answer;
                ShowAnswer(AppLog.LastAnswer() ?? answer);   // what the window shows is read from the log
            }
            return sig;
        }
        catch (Exception ex)
        {
            AppLog.Error("分析出错", ex);
            Cloak(false);
            if (manual) ShowAnswer("出错了，详见 设置→报错日志");
            return null;
        }
        finally
        {
            _busy = false;
            UpdateBallLook();
        }
    }

    /// <summary>The frame the scanner could not read goes to the gallery (Pictures/Tartarus).</summary>
    void SaveDebugImage(int w, int h, byte[] rgba)
    {
        try
        {
            using var bmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
            bmp.CopyPixelsFromBuffer(Java.Nio.ByteBuffer.Wrap(rgba)!);
            var values = new ContentValues();
            values.Put("_display_name", $"tartarus_fail_{DateTime.Now:HHmmss}.jpg");
            values.Put("mime_type", "image/jpeg");
            values.Put("relative_path", "Pictures/Tartarus");
            var uri = ContentResolver!.Insert(MediaStore.Images.Media.ExternalContentUri!, values);
            if (uri == null) { AppLog.Error("保存失败截图：无法创建文件"); return; }
            using var stream = ContentResolver.OpenOutputStream(uri)!;
            bmp.Compress(Bitmap.CompressFormat.Jpeg!, 85, stream);
            AppLog.Error($"[调试] 识别失败的那一帧已存到 相册/Tartarus ({w}x{h})");
        }
        catch (Exception ex)
        {
            AppLog.Error("保存失败截图出错", ex);
        }
    }

    void Cloak(bool on)
    {
        if (_ball != null && _ball.Visibility != ViewStates.Gone)
            _ball.Visibility = on ? ViewStates.Invisible : ViewStates.Visible;
        if (_answer != null && _answer.Visibility != ViewStates.Gone)
            _answer.Visibility = on ? ViewStates.Invisible : ViewStates.Visible;
    }

    void HideAnswer()
    {
        if (_answer != null) _answer.Visibility = ViewStates.Gone;
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
            _answer.Click += (_, _) => HideAnswer();   // tap to dismiss
            _answerLp = new WindowManagerLayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent,
                WindowManagerTypes.ApplicationOverlay, WindowManagerFlags.NotFocusable | WindowManagerFlags.LayoutNoLimits, Format.Translucent)
            { Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal, Y = (int)(24 * _density) };
        }
        _answer.Text = text;
        _answer.Visibility = ViewStates.Visible;
        if (!_answerAdded) { _wm.AddView(_answer, _answerLp); _answerAdded = true; }
        else _wm.UpdateViewLayout(_answer, _answerLp);
        _handler.RemoveCallbacks(_hideAnswer);
        if (!_live) _handler.PostDelayed(_hideAnswer, 10000);   // the live mode keeps the newest answer up
    }

    public override void OnDestroy()
    {
        Running = false;
        StopLive();
        try { if (_ballAdded) _wm.RemoveView(_ball); } catch { }
        try { if (_answerAdded) _wm.RemoveView(_answer); } catch { }
        _ballAdded = _answerAdded = false;
        _grabber?.Dispose();
        _engine?.Dispose();
        base.OnDestroy();
    }
}
