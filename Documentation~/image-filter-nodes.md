# 基础图像编辑节点

本页说明 Resize、Levels、Blend；Uniform Color、Grayscale、Invert、Histogram Scan、Transform 2D、Gaussian Blur、Directional Warp、Normal 见 [材质处理节点](material-filter-nodes.md)。

所有节点通过 ComputeShader 在本地执行，具有 Image 输出、实时本地预览和 Preview 按钮；在 Preview 窗口工具栏使用 Save 保存完整 RGBA 原图。可以使用 Image、CameraColor 已捕获截图、AiGenerate 已有结果或其他编辑节点的输出作为图片来源。

Levels 的输入/输出黑白场与 Blend 的 Opacity 使用 0–1 滑条，Gamma 使用 0.01–10 滑条；滑条右侧可输入精确数值。Resize 的像素宽高保留整数输入。

## Graph Settings

点击 graph 顶部工具栏的 **Graph Settings**，通过 **Working Space** 选择 `sRGB` 或 `Linear`。设置保存在当前 `.aicanvas` 文件中，各 graph 独立，支持 Undo / Redo；切换后自动更新本地预览与直方图。

通过 AiCanvas 菜单创建的新 graph 默认 sRGB。没有这项配置的旧 graph 使用 Linear，保留原有 Resize / Blend 行为。所有 Levels 节点统一使用 graph 的配置；旧节点保存的单独色彩空间不再生效。

色彩空间只在 Graph Settings 中设置。Levels 的直方图状态行显示实际统计空间。全局切换也会改变上游 Resize / Blend 的工作空间，因此整条链的结果不一定等于旧版本只切换 Levels。

工作空间决定 Color 图片的运算数值：sRGB 在编码后的 RGB 上调节、插值和混合；Linear 在线性 RGB 上运算。节点间传递的 Color 纹理始终为线性 RGB，数据贴图与 Alpha 不应用颜色转换。它不会修改 Unity 项目的 Color Space，也不会修改 Texture Importer 的 sRGB 开关。改变空间不会换算现有参数，因此结果可能改变。

## Resize

菜单：`Filters/Transform/Resize`。

- Width / Height：输出画布的像素尺寸，范围 1 到设备上限与 16384 的较小值。
- Stretch：图片直接缩放到目标宽高。
- Contain：保持比例、居中缩放，剩余区域填透明色。
- Cover：保持比例、填满画布，并居中裁掉超出部分。
- Nearest：按像素中心最近邻取样，适合像素图和离散数据。
- Bilinear：双线性取样。Color 图片在 graph 的 Working Space 中采用预乘 Alpha 插值，输出恢复为非预乘 Alpha，以避免透明像素的隐藏颜色渗入边缘。Data 图片各通道独立插值。

输出始终为指定的 Width × Height。默认 1024 × 1024、Stretch、Bilinear。当前没有高质量面积缩小滤波；大幅缩小时可能出现锯齿。

## Levels

菜单：`Filters/Adjustment/Levels`。

- Input Black / White：输入映射范围，均为 0–1，White 必须大于 Black。
- Gamma：0.01–10；1 为不变，大于 1 提亮中间调，小于 1 压暗。
- Output Black / White：输出范围，均为 0–1；允许反向设置以反转数值。
- Affect Alpha：默认关闭，保留原 Alpha；打开后将相同映射应用于 Alpha。

映射为 `lerp(outputBlack, outputWhite, pow(saturate((input - inputBlack) / (inputWhite - inputBlack)), 1 / gamma))`。RGB 三通道共用参数，直方图和色阶运算统一使用 graph 的 Working Space；Color 在 sRGB 模式下先编码再映射，结果转回线性 RGB 供下游使用。Data 直接处理原始数值，Alpha 不做颜色空间转换。默认参数对 0–1 输入为恒等映射；HDR 输入超出该范围会被截断。

SD 的 [Legacy 色彩管理模式](https://experienceleague.adobe.com/en/docs/substance-3d-designer/using/color-management/color-management) 使用 sRGB 工作空间。同一张 PNG 在 sRGB 与 Linear 下的直方图位置、宽度和峰值会明显不同；要与 SD Legacy 对比，应选择 sRGB，并确保两边连接的是相同阶段、相同尺寸的输入。不同软件的采样、缩放和桶分辨率仍可能造成细节差异。

默认打开输入直方图界面：

- Display 可选择 RGB 或 Luminance（亮度）：RGB 彩色叠加显示 R/G/B 分布；Luminance 用灰白色显示每个像素的加权亮度分布，权重为 `0.2126729 R + 0.7151522 G + 0.0721750 B`。Color 先转换到 Working Space 再加权：Linear 是线性亮度，sRGB 是编码值的加权亮度；Data 直接加权原始数值。亮度在加权后归入 0–1 桶。Display 切换仅影响显示，RGB 仍共用一套色阶参数；当前编辑器会话记住各节点的显示选择。
- 直方图上方的黑、灰、白手柄在同一行，箭头朝下，分别调节输入黑场、Gamma、输入白场。输入黑白手柄不会交叉；灰色手柄表示归一化输出达到 0.5 时的输入位置，即 `black + (white - black) * pow(0.5, gamma)`。
- 直方图下方的 Output 黑白手柄在同一行，箭头朝上，调节输出范围并允许交叉。
- 输入精确数值位于上方手柄之上，输出精确数值位于下方手柄之下。拖动会实时更新节点与 Preview；方向键微调，Shift + 方向键精细调节。一次 Undo/Redo 对应整段手柄拖动。
- Reset 恢复默认色阶并关闭 Affect Alpha；Invert 交换输出黑白值。Sliders / Histogram 在原滑条与直方图界面之间切换，切换不会改变参数。

直方图通过一次 ComputeShader 统计 R/G/B/亮度四组各 256 桶计数，仅异步读回 4 KB。大于 1024 像素的维度使用均匀点采样，界面标注“采样”；较小图片统计全部像素，包括透明像素的 RGB 和亮度。超出 0–1 的有限值归入两端，非有限值忽略。统计用于显示，不降低 Levels 运算精度。直方图按输入版本与有效工作空间缓存；调整黑白场 / Gamma 或切换 Display 不会重新统计。上游或工作空间变化时获取对应统计并拒绝过期回调；切回已缓存空间直接复用。暂不支持逐通道编辑、自动色阶与输出直方图。

## Blend

菜单：`Filters/Blending/Blend`。

必需输入 Background 和 Foreground，可选 Mask。输出继承 Background 的尺寸和颜色/数据类型；不同尺寸的前景与蒙版使用双线性重采样铺满背景画布。需要保持前景比例时，在其上游使用 Resize 的 Contain 或 Cover。

- Normal：前景覆盖背景。
- Multiply：重叠区域相乘。
- Screen：重叠区域使用滤色公式 `1 - (1 - background) * (1 - foreground)`。
- Add：重叠区域相加并截断到 0–1。
- Opacity：0–1，与前景 Alpha 和 Mask 的 R 通道相乘。Mask 未连接时为白色，0 保留背景、1 应用前景。

模式计算后按 Source Over 合成，再恢复非预乘 Alpha。该公式处理背景和前景都半透明的情况，依据 [W3C 合成与混合公式](https://www.w3.org/TR/compositing-1/#generalformula) 与 [Source Over](https://www.w3.org/TR/compositing-1/#porterduffcompositingoperators_srcover)。Color 输入在 graph 的 Working Space 中重采样、混合和合成，再转回线性 RGB 输出；sRGB 与 Linear 的结果可能不同。

蒙版读取实际输入数值。用于精确权重的灰度纹理应以 Data 输入（Texture Importer 关闭 sRGB），否则 sRGB 图片会先被解码为线性值。输出类型继承背景；建议前景与背景采用相同的颜色/数据约定。

## 验证连接

```text
Image → Resize → Levels → Blend.Background
Image → Resize ─────────→ Blend.Foreground
Image（灰度蒙版）───────→ Blend.Mask
                          Blend.Image → Image Output
```

先用 Preview 检查结果，再拖动 Levels 的 Gamma 或 Blend 的 Opacity。拖动过程中节点预览与 Preview 窗口持续更新，最多每秒刷新 30 次；停止拖动后补上最终值。只有受参数变化影响的节点与下游会重新 Dispatch；不变分支复用 GPU 缓存。Preview 窗口的 Save 导出完整 RGBA 图片，整个流程不需要 AI 连接。
