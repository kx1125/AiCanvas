# AiCanvas

AiCanvas 是基于 Unity Graph Toolkit 的编辑器图片工作流插件，以 UPM 包管理。可以通过 OpenAI Images API 或 ChatGPT 账号生成和编辑图片。

## 环境与安装

- Unity 6000.5.3f1 或更新的 6000.5 / 6000.6 编辑器，使用编辑器内置 Graph Toolkit。
- Universal RP 17.5 或更新版本；包依赖由 UPM 解析。
- ChatGPT 账号连接的托管运行组件支持 Windows x64 Editor。

在 Unity 中打开 **Window > Package Management > Package Manager**，选择 **Install package from git URL**，输入：

```text
https://github.com/kx1125/AiCanvas.git#v0.1.1
```

也可以在项目的 `Packages/manifest.json` 中添加：

```json
{
  "dependencies": {
    "com.tsukuyomi.ai-canvas": "https://github.com/kx1125/AiCanvas.git#v0.1.1"
  }
}
```

本地开发时，将包放入 `Packages/com.tsukuyomi.ai-canvas`，或在 Package Manager 中选择 **Install package from disk**，指定本包的 `package.json`。插件程序集为 `Tsukuyomi.AiCanvas.Editor`，仅在编辑器中加载。

Unity 6.5 使用窗口顶部的 AiCanvas 工具栏；6.6 使用 Graph Toolkit 工具栏扩展。6.5 缺少的窗口、节点/端口标识、选项写入和多连线 API 由 `GraphToolkitCompatibility` 集中适配，6.6 使用公开 API。图文件与节点标识保持兼容。

## 创建工作流

1. 打开 **Tools > AiCanvas > Connection Settings**，配置 API Key 或 ChatGPT 账号连接。
2. 通过 **Tools > AiCanvas > Create Graph** 或 **Assets > Create > AiCanvas > Graph** 在 `Assets/AiCanvasResource/Graphs` 创建并打开空白图，名称重复时自动编号。
3. 在节点创建菜单添加 **Prompt**、**AiGenerate** 和 **Image Output**，连接 `Prompt.Text → AiGenerate.Prompt`、`AiGenerate.Image → Image Output.Image`。
4. 填写提示词，设置 **Image Size**、**Quality** 和 **Output Count**，点击 AiGenerate 节点的 **Run**。
5. 使用 **Ctrl+S** 保存；双击 `.aicanvas` 资源重新打开。图支持平移、缩放、连线、撤销和重做。

节点 **Run** 执行当前 AiGenerate，并更新其后的本地图像处理和 Image Output，在下一个 AiGenerate 处停止。上游 AiGenerate 使用其对应输出端口已有的图片；缺少结果时需要先运行上游。工具栏 **Run Graph** 按依赖顺序执行整条输出链，纯本地图不需要配置 AI 连接。**Cancel** 取消当前任务，已经保存的图片继续保留。

AiGenerate 自身的 Prompt 可留空：只要连接到 Inputs 的任意 Image Input 提供了有效 Prompt（直接填写或连接 Prompt 节点），就会将这些逐图说明组合后提交生成。自身及所有已连接 Image Input 的 Prompt 都为空或只有空白时，会提示补充提示词。

提示词支持中文输入法。完成选字后点击空白处、保存图或执行 Run，会提交已确认的编辑内容。选字过程中不会提交生成。

执行 Run 会向所选服务提交提示词和输入图片，并使用对应账号额度或产生 API 费用。保存、导入、调整节点和预览图片不会触发生成。

## 节点分类

**Add a graph node** 按功能组织节点，直接使用以下顶级分类：

| 分类 | 节点 | 用途 |
| --- | --- | --- |
| Inputs | Image、Prompt | 提供通用图片和文本数据。 |
| Unity | CameraColor | 获取 Unity 场景或渲染数据。 |
| AI | Image Input、AiGenerate | 组织 AI 请求的图片角色与逐图提示词，生成或编辑图片。 |
| Generators | Uniform Color | 生成指定尺寸的纯色或灰度数据图。 |
| Generators/Gradients | Gradient Linear | 带方向、周期和相位的渐变。 |
| Generators/Patterns | Shape、Tile Generator | 基础形状、平铺阵列、错行和随机变化。 |
| Generators/Noises | Tileable Noise | 固定 Seed 的可平铺分形噪声。 |
| Filters/Transform | Resize、Transform 2D、Directional Warp | 调整尺寸、平移/旋转/镜像/平铺，以及强度图驱动的定向变形。 |
| Filters/Adjustment | Levels、Hue Saturation Lightness (HSL)、Invert、Histogram Scan、Histogram Range、Histogram Select、Gradient Map | 色阶、色相/饱和度/明度、反相、灰度范围与蒙版提取、多色标渐变映射。 |
| Filters/Channels | Grayscale、RGBA Split、RGBA Merge、Channel Shuffle | 亮度提取、RGBA 四路拆分、数据通道打包与重排。 |
| Filters/Blur | Gaussian Blur | 两遍可分离高斯模糊，支持平铺和透明边缘。 |
| Filters/Normal | Normal | 高度图转切线空间法线，支持 OpenGL / DirectX。 |
| Filters/Blending | Blend | 带蒙版的多图合成。 |
| Outputs | Image Output | 接收并预览工作流结果。 |

**Hue Saturation Lightness (HSL)** 提供三个以 0 为原点的滑条。Hue 在 −180° 到 +180° 之间旋转色相。Saturation 从 −100% 的全灰调到 +100% 的全饱和，灰色像素保持中性。Lightness 从 −100% 的黑色调到 +100% 的白色。三个值均为 0 时直接输出原图。调整时在图的工作空间内对 0–1 的 RGB 计算 HSL，保留输入尺寸和 Alpha。

Image Input 将图片、提示词和 Main / Reference / Mask 角色组合成 AI 输入，因此归入 AI；通用图片源 Image 归入 Inputs。

后续图片处理节点按功能扩展到 `Filters/Adjustment`、`Filters/Blending`、`Filters/Blur`、`Filters/Channels`、`Filters/Transform` 等分类；程序化图案或噪声节点归入 `Generators`。这些分类在加入对应节点后才会显示。

## 图片输入与输出

工具栏 **Graph Settings > Working Space** 统一设置当前 graph 的 `sRGB` / `Linear` 工作空间，随 `.aicanvas` 保存并支持 Undo / Redo。Resize、Blend 和所有新旧 Levels 均使用该配置，节点上没有单独的色彩空间选项；数据贴图与 Alpha 保留原始数值。新 graph 默认 sRGB，没有全局配置的旧 graph 使用 Linear。

打开图或修改图片处理参数后，所有本地处理节点和 Image Output 的预览会合并刷新，包括尚未连接输出的处理分支。工具栏 **Preview** 可手动刷新本地预览，使用已有 AiGenerate 结果和 CameraColor 截图；不会请求 AI 或重新拍摄相机。输入缺失、非法参数或环路的错误显示在对应分支，其余有效分支继续预览。仅移动节点不会重新发布相同结果，无关资源导入不会刷新该图。

本地处理使用 ComputeShader，中间结果保留为 GPU 纹理并缓存。**Image Output** 和后续本地处理节点的 **Preview** 可放大查看，预览窗口工具栏的 **Save** 将完整 RGBA 原图保存到 `Assets/AiCanvasResource/Generated`。预览纹理不写入图资源，重新打开图时会重新计算。提交到 AI 或保存 PNG 时才读回完整图片；PNG 为 8 位 RGBA。Levels 支持 sRGB / Linear 工作空间与 RGB / 亮度直方图；输入直方图只异步读取 4 KB 的 GPU 统计计数，调节黑白场 / Gamma 时复用统计结果。

图片节点统一通过 **Preview** 打开预览窗口，不再提供节点上的保存按钮。窗口工具栏支持 **RGBA / R / G / B / A**：RGBA 查看原图，R/G/B/A 分别以不透明灰度查看单通道，通道切换不修改图片、无需 CPU 回读，并继续跟随本地节点实时更新。**Save** 支持图片资产、CameraColor 截图、AI 结果及 GPU 处理结果，始终保存完整 RGBA 原图。

已提供 20 种本地 GPU 节点（含 5 种生成器）。基础功能见 [基础图像编辑节点](Documentation~/image-filter-nodes.md)，空间处理与法线见 [材质处理节点](Documentation~/material-filter-nodes.md)，渐变、打包、图案、噪声和平铺见 [程序化节点](Documentation~/procedural-image-nodes.md)，开发扩展约定见 [GPU 图像执行](Documentation~/gpu-image-execution.md)。

从 Project 拖入图片会创建 **Image** 节点。添加 **Image Input**，连接 `Image.Image → Image Input.Image`，填写或连接逐图 Prompt，然后把 `Image Input.Input → AiGenerate.Inputs`。一个 Inputs 可连接多个 Image Input。

| Role | 用途 |
| --- | --- |
| Main | 编辑主图，最多一张，作为第一张图片提交。 |
| Reference | 参考图片，保持相对连接顺序。 |
| Mask | 主图的 Alpha 蒙版，最多一张，单独提交。 |

Main 与 Reference 合计最多 16 张。Mask 必须配合 Main，尺寸相同并包含 Alpha 通道；透明区域表示编辑区域。API 模式每张输入 PNG 小于 50 MB，蒙版小于 4 MB，最终提示词包含逐图说明后最多 32000 字符。

**Output Count** 为 1–10。每个输出端口对应一张图片，端口和预览随数量同步更新；拖动数量控件在松开后提交一次修改，支持 Undo。**Preview** 打开独立预览窗口。

**Image Size** 支持 Auto、预设和自定义尺寸。Auto 由服务决定实际尺寸。自定义宽高为 16 的倍数，长宽比在 1:3–3:1 之间，单边不超过 3840，总像素在 655360–8294400 之间。较大尺寸的可用性取决于服务能力。实际输出宽高记录在结果 JSON 中。

## 连接设置

### API Key

默认 Base URL 为 `https://api.openai.com/v1`。填写 Key 后点击 **Apply Settings**，或在启动 Unity 前配置 `OPENAI_API_KEY` 环境变量。会话 Key 优先，并按项目与服务地址隔离。

在 **Tools > AiCanvas > Connection Settings** 点击 **获取可用图像模型**，从 **选择模型** 下拉框选择当前 Key 列出的 GPT 图像模型，也可以直接填写服务支持的图像模型 ID。点击 **Apply Settings** 后生效。默认 ID 为 `gpt-image-2`。文生图与带图编辑都使用所选 ID，结果 JSON 的 `provider` 记录此次请求使用的模型。

API Key 存在 Unity SessionState 中，可跨脚本重载保留，退出 Unity 后清除。Base URL、连接方式和超时等非敏感设置使用 EditorPrefs。凭证不进入图、包或结果 JSON。

无图片时请求 `/images/generations`；有主图或参考图时请求 `/images/edits`。支持独立 Mask、尺寸、质量及批量 PNG 结果。兼容服务地址须支持对应的 Images API。

### ChatGPT 账号

在 Connection Settings 选择 ChatGPT 账号模式，点击 **安装运行组件并连接**；已有组件可点击 **重新下载并修复组件**。组件就绪后使用浏览器登录或设备码登录。插件管理固定版本的官方 Codex 原生组件，无需单独安装 Node.js、CLI 或桌面应用。

登录后点击 **获取账号可用会话模型**，在 **Codex 会话模型** 中选择，或保留 **账号默认**。会话模型负责调用内置图像工具，底层绘图模型由 ChatGPT 服务决定。要指定绘图模型 ID，请使用 API Key 模式。

账号模式支持文生图、主图编辑、参考图和逐张生成多个结果。尺寸与质量作为生成要求传递，实际输出由服务决定；精确 Alpha Mask 编辑需要使用 API Key 模式。

组件安装于 `Library/AiCanvas/Codex/runtime`，每次任务的临时输入位于 `workspace`，完成后清理。独立账号 home 由组件管理凭证；新安装使用 `Library/AiCanvas/Codex/home`，从旧包升级时保留已有 home 路径，以维持系统凭据存储中的账号身份。插件不读取全局 Codex 凭证。

工具栏分别显示连接状态与当前图的运行状态。后台组件停止后保留最近确认的登录状态，下一次生成会重新检查。可以在设置页刷新状态、退出账号或卸载运行组件；卸载组件保留账号缓存。请勿提交或分享 Library 中的账号缓存。

### 超时与错误

超时默认 600 秒，可设置为 30–1800 秒。错误会提示认证、权限、额度、网络或服务原因；服务返回的 Request ID 和 Retry-After 可用于排查，消息中的敏感信息会被隐藏。

账号模式每完成一张图片就保存 PNG 和对应 JSON。后续图片失败、超时或取消时，已保存文件仍保留，状态中显示保留数量；整批成功并通过版本检查后才更新图关联。`ImageGraphRunner.LastSavedFiles` 可读取最近一次执行已保存的文件清单。

失败和超时不会自动重试。取消本地请求不能保证服务端已接受的生成停止处理或计费。脚本重载和退出编辑器会取消任务并回收后台进程；任务不跨重载续跑。

## CameraColor

**CameraColor** 节点手动捕获相机最终颜色。默认使用 Main Camera，也可从 Hierarchy 指定相机。**Camera Size** 使用相机尺寸，关闭后可设置 1–4096 的宽高，受 GPU 上限约束。

点击 **Capture** 保存 PNG 并更新预览。URP 捕获 Base Camera 的相机堆叠和后处理；选择 Overlay Camera 时需要改选对应 Base Camera。输出不包含 Scene 视图 Gizmos、编辑器界面和 Screen Space Overlay UI，也不支持立体相机。

连接 `CameraColor.Image → Image Input.Image → AiGenerate.Inputs`，或直接连接 Image Output。AiGenerate 使用最近一次捕获；场景或相机变化后需再次 Capture。捕获过程中会恢复相机状态并释放临时纹理。

每个节点维护固定的 `Assets/AiCanvasResource/Captures/CameraColor_<GraphGUID>_<NodeID>.png`，重复捕获保留 GUID。Undo 只恢复图关联，不恢复被覆盖的图片像素。已保存场景的指定相机使用 GlobalObjectId，未保存场景中的指定相机仅在当前会话有效。

## 数据与结构

- `Editor/Graph`：图资产、节点模型、输入连接和资源导入器。
- `Editor/UI`：节点控件、提示词输入、图片预览、工具栏和设置页。
- `Editor/Services`：执行计划、图片快照、服务连接、相机捕获和结果存储。

插件创建的资源统一位于 `Assets/AiCanvasResource`，按用途分类：

- `Graphs`：`.aicanvas` 工作图。
- `Generated`：生成的 PNG 与对应参数 JSON。
- `Captures`：CameraColor 捕获的 PNG。

自定义图片输出目录也必须位于 `Assets/AiCanvasResource` 内。导入的外部参考图片保留原有位置，图通过资源引用使用它们。

生成图片命名为 `AiCanvas_<UTC时间>_<批次ID>_<序号>.png`，每次执行创建新文件。JSON 记录整体任务、最终提示词、图片角色与说明、输入版本、尺寸、质量、数量及输出路径，不包含凭证。

提交前冻结所有外部输入的 PNG 快照；运行期间修改或重导入源图片不会改变已提交的内容。回填前检查图和输入版本，发生变化时保留结果文件并提示重新运行。

Color PNG 使用未压缩 sRGB，Data PNG 不作 sRGB 解码，均保留非二次幂尺寸。导入上限按源尺寸设置，最高 16384，实际可用尺寸受设备限制；CameraColor 的捕获尺寸上限仍为 4096。生成图片启用 mipmap，CameraColor 关闭 mipmap。图仅引用图片资产，撤销结果关联不会删除已生成图片。

模块职责见 [架构说明](Documentation~/architecture.md)。

## 许可

本项目采用 [MIT License](LICENSE.md)。
