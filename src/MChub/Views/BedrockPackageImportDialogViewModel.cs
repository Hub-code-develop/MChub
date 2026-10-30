using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MChub.Core.Minecraft.Classes;
using MChub.Core.Minecraft.Instance;
using MChub.Core.Minecraft.Instance.Bedrock;
using MChub.Core.Minecraft.Models;
using MChub.Core.Minecraft.Services;
using MChub.Localization;
using TioUi.Common.Interfaces;

namespace MChub.Views;

/// <summary>
/// 基岩版整合包导入的目标选择模型。
/// 原先和 <c>BedrockPackageImportDialog</c> 放在同一个文件里，随旧 UI 一起被删；
/// 但这是**业务逻辑**（列出可导入实例、解析存档用户、判定能否导入），
/// 所以从 git 历史里捞出来单独保留，只去掉与对话框控件耦合的部分。
/// </summary>
public sealed record BedrockPackageImportDialogResult(MinecraftInstance Instance, string? WorldUserId);

public sealed record BedrockPackageInstanceItem(MinecraftInstance Instance, string Name, string Version);

public partial class BedrockPackageImportDialogViewModel : ObservableObject, IDialogContext
{
    private readonly BedrockPackageInspection? _inspection;

    public BedrockPackageImportDialogViewModel(BedrockPackageInspection inspection)
    {
        _inspection = inspection;
        InitializeInstances();
    }

    public ObservableCollection<BedrockPackageInstanceItem> Instances { get; } = [];
    public ObservableCollection<string> WorldUserIds { get; } = [];
    public bool HasNoInstances => Instances.Count == 0;

    public bool RequiresUserId => _inspection?.ArchiveType == BedrockPackageArchiveType.Mcworld;

    public bool CanImport =>
        SelectedInstance != null && (!RequiresUserId || !string.IsNullOrWhiteSpace(SelectedWorldUserId));

    public string PackageDescription => RequiresUserId
            ? string.Format(CommonLanguageManager.Instance.bedrockPackageImport_displayNameWithSave.CurrentValue(),
                _inspection!.DisplayName)
            : string.Format(CommonLanguageManager.Instance.bedrockPackageImport_displayNameWithContents.CurrentValue(),
                _inspection!.DisplayName,
                string.Join("、", _inspection.Contents.Select(content => content.Type switch
                {
                    BedrockPackageContentType.ResourcePack =>
                        CommonLanguageManager.Instance.resourceList_packNameResourcePack.CurrentValue(),
                    BedrockPackageContentType.BehaviorPack =>
                        CommonLanguageManager.Instance.resourceList_packNameBehaviorPack.CurrentValue(),
                    BedrockPackageContentType.SkinPack =>
                        CommonLanguageManager.Instance.resourceList_packNameSkinPack.CurrentValue(),
                    BedrockPackageContentType.WorldTemplate =>
                        CommonLanguageManager.Instance.bedrockPackageImport_worldTemplate.CurrentValue(),
                    _ => CommonLanguageManager.Instance.bedrockPackageImport_bedrockPackage.CurrentValue()
                }).Distinct()));

    [ObservableProperty] public partial BedrockPackageInstanceItem? SelectedInstance { get; set; }
    [ObservableProperty] public partial string? SelectedWorldUserId { get; set; }

    public void Close()
    {
        Cancel();
    }

    public event EventHandler<object?>? RequestClose;

    private void InitializeInstances()
    {
        foreach (var instance in InstanceManager.Instance.Instances.Where(instance => instance.IsBedrock))
            Instances.Add(new BedrockPackageInstanceItem(instance, instance.InstanceName, instance.VersionId));
        SelectedInstance = Instances.FirstOrDefault();
    }

    partial void OnSelectedInstanceChanged(BedrockPackageInstanceItem? value)
    {
        WorldUserIds.Clear();
        if (RequiresUserId && value?.Instance.BedrockConfig is { } config)
            foreach (var userId in BedrockDataPathResolver.GetWorldUserIds(config))
                WorldUserIds.Add(userId);
        SelectedWorldUserId =
            WorldUserIds.FirstOrDefault(userId => !string.Equals(userId, "Shared", StringComparison.OrdinalIgnoreCase))
            ?? WorldUserIds.FirstOrDefault();
        OnPropertyChanged(nameof(CanImport));
    }

    partial void OnSelectedWorldUserIdChanged(string? value)
    {
        OnPropertyChanged(nameof(CanImport));
    }

    public void Import()
    {
        if (SelectedInstance != null && CanImport)
            RequestClose?.Invoke(this,
                new BedrockPackageImportDialogResult(SelectedInstance.Instance, SelectedWorldUserId));
    }

    public void Cancel()
    {
        RequestClose?.Invoke(this, null);
    }
}