/*
 * 本文件是 MChub 自己写的「壳」—— 对应上游 net.kdt.pojavlaunch.ExitActivity
 * （AngelAuraMC/Amethyst-Android @ v3_openjdk，GPL-3.0-or-later）。
 *
 * 为什么必须有：libpojavexec.so 里的 JREUtils.setupExitMethod 会做
 *
 *     FindClass("net/kdt/pojavlaunch/ExitActivity")
 *     GetStaticMethodID(..., "showExitMessage", "(Landroid/content/Context;IZ)V")
 *
 * 缺了它，装完 libpojavexec 后第一次 setupExitMethod 就会
 * 「JNI NewGlobalRef called with pending exception ClassNotFoundException」直接 SIGABRT（实测过）。
 *
 * MChub 与上游的差别：上游这里是个 AppCompatActivity，退出码非 0 时弹一个对话框让用户分享日志。
 * MChub 不另起 Activity —— 退出结果本来就会顺着
 * VMLauncher.launchJVM → LauncherBridge.launch 的返回值回到启动器界面（MobileGameLauncher 会
 * 如实显示退出码/失败原因）。所以这里只做两件事：写 logcat、把退出信息落一份文件，
 * 以便「进程整体被 System.exit 带走」时还能查得到死因。
 *
 * 注意：native 的退出流程在调用本方法之后会对 ART 调 System.exit(0)，也就是**整个应用进程结束**
 * —— 这是 Pojav/Amethyst 的既定设计（游戏即应用），不是 MChub 引入的行为。
 */
package net.kdt.pojavlaunch;

import android.content.Context;
import android.util.Log;

import java.io.File;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;

/** 只为 libpojavexec.so 的退出陷阱提供 showExitMessage(Context, int, boolean)。 */
public final class ExitActivity {
    private static final String TAG = "MChubBridge";

    private ExitActivity() {
    }

    /**
     * 游戏进程结束（正常退出码非 0，或捕获到致命信号）时的回调。
     *
     * @param context  应用 Context（native 侧存的是 setupExitMethod 时传进去的那个）
     * @param code     退出码
     * @param isSignal true 表示是信号导致的（code 是信号号）
     */
    public static void showExitMessage(Context context, int code, boolean isSignal) {
        String reason = isSignal ? ("信号 " + code) : ("退出码 " + code);
        String line = "游戏结束：" + reason
                + "（" + new SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US).format(new Date()) + "）";

        Log.e(TAG, line);

        if (context == null) {
            return;
        }

        try {
            File logs = new File(context.getFilesDir(), "Logs");
            if (logs.isDirectory() || logs.mkdirs()) {
                File marker = new File(logs, "last-game-exit.txt");
                java.io.FileOutputStream stream = new java.io.FileOutputStream(marker, false);
                stream.write(line.getBytes("UTF-8"));
                stream.close();
            }
        } catch (Throwable error) {
            // 记不下来也不能让退出流程崩在这里。
            Log.w(TAG, "写退出标记失败：" + error);
        }
    }
}
