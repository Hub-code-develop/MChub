# InfinityUI

直角 + 五色的 Avalonia 控件库。控件类型与模板全部自绘，不继承任何第三方视觉主题。

**设计约束**

- **零圆角**：所有控件 `CornerRadius` 恒为 0，层级靠 1px 描边与灰阶区分，不使用柔和阴影。
- **五色纪律**：黑白（主题色两极）+ 红 + 黄 + 绿。红黄绿仅用于错误 / 警告 / 成功语义，界面上不出现第六种颜色。
- **双主题**：令牌用 `ThemeDictionaries` 提供 Dark / Light 两套，跟随 `RequestedThemeVariant` 自动切换。

## 安装

```xml
<PackageReference Include="InfinityUI" Version="0.1.0" />
```

## 接入

`App.axaml`：

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://InfinityUI/Tokens/InfinityTokens.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>

<Application.Styles>
  <!-- 放在最后，才能覆盖宿主里已有的控件主题 -->
  <StyleInclude Source="avares://InfinityUI/Themes/InfinityTheme.axaml" />
</Application.Styles>
```

## 控件

| 控件 | 说明 |
| --- | --- |
| `InfinityButton` | `Variant`：`Primary` / `Secondary` / `Ghost` / `Danger`；`IsCompact` 切 28 高 |
| `InfinityIconButton` | 方形图标按钮，`Glyph` + `GlyphFontFamily` + `BoxSize` |
| `InfinityToggleSwitch` | 矩形轨道 + 方块滑块，支持 `OnContent` / `OffContent` |
| `InfinityCard` | 直角容器，`IsRaised` 抬高一级底色，`ShowBorder` 控制描边 |
| `InfinityNavItem` | 导航项，`IsActive` 实心选中，`ShowIndicator` 左侧 3px 指示条 |
| `InfinityToast` | Metro 通知卡片：左侧 6px 语义色条 |
| `InfinityToastHost` | 通知宿主：右下角堆叠、自动超时、淡入淡出 |

## 示例

```xml
<ui:InfinityButton Content="开始游戏" Variant="Primary" Click="OnLaunch" />
<ui:InfinityButton Content="删除" Variant="Danger" />
<ui:InfinityIconButton Glyph="&#xe627;" GlyphFontFamily="{StaticResource IconFont}" />

<ui:InfinityCard IsRaised="True">
  <TextBlock Text="内容" />
</ui:InfinityCard>

<ui:InfinityToggleSwitch IsChecked="{Binding Enabled}" OnContent="开" OffContent="关" />
```

```csharp
// 通知
var host = new InfinityToastHost();
rootPanel.Children.Add(host);

host.Show("安装完成", InfinityToastLevel.Success);
host.Show("认证失败，请检查网络", InfinityToastLevel.Error, "启动失败");
```

## 令牌

`InfSurface` / `InfSurfaceAlt` / `InfSurfaceRaised`、`InfBorder` / `InfBorderStrong`、
`InfOnSurface` / `InfMuted`、`InfAccent` / `InfOnAccent`、`InfDanger` / `InfWarning` / `InfSuccess`。

尺寸：`InfControlHeight`(34)、`InfControlHeightCompact`(28)、`InfFontSize`(13)、`InfStrokeThickness`(1)、`InfRadius`(0)。

## 已知限制

- 目前只覆盖 Button / IconButton / ToggleSwitch / Card / NavItem / Toast；
  TextBox、ComboBox、Slider、菜单与对话框尚未实现。
- 通知的自动关闭依赖 UI 线程的 `DispatcherTimer`，在后台线程调用 `Show` 需自行切回 UI 线程。

## 许可

GPL-3.0-or-later
