using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

namespace MChub.Mobile;

[Activity(
    Label = "MChub",
    Theme = "@style/MChubTheme",
    Icon = "@drawable/mchub_icon",
    MainLauncher = true,
    // Minecraft Java 版是横屏游戏，直接锁横屏，避免启动后反复重建 Activity。
    ScreenOrientation = ScreenOrientation.Landscape,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.UiMode
        | ConfigChanges.Density
        | ConfigChanges.Keyboard
        | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// 当前 Activity。
    ///
    /// <para>游戏要往 Activity 的 content 上挂 SurfaceView（见
    /// <see cref="Services.MobileGameSurfaceHost"/>），而托管侧只有 Application Context、
    /// 拿不到 Activity，所以在这里留一个引用。本项目是单 Activity 应用，直接保存即可。</para>
    /// </summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;

        // 把 Application Context 交给「只能拿 JNI 符号」的那几个 Java 类
        // （org.lwjgl.glfw.CallbackBridge 要用它读剪贴板与 DPI）。
        Services.JavaRuntimeBridge.AttachApplicationContext(Application.Context);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
        }

        base.OnDestroy();
    }
}
