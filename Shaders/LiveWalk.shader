Shader "Hidden/WIM/Live Walk"
{
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline"}
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        TEXTURE2D_X(_WIMLiveAlbedo); TEXTURE2D_X(_WIMLiveOpaque); TEXTURE2D_X(_WIMLiveFiltered);
        StructuredBuffer<float4> _WIMLiveReference;
        float _Envelope,_InnerRadius,_OuterRadius,_Strength,_Flatten,_Desaturate;
        float _KernelRadius,_AxisX,_AxisY,_GaussianScale;
        float L(float3 c){return dot(c,float3(.2126,.7152,.0722));}
        float Depth(float2 uv){return LinearEyeDepth(SampleSceneDepth(saturate(uv)),_ZBufferParams);}
        bool Valid(float z){return z>.001&&z<_ProjectionParams.z*.95;}
        float Weight(float2 uv)
        {
            float r=length((uv-.5)*float2(_ScreenParams.x/_ScreenParams.y,1));
            float t=saturate((r-_InnerRadius)/max(.001,_OuterRadius-_InnerRadius));
            return t*t*t*(t*(t*6-15)+10)*_Envelope;
        }
        float4 Base(Varyings i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float2 uv=i.texcoord;float4 lit=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,uv,0);
            float w=Weight(uv);if(w==0||!Valid(Depth(uv)))return lit;
            float3 c=lit.rgb;
            if(_Flatten>.5)
            {
                float4 a=SAMPLE_TEXTURE2D_X_LOD(_WIMLiveAlbedo,sampler_PointClamp,uv,0);float valid=a.a;
                [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++)valid=min(valid,SAMPLE_TEXTURE2D_X_LOD(_WIMLiveAlbedo,sampler_PointClamp,saturate(uv+float2(x,y)/_ScreenParams.xy),0).a);
                float3 opaque=SAMPLE_TEXTURE2D_X_LOD(_WIMLiveOpaque,sampler_PointClamp,uv,0).rgb;
                float3 d=abs(lit.rgb-opaque);
                if(valid<.99||max(d.x,max(d.y,d.z))>.002)return lit;
                float3 flat=a.rgb*_WIMLiveReference[0].x*.88;
                flat*=min(1,max(L(lit.rgb),.0001)*exp2(.06)/max(L(flat),.0001));
                c=lerp(c,flat,w*saturate(.75*_Strength));
            }
            c=lerp(c,L(c).xxx,w*saturate(_Desaturate*.25*_Strength));
            return float4(c,lit.a);
        }
        float3 C(float2 uv){return saturate(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,saturate(uv),0).rgb);}
        float4 Gaussian(Varyings i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float4 original=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,i.texcoord,0);
            if(!Valid(Depth(i.texcoord)))return original;
            float2 px=float2(_AxisX,_AxisY)/_ScreenParams.xy*(_ScreenParams.y/540)*_Strength*_GaussianScale;
            float3 sum=0;float total=0;
            [unroll]for(int k=-10;k<=10;k++)
            {
                float2 uv=saturate(i.texcoord+px*k);if(!Valid(Depth(uv)))continue;
                float a=exp(-.5*k*k/(4.5*4.5));sum+=C(uv)*a;total+=a;
            }
            return float4(sum/max(total,.0001),original.a);
        }
        float4 Guided(Varyings i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float4 original=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,i.texcoord,0);
            float z=Depth(i.texcoord);if(!Valid(z))return original;
            float sigma=max(.08,z*.03);float3 sum=0;float total=0;
            float2 px=float2(_AxisX,_AxisY)/_ScreenParams.xy*(_ScreenParams.y/540)*_KernelRadius*_Strength;
            [unroll]for(int k=-4;k<=4;k++)
            {
                float offset=k/4.0;float2 uv=saturate(i.texcoord+px*offset);float nz=Depth(uv);float dz=abs(nz-z);
                if(!Valid(nz)||dz>sigma*3)continue;
                float a=exp(-2*offset*offset-.5*(dz/sigma)*(dz/sigma));sum+=C(uv)*a;total+=a;
            }
            return float4(sum/max(total,.0001),original.a);
        }
        float4 Composite(Varyings i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float4 c=SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,i.texcoord,0);
            float w=Weight(i.texcoord);if(w==0||!Valid(Depth(i.texcoord)))return c;
            return float4(lerp(c.rgb,SAMPLE_TEXTURE2D_X_LOD(_WIMLiveFiltered,sampler_LinearClamp,i.texcoord,0).rgb,w),c.a);
        }
        float4 Copy(Varyings i):SV_Target{return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,i.texcoord,0);}
        ENDHLSL
        Pass { Name "AlbedoAndDesaturate" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Base
            #pragma target 4.5
            ENDHLSL }
        Pass { Name "Gaussian" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Gaussian
            #pragma target 4.5
            ENDHLSL }
        Pass { Name "SeparableDepthGuided" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Guided
            #pragma target 4.5
            ENDHLSL }
        Pass { Name "ProtectedComposite" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Composite
            #pragma target 4.5
            ENDHLSL }
        Pass { Name "Copy" HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Copy
            #pragma target 4.5
            ENDHLSL }
    }
}
