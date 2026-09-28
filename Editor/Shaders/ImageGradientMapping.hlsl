#ifndef AICANVAS_IMAGE_GRADIENT_MAPPING_INCLUDED
#define AICANVAS_IMAGE_GRADIENT_MAPPING_INCLUDED

float4 _GradientColor0, _GradientColor1, _GradientColor2, _GradientColor3;
float4 _GradientColor4, _GradientColor5, _GradientColor6, _GradientColor7;
float4 _GradientAlpha0, _GradientAlpha1, _GradientAlpha2, _GradientAlpha3;
float4 _GradientAlpha4, _GradientAlpha5, _GradientAlpha6, _GradientAlpha7;
int _ColorCount, _AlphaCount, _GradientMode, _GradientAddress, _MultiplyInputAlpha;

float4 GradientColorKey(int index)
{
    if (index == 0) return _GradientColor0;
    if (index == 1) return _GradientColor1;
    if (index == 2) return _GradientColor2;
    if (index == 3) return _GradientColor3;
    if (index == 4) return _GradientColor4;
    if (index == 5) return _GradientColor5;
    if (index == 6) return _GradientColor6;
    return _GradientColor7;
}

float2 GradientAlphaKey(int index)
{
    if (index == 0) return _GradientAlpha0.xy;
    if (index == 1) return _GradientAlpha1.xy;
    if (index == 2) return _GradientAlpha2.xy;
    if (index == 3) return _GradientAlpha3.xy;
    if (index == 4) return _GradientAlpha4.xy;
    if (index == 5) return _GradientAlpha5.xy;
    if (index == 6) return _GradientAlpha6.xy;
    return _GradientAlpha7.xy;
}

// Oklab reference: https://bottosson.github.io/posts/oklab/
float3 GradientToOklab(float3 rgb)
{
    float3 lms = float3(dot(rgb, float3(0.4122214708, 0.5363325363, 0.0514459929)),
        dot(rgb, float3(0.2119034982, 0.6806995451, 0.1073969566)),
        dot(rgb, float3(0.0883024619, 0.2817188376, 0.6299787005)));
    lms = pow(max(lms, 0), 1.0 / 3.0);
    return float3(dot(lms, float3(0.2104542553, 0.7936177850, -0.0040720468)),
        dot(lms, float3(1.9779984951, -2.4285922050, 0.4505937099)),
        dot(lms, float3(0.0259040371, 0.7827717662, -0.8086757660)));
}

float3 GradientFromOklab(float3 lab)
{
    float3 lms = float3(lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z,
        lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z,
        lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z);
    lms = lms * lms * lms;
    return max(float3(dot(lms, float3(4.0767416621, -3.3077115913, 0.2309699292)),
        dot(lms, float3(-1.2684380046, 2.6097574011, -0.3413193965)),
        dot(lms, float3(-0.0041960863, -0.7034186147, 1.7076147010))), 0);
}

float GradientWeight(float value, float start, float end)
{
    // Unity's Fixed mode selects the next key immediately after the previous key.
    if (_GradientMode == 1 || end <= start) return value > start ? 1 : 0;
    return saturate((value - start) / (end - start));
}

[numthreads(8, 8, 1)]
void GradientMapImage(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)_Width || id.y >= (uint)_Height) return;
    float4 source = _Input.Load(int3(id.xy, 0));
    float value = _GradientAddress == 0 ? saturate(source.r) : frac(source.r);
    float4 previous = GradientColorKey(0);
    float3 color = _GradientMode == 2 ? GradientToOklab(previous.rgb) : previous.rgb;
    [loop] for (int i = 1; i < _ColorCount; i++)
    {
        float4 next = GradientColorKey(i);
        float3 nextColor = _GradientMode == 2 ? GradientToOklab(next.rgb) : next.rgb;
        color = lerp(color, nextColor, GradientWeight(value, previous.w, next.w));
        previous = next;
    }
    float2 previousAlpha = GradientAlphaKey(0);
    float alpha = previousAlpha.x;
    [loop] for (int j = 1; j < _AlphaCount; j++)
    {
        float2 nextAlpha = GradientAlphaKey(j);
        alpha = lerp(alpha, nextAlpha.x, GradientWeight(value, previousAlpha.y, nextAlpha.y));
        previousAlpha = nextAlpha;
    }
    if (_MultiplyInputAlpha != 0) alpha *= source.a;
    _Output[id.xy] = _GradientMode == 2 ? float4(GradientFromOklab(color), alpha) : FromWorking(float4(color, alpha), true);
}

#endif
