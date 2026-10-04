using System;
using System.Linq;

namespace MChub.Bedrock.Core;

/// <summary>
/// 基岩版 GDK 安装包使用内容密钥（CIK）加密。这些取值并非私密凭据，而是社区启动器
/// 已公开的已知密钥；安装包会在 XvcInfo 中声明自己需要的密钥 GUID，据此自动匹配即可。
/// 若构建期通过 PRE_MC_KEY / REL_MC_KEY 注入了 48 字节密钥，则优先采用注入值。
/// </summary>
internal static class BedrockCikKeys
{
	private static readonly CikKey[] Known =
	[
		new("91E7B9BD7CC93437E1A8BC602552DF06C9A969FBFCBBF5F46D71250AF226CF6AC7D15C25F9546344549391D16857391F"),
		new("3FD6491FF58B8D1FED7EDBD89477DAD9802814007571F6A353C710BA972EF113C6F250C54B315AF61A33CCA5DE85B08A"),
		new("3684EC330E5A0D4FB1CE3F29C3955039217587B8E319459CBA2EF26F8DE68EA89AB6DC0FBC1142D09F4498B0BEE22496"),
	];

	public static CikKey? Resolve(string[] declaredKeyIds, byte[]? injected)
	{
		if (injected is { Length: CikKey.MaxSize })
		{
			return new CikKey(injected);
		}

		foreach (CikKey key in Known)
		{
			if (declaredKeyIds.Contains(key.Guid.ToString(), StringComparer.OrdinalIgnoreCase))
			{
				return key;
			}
		}

		return null;
	}
}
