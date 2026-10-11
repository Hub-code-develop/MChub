/*
 * 本文件是 MChub 自己写的「壳」——**不是**上游 MainActivity 的移植，也不是本 App 的 Activity。
 *
 * 为什么需要它：libpojavexec_awt.so（源文件 jni/awt_bridge.c）的 JNI_OnLoad 会做
 *
 *     FindClass("net/kdt/pojavlaunch/MainActivity")
 *     GetStaticMethodID(..., "openLink",           "(Ljava/lang/String;)V")
 *     GetStaticMethodID(..., "querySystemClipboard","()V")
 *     GetStaticMethodID(..., "putClipboardData",    "(Ljava/lang/String;Ljava/lang/String;)V")
 *
 * 少任何一个，装载这个库时就会「JNI NewGlobalRef called with pending exception
 * java.lang.ClassNotFoundException」直接 SIGABRT（实测过）。
 *
 * 这三个方法只服务 Caciocavallo（Android 上的 AWT）那一条路径：游戏里 java.awt 的剪贴板与
 * 「用系统程序打开链接」。所以这里只实现这三个静态方法，其余一概不搬 ——
 * 上游那个 MainActivity 是它自己的 Activity，我们用的是 .NET Android 的 Avalonia Activity，
 * 两者没有关系，只是名字恰好撞在一起。
 */
package net.kdt.pojavlaunch;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.util.Log;

/** 只为 libpojavexec_awt.so 提供三个静态入口的壳类（见文件头注释）。 */
public final class MainActivity {
    private static final String TAG = "MChubBridge";

    private MainActivity() {
    }

    /** AWT 路径要求「用系统程序打开这个链接」。 */
    public static void openLink(String link) {
        Context context = hub.code.mchub.runtime.AndroidBridge.context();
        if (context == null || link == null) {
            return;
        }

        try {
            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(link));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(intent);
        } catch (Throwable error) {
            // 设备上没有能处理该链接的 App 时 startActivity 会抛 ActivityNotFoundException。
            Log.w(TAG, "打开链接失败：" + link + " —— " + error);
        }
    }

    /** AWT 路径询问「系统剪贴板里是什么」，结果经 AWTInputBridge 回给游戏。 */
    public static void querySystemClipboard() {
        Context context = hub.code.mchub.runtime.AndroidBridge.context();
        ClipboardManager manager = context == null
                ? null
                : (ClipboardManager) context.getSystemService(Context.CLIPBOARD_SERVICE);

        ClipData clip = manager == null ? null : manager.getPrimaryClip();
        if (clip == null || clip.getItemCount() == 0) {
            AWTInputBridge.nativeClipboardReceived(null, null);
            return;
        }

        CharSequence text = clip.getItemAt(0).getText();
        if (text == null) {
            AWTInputBridge.nativeClipboardReceived(null, null);
            return;
        }

        AWTInputBridge.nativeClipboardReceived(text.toString(), "plain");
    }

    /** AWT 路径要把内容写进系统剪贴板。 */
    public static void putClipboardData(String data, String mimeType) {
        Context context = hub.code.mchub.runtime.AndroidBridge.context();
        if (context == null) {
            return;
        }

        ClipboardManager manager = (ClipboardManager) context.getSystemService(Context.CLIPBOARD_SERVICE);
        if (manager == null) {
            return;
        }

        ClipData clip = null;
        if ("text/plain".equals(mimeType)) {
            clip = ClipData.newPlainText("AWT Paste", data);
        } else if ("text/html".equals(mimeType)) {
            clip = ClipData.newHtmlText("AWT Paste", data, data);
        }

        if (clip != null) {
            manager.setPrimaryClip(clip);
        }
    }
}
