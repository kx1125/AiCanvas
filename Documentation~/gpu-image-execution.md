# GPU 图像执行

## 执行范围

本层提供本地图像处理基础；已有 21 种本地节点（含 5 种生成器）。后续编辑节点继承 `ComputeImageNode`。功能说明见 [基础图像编辑节点](image-filter-nodes.md)、[材质处理节点](material-filter-nodes.md) 与 [程序化节点](procedural-image-nodes.md)。

- `Run Graph`：按 Image Output 的依赖顺序混合执行本地处理和 AI 生成；没有输出节点时执行独立的处理节点。只有计划包含 AI 步骤时才创建 Provider。
- AiGenerate 的 `Run`：使用上游 AI 已发布的结果，计算所需上游本地节点，执行目标 AI，再更新其后的本地节点和 Image Output，包括未连接输出的本地下游；不会越过另一 AI 节点，无关分支的参数不参与校验。
- `Preview` 与自动刷新：以所有本地处理节点和 Image Output 为根，使用 AI 已发布图片与相机已有截图。无网络请求、无相机重拍、无图片文件写入、无图资源保存。
- Preview 窗口工具栏 `Save`：显式读回并保存完整 RGBA 原图，输出到 Generated 目录；不受当前查看通道影响。Data 图片以非 sRGB 导入，Color 图片以 sRGB 导入。

## GPU 与格式

中间结果为启用随机写入的 `R16G16B16A16_SFloat` RenderTexture，无深度、MSAA 和 mipmap。端口继续声明 `Texture2D`，以兼容现有图连线；实际求值结果由执行层管理，不向端口或序列化选项写入 RenderTexture。

- `Color`：RGB 为线性颜色；源纹理的 sRGB 采样视图负责解码。没有 sRGB 视图但源数据为 sRGB 时，由导入 kernel 解码。导出 PNG 时将 RGB 编码为 sRGB。
- `Data`：保留原始数值，适用于蒙版、通道数据等；导入和导出都不应用颜色传递函数。
- Alpha 始终是数值，不做 Gamma 转换；默认使用非预乘 Alpha。涉及混合、模糊等操作的 kernel 必须显式实现自己的 Alpha 规则。
- 图片文件从源文件按原生宽高解码。项目画质、平台 Max Size、纹理压缩和流式 mipmap 只影响 Unity 使用的导入贴图。节点缩略图、通道预览、滤镜、AI 输入和 Save 共用原生像素。没有独立源文件的纹理使用可取得的完整 mip 0；无法取得时会提示错误。
- 导入根据源 `Texture2D.isDataSRGB` 判断 Color/Data。节点可以显式指定输出类型；`Inherit` 使用第一个输入的类型。
- PNG 边界为 RGBA8，数值截断到 0–1。GPU 中间处理保留半精度范围；本层尚不提供 HDR 文件导出。
- 全分辨率预览，无隐式降采样；单边最大为设备限制与 16384 的较小值。kernel 必须检查越界线程。设备须支持 ComputeShader 和格式的 Sample/LoadStore。异步读回不可用时仅在导出边界同步读回。

## 添加节点

1. 定义 `[Serializable]` 的 `ComputeImageNode` 子类，通过 `[Node("Filters/…", "", "…")]` 指定菜单位置。跨程序集时加 `[UseWithGraph(typeof(AiCanvasGraph))]`。
2. 在 `OnDefineImageInputs` 定义所需的 `Texture2D` 输入端口；基类默认创建名为 `Image` 的输出。多输出节点重写 `OutputCount` 和 `OutputName(index)`，输出名称与顺序需保持稳定以兼容保存的图。
3. 在 `OnDefineComputeOptions` 定义可编辑选项；基类自动添加一个 Preview，多输出节点在同一预览区域中提供 Channel 下拉菜单。预览选项记录通道名称与当前选择，选择随图保存并支持 Undo；不参与 operation 和执行计划指纹。
4. `CreateOperation` 返回新的 `ComputeImageOperation`，包含 shader、kernel、端口到纹理的绑定、全部标量/整数/向量参数。需要使用 graph 设置时，重写 `CreateOperation(ImageProcessingContext context)`，从 `context.WorkingSpace` 读取工作空间，并把有效配置写入 operation 的参数；无参方法可转发到 `CreateOperation(default)`，默认上下文为 Linear。已有仅实现无参方法的节点仍可使用。
5. 宽高设为 0、0 时继承第一个输入尺寸；显式尺寸必须同时提供。没有输入的生成器必须指定尺寸和 Color/Data 类型。

Operation 构造时复制参数数组并生成包含 ComputeShader 依赖版本的指纹。不要在 kernel 参数中依赖时钟、场景状态或上次 Dispatch 的残留值；所有影响结果的数据必须来自显式输入或参数。可选输入未连接时使用白色纹理。

多输出节点还需重写 `CreateOperation(ImageProcessingContext context, int outputIndex)`，为每个输出返回独立 operation；默认实现调用原有单输出接口。RGBA Split 使用同一 kernel 和不同的 `_SplitChannel` 参数，每路写入独立纹理。计划以节点 ID + 输出索引去重和寻址，指纹包含输出索引，预览与缓存不会把四路混为一个结果。无参 `CreateOperation()` 仍代表第一个输出。

内置 RGBA Split 的四路 operation 在同一计划中均存在、均未命中缓存且设备支持至少四个随机写入目标时，由执行层合并为一次 `RgbaSplitAll` Dispatch。每路仍使用原有独立缓存键和 RGBAHalf 纹理；部分缓存命中只计算缺失通道，不覆盖已有结果。只需要部分输出或设备不满足条件时继续逐路执行。这是内置节点的执行优化，不扩展自定义 operation 的输出绑定协议。

默认每个 operation 对应一个 kernel 和一个输出。可选 `followUpKernel` 支持单输入的两遍运算：第一遍写入同尺寸临时纹理，第二遍通过相同输入绑定读取该纹理，共用参数、输出类型与线性 Color / 原始 Data 约定。执行层仅缓存最终结果，并在成功或异常时释放临时纹理；两遍禁止原地读写。两个 kernel 名称均进入指纹，旧构造调用保持兼容。Gaussian Blur 使用横向/纵向两个 kernel，Sigma 为 0 时只执行恒等拷贝；Sigma > 1 时采用沿采样轴的共享邻域和权重，Sigma ≤ 1 使用直接采样以避免同步开销。此公开 operation 协议尚不支持任意多遍、不同中间分辨率或自定义单次 Dispatch 写入多个输出。

`ImageFilterCommon.hlsl` 统一颜色转换与原有双线性采样；新材质 kernel 位于 `ImageMaterialFilters.compute`。shader include 通过 AssetDatabase 依赖参与指纹和预览刷新。Grayscale / Histogram Scan / Normal 显式输出 Data，Uniform Color 根据模式输出 Color 或 Data，空间过滤继承输入类型。

`ImageProcedural.compute` 包含程序化图案、噪声、平铺、通道打包及范围过滤；通过 `ImageGradientMapping.hlsl` 实现多色标映射。Gradient Map 在创建 operation 时读取原生 Gradient 的颜色/Alpha 色标与模式，将最多各 8 个色标展开为普通 Vector 参数；快照不持有可变 Gradient 对象，不使用 CPU 图片读回或渐变 LUT。Seed 以 Int 参数传递至整数 hash，参数与随机输出都不依赖时间。其余新程序化节点输出 Data，Gradient Map 显式输出 Color。

kernel 统一约定：

```hlsl
Texture2D<float4> _Input; // 名称由 ComputeImageInput 映射
RWTexture2D<float4> _Output;
int _Width;
int _Height;

[numthreads(8, 8, 1)]
void Process(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_Width || id.y >= (uint)_Height) return;
    _Output[id.xy] = _Input.Load(int3(id.xy, 0));
}
```

调度层通过 `GetKernelThreadGroupSizes` 计算向上取整的二维 Dispatch 数。`_Output`、`_Width`、`_Height` 是保留名称；每个输入还提供 `<ShaderName>IsColor` 整数，例如 `_InputIsColor`，1 表示 Color、0 表示 Data，同样为保留名称。节点可用它选择透明边缘插值等策略。每个有效输出像素都必须被写入；不得读写同一输入纹理。输入尺寸不一致时由具体节点定义重采样或拒绝规则。

## Graph 全局设置

`AiCanvasGraph.Settings` 返回可序列化的 `AiCanvasGraphSettings` 值。通过 `SetSettings` 更新会校验配置、记录 Graph Toolkit Undo 并通知本地预览刷新。工具栏 `Graph Settings` 编辑当前窗口的 graph，撤销后重新取得 graph 实例；设置随 `.aicanvas` 保存，各文件独立。

`AiCanvasMenus.CreateGraph` 将新图初始化为 sRGB；旧文件缺失字段时反序列化为 Linear。执行计划构建时捕获不可变的 `ImageProcessingContext`，Resize / Blend / Levels 统一使用其中的工作空间，不提供节点覆盖。旧 `LevelsSettings.workingSpace` 字段仅保留用于序列化兼容，不参与运算或直方图。以后增加全局设置时，扩展 settings、面板和上下文，并将影响结果的有效配置写入 operation。

工作空间与纹理存储空间分开：Color 中间纹理始终存线性 RGB，kernel 在需要时编码为 sRGB，运算结束后解码回线性；Data 与 Alpha 保留数值。Resize 双线性插值、Blend 前景重采样与合成、Levels 映射使用有效工作空间；最近邻无需颜色运算。统一设置不改写 Texture Importer 或 Unity 项目的 Color Space。

## 缓存与生命周期

缓存键包含源资产依赖版本（内存纹理包含 updateCount）、源尺寸与颜色类型、kernel、shader 依赖版本、参数及上游结果版本。参数或 graph 设置变动后重新构建计划，未改变的分支复用缓存。工作空间通过 operation 参数参与指纹；切回原有配置时可复用对应结果。执行中的计划保留捕获的设置，发布时的指纹检查阻止过期结果覆盖新配置。图执行期间的外部输入在首次 await 之前冻结为 GPU 副本。

`ImageAssetQueryScope` 仅在一次同步构建计划、指纹计算或输入冻结期间复用资产依赖 Hash、Shader 版本和 Texture 版本；嵌套作用域共享查询结果，离开作用域后不保留。禁止跨 await、资产修改或对外事件回调持有作用域，后续调用仍读取最新版本。预览依赖扫描也会跳过已处理的资产路径。

LRU 缓存默认预算为 256 MiB。此预算仅覆盖缓存保留的纹理，当前执行、可见预览与独立预览窗口的租约可能额外占用显存。缓存淘汰不会销毁仍被使用的结果。关闭图、替换预览、结束执行时释放各自租约；脚本重载和编辑器退出时统一回收。

异步读回持有源租约和专用 RGBA8 编码纹理。取消任务后仍等待已提交读回结束，再释放纹理。保存和请求前检查取消；长 GPU Dispatch 已提交后无法撤回。

图或输入在异步生成期间改变时，发布前重新比对完整计划指纹，包括本地节点参数；过期结果不会回填图，已保存的 AI 文件保留。

自动刷新仅跟踪打开的图窗口，参数/Undo/相关资源变化按最多 30 Hz 合并刷新。资源事件按图引用的图片与 Shader 依赖筛选；执行指纹相同时保留现有预览，不重新发布。连续拖动不会推迟已安排的刷新，停止拖动后的最后一个值也会刷新；实际帧率取决于图复杂度与 Editor 更新频率。节点卡片与 Preview 窗口按图接收更新，无需 CPU 回读。

预览覆盖所有本地处理节点；缺失必需输入、非法参数或环路按根节点记录错误，只清除受影响节点的旧预览，其余有效分支继续显示。执行阶段的设备或 GPU 导入错误仍作为该图的整体预览错误处理。已保存图的缓存键为稳定 Asset GUID，内存图使用实例标识；跟踪对象记住解绑时所需的键，移动、更名、关闭或删除图都不会遗留旧路径下的租约。

Levels 额外保留其输入 GPU 租约供直方图使用。`ImageHistogram` 以每组共享计数减少原子争用，最多采样 1024×1024 个点，一次统计 R/G/B/亮度四组各 256 桶，并异步读取 1024 个 uint 计数（4 KB）；没有完整图片回读。Color 在 graph 的 sRGB / Linear 工作空间逐像素加权，Data 输入直接加权原始 RGB。统计缓存最多保留 32 个“输入版本 + 有效工作空间”组合，相同组合的并发请求合并；RGB / Luminance 显示切换复用同一份缓存。统计期间独立持有纹理和缓冲区，GPU 请求结束后释放；关闭图时释放预览层的输入租约。设备不支持异步回读时显示提示，手柄及色阶运算仍可使用。新旧 Levels 均使用 graph 的工作空间，处理 RGB 后转回线性 Color 纹理；Alpha 与 Data 从不应用 sRGB 转换。

## 验证

`Tests/Editor` 按执行计划、GPU、过滤器、直方图、预览 UI、生命周期和批量交付拆分 EditMode 测试。临时为 Standalone 添加 `AICANVAS_GPU_TESTS` 编译符号后，在 Test Runner 运行 `Tsukuyomi.AiCanvas.Editor.Tests`，结束后移除该符号。测试程序集默认不加载，因此测试 Gain 节点不会出现在正常节点菜单。执行计划与设置测试不依赖 GPU 支持，其余测试使用真实 GPU。

测试覆盖颜色/Alpha 往返、非整线程组尺寸、缓存失效与淘汰、依赖排序与环路、本地执行不创建 Provider、AI 前后串联、局部执行边界、分支错误隔离、预览租约释放与资源筛选。离线 Provider 验证批量失败/取消保留文件、跨线程交付、重复结果拒绝，以及生成期间移动图后的回填。所有 Provider 测试均不访问网络或消耗额度。测试资产使用独立随机目录并在 TearDown 清理。

2026-09-19 性能优化后的最终完整 EditMode 回归：**104/104 通过，0 失败、0 跳过**（Unity 6000.6.0b3 / NVIDIA GeForce RTX 5060）。新增覆盖高斯共享邻域、跨组边界与二维数值参考，Split 合批/部分缓存命中，以及资产查询作用域失效。
