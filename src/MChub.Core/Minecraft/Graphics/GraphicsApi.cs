namespace MChub.Core.Minecraft.Graphics;

public enum GraphicsApi
{
    Default,
    OpenGL,
    Vulkan
}

public static class GraphicsApiExtensions
{
    private static readonly GameVersion Version26_2Snap1 = GameVersion.Parse("26.2-snapshot-1");
    private static readonly GameVersion Version26_2Snap2 = GameVersion.Parse("26.2-snapshot-2");
    private static readonly GameVersion Version26_2 = GameVersion.Parse("26.2");

    public static GraphicsApi GetDefault(GameVersion version)
    {
        return GetDefault(version, null);
    }

    public static GraphicsApi GetDefault(GameVersion version, PlatformInfo? platform)
    {
        // macOS 上游戏自带的 default 会优先走 Vulkan，实际落到 MoltenVK → Metal（苹果原生后端）。
        // 这条链路在客户端里问题很多（黑屏、花屏、启动即崩），所以 macOS 默认回退 OpenGL，
        // 想用 Vulkan 的用户仍然可以在实例设置里显式选。
        if (platform is { Os: OperatingSystemKind.MacOS })
            return GraphicsApi.OpenGL;

        if (version < Version26_2Snap1)
            return GraphicsApi.OpenGL;

        if (version < Version26_2)
            return GraphicsApi.Vulkan;

        return GraphicsApi.OpenGL;
    }

    public static bool IsSupported(this GraphicsApi api, GameVersion version)
    {
        return api switch
        {
            GraphicsApi.Default or GraphicsApi.OpenGL => true,
            GraphicsApi.Vulkan => version >= Version26_2Snap1,
            _ => false
        };
    }

    public static string GetMinecraftArg(this GraphicsApi api)
    {
        return api switch
        {
            GraphicsApi.OpenGL => "opengl",
            GraphicsApi.Vulkan => "vulkan",
            _ => "default"
        };
    }

    public static bool CanChooseBackend(this GameVersion version)
    {
        return version >= Version26_2Snap2;
    }
}