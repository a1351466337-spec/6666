using System.Diagnostics;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
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

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.FitsSystemWindows = true;
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
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.FitsSystemWindows = true;
        root.SetPadding((int)(12 * d), (int)(12 * d), (int)(12 * d), (int)(12 * d));

        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var title = new TextView(this) { Text = "报错日志" };
        title.TextSize = 18;
        bar.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var refresh = new Button(this) { Text = "刷新" };
        refresh.Click += (_, _) => Refresh();
        bar.AddView(refresh
