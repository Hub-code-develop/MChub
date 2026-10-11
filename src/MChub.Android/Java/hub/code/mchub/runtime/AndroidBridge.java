package hub.code.mchub.runtime;

import android.content.Context;

/**
 * 把 Application Context 交给「只能拿 JNI 符号、拿不到 C# 对象」的那几个 Java 类
 * （{@code org.lwjgl.glfw.CallbackBridge} 需要读剪贴板与 DPI）。
 *
 * <p>为什么不直接用 Android 的全局变量：ART 里没有全局 Application 句柄，
 * .NET Android 是从托管侧拿 {@code Android.App.Application.Context}。
 * 所以在启动链路最前面（C# 侧）调一次 {@link #attach(Object)} 即可。</p>
 */
public final class AndroidBridge {
    private static volatile Context sContext;

    private AndroidBridge() {
    }

    /** 登记 Application Context（幂等，重复调用以最后一次为准）。 */
    public static void attach(Object context) {
        if (context instanceof Context) {
            sContext = ((Context) context).getApplicationContext();
        }
    }

    /** 取 Application Context；未登记时返回 null，调用方需判空。 */
    public static Context context() {
        return sContext;
    }
}
