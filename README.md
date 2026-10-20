<img src="assets/patch.png" alt="PCL 下载补丁图标" width="104">

# PCL 下载功能补丁（第三方）

PCL 原作者：[龙腾猫跃 / Meloong-Git](https://github.com/Meloong-Git/PCL)。[PCL 官网与支持原作者入口](https://meloong.com/PCL)。

由 Acetab 独立维护的 Windows 补丁工具，提供结构检查、原版备份、还原和内置使用说明。**非 PCL 官方发布。**

## 下载和使用

从 [Releases](https://github.com/Acetab/pcl-download-patch/releases/latest) 下载 `PCL-Download-Patch.exe` 或 Windows ZIP 包。使用说明已内置，无需额外说明文件。下载文件使用英文名称，程序界面保持中文。

1. 退出 PCL，将工具放在启动器旁边。
2. 双击工具，选择 PCL 的 EXE；也支持拖入文件。
3. 显示“可应用补丁”后，点击“恢复下载”。
4. 正常打开 PCL。要查看说明，点击“使用说明 / 关于”。

备份保存在启动器旁的 `PCL补丁备份` 文件夹，名称包含版本与时间。撤销时选择同一个 EXE，点击“还原原版”。旧版工具生成的哈希命名备份仍支持还原。

## 适配范围

**仅实测正式版 PCL 2.99.0.0，不保证每次更新都能适配。**

| 情况 | 工具行为 |
| --- | --- |
| 与已适配版本保持相同方法及指令结构 | 检查通过后可应用 |
| 更新覆盖了已打补丁的 EXE | 可重新打开工具检查 |
| 方法长度、指令、类型名称或文件结构变化 | 停止并提示需要适配 |
| 带有 Authenticode 签名或程序集强名称 | 当前工具拒绝修改 |

本工具按 PE/CLI 元数据定位类型、字段和方法，校验关键指令与停用提示，再修改下载页面初始化方法的一段 48 字节区域。该版本实际变化 41 字节，使用启动器原有的下载加载器。补丁不会自动联网获取或执行新规则。

补丁不关闭 PCL 更新，也不修改 `Setup.ini`。此前若设置了 `SystemSystemUpdate:3`，需自行重新开启更新提示。

## 验证

- PCL 2.99.0.0：真实窗口中恢复版本列表，并完成 `rd-132211` 下载和安装；下载 JAR 的 SHA-1 与 Mojang 对象地址一致。
- 备份与完整还原、重复应用、备份复用、新旧备份兼容、损坏备份拒绝还原均已测试。
- 未测试所有 MC 版本、模组、整合包或游戏启动。
- 本工具仅改选中的 EXE。仓库不包含 PCL 原版/修改版程序、游戏数据或用户账号信息。

## 构建与测试

需要 Windows，安装 .NET Framework 4.8。构建使用系统 Framework 的 C# 编译器，不需要安装额外 NuGet 包。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

产物位于 `dist/`。图标和说明均作为资源嵌入 EXE。

用一份未修改的、兼容的 PCL EXE 或原版备份做本地回归测试（只操作测试副本）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-Backup.ps1 -OriginalLauncher 'D:\path\original-PCL.bak'
```

CI 负责构建并上传产物；带 `v` 前缀的版本标签触发 Release 发布。CI 构建成功不代表兼容所有未来 PCL 版本。

## 署名与许可

补丁代码采用 [MIT](LICENSE) 许可。PCL 名称及原图标的权益不属于本项目的 MIT 授权范围，详见 [第三方说明](NOTICE.md)。图标以用户提供的 PCL 图标为参考，通过 imagegen 生成带扳手徽标的第三方工具变体。

若提交问题，请提供 PCL 版本、补丁版本、提示文字和 EXE 的 SHA-256；不要上传账号配置、令牌、游戏数据或含个人信息的完整日志。
