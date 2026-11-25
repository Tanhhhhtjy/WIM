using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace WIM.Prototype
{
    public sealed class LiveWalkFeature:ScriptableRendererFeature
    {
        public Camera mainCamera,auxCamera;
        public Material material;
        public Material[] kernels;
        public ComputeShader median;
        public RTHandle albedo,opaque;
        public GraphicsBuffer reference;
        public bool flatten,desaturate,blur,flat2D;
        public float envelope,deltaTime;
        public int mainPassFrames,auxPassFrames;
        Pass capture,basePass,style;
        public override void Create(){capture=new Pass{phase=0};basePass=new Pass{phase=1};style=new Pass{phase=2};}
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            if(material==null||envelope<=.001f)return;
            bool aux=data.cameraData.camera==auxCamera;
            if(!aux&&data.cameraData.camera!=mainCamera)return;
            if(aux||flatten)
            {
                capture.owner=this;capture.aux=aux;capture.renderPassEvent=RenderPassEvent.BeforeRenderingTransparents;
                capture.ConfigureInput(ScriptableRenderPassInput.Color|ScriptableRenderPassInput.Depth);renderer.EnqueuePass(capture);
            }
            if(aux){auxPassFrames++;return;}
            mainPassFrames++;
            if(flatten||desaturate){basePass.owner=this;basePass.renderPassEvent=RenderPassEvent.BeforeRenderingPostProcessing;basePass.ConfigureInput(ScriptableRenderPassInput.Color|ScriptableRenderPassInput.Depth);renderer.EnqueuePass(basePass);}
            if(blur||flat2D){style.owner=this;style.renderPassEvent=RenderPassEvent.AfterRenderingPostProcessing;style.ConfigureInput(ScriptableRenderPassInput.Color|ScriptableRenderPassInput.Depth);renderer.EnqueuePass(style);}
        }
        sealed class Pass:ScriptableRenderPass
        {
            public int phase;public bool aux;public LiveWalkFeature owner;
            sealed class Data{public TextureHandle source,filtered,albedo,opaque;public BufferHandle reference;public Material material;public int index;}
            sealed class ComputeData{public TextureHandle lit,albedo;public BufferHandle reference;public ComputeShader shader;public int width,height;public float dt;}
            public Pass(){requiresIntermediateTexture=true;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer context)
            {
                var r=context.Get<UniversalResourceData>();if(r.isActiveTargetBackBuffer)return;
                var source=r.activeColorTexture;
                if(phase==0)
                {
                    var destination=graph.ImportTexture(aux?owner.albedo:owner.opaque);
                    using(var b=graph.AddRasterRenderPass<Data>(aux?"WIM Live Albedo":"WIM Live opaque",out var d))
                    {
                        d.source=source;d.material=owner.material;b.UseTexture(source,AccessFlags.Read);b.SetRenderAttachment(destination,0,AccessFlags.Write);b.AllowPassCulling(false);
                        b.SetRenderFunc((Data p,RasterGraphContext c)=>Blitter.BlitTexture(c.cmd,p.source,new Vector4(1,1,0,0),p.material,4));
                    }
                    if(!aux)
                    using(var b=graph.AddComputePass<ComputeData>("WIM Live GPU median + EMA",out var d))
                    {
                        d.lit=destination;d.albedo=graph.ImportTexture(owner.albedo);d.reference=graph.ImportBuffer(owner.reference);d.shader=owner.median;d.width=owner.opaque.rt.width;d.height=owner.opaque.rt.height;d.dt=owner.deltaTime;
                        b.UseTexture(d.lit,AccessFlags.Read);b.UseTexture(d.albedo,AccessFlags.Read);b.UseBuffer(d.reference,AccessFlags.ReadWrite);b.AllowPassCulling(false);
                        b.SetRenderFunc((ComputeData p,ComputeGraphContext c)=>{
                            c.cmd.SetComputeTextureParam(p.shader,0,"_Lit",p.lit);c.cmd.SetComputeTextureParam(p.shader,0,"_Albedo",p.albedo);c.cmd.SetComputeBufferParam(p.shader,0,"_Reference",p.reference);
                            c.cmd.SetComputeIntParam(p.shader,"_Width",p.width);c.cmd.SetComputeIntParam(p.shader,"_Height",p.height);c.cmd.SetComputeFloatParam(p.shader,"_DeltaTime",p.dt);c.cmd.DispatchCompute(p.shader,0,1,1,1);
                        });
                    }
                    return;
                }
                var desc=graph.GetTextureDesc(source);desc.msaaSamples=MSAASamples.None;desc.clearBuffer=false;
                if(phase==1)
                {
                    desc.name="WIM Live de-light and desaturate";var output=graph.CreateTexture(desc);
                    using(var b=graph.AddRasterRenderPass<Data>(desc.name,out var d))
                    {
                        d.source=source;d.material=owner.material;b.UseTexture(source,AccessFlags.Read);b.UseTexture(r.cameraDepthTexture,AccessFlags.Read);
                        d.reference=graph.ImportBuffer(owner.reference);b.UseBuffer(d.reference,AccessFlags.Read);
                        if(owner.flatten){d.albedo=graph.ImportTexture(owner.albedo);d.opaque=graph.ImportTexture(owner.opaque);b.UseTexture(d.albedo,AccessFlags.Read);b.UseTexture(d.opaque,AccessFlags.Read);}
                        b.SetRenderAttachment(output,0,AccessFlags.Write);b.AllowGlobalStateModification(true);b.UseAllGlobalTextures(true);
                        if(r.mainShadowsTexture.IsValid())b.UseTexture(r.mainShadowsTexture,AccessFlags.Read);
                        if(r.additionalShadowsTexture.IsValid())b.UseTexture(r.additionalShadowsTexture,AccessFlags.Read);
                        b.SetRenderFunc((Data p,RasterGraphContext c)=>{
                            if(p.albedo.IsValid()){c.cmd.SetGlobalTexture("_WIMLiveAlbedo",p.albedo);c.cmd.SetGlobalTexture("_WIMLiveOpaque",p.opaque);}
                            c.cmd.SetGlobalBuffer("_WIMLiveReference",p.reference);Blitter.BlitTexture(c.cmd,p.source,new Vector4(1,1,0,0),p.material,0);
                        });
                    }
                    r.cameraColor=output;return;
                }
                var input=source;
                for(int j=0;j<8;j++)
                {
                    if(j<6&&!owner.flat2D||j>=6&&!owner.blur)continue;
                    desc.name="WIM Live "+(j<6?"guided ":"gaussian ")+j;var output=graph.CreateTexture(desc);
                    using(var b=graph.AddRasterRenderPass<Data>(desc.name,out var d))
                    {
                        d.source=input;d.material=owner.kernels[j];d.index=j<6?2:1;
                        b.UseTexture(input,AccessFlags.Read);b.UseTexture(r.cameraDepthTexture,AccessFlags.Read);b.SetRenderAttachment(output,0,AccessFlags.Write);
                        b.SetRenderFunc((Data p,RasterGraphContext c)=>Blitter.BlitTexture(c.cmd,p.source,new Vector4(1,1,0,0),p.material,p.index));
                    }
                    input=output;
                }
                desc.name="WIM Live protected style composite";var result=graph.CreateTexture(desc);
                using(var b=graph.AddRasterRenderPass<Data>(desc.name,out var d))
                {
                    d.source=source;d.filtered=input;d.material=owner.material;b.UseTexture(source,AccessFlags.Read);b.UseTexture(input,AccessFlags.Read);b.UseTexture(r.cameraDepthTexture,AccessFlags.Read);b.SetRenderAttachment(result,0,AccessFlags.Write);b.AllowGlobalStateModification(true);
                    b.SetRenderFunc((Data p,RasterGraphContext c)=>{c.cmd.SetGlobalTexture("_WIMLiveFiltered",p.filtered);Blitter.BlitTexture(c.cmd,p.source,new Vector4(1,1,0,0),p.material,3);});
                }
                r.cameraColor=result;
            }
        }
    }
}
