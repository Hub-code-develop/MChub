using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MChub.Localization;

namespace MChub.Bedrock.Core;

public class BedrockCore
{
	public virtual async Task<InstallResult?> InstallPackageAsync(LocalGamePackageOptions options)
	{
		ArgumentNullException.ThrowIfNull(options, "options");
		Directory.CreateDirectory(options.InstallDstFolder);
		if (options.Type == MinecraftBuildTypeVersion.GDK)
		{
			await Task.Run(async delegate
			{
				MinecraftGameTypeVersion gameTypeVersion = options.GameTypeVersion;
				byte[] array = gameTypeVersion switch
				{
					MinecraftGameTypeVersion.Release => CikKeys.Release, 
					MinecraftGameTypeVersion.Preview => CikKeys.Preview, 
					MinecraftGameTypeVersion.Beta => CikKeys.Preview, 
					_ => null, 
				};
				byte[] cik = array;
				if (cik == null)
				{
					throw new InvalidOperationException($"Unsupported game type for GDK package: {options.GameTypeVersion}");
				}
				using MsiXVDStream stream = new MsiXVDStream(options.FileFullPath);
				stream.Parse();
				CikKey? key = BedrockCikKeys.Resolve(stream.EncryptionKeys, cik);
				if (key == null)
				{
					throw new InvalidOperationException(CommonLanguageManager.Instance.bedrockInstall_gdkCikMissing.CurrentValue());
				}
				using MsiXVDDecoder decoder = new MsiXVDDecoder(key, options.UseHardwareDecode);
				options.InstallStates?.Report(InstallStates.Extracting);
				await stream.ExtractTaskAsync(Path.GetFullPath(options.InstallDstFolder), decoder, options.ExtractionProgress, options.CancellationToken ?? CancellationToken.None);
				options.InstallStates?.Report(InstallStates.Extracted);
			}, options.CancellationToken ?? CancellationToken.None);
			return new InstallResult();
		}
		throw new PlatformNotSupportedException(CommonLanguageManager.Instance.bedrockInstall_uwpWindowsOnly.CurrentValue());
	}

	public async Task<string> GetPackageUri(BuildInfo buildInfo, Architecture devicesArch)
	{
		Variation variation = buildInfo.Variations.FirstOrDefault((Variation variation2) => variation2.Arch == devicesArch) ?? throw new BedrockCoreException($"Unable to find {devicesArch} Version");
		if (variation.MetaData.Count == 0)
		{
			throw new BedrockCoreNoAvailbaleVersionUri("There is no available Uri to download");
		}
		string metadata = variation.MetaData.Last();
		if (metadata.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return metadata;
		}
		try
		{
			string uri = await UpdateIDHelper.GetUriAsync(metadata);
			if (string.IsNullOrEmpty(uri))
			{
				throw new BedrockCoreNoAvailbaleVersionUri("There is no available uri for this");
			}
			return uri;
		}
		catch (BedrockCoreException)
		{
			throw;
		}
	}
}
