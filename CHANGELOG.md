# Changelog

## 0.1.1 — 2026-11-07

- 新增 Hue Saturation Lightness (HSL) 节点，可实时调整色相、饱和度和明度，并保留输入图片的透明度。
- 节点缩略图、预览、滤镜、AI 输入和 PNG 保存均按源图片的原始分辨率处理。蒙版校验和生成记录也使用原始宽高。
- API Key 连接支持获取、选择或手动填写图像模型 ID。ChatGPT 账号连接支持选择调用图像工具的 Codex 会话模型。
- 统一滤镜和程序化节点在负坐标处的平铺取样，修正图像处理着色器的 FXC 编译警告。

## 0.1.0 — 2026-09-29

首次公开发布，包含 Unity 6.5 兼容适配、AI 图片生成与编辑、本地 GPU 图像处理和以下开发期改进。

### Unity 6.5 兼容

- 支持 Unity 6000.5.3f1，补齐 Graph Toolkit 窗口识别、工具栏、稳定节点/端口 ID、选项写入和多输入连线接口的版本差异。
- Unity 6.5 保留 Graph Settings、Preview、Run Graph、Cancel、状态和 Connection 工具栏；窗口切换、拖入图片和本地预览使用同一兼容层。
- 图资产 GUID 改用两个版本均支持的 GraphDatabase API；现有 UI 与图像测试通过兼容层覆盖 6.5 / 6.6。

### 保持图像语义的性能优化

- Gaussian Blur 在 Sigma > 1 时沿采样轴共享邻域像素和高斯权重，减少重复采样与颜色转换；小 Sigma 保留直接采样。高斯半径、两遍处理、边界、工作空间与 Alpha 规则保持不变。
- RGBA Split 四路均未命中缓存时合并为一次 Dispatch；部分命中仅计算缺失通道，完整命中不再 Dispatch。四路仍为独立、全分辨率 RGBAHalf 结果，显存占用与预览切换行为不变；设备不足四个随机写入目标时沿用逐路执行。
- 同步构建计划、计算指纹与冻结输入期间复用重复的资产版本查询；跨调用重新读取版本，预览依赖扫描对重复资产去重。
- 合并尺寸和单位区间的重复参数校验，增加模糊数值、Split 调度/缓存与查询作用域回归测试。

### RGBA 通道拆分

- 增加 RGBA Split，一路 Input 拆为 R/G/B/A 四个独立灰度 Data 输出，尺寸继承输入，各输出 Alpha=1；保留原始通道数值，可接回 RGBA Merge。
- 扩展 ComputeImageNode 的多输出端口与按输出创建 operation 接口；执行计划按端口索引求值、缓存和发布预览，原有单输出节点保持兼容。
- 节点保留一个 Preview，通过 Channel 下拉菜单选择 R/G/B/A；选择随图保存并支持 Undo，切换显示复用已有 GPU 结果，已打开的预览同步跟随。
- 补充通道数值、拆分合并往返、非 R 分支、保存重载、缓存、错误隔离及实际图窗口切换显示测试。

### 程序化输入、渐变与通道打包

- 增加 Gradient Map、Histogram Range / Select、RGBA Merge、Channel Shuffle、Gradient Linear、Shape、Tileable Noise 和基础 Tile Generator，共 9 个本地 GPU 节点。
- Gradient Map 提供原生多颜色/Alpha 色标编辑器，支持 Blend、Fixed、Perceptual Blend 与 Clamp / Repeat；operation 冻结色标，保留运行中快照和缓存稳定性。
- 程序化生成器支持独立输出尺寸；噪声与 Tile Generator 提供固定 Seed 和周期 UV 偏移，Tile Generator 支持形状、错行、间距、旋转、随机尺寸/位置/灰度及缺失率。
- 通道重排与打包保留原始数值，输出 Data；渐变映射输出 Color。接入现有预览、Undo、图序列化与 PNG 保存。
- 增加数值、平铺、随机种子、颜色空间、快照、序列化和原生渐变编辑回归测试；GPU 测试等待时请求 player-loop 更新，支持编辑器后台读回。

### Substance Designer 风格材质处理节点

- 增加 Uniform Color、Grayscale、Invert、Histogram Scan、Transform 2D、Gaussian Blur、Directional Warp、Normal，共 8 个本地 GPU 节点，接入现有预览、保存、缓存及图序列化。
- 提供 Clamp / Repeat / Transparent 边界；颜色插值和模糊使用预乘 Alpha，灰度、蒙版与法线显式输出 Data。
- 增加单输入两遍 Compute 协议供可分离高斯模糊使用，临时纹理及时释放，仅缓存最终结果；原有扩展接口保持兼容。
- 增加像素、颜色空间、图序列化、节点参数控件、Undo、离线串联及缓存回归测试，补充参数约定和后续节点路线文档。

### 2026-09-19 审查修复

- 账号批量生成逐张保存 PNG/JSON；后续失败或取消保留已完成文件，整批成功后再关联图。
- 单节点 Run 通过独立拓扑索引确定范围，无关分支的非法参数不再阻断执行；未连接输出的本地下游也会更新。
- 预览缓存和运行状态使用稳定图 GUID，移动或删除图后正确定位结果并释放租约。
- 本地预览覆盖全部处理节点，按分支显示计划错误；按图片和 Shader 依赖筛选资源刷新，执行指纹相同时不重复发布。
- 统一事件异常隔离，拆分预览窗口、直方图控件及测试文件，清理无调用的 internal 辅助成员；旧 public 图片捕获接口标记废弃并保留兼容。
- 增加批量失败/取消、跨线程交付、图移动/删除、执行范围及预览隔离回归测试；更新架构和图片导入尺寸说明。

### 基础功能

- 图片节点移除 Save PNG，Open Preview 更名为 Preview；预览窗口工具栏统一提供 Save 和 RGBA/R/G/B/A 通道查看，保存完整 RGBA 原图，单通道 GPU 预览保持实时更新。
- Levels 增加输入 RGB 直方图、五个可拖动手柄、精确数值、重置/反转和滑条切换；GPU 统计输入分布并缓存，拖动支持实时预览与整段撤销。
- Levels 的输入/输出黑白场、Gamma，以及 Blend 的 Opacity 使用范围滑条。
- 图像节点参数支持拖动实时预览，按最多 30 Hz 刷新；Open Preview 窗口同步跟随节点结果。
- 增加 Resize、Levels、Blend 三种本地图像节点，支持透明边缘重采样、蒙版合成与独立参数面板。

- 增加 ComputeShader 图像执行层、半精度 GPU 中间结果、按依赖执行与结果缓存，以及 ComputeImageNode 扩展接口。
- 本地预览与 AI 请求独立调度；支持 Preview、预览窗口保存及本地处理与 AI 节点混合串联。
- 节点菜单按 Inputs、Unity、AI、Outputs 分类。

- Generate 节点显示名称改为 AiGenerate；连接到 Inputs 的 Image Input 有有效 Prompt 时，节点自身的 Prompt 可留空。全部提示词为空时仍阻止生成。
- 插件资源统一保存到 Assets/AiCanvasResource，工作图、生成结果和相机捕获分别进入 Graphs、Generated、Captures 子目录；自定义图片输出路径限定在该根目录内。
- 图资源路径从 AssetDatabase 实时读取，移动资源后不再保留过期的内部路径。

- 插件统一命名为 AiCanvas，UPM 标识为 com.tsukuyomi.ai-canvas，程序集为 Tsukuyomi.AiCanvas.Editor，图扩展名为 .aicanvas。
- 发布 GPT Image 2 图片工作流，支持 API Key 和托管 ChatGPT 账号连接。
- 支持 Prompt、Image、Image Input、Generate / Edit、Image Output 和 CameraColor 节点。
- 支持主图、参考图、Alpha Mask、尺寸、质量、多个结果及独立图片预览。
- 支持单节点和全图异步执行、取消、超时、输入快照、结果持久化与过期结果保护。
- 清理开发期工具、演示入口和替身实现；新建图为空白工作流，默认提示词为空。
- 统一菜单、设置、资源路径与文档，保持资源 GUID、已有图结构及账号身份。
