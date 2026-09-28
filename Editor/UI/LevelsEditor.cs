using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tsukuyomi.AiCanvas.Editor
{
    // Native PropertyFields remain the Graph Toolkit binding/Undo adapters in both views.
    // The histogram never writes to the graph model or stores its GPU data in the asset.
    internal sealed class LevelsEditor : VisualElement
    {
        private static readonly string[] Fields = { "inputBlack", "gamma", "inputWhite", "outputBlack", "outputWhite" };
        private static readonly string[] Labels = { "Input Black", "Gamma", "Input White", "Output Black", "Output White" };
        private readonly SerializedProperty property;
        private readonly PropertyField[] adapters = new PropertyField[5];
        private readonly PropertyField alphaAdapter;
        private readonly FloatField[] numbers = new FloatField[5];
        private readonly VisualElement histogramPanel, sliderPanel;
        private readonly HistogramPlot plot;
        private readonly Label status;
        private readonly Button mode;
        private readonly DropdownField channel;
        private readonly LevelsHandleBar inputBar, outputBar;
        private readonly string nodeId, preferenceKey;
        private string requestedVersion;
        private int revision, undoGroup = -1;
        private bool histogramMode;
        internal ImageHistogramData HistogramData => plot.Data;

        internal HistogramChannel DisplayChannel
        {
            get => plot.Channel;
            set
            {
                plot.Channel = value == HistogramChannel.Luminance ? HistogramChannel.Luminance : HistogramChannel.RGB;
                channel.SetValueWithoutNotify(channel.choices[(int)plot.Channel]);
                SessionState.SetInt(preferenceKey + ".Channel", (int)plot.Channel);
                plot.MarkDirtyRepaint();
                RefreshStatus();
            }
        }

        internal bool HistogramMode
        {
            get => histogramMode;
            set
            {
                histogramMode = value;
                histogramPanel.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
                sliderPanel.style.display = value ? DisplayStyle.None : DisplayStyle.Flex;
                mode.text = value ? "Sliders" : "Histogram";
                SessionState.SetBool(preferenceKey, value);
                if (value && panel != null) RefreshHistogram();
            }
        }

        internal LevelsEditor(SerializedProperty source)
        {
            property = source.Copy();
            nodeId = property.FindPropertyRelative("nodeId").stringValue;
            preferenceKey = "AiCanvas.Levels.Histogram." + nodeId;
            name = "ai-canvas-levels-editor";
            style.width = 340;
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            var title = new Label("Levels"); title.style.flexGrow = 1;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            toolbar.Add(title);
            toolbar.Add(new Button(Reset) { text = "Reset", name = "levels-reset" });
            toolbar.Add(new Button(Invert) { text = "Invert", name = "levels-invert", tooltip = "交换输出黑白值。" });
            mode = new Button(() => HistogramMode = !HistogramMode) { name = "levels-mode" };
            toolbar.Add(mode); Add(toolbar);

            sliderPanel = new VisualElement { name = "levels-sliders" };
            for (int i = 0; i < Fields.Length; i++)
            {
                adapters[i] = new PropertyField(property.FindPropertyRelative(Fields[i]), Labels[i]);
                sliderPanel.Add(adapters[i]);
            }

            histogramPanel = new VisualElement { name = "levels-histogram" };
            channel = new DropdownField("Display", new List<string> { "RGB", "Luminance" }, 0)
            {
                name = "levels-histogram-channel",
                tooltip = "切换输入直方图的 RGB / 亮度显示。亮度由每个像素的 RGB 加权得到；色阶仍使用当前共用参数调节 RGB。"
            };
            channel.labelElement.style.minWidth = 0; channel.labelElement.style.width = 52;
            channel.RegisterValueChangedCallback(_ => DisplayChannel = channel.index == 1 ? HistogramChannel.Luminance : HistogramChannel.RGB);
            histogramPanel.Add(channel);
            status = new Label("等待输入图像") { name = "levels-histogram-status" };
            status.style.fontSize = 10; status.style.whiteSpace = WhiteSpace.Normal;
            status.tooltip = "输入图像的 RGB / 亮度分布；包含透明像素，超出 0–1 的值计入端点。Color 按 Graph Settings 的工作空间统计，Data 直接加权原始 RGB 数值。";
            histogramPanel.Add(status);
            plot = new HistogramPlot();
            inputBar = new LevelsHandleBar(true, Read, SetParameter, BeginGesture, EndGesture);
            outputBar = new LevelsHandleBar(false, Read, SetParameter, BeginGesture, EndGesture);
            histogramPanel.Add(NumberRow(0, 3));
            histogramPanel.Add(inputBar);
            histogramPanel.Add(plot);
            histogramPanel.Add(outputBar);
            var outputTitle = new Label("Output"); outputTitle.style.fontSize = 10;
            histogramPanel.Add(outputTitle);
            histogramPanel.Add(NumberRow(3, 2));
            Add(histogramPanel); Add(sliderPanel);
            alphaAdapter = new PropertyField(property.FindPropertyRelative("affectAlpha"), "Affect Alpha");
            Add(alphaAdapter);

            this.TrackPropertyValue(property, _ => RefreshValues());
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ImagePreviewStore.Changed -= OnPreviewChanged;
                ImagePreviewStore.Changed += OnPreviewChanged;
                schedule.Execute(() => { RefreshValues(); RefreshHistogram(); });
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                ImagePreviewStore.Changed -= OnPreviewChanged;
                revision++; requestedVersion = null;
                EndGesture();
            });
            DisplayChannel = (HistogramChannel)SessionState.GetInt(preferenceKey + ".Channel", 0);
            HistogramMode = SessionState.GetBool(preferenceKey, true);
            RefreshValues();
        }

        private VisualElement NumberRow(int first, int count)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            for (int i = first; i < first + count; i++)
            {
                int index = i;
                var number = new FloatField(i == 1 ? "Gamma" : i == 0 || i == 3 ? "Black" : "White")
                    { name = "levels-number-" + Fields[i], isDelayed = true, tooltip = Labels[i] };
                number.style.flexGrow = 1; number.style.flexBasis = 0; number.style.minWidth = 0;
                number.labelElement.style.minWidth = 0; number.labelElement.style.width = i == 1 ? 52 : 36;
                number.RegisterValueChangedCallback(evt =>
                {
                    BeginGesture(); SetParameter(index, evt.newValue); EndGesture();
                    number.SetValueWithoutNotify(LevelsHandles.Read(Read(), index));
                });
                numbers[i] = number; row.Add(number);
            }
            return row;
        }

        private LevelsSettings Read()
        {
            float Value(int index) => adapters[index].Q<Slider>()?.value ?? property.FindPropertyRelative(Fields[index]).floatValue;
            return new LevelsSettings
            {
                nodeId = nodeId, inputBlack = Value(0), gamma = Value(1), inputWhite = Value(2),
                outputBlack = Value(3), outputWhite = Value(4), affectAlpha = property.FindPropertyRelative("affectAlpha").boolValue
            };
        }

        private void SetParameter(int index, float value)
        {
            var adapter = adapters[index].Q<Slider>();
            if (adapter == null) return;
            adapter.value = LevelsHandles.Clamp(Read(), index, value);
            RefreshValues();
        }

        private void RefreshValues()
        {
            var value = Read();
            for (int i = 0; i < numbers.Length; i++) numbers[i]?.SetValueWithoutNotify(LevelsHandles.Read(value, i));
            inputBar.MarkDirtyRepaint(); outputBar.MarkDirtyRepaint();
        }

        private void BeginGesture()
        {
            if (undoGroup >= 0) return;
            Undo.IncrementCurrentGroup(); undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Adjust Levels");
        }
        private void EndGesture()
        {
            if (undoGroup < 0) return;
            Undo.CollapseUndoOperations(undoGroup); undoGroup = -1;
        }

        internal void Reset()
        {
            BeginGesture();
            // Expand the input interval before resetting its other endpoint.
            SetParameter(2, 1); SetParameter(0, 0); SetParameter(1, 1);
            SetParameter(3, 0); SetParameter(4, 1);
            var alpha = alphaAdapter.Q<Toggle>(); if (alpha != null) alpha.value = false;
            EndGesture();
        }
        internal void Invert()
        {
            var value = Read(); BeginGesture();
            SetParameter(3, value.outputWhite); SetParameter(4, value.outputBlack);
            EndGesture();
        }

        private void RefreshStatus()
        {
            var data = plot.Data;
            if (data == null) return;
            status.text = "Input " + (DisplayChannel == HistogramChannel.Luminance ? "Luminance" : "RGB") +
                " · " + (data.Kind != ImageValueKind.Color ? "Data (raw)" : data.Space == ImageWorkingSpace.SRgb ? "sRGB" : "Linear") +
                $" · {data.SampleWidth}×{data.SampleHeight}" + (data.Sampled ? " 采样" : " 全像素");
        }

        private void OnPreviewChanged(string key)
        {
            var graph = AiCanvasWindowRegistry.FindGraph(this);
            if (graph != null && (key == null || key == ImagePreviewStore.Key(graph))) RefreshHistogram();
        }

        private async void RefreshHistogram()
        {
            if (panel == null || !histogramMode) return;
            var graph = AiCanvasWindowRegistry.FindGraph(this);
            var input = ImagePreviewStore.FindInput(graph, nodeId, out string error);
            if (input == null)
            {
                revision++; requestedVersion = null; plot.Data = null;
                status.text = error ?? "等待输入图像"; plot.MarkDirtyRepaint(); return;
            }
            var space = graph.Settings.workingSpace;
            string key = ImageHistogram.Key(input, space);
            if (requestedVersion == key) return;
            requestedVersion = key;
            int ticket = ++revision;
            plot.Data = null; plot.MarkDirtyRepaint(); status.text = "正在统计输入直方图…";
            try
            {
                var data = await ImageHistogram.GetAsync(input, space);
                if (panel == null || ticket != revision) return;
                plot.Data = data; plot.MarkDirtyRepaint();
                RefreshStatus();
            }
            catch (Exception exception)
            {
                if (panel != null && ticket == revision) status.text = exception.Message;
            }
        }
    }

}
