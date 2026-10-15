#!/bin/zsh
# MChub 桌面端启动器（macOS，需 CodeNet arm64 runtime 以规避 macOS 27 的 Wall#2）
#
# 说明：CodeNet fork 版 runtime 在 Apple Silicon + macOS 27 beta 下，加载 FluentAvalonia 主题+
# 全部 XAML 时首次 GC 会 SIGSEGV（WKS::gc_heap::mark_object_simple1 读空指针）。
# 以下环境变量是 CoreCLR 在 Apple Silicon / beta macOS 上的标准加固项，逐个可关：
#   COMPlus_EnableWriteXorExecute=0 关闭 W^X（fork 版 runtime + SIP disabled 时最关键）
#   COMPlus_TieredCompilation=0     关闭分层 JIT，避免 JIT 重编译导致的 GC-info 错配
#   COMPlus_gcConcurrent=0          关闭后台 GC，启动期只走前台 GC
#   COMPlus_gcServer=1              改用 Server GC 替代崩溃的 WKS GC
export COMPlus_EnableWriteXorExecute=0
export COMPlus_TieredCompilation=0
export COMPlus_gcConcurrent=0
export COMPlus_gcServer=1

cd /Users/cangcang/WorkBuddy/2026-09-03-19-52-29/MChub
/Users/cangcang/bin/codenet exec /Users/cangcang/WorkBuddy/2026-09-03-19-52-29/MChub/src/MChub.Desktop/bin/Debug/net10.0/MChub.Desktop.dll
