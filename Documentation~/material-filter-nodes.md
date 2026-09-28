# 材质处理节点

2026-09-19 补充。检查时已有 Resize / Levels / Blend，以及 GPU 缓存、所有本地节点预览、参数编辑和 PNG 保存。本批沿用这些机制，补齐从图片提取灰度/蒙版，再进行空间处理并生成法线的链路。

功能参考 Adobe Substance 3D Designer 的节点职责；这是 AiCanvas 的独立实现，不读取 `.sbs` / `.sbsar`，不保证与 Designer 的内部算法或数值参数逐像素等价。所有节点在本地 ComputeShader 上执行，修改参数不会调用 AI。

## 节点与数据约定

| 菜单 / 节点 | 输入 | 输出 | 主要用途 |
| --- | --- | --- | --- |
| Generators / Uniform Color | 无 | Color 或 Data | 常量颜色、蒙版或基础高度 |
| Filters / Channels / Grayscale | Image | Data | 亮度及 RGBA 通道提取 |
| Filters / Adjustment / Invert | Image | 继承输入 | RGB 反相，可同时反相 Alpha |
| Filters / Adjustment / Histogram Scan | Grayscale | Data | 灰度阈值与软蒙版 |
| Filters / Transform / Transform 2D | Image | 继承输入 | 平移、缩放、旋转、镜像和平铺 |
| Filters / Blur / Gaussian Blur | Image | 继承输入 | 柔化图像和蒙版 |
| Filters / Transform / Directional Warp | Image、Intensity | 继承 Image | 灰度图驱动的定向变形 |
| Filters / Normal / Normal | Height | Data | 高度转切线空间法线 |

Image 输出尺寸继承第一个输入；Uniform Color 自己指定尺寸。Color 纹理在节点间保存为线性 RGB，在 graph 的 Working Space 中做颜色运算。Data 和 Alpha 始终保存原始数值。

Histogram Scan、Directional Warp 的 Intensity、Normal 的 Height 读取实际输入 **R 通道**，不做额外颜色转换或自动亮度提取。外部灰度数据贴图应关闭 Texture Importer 的 sRGB；彩色图片请先通过 Grayscale 明确提取亮度。Grayscale 的输出可直接连接 Blend.Mask。

## Uniform Color

- Width / Height：1 到设备上限与 16384 的较小值，默认 1024 × 1024。
- Color 模式：使用带 Alpha 的标准 sRGB 颜色选择器；节点输出为线性 Color，PNG 保存重新编码为 sRGB。默认中灰。
- Grayscale 模式：Value 为 0–1 的原始数值，复制到 RGB，Alpha 为 1，输出为 Data。默认 0.5。
- 不需要输入图片，可作为纯本地图的起点。图的 Working Space 不改变所选常量本身。

## Grayscale 与 Invert

Grayscale 的 Channel 可选 Luminance、Red、Green、Blue、Alpha。Luminance 使用 `0.2126729 R + 0.7151522 G + 0.0721750 B`，与 Levels 直方图一致；Color RGB 先转换至 graph 工作空间。默认输出 Alpha=1；Preserve Alpha 可保留输入透明度。输出始终为 Data，因此提取的 0.5 权重保存后仍为 0.5，不做 sRGB 编码。

Invert 对工作空间 RGB 计算 `1 - value`，默认保留 Alpha，Affect Alpha 可同时反相。它继承 Color/Data 类型。输入限定在 0–1 时得到通常的反色；HDR Data 不额外截断，PNG 导出仍限制在 0–1。

## Histogram Scan

- Position：0–1，默认 0.5；增大会扩大白色区域，0 全黑、1 全白。
- Contrast：0–1，默认 0.5；1 为硬阈值，降低后扩大灰色过渡范围。
- Invert：反相最后的蒙版结果。
- 输出 RGB 相同，Alpha 固定为 1，不需要读取或统计输入直方图。

本实现定义：先将输入 R 截断至 0–1；`width = 1 - contrast`，`threshold = lerp(1 + width/2, -width/2, position)`，软过渡为 `saturate((R - threshold)/width + 0.5)`。Contrast=1 使用硬阈值（等于阈值属于白色）；Position 两端强制全黑/全白。Position=0.5、Contrast=0 为恒等灰度映射。此参数化保证 Position 在整个范围可连续扩张蒙版。

## 空间采样边界

Transform 2D、Gaussian Blur、Directional Warp、Normal 共享 Border：

- Clamp：复制边缘像素。
- Repeat（默认）：跨边界平铺；双线性逐 tap 寻址，使首尾像素在接缝处正确插值。
- Transparent：图外为 RGBA=(0,0,0,0)，双线性与模糊核也包含图外样本。用于 Normal 时意味着图外高度为 0，可能在边缘产生坡度。

Color 的双线性与高斯过滤在工作空间中使用预乘 Alpha，结果恢复非预乘形式，避免透明像素隐藏颜色渗入边缘；Data 的四个通道分别过滤。

## Transform 2D

- Offset (UV)：平移图像，X 向右、Y 向上；1 表示完整画布宽度或高度，范围 -100–100。
- Scale：1 为原大小，大于 1 放大；负数实现镜像。每个轴绝对值范围 0.001–100，禁止 0。
- Rotation：以图像中心逆时针旋转，单位度，范围 -360–360。先缩放再旋转，使用像素坐标以保持非正方形图片的宽高比。
- Sampling：Nearest 或 Bilinear；默认 Bilinear。

输出画布大小不变。缩小 Scale 并配合 Repeat 可平铺内容；需要改变画布尺寸时使用 Resize。目前没有剪切矩阵、任意枢轴、2D 操作手柄或 mipmap 缩小抗锯齿。

## Gaussian Blur

Sigma (px) 为高斯标准差，范围 0–16，默认 2；当前分辨率中的采样半径为 `ceil(3 × sigma)`。Sigma=0 精确保留原图（包含透明像素隐藏 RGB）。非零时使用横向、纵向两遍可分离高斯核，权重归一化；仅最终输出进入缓存，中间纹理及时释放。

Sigma > 1 时每组沿采样轴处理 64 个像素，共享邻域与权重，复用采样和颜色转换；非整线程组尺寸仍参与必要的同步。Sigma ≤ 1 保留直接采样，避免小核的共享内存开销。两种路径使用相同半径、权重、累加顺序、边界及 Alpha 规则，中间纹理仍为 RGBAHalf。

参数以像素为单位，因此不同分辨率下的视觉模糊比例不同。大图和大 Sigma 会增加显存占用与运算时间；这里没有分辨率自适应、近似金字塔模糊或性能帧率保证。

## Directional Warp

- Intensity (px)：范围 -256–256，默认 10；乘以强度图 R 减去 Map Offset 后的值。
- Angle：单位度，范围 -360–360；正位移 0° 向右，90° 向上。采用反向采样，`sourcePixel = outputPixel - direction × intensity × (map.r - mapOffset)`。
- Map Offset：0–1，默认 0（黑色无位移）；0.5 可用中灰控制双向位移。
- Sampling：Nearest / Bilinear；Border 使用上述共享约定。
- Intensity 为必需输入，尺寸不同时使用原始通道的双线性采样铺满 Image 画布。

该节点提供定向扭曲，不是梯度驱动的 Warp、Vector Warp 或 Slope Blur。

## Normal

- Strength：0–100，默认 1。用高度 R 的中央差分计算 UV 导数：`dx = (right-left) × width/2`，`dy = (up-down) × height/2`，法线为 `normalize(-strength×dx, -strength×dy, 1)`。
- Format：默认 OpenGL；DirectX 翻转 Y / Green。+V 以图像向上为正；编码 RGB 为 `normal × 0.5 + 0.5`。
- Height in Alpha：关闭时 Alpha=1，开启时将输入高度 R 写入 Alpha。
- 输出始终为 Data。平面得到 `(0.5, 0.5, 1)`，保存 PNG 时不进行 sRGB 编码，也不会自动设为 Unity 的 Normal Map importer 类型。

按 UV 导数计算可减轻更换分辨率造成的强度变化；边缘差分和采样分辨率仍影响细节。本实现是中央差分，不是 Designer 的 Normal Sobel 或世界单位高度标定。

## 推荐连接与下一步

```text
Image → Grayscale → Histogram Scan → Gaussian Blur → Normal → Image Output
                     └────────────────────────────→ Blend.Mask
Image → Transform 2D → Directional Warp.Image → Levels → Blend.Background
Grayscale / 灰度图片 → Directional Warp.Intensity
Uniform Color ────────────────────────────────────→ Blend.Foreground
```

打开 Preview 检查结果，调节 Sigma、Position 或 Strength 后查看下游变化，再使用 Save 保存 PNG。无需配置 AI 连接。

下一批已实现 Gradient Map、Histogram Range / Select、RGBA Merge、RGBA Split、Channel Shuffle、Gradient Linear、Shape、Tileable Noise 和基础 Tile Generator，见 [程序化节点](procedural-image-nodes.md)。

后续建议顺序（在第二批基础上）：

1. **Tile Sampler、自定义图案输入与控制贴图**：在现有基础 Tile Generator 上加入图案采样、尺寸/旋转/遮罩贴图。
2. **Slope Blur、距离、边缘与形态处理**：补齐磨损、轮廓和更复杂的蒙版处理。
3. **Normal Blend、AO / Curvature、材质预览与多通道导出**：进一步定义法线合成、物理尺度和材质输出约定。

## 验证记录

2026-09-19，在 Unity 6000.6.0b3 / NVIDIA GeForce RTX 5060 上运行完整 EditMode 套件：**68/68 通过，0 失败、0 跳过**。保留原有 51 个用例，新增 17 个用例覆盖像素公式、颜色空间、Alpha、边界与平铺、两遍模糊及临时纹理释放、缓存、序列化、真实节点面板绑定与 Undo、离线执行及 Data PNG 导出。测试未调用 AI 服务。参数单位及算法差异以本页说明为准，未做与 Designer 的逐像素一致性认证。

## 参考

- [Adobe 节点库](https://experienceleague.adobe.com/en/docs/substance-3d-designer/using/substance-graphs/nodes-reference-for-substance-graphs/node-library/node-library)：节点职责与分类。
- [Histogram Scan](https://experienceleague.adobe.com/en/docs/substance-3d-designer/using/substance-graphs/nodes-reference-for-substance-graphs/node-library/filters/adjustments/histogram-scan)：Position、Contrast 与蒙版扩张用途。
- [Transformation 2D](https://experienceleague.adobe.com/en/docs/substance-3d-designer/using/substance-graphs/nodes-reference-for-substance-graphs/atomic-nodes/transformation-2d)：图像空间变换。
- [Directional Warp](https://experienceleague.adobe.com/en/docs/substance-3d-designer/using/substance-graphs/nodes-reference-for-substance-graphs/atomic-nodes/directional-warp)：强度图与指定方向控制位移。
- [Normal](https://experienceleague.adobe.com/en/docs/substance-3d-designer/using/substance-graphs/nodes-reference-for-substance-graphs/atomic-nodes/normal)：高度转法线、格式与 Alpha 选项。
