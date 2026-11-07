使用 System.Diagnostics;
按钮(
使用 Android.Content;
使用 Android.Content.PM;
使用 Android.Graphics;
使用 Android.Graphics.Drawables；
使用 Android.Media;
使用 Android.Media.Projection;
使用 Android.OS；
使用 Android.Provider；
使用 Android.Runtime;
使用 Android.Util;
使用 Android.Views;
usingAndroid Widget；

[assembly: UsesPermission(Name = “android.permission.SYSTEM_ALERT_WINDOW”)]
[assembly: UsesPermission(Name = “android.permission.FOREGROUND_SERVICE”)]
[assembly: UsesPermission(Name = “android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION”)]
[assembly: UsesPermission(Name = “android.permission.POST_NOTIFICATIONS”)]
[assembly: UsesPermission(Name = “android.permission.VIBRATE”)]

命名空间 塔耳塔罗斯安卓版;

// ------------------------------------------------------------------------------------------------
// Main page: start / stop the floating ball; a "设置" bar at the bottom opens the error-log page.
// ------------------------------------------------------------------------------------------------
[活动(标签 ="塔罗斯助手", MainLauncher = 真实, Exported = 真实)]
公共的 班级 MainActivity : 活动
{
constint ReqProjection =1001;
TextView _status = 无!;

受保护 覆盖 无效 OnCreate(Bundle? savedInstanceState)
    {
基础.OnCreate(savedInstanceState);
AppLog.Init(FilesDir!.AbsolutePath);
float d = Resources!.DisplayMetrics!.Density;

变量 根 = 新 LinearLayout(这个) { Orientation = Orientation.Vertical };
root.FitsSystemWindows = 真；
root.SetPadding((int)(16* d)，(int)(16* d)，(int)(16* d)，0);

_status = 新 TextView(这个) { 文本 =点击“开启悬浮球”，授权截图后，悬浮球就会出现在屏幕中央。
单击：进行截图分析；双击：隐藏悬浮球；长按：可以移动悬浮球的位置。  
如果隐藏了悬浮球，可以从通知栏中的“显示悬浮球”选项将其找回来。 };
_status.TextSize =15;
root.AddView(_status);

变量 开始 = 新按钮(这个) { 文本 ="开启悬浮球" };
start.Click += (_, _) => StartBall();
root.AddView(start);

        变量 停止 = 新按钮(这个) { 文本 = "关闭悬浮球" };
stop.Click += (_, _) => StopService(
root.AddView(stop);

Intent(这个, 类型(FloatingService));settings.Click += (_, _) => StartActivity(,1f));

        变量设置 = 新按钮(这个) { 文本 = "⚙  设置" };
settings.Click += (_, _) => StartActivity(
root.AddView(settings);

SetContentView(root);

如果 (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu
&& CheckSelfPermission(“android.permission.POST_NOTIFICATIONS”)!= Permission.Granted)
RequestPermissions(新[] { “android.permission.POST_NOTIFICATIONS” },1);
    }

无效的StartBall()函数
    {
        如果 (!Settings.CanDrawOverlays(这个))
        {
Toast.MakeText(这个,"请先允许「显示在其他应用上层」，然后回来再点一次", ToastLength.Long)!.显示();
            StartActivity(新 Intent(Settings.ActionManageOverlayPermission, Android.Net.Uri.Parse(“包名：” + PackageName)));
            返回;
        }
变量 mpm = GetSystemService(MediaProjectionService)!.JavaCast<MediaProjectionManager>();
StartActivityForResult(mpm.CreateScreenCaptureIntent(), ReqProjection);
    }

受保护 覆盖 无效 OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
基础.OnActivityResult(requestCode, resultCode, data);
如果 (requestCode!= ReqProjection) 则返回；
如果 (resultCode == Result.Ok && data!= 无)
        {
变量 i = 新 Intent(这个, 类型(FloatingService));
i.SetAction(FloatingService.ActionStart);
i.PutExtra(“代码”, (int)resultCode);
i.PutExtra(“数据”, data);
StartForegroundService(i);
_status.Text ="悬浮球已开启（首次启动要解压模型，约几十秒）。切到游戏里点悬浮球即可。";
        }
否则
        {
_status.Text ="没有授权截屏，悬浮球无法使用。";
        }
    }
}

// ------------------------------------------------------------------------------------------------
// The settings page: only the error log.
// ------------------------------------------------------------------------------------------------
[活动(标签 ="设置")]
公共的 班级 日志活动 : 活动
{
TextView _text = 无!;

受保护 覆盖 无效 OnCreate(Bundle? savedInstanceState)
    {
基础.OnCreate(savedInstanceState);
float d = Resources!.DisplayMetrics!.Density;
变量 根 = 新 LinearLayout(这个) { Orientation = Orientation.Vertical };
root.FitsSystemWindows = 真；
root.SetPadding((int)(12* d)，(int)(12* d)，(int)(12* d)，(int)(12* d）；

变量酒吧 = 新LinearLayout(这个) { Orientation = Orientation.Horizontal };
变量 标题 = 新 TextView(这个) { 文本 ="报错日志" };
        title.TextSize =18;
        bar.AddView(title, 新 LinearLayout.LayoutParams(0ViewGroup.LayoutParams.WrapContent，1f));
变量 刷新 = 新按钮(这个) { 文本 ="刷新" };
refresh.Click += (_, _) => Refresh();
bar.AddView(refresh)
