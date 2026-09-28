#ifndef AICANVAS_IMAGE_FILTER_COMMON_INCLUDED
#define AICANVAS_IMAGE_FILTER_COMMON_INCLUDED
int _WorkingSrgb;

float4 Premultiply(float4 value) { return float4(value.rgb * value.a, value.a); }

float3 WorkingEncodeSrgb(float3 value)
{
    value = max(value, 0.0);
    return lerp(value * 12.92, 1.055 * pow(value, 1.0 / 2.4) - 0.055, step(0.0031308, value));
}

float3 WorkingDecodeSrgb(float3 value)
{
    return lerp(value / 12.92, pow(max((value + 0.055) / 1.055, 0.0), 2.4), step(0.04045, value));
}

float4 ToWorking(float4 value, bool isColor)
{
    if (_WorkingSrgb != 0 && isColor) value.rgb = WorkingEncodeSrgb(value.rgb);
    return value;
}

float4 FromWorking(float4 value, bool isColor)
{
    if (_WorkingSrgb != 0 && isColor) value.rgb = WorkingDecodeSrgb(value.rgb);
    return value;
}

// Pixel-center resampling with explicit clamping, independent of source import/filter settings.
float4 ReadBilinear(Texture2D<float4> image, float2 uv, bool alphaAware)
{
    uint width, height;
    image.GetDimensions(width, height);
    float2 position = uv * float2(width, height) - 0.5;
    int2 basePixel = (int2)floor(position);
    float2 weight = frac(position);
    int2 lastPixel = int2(width, height) - 1;
    float4 a = image.Load(int3(clamp(basePixel, int2(0, 0), lastPixel), 0));
    float4 b = image.Load(int3(clamp(basePixel + int2(1, 0), int2(0, 0), lastPixel), 0));
    float4 c = image.Load(int3(clamp(basePixel + int2(0, 1), int2(0, 0), lastPixel), 0));
    float4 d = image.Load(int3(clamp(basePixel + int2(1, 1), int2(0, 0), lastPixel), 0));
    a = ToWorking(a, alphaAware); b = ToWorking(b, alphaAware);
    c = ToWorking(c, alphaAware); d = ToWorking(d, alphaAware);
    if (alphaAware) { a = Premultiply(a); b = Premultiply(b); c = Premultiply(c); d = Premultiply(d); }
    float4 result = lerp(lerp(a, b, weight.x), lerp(c, d, weight.x), weight.y);
    if (alphaAware) result.rgb = result.a > 1e-6 ? result.rgb / result.a : 0;
    return result;
}


#endif
