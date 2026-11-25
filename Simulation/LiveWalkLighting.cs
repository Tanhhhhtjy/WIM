#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace WIM.Prototype
{
    public sealed partial class LiveWalkPrototype
    {
        [Serializable]
        public sealed class LightLook
        {
            public float exposureEV,fill=1,sunPower,shadow,elevation,azimuth,warmth,contrast;
        }
        readonly LightLook[] lightLooks=new LightLook[3];
        public bool lightingPanelOpen,advancedLighting;
        public LightLook CurrentLightLook=>lightLooks[lightMode]??(lightLooks[lightMode]=DefaultLightLook(lightMode));
        static readonly string[] LightNames={"原版白天","强阴影白天 · 60m","偏暗黄昏"};
        static readonly string[] LightShortNames={"白天","强影","黄昏"};
        public string LightingLabel=>LightNames[lightMode]+(LightingModified?" · 已调":"");
        string LightingShortLabel=>LightShortNames[lightMode]+(LightingModified?"*":"");
        bool LightingModified
        {
            get
            {
                var a=CurrentLightLook;var b=DefaultLightLook(lightMode);
                return !Mathf.Approximately(a.exposureEV,b.exposureEV)||!Mathf.Approximately(a.fill,b.fill)||!Mathf.Approximately(a.sunPower,b.sunPower)||!Mathf.Approximately(a.shadow,b.shadow)||!Mathf.Approximately(a.elevation,b.elevation)||!Mathf.Approximately(a.azimuth,b.azimuth)||!Mathf.Approximately(a.warmth,b.warmth)||!Mathf.Approximately(a.contrast,b.contrast);
            }
        }
        LightLook DefaultLightLook(int preset)
        {
            float contrast=0;
            if(volumes.Count>0&&volumes[0].original.TryGet<ColorAdjustments>(out var grade))contrast=grade.contrast.value;
            return new LightLook{sunPower=preset==0?sunIntensity:preset==1?1.5f:.48f,
                shadow=preset==0?sunShadow:.9f,elevation=preset==0?sunRotation.eulerAngles.x:preset==1?18:8,
                azimuth=preset==0?sunRotation.eulerAngles.y:preset==1?75:105,warmth=preset==2?1:0,contrast=preset==1?60:contrast};
        }
        public object LightingStatus()=>new{preset=LightNames[lightMode],modified=LightingModified,lightingPanelOpen,advancedLighting,settings=CurrentLightLook};
        public void OpenLightingPanel()
        {
            int previousMode=mode;SetPanel(true);mode=previousMode;lightingPanelOpen=true;Log("lighting panel opened");
        }
        public void SetLighting(int value)
        {
            lightMode=(value%3+3)%3;
            foreach(var v in volumes)
            {
                var profile=LightingSource(v.original);
                foreach(var component in v.clone.components)Object.DestroyImmediate(component);
                v.clone.components.Clear();
                foreach(var component in profile.components)
                {
                    var copy=Object.Instantiate(component);copy.hideFlags=HideFlags.HideAndDontSave;v.clone.components.Add(copy);
                }
                if(lightMode==1&&v.clone.TryGet<UnityEngine.Rendering.Universal.Vignette>(out var vg))vg.intensity.Override(0);
            }
            ApplyLightingControls();Log("lighting preset");
        }
        VolumeProfile LightingSource(VolumeProfile original)=>lightMode==1?(AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/ReferenceLook_MutedCinematic.asset")??original):original;
        public void ResetLightingControls()
        {
            lightLooks[lightMode]=DefaultLightLook(lightMode);ApplyLightingControls();Log("lighting defaults restored");
        }
        public void LiftDarkLighting()
        {
            var look=CurrentLightLook;look.fill=lightMode==2?3:lightMode==1?2:1.25f;look.exposureEV=.15f;look.shadow=.55f;look.contrast=Mathf.Min(look.contrast,35);
            ApplyLightingControls();Log("lighting dark areas lifted");
        }
        public void ApplyLightingControls()
        {
            if(!initialized)return;
            var look=CurrentLightLook;
            look.exposureEV=Mathf.Clamp(look.exposureEV,-2,2);look.fill=Mathf.Clamp(look.fill,0,5);
            look.sunPower=Mathf.Clamp(look.sunPower,0,3);look.shadow=Mathf.Clamp01(look.shadow);
            look.elevation=Mathf.Clamp(look.elevation,5,85);look.azimuth=Mathf.Clamp(look.azimuth,0,360);
            look.warmth=Mathf.Clamp01(look.warmth);look.contrast=Mathf.Clamp(look.contrast,-30,70);
            sun.intensity=look.sunPower;sun.shadowStrength=look.shadow;sun.shadows=lightMode==0?sunShadows:LightShadows.Soft;
            sun.color=Color.Lerp(sunColor,new Color(1,.66f,.38f),look.warmth);
            sun.transform.rotation=lightMode==0&&Mathf.Approximately(look.elevation,sunRotation.eulerAngles.x)&&Mathf.Approximately(look.azimuth,sunRotation.eulerAngles.y)
                ?sunRotation:Quaternion.Euler(look.elevation,look.azimuth,0);
            float scale=(lightMode==0?1:lightMode==1?.5f:.18f)*look.fill;
            float intensityScale=(lightMode==0?1:lightMode==1?.65f:.3f)*look.fill;
            RenderSettings.ambientLight=ambientColor*scale;RenderSettings.ambientIntensity=ambientIntensity*intensityScale;
            RenderSettings.reflectionIntensity=reflection*intensityScale;
            var probe=ambientProbe;for(int c=0;c<3;c++)for(int i=0;i<9;i++)probe[c,i]*=scale;RenderSettings.ambientProbe=probe;
            pipeline.shadowDistance=lightMode==0?originalShadowDistance:60;
            foreach(var v in volumes)
            {
                var source=LightingSource(v.original);
                bool hasSource=source.TryGet<ColorAdjustments>(out var baseGrade);
                if(!v.clone.TryGet<ColorAdjustments>(out var grade))grade=v.clone.Add<ColorAdjustments>(false);
                float baseExposure=lightMode==1?.30f:hasSource?baseGrade.postExposure.value:0;
                float baseContrast=lightMode==1?60:hasSource?baseGrade.contrast.value:0;
                grade.postExposure.value=baseExposure;grade.postExposure.overrideState=lightMode==1||hasSource&&baseGrade.postExposure.overrideState;
                grade.contrast.value=baseContrast;grade.contrast.overrideState=lightMode==1||hasSource&&baseGrade.contrast.overrideState;
                grade.active=lightMode==1||hasSource&&baseGrade.active;
                if(!Mathf.Approximately(look.exposureEV,0)){grade.postExposure.Override(baseExposure+look.exposureEV);grade.active=true;}
                if(!Mathf.Approximately(look.contrast,baseContrast)){grade.contrast.Override(look.contrast);grade.active=true;}
            }
        }
        void DrawLightingPanel()
        {
            GUI.Label(new Rect(30,25,380,35),"测试光照 · 自由调节",titleStyle);
            if(Button(new Rect(420,27,155,30),"← 周边效果","返回周边效果混搭面板；已经调好的光照继续保留。")){lightingPanelOpen=false;mode=6;}
            if(Button(new Rect(587,27,96,30),"收起","收起面板继续行走；按7打开面板，再点光照调节可回来。"))SetPanel(false);
            for(int i=0;i<3;i++)
            {
                var old=GUI.backgroundColor;if(i==lightMode)GUI.backgroundColor=new Color(.5f,.8f,1);
                if(Button(new Rect(30+i*219,72,212,32),new[]{"原版白天","强阴影测试","偏暗黄昏"}[i],new[]{"原工程光照。适合先看清场景，再自由调整。","强阴影压力测试：压低环境光、长阴影和高对比度，背光面会更暗。可用暗部补光单独改善。","暖色低角度太阳、低环境光。适合检查暗场效果，可自由提高补光而保留黄昏方向。"}[i]+" 每档调整在本次Play内分别记住，L也可切换。"))SetLighting(i);
                GUI.backgroundColor=old;
            }
            GUI.Label(new Rect(30,112,650,24),LightingLabel+" · 仅测试光照，停止 Play 自动还原",labelStyle);
            var look=CurrentLightLook;GUI.changed=false;
            look.exposureEV=Slider(140,"整体亮度（EV，0 为当前预设）",look.exposureEV,-2,2,"整体变亮或变暗；+1 EV约增加一档曝光。天空与亮处也会一起变亮。只想看清背光人物时，优先提高下一项暗部补光。");
            look.fill=Slider(196,"暗部补光（当前预设的倍率）",look.fill,0,5,"增加环境补光，让老太太背面、船底和阴影里更容易看清；不会转动太阳。1倍是该预设默认，2倍是加倍。过高会让画面显平。");
            look.sunPower=Slider(252,"太阳亮度",look.sunPower,0,3,"调整太阳直射强度。主要改变朝向太阳的亮面；如果人物背光很黑，仅把太阳加亮可能没有帮助，可配合暗部补光。");
            look.shadow=Slider(308,"阴影浓度",look.shadow,0,1,"0表示去掉实时投射阴影，1表示最浓。贴图自带的暗色和物体背向太阳的明暗仍可能存在；不会改变太阳位置。");
            bool primaryChanged=GUI.changed;
            if(Button(new Rect(30,372,650,32),advancedLighting?"收起太阳方向、冷暖和对比度 ▲":"展开太阳方向、冷暖和对比度 ▼","更多可选光照控制。只影响测试环境，不修改周边处理算法，也不保存原场景。"))advancedLighting=!advancedLighting;
            bool advancedChanged=false;
            if(advancedLighting)
            {
                GUI.changed=false;
                look.elevation=Slider(418,"太阳高度（度）",look.elevation,5,85,"数值越高，太阳越接近头顶，投影通常更短；低角度太阳更容易产生长阴影和背光。配合方向一起调整。");
                look.azimuth=Slider(474,"太阳方向（度）",look.azimuth,0,360,"让太阳绕场景转一圈，可以把老太太从背光调整成侧光或正面受光。0和360指向相同方向。");
                look.warmth=Slider(530,"太阳暖色（0 原色 → 1 橙暖）",look.warmth,0,1,"从原工程太阳颜色过渡到黄昏橙暖色。只改变太阳颜色，天空贴图不会自动变成日落。");
                look.contrast=Slider(586,"画面对比度",look.contrast,-30,70,"降低对比度能缓和过黑暗部和过亮亮面；高对比度会放大明暗差。强阴影预设默认60，若看不清可试0到20。");
                advancedChanged=GUI.changed;
            }
            if(primaryChanged||advancedChanged)ApplyLightingControls();
            float buttonsY=advancedLighting?653:427;float helpY=advancedLighting?702:480;
            if(Button(new Rect(30,buttonsY,312,32),"本档恢复默认","只清除当前光照档的调整，恢复它原本的太阳、补光和对比度；不会重置周边效果或其他光照档。"))ResetLightingControls();
            if(Button(new Rect(352,buttonsY,328,32),"暗处看清一点","温和提亮起点：白天补光1.25倍、强阴影2倍、黄昏3倍；曝光+0.15 EV、阴影.55、对比度不高于35。保留太阳方向与冷暖；随后仍可细调。"))LiftDarkLighting();
            GUI.color=new Color(.13f,.19f,.24f,1);GUI.DrawTexture(new Rect(28,helpY,655,110),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.Label(new Rect(40,helpY+10,632,90),string.IsNullOrEmpty(currentTooltip)?"看不清老太太背面：先加暗部补光，必要时减少阴影浓度；不必一味加亮太阳。L切预设，每档调整会保留到本次Play结束。":currentTooltip,helpStyle);
        }
    }
}
#endif
