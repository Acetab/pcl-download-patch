# 来源与第三方组件

PCL 原作者：龙腾猫跃，原项目 https://github.com/Meloong-Git/PCL 。本项目非官方发布；补丁代码的 MIT 许可不授予 PCL 名称、图标或上游程序的权利。

本工具由 Acetab 维护。设计参考：Criyc/PCL2- 的入口覆盖与备份对应关系，Jelly10086/PCL-Re.Download_PATCH 的完整恢复与前后校验，1244026418/PCL-Download-Fix 的副本模式，以及前一轮对 PCL2FIX、PaNing6657/launcher-unlocker、TSO4/PCL-DownloadSwitchPlugin 的比较。新引擎独立实现，不包含这些项目的 EXE 或整份补丁源码。五个修复入口来自 PCL 2.99.0.0 的实际指令结构检查；手动整合包入口使用独立工作类型与 Windows 文件选择对话框。

Mono.Cecil 0.11.6 来自 NuGet 官方包，MIT 许可证见 vendor/Mono.Cecil-LICENSE.txt。包 SHA-256：d2a23832aaa948ba9a01acc42b5726e34c5f995958f1b30d45c0e7c70b3a72d5；内嵌 DLL SHA-256：c41bdb9ffd3c5f6e17d2382c1012d73703e035e3f1100245fdd4e08c8dc6eb5b。

图标沿用 1.1.1 的第三方扳手变体，由 imagegen 生成。图标内置于发行程序；使用时不自动联网获取规则。

2.1 主题选项：assets/theme-palette.json 从用户提供的 Plain Craft Launcher 2.patch.exe 中提取单个 ThemeRefresh 方法的配色指令及两段渐变数据，SHA-256 02e882a8f90f333c3451656978fc8248c35e3e423a92f1aa4931640118200e5d。用于恢复主题配色，不含完整启动器。该上游模板不纳入独立工具代码的 MIT 授权；PCL 上游权利和许可继续适用。没有复制土豆码、赞助或更新授权实现。

PCL 上游许可副本见 PCL-LICENCE.txt。第三方项目的参考范围不表示对本工具的背书；本工具不附带原版或修复后的 PCL EXE。
