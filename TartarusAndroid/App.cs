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
