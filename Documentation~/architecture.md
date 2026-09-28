# AiCanvas 架构

## 程序集

`Tsukuyomi.AiCanvas.Editor` 是默认加载的正式程序集，限定 Editor 平台。测试程序集 `Tsukuyomi.AiCanvas.Editor.Tests` 仅在启用 `AICANVAS_GPU_TESTS` 时加载。Graph Toolkit 来自 Unity 6000.6 内置模块；URP/Core 和 Newtonsoft.Json 通过 asmdef 显式引用。

## 执行流程

`ImageGraphRunner` 管理单任务执行、取消和进度。全图执行按依赖顺序重新生成输出链；单节点执行使用上游已发布的结果。

执行顺序：构建计划 → GPU 冻结外部输入 → 按拓扑顺序执行 ComputeShader 或图片服务，逐张保存已完成的 AI 结果 PNG/JSON → 校验输入版本 → 回填持久结果与 GPU 预览。纯本地执行不构造图片服务。

| 模块 | 职责 |
| --- | --- |
| GraphOptionAccess | 读写 Graph Toolkit 节点选项。 |
| ImageGraphTopology | 只读取节点和端口连线，确定依赖及目标的本地下游，不创建 operation。 |
| ImageExecutionPlanBuilder / ExecutionPlan | 在选定范围内检测环路、校验输入、建立包含资产版本的指纹；本地预览记录各根节点的错误。 |
| ImageInputSnapshot | 保存独立 PNG、尺寸、Alpha、资源路径、版本与内容 Hash。 |
| IImageProvider / IIncrementalImageProvider / ImageRequest | 以不可变请求提交图片；保留整批返回接口，并允许 Provider 按输出顺序逐张交付。 |
| ImageBatchDelivery | 验证交付顺序和数量，将回调调度回编辑器线程，每张保存完成后再接收下一张。 |
| OpenAIImageProvider | GPT Image 2 的 JSON/multipart 请求、超时、取消、错误处理与 PNG 解码。 |
| OpenAIImageSettings | 按项目保存连接配置，在主线程捕获请求所需的凭证与设置。 |
| CodexRuntime | 固定版本下载、SHA-512 校验、受限解压、组件安装与路径边界。 |
| CodexRpcClient | 后台 stdin/stdout JSON-RPC、请求分发、通知与进程树回收。 |
| CodexConnection | 账号登录、能力查询、连接生命周期和编辑器线程状态通知。 |
| CodexImageProvider / CodexImageRun | 独立工作目录、输入文件、逐张生成、图片事件处理和结果读取。 |
| ImageAssetStore / AiCanvasTextureImporter | PNG/JSON 保存和首次导入设置。 |
| ImageRunRecord | 保存生成参数、输入版本与输出信息。 |
| GraphResultPublisher | 按稳定 GUID 定位图，校验当前图指纹，在 Undo 中关联完整结果并保存。 |
| CameraColorCapture | 相机解析、捕获、状态恢复与图片持久化。 |
| ComputeImageNode / ComputeImageOperation | 本地节点协议和不可变的 kernel、输入绑定、参数、尺寸与数据类型快照。 |
| ImageComputeDevice / GpuImage / GpuImageCache | ComputeShader 调度、GPU 纹理租约、256 MiB LRU 缓存和边界读回。 |
| ImageEvaluationSession | 一次执行的输入冻结、结果解析、本地执行与 AI 输入快照转换。 |
| ImagePreviewStore / ImageLocalPreview | 以稳定 GUID 保存非序列化 GPU 预览、按依赖合并刷新、关闭或删除图时释放预览租约。 |
| EditorEvent | 逐个调用观察者并记录异常，保证其余观察者和任务清理继续执行。 |

## 一致性与生命周期

所有外部图片在第一次 await 前复制到自有 GPU 纹理，同一次任务复用相同源图片。只在 AI 请求或显式保存 PNG 的边界读回，AI 直接连接 AI 时复用已有 PNG 字节。`CopyPngBytes()` 返回副本，Provider 不会修改原始快照。具体格式、生命周期和扩展规则见 [GPU 图像执行](gpu-image-execution.md)。

图片捕获和图资产操作在 Unity 主线程完成。HTTP、组件下载和响应处理可异步运行；临时 RenderTexture、Texture2D 和进程在 finally、脚本重载或编辑器退出时回收。

只有所选连接设置能够决定内置 Provider。共享 HttpClient 禁用自动重定向，每次请求独立设置授权头和超时，不自动重试。账号进程使用独立 home，不继承全局凭证或 API 环境变量；保留内置图片工具需要的 Code Mode host。

运行期间图或输入发生变化时，生成文件继续保留，但不回填旧结果。账号批量生成中途失败或取消也保留每张已交付图片及 JSON，`ImageGraphRunner.LastSavedFiles` 暴露本次保存清单；部分批次不更新图关联。事件观察者的异常会记录到 Console，不会阻断其余观察者或把已成功完成的任务改为失败状态。工具栏按图 GUID 区分结果状态，移动或重命名图不影响正在执行的结果定位。

## 图与 UI

图资源扩展名为 `.aicanvas`。AiCanvasImporter 独立于 AiCanvasAsset，使用持久的 MonoScript GUID。主资源的内部标识保持稳定，避免更名后改变资源局部 ID。序列化模型使用 MovedFrom 标记描述原有类型身份；图内嵌的泛型选项类型在升级资源时一起迁移。

AiCanvasWindowRegistry 集中发现图窗口，按图变化、Undo/Redo 和窗口几何变化合并刷新。GenerateNodeLayout 维护设置、输入端口、运行控件和结果预览的布局，保留 Graph Toolkit 原生端口及连线交互。

PromptFieldCompatibility 使用 IMGUI 编辑缓冲处理中文输入法，确认后通过原生字段提交。Output Count 在拖动结束后一次提交，避免控件重建时失去指针捕获。

按钮和工具栏在挂载时订阅运行与连接事件，离开面板时退订。资源后处理按图引用的纹理与 Shader 依赖安排刷新；预览通知携带图标识，节点卡片、Levels 直方图和独立窗口只响应自己的图。`ImagePreviewWindow` 与 `LevelsHistogramControls` 分别保存窗口、直方图和手柄控件，不从服务层枚举窗口。

## 文件和凭证

`AiCanvasResourcePaths` 集中定义 `Assets/AiCanvasResource` 及其 `Graphs`、`Generated`、`Captures` 子目录。新建图固定进入 Graphs；图片生成与相机捕获使用各自的默认目录，自定义输出路径也必须位于资源根目录内。

生成图片按批次创建新文件；CameraColor 按图 GUID 与节点 ID 覆盖固定文件并保留 GUID，只复用当前捕获目录内的图片。输出导入规则匹配专用文件名，支持独立导入进程和资源根目录内的自定义子目录。

API Key 保存在按项目与 Base URL 隔离的 SessionState 中；非敏感设置使用 EditorPrefs。账号凭证由独立运行组件管理。已有 home 路径在包更名后保持稳定，避免系统凭据存储身份变化；新的运行组件和临时工作目录使用 AiCanvas 路径。

图、包和结果 JSON 不保存凭证。账号任务的临时输入在任务结束后清理；Library 中的账号 home 由用户管理，不随卸载运行组件删除。
