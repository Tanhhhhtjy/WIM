# WIM 周边视野与走动原型

给 `pjy-vr` 的桌面 Unity Editor 体验插件：键鼠贴地漫游、6 种周边效果、自由混搭、中文说明和自由调光。安装后打开 **Assets/S01.unity**，点 **Play** 即可。

## 安装前

- 使用原工程的 **Unity 6000.0.79f1、URP 17.0.4、XRI 3.4.1**；Input System 1.19.0。包会声明所需依赖。
- 面向 Windows / macOS Editor。中文字体依次回退到微软雅黑、黑体、苹方、黑体简等系统字体；两系统的控制键位相同，Mac 功能键被系统占用时加 `fn`。
- 这是桌面 Editor 原型；无头显也能用。尚未在 Windows 实机验收，不承诺 Windows 显卡帧率或 Quest/头显防晕效果。它不会自动给 Android Player 加入实验控制器。
- 先退出 Play。旧测试版如果已放在 `Assets/WIM_Prototype/`，请先在项目外备份并移走旧测试版，避免重复类与同名 Shader；原版工程没有这一步。

## 安装方式 1：Git URL（推荐）

1. 电脑已安装 Git，关闭并重新打开 Unity Hub / Editor，让它识别 Git。
2. Unity 菜单 **Window → Package Manager → + → Add package from git URL…**。
3. 粘贴 `https://github.com/Tanhhhhtjy/WIM.git`，点击安装并等待编译完成。仓库根目录就是包，不需要 `?path=`。
4. 后续在 Package Manager 选中本包点击 **Update** 更新；如果当前 Unity 没显示该按钮，再次通过同一 Git URL 添加即可解析最新提交。更新前退出 Play。

## 安装方式 2：ZIP / 无 Git

1. 在 [GitHub 仓库](https://github.com/Tanhhhhtjy/WIM) 点击 **Code → Download ZIP**，解压到项目外一个固定目录。
2. **Package Manager → + → Add package from disk…**，选择解压目录里的 `package.json`。请保留这个目录，不能安装后删除或移动。
3. 也可将解压后的整个包文件夹拖入 `Assets/`。保留 Editor / Runtime / Shaders 结构和 `.meta`；两种方式只选一种，不要重复安装。拖入方式依赖原工程已有 URP/XRI/Input System 包。

首次安装会在缺少时导入 **XRI 3.4.1 官方 XR Device Simulator Sample** 到 `Assets/Samples/`；等待这一次导入和编译结束。已有 Sample 不会被覆盖。插件不改 XRI 自动模拟器开关：有现成模拟器则复用，否则只在 Play 中实例化官方预制体。若下载/导入被中断，停止 Play 后点击 **WIM Prototype → Prepare XR Simulator** 重试。

## 1 分钟上手

打开 `Assets/S01.unity`，确认 **WIM Prototype → Enable Live Walk Prototype** 已勾选，Game View 建议 **Full HD 1920×1080**，点 **Play**。默认是 **M2 去光影＋柔虚化、键鼠漫游**。

| 操作 | 键鼠模式 |
| --- | --- |
| 走路 / 转头 | 点击游戏画面空白处锁鼠；WASD 走，鼠标转头；Esc 释放 |
| 1–6 | 1 原图；2 官方黑框；3 柔羽化黑边；4 去光影保清晰；5 去光影＋柔虚化；6 去光影＋保边平涂 |
| 7 | 打开自由混搭：5 个模块勾选框、中心圈与外圈力度两个滑块；悬停看中文说明 |
| 7 → 光照调节 | 曝光、暗部补光、太阳亮度、阴影；展开后调太阳方向、高度、冷暖、对比度 |
| [ / ] | 上一个 / 下一个周边效果模式 |
| P | 常开预览，适合站着比较；默认只在水平行走时淡入，停下约0.30秒淡出 |
| H | 隐藏 / 恢复实验 UI，方便录屏 |
| L | 原版白天 / 强阴影测试 / 偏暗黄昏；分别记住本次 Play 的调整 |
| F1–F4 | 老奶竹篮 / 依斗门广场 / 门洞 / 码头近船底 |

面板打开时暂停移动并放开鼠标，点击“收起”继续走。老太太背面太暗，先点 **暗处看清一点**，再微调 **暗部补光**；**本档恢复默认**只复位当前光照。跨 Play 不保存自定义参数。

面板里的 **切到 XR 模拟器** 恢复官方输入方式。为了避免抢占模拟器原生键位，此时改用：

| 功能 | XR 模拟器键位 |
| --- | --- |
| 六预设 / 混搭 | F5–F10 / F11 |
| 常开 / 藏 UI / 灯光 | F12 / 0 / 分号 `;` |
| 前后切 / 点位 | [ / ] / F1–F4 |
| 左手摇杆走路 | 先按 1 选主摇杆轴，按住左 Shift + WASD |
| 右手 45°转向 | 按住 Space + A/D；先松方向键使轴归零，再松 Space |

原地转头、Snap Turn、瞬移不会触发周边效果。切回键鼠就在同一面板点按钮。S01 为主要验证场景；S01 1 使用新城安全点，大坝没有 XR Origin，S02 重场景不属于本插件的开箱验证范围。

## 停止与核查

停止 Play 后恢复原玩家、光照、Volume、URP 与官方黑边；不要保存运行中的临时场景。菜单开关可以关闭下一次自动启动。场景原有未保存编辑应在使用前自行保存或备份，插件不替你丢弃这些编辑。

“运行六档帧率自测”约需24秒，在系统临时缓存的 `WIM_LiveWalk_Benchmark/live-benchmark.json` 输出真实 Editor 帧间隔，完整路径会打印到 Console。位置由 Unity 根据 Windows/macOS 自动选择，没有本机绝对路径依赖。

包内只有实时原型代码、Shader 和说明，无原场景、美术、录屏脚本或测试证据。若缺少官方 TunnelingVignette，黑边功能停用，其余效果继续；若缺少工程的 ReferenceLook 配置，使用当前场景 Volume 并施加测试曝光/对比度。
