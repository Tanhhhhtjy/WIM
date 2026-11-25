#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using Object=UnityEngine.Object;

namespace WIM.Prototype
{
    [DefaultExecutionOrder(10000)]
    public sealed partial class LiveWalkPrototype:MonoBehaviour
    {
        const string PackageRoot="Packages/com.wim.livewalk";
        public const string EnabledKey="WIM.LiveWalk.Enabled";
        public static LiveWalkPrototype Instance {get;private set;}
        public static string Output=>Path.Combine(Application.temporaryCachePath,"WIM_LiveWalk_Benchmark");
        public static readonly string[] Names={"B0 原图","B1 官方黑框 .70/.20","B2 官方柔羽化 .70/.50","M1 去光影·保清晰","M2 去光影+柔和虚化","M3 去光影+2D保边平涂","自由混搭"};
        public int mode=4,lightMode;
        public bool keyboardWalk=true,panelOpen,uiHidden,alwaysPreview;
        public bool flatten=true,blur=true,flat2D,desaturate=true,vignette,softVignette=true;
        public float innerRadius=.25f,strength=1,envelope,speed,ratioEMA=1,rawRatio=1,validSamples;
        public int albedoCaptures,referenceReadbacks;
        public bool initialized {get;private set;}
        public string currentTooltip="";
        public Camera ActiveCamera=>keyboardWalk?walkCamera:xrCamera;
        public LiveWalkFeature Feature=>feature;
        public bool BenchmarkRunning {get;private set;}
        public readonly List<object> events=new List<object>();
        Camera xrCamera,walkCamera,auxCamera;Unity.XR.CoreUtils.XROrigin origin;XRDeviceSimulator simulator;
        GameObject body;CharacterController character;
        RenderPipelineAsset oldPipeline;UniversalRenderPipelineAsset pipeline;UniversalRendererData liveRenderer;LiveWalkFeature feature;
        Material material;Material[] kernels;GraphicsBuffer reference;RTHandle albedo,opaque;RenderTexture auxTarget;
        readonly List<(Behaviour item,bool enabled)> providers=new List<(Behaviour,bool)>();
        readonly List<(Collider item,bool enabled)> rigColliders=new List<(Collider,bool)>();
        readonly List<(Renderer item,bool enabled)> rigVisuals=new List<(Renderer,bool)>();
        readonly List<(Canvas canvas,bool enabled)> canvases=new List<(Canvas,bool)>();
        readonly List<(Volume volume,VolumeProfile original,VolumeProfile clone)> volumes=new List<(Volume,VolumeProfile,VolumeProfile)>();
        readonly List<Renderer> hiddenTransparent=new List<Renderer>();Renderer[] transparent;
        TunnelingVignetteController official,liveVignette;bool oldOfficialActive,oldCameraEnabled,oldSimEnabled,oldRunInBackground;
        Vector3 originalRigPosition,originalCameraPosition;Quaternion originalRigRotation,originalCameraRotation;
        Vector3 lastEye;float yaw,pitch,gravity,ignoreMotionUntil,fpsDt=.016f,nextReadback;bool readbackPending,disposed;
        Light sun;float sunIntensity,sunShadow,ambientIntensity,reflection,originalShadowDistance;Color sunColor,ambientColor;Quaternion sunRotation;LightShadows sunShadows;SphericalHarmonicsL2 ambientProbe;
        Font font;bool ownsFont;GameObject ownedSimulator;
        public bool VignetteAvailable=>liveVignette!=null&&vignetteUpdate!=null;
        GUIStyle labelStyle,titleStyle,buttonStyle,toggleStyle,helpStyle,capsuleStyle;float guiScale=1;Rect panelRect;bool stylesReady;
        readonly MethodInfo vignetteUpdate=typeof(TunnelingVignetteController).GetMethod("UpdateTunnelingVignette",BindingFlags.Instance|BindingFlags.NonPublic);
        readonly VignetteParameters vignetteParams=new VignetteParameters();
        static readonly Vector3[] Spots={new Vector3(2.72f,6.74142456f,142.53f),new Vector3(49.5f,19.8625164f,148),new Vector3(52.65303f,19.8636379f,144.032761f),new Vector3(-7.82849f,7.40091953f,147.743439f)};
        static readonly float[] SpotYaw={-95.91559f,134.467026f,142.24231f,-10.59086f};
        static readonly string[] SpotNames={"老奶与竹篮","依斗门广场前","依斗门门洞口","码头近船底"};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void AutoStart()
        {
            if(!EditorPrefs.GetBool(EnabledKey,true)||Instance!=null)return;
            var camera=Camera.main;
            if(camera==null||camera.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>()==null){Debug.Log("WIM：当前场景没有玩家 XR Origin；请打开 S01 后点 Play。");return;}
            var go=new GameObject("_WIM Live Walk Prototype"){hideFlags=HideFlags.HideAndDontSave};Instance=go.AddComponent<LiveWalkPrototype>();
        }
        IEnumerator Start()
        {
            Instance=this;
            for(int i=0;i<120&&Camera.main==null;i++)yield return null;
            yield return null;
            try{Initialize();}catch(Exception e){Debug.LogException(e);Shutdown();}
        }
        void Initialize()
        {
            xrCamera=Camera.main;if(xrCamera==null)throw new Exception("Live Walk requires a player camera");
            origin=xrCamera.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();if(origin==null)throw new Exception("Live Walk requires XR Origin");
            simulator=XRDeviceSimulator.instance;
            if(simulator==null)
            {
                var prefab=FindPrefab<XRDeviceSimulator>("Assets/Samples/XR Interaction Toolkit/3.4.1/XR Device Simulator/XR Device Simulator.prefab","XR Device Simulator");
                if(prefab!=null){ownedSimulator=Object.Instantiate(prefab);ownedSimulator.name="_WIM Temporary XR Device Simulator";ownedSimulator.hideFlags=HideFlags.HideAndDontSave;simulator=ownedSimulator.GetComponent<XRDeviceSimulator>();}
            }
            oldSimEnabled=simulator!=null&&simulator.enabled;oldCameraEnabled=xrCamera.enabled;
            originalRigPosition=origin.transform.position;originalRigRotation=origin.transform.rotation;originalCameraPosition=xrCamera.transform.position;originalCameraRotation=xrCamera.transform.rotation;
            oldRunInBackground=Application.runInBackground;Application.runInBackground=true;
            foreach(var p in origin.GetComponentsInChildren<LocomotionProvider>(true)){providers.Add((p,p.enabled));if(p is TeleportationProvider){p.locomotionStarted+=OnTeleport;p.locomotionEnded+=OnTeleport;}}
            foreach(var r in origin.GetComponentsInChildren<Renderer>(true))rigVisuals.Add((r,r.enabled));
            foreach(var c in origin.GetComponentsInChildren<Collider>(true))rigColliders.Add((c,c.enabled));
            foreach(var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,FindObjectsSortMode.None))canvases.Add((c,c.enabled));
            official=origin.GetComponentInChildren<TunnelingVignetteController>(true);if(official!=null){oldOfficialActive=official.gameObject.activeSelf;official.gameObject.SetActive(false);}
            oldPipeline=QualitySettings.renderPipeline;pipeline=Object.Instantiate((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline);pipeline.hideFlags=HideFlags.HideAndDontSave;
            originalShadowDistance=pipeline.shadowDistance;
            var sourceSO=new SerializedObject(pipeline);var rendererIndex=sourceSO.FindProperty("m_DefaultRendererIndex").intValue;var sourceRenderer=sourceSO.FindProperty("m_RendererDataList").GetArrayElementAtIndex(rendererIndex).objectReferenceValue as UniversalRendererData;
            if(sourceRenderer==null)throw new InvalidOperationException("WIM requires a URP Universal Renderer (3D).");
            liveRenderer=Object.Instantiate(sourceRenderer);liveRenderer.hideFlags=HideFlags.HideAndDontSave;liveRenderer.renderingMode=RenderingMode.Forward;
            var shader=Shader.Find("Hidden/WIM/Live Walk")??LoadPackageAsset<Shader>("Shaders/LiveWalk.shader","LiveWalk t:Shader");
            if(shader==null)throw new InvalidOperationException("WIM LiveWalk.shader is missing; reinstall the package.");
            material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
            kernels=Enumerable.Range(0,8).Select(_=>new Material(material){hideFlags=HideFlags.HideAndDontSave}).ToArray();
            reference=new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,16);reference.SetData(new[]{new Vector4(1,1,0,0)});
            feature=ScriptableObject.CreateInstance<LiveWalkFeature>();feature.hideFlags=HideFlags.HideAndDontSave;feature.material=material;feature.kernels=kernels;feature.reference=reference;feature.median=LoadPackageAsset<ComputeShader>("Shaders/LiveRatio.compute","LiveRatio t:ComputeShader");
            if(feature.median==null||ShaderUtil.ShaderHasError(material.shader))throw new Exception("Live Walk shaders are not ready");
            liveRenderer.rendererFeatures.Add(feature);liveRenderer.SetDirty();sourceSO.FindProperty("m_RendererDataList").arraySize=1;sourceSO.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=liveRenderer;sourceSO.FindProperty("m_DefaultRendererIndex").intValue=0;sourceSO.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.renderPipeline=pipeline;
            body=new GameObject("_WIM Walk Body"){hideFlags=HideFlags.HideAndDontSave};character=body.AddComponent<CharacterController>();character.height=1.78f;character.center=new Vector3(0,.89f,0);character.radius=.18f;character.skinWidth=.015f;character.stepOffset=.32f;character.slopeLimit=55;
            walkCamera=MakeCamera("_WIM Walk Camera",body.transform);walkCamera.transform.localPosition=Vector3.up*1.6f;walkCamera.enabled=true;
            auxCamera=MakeCamera("_WIM Live Albedo",transform);auxCamera.enabled=false;
            var vignetteSource=official!=null?official.gameObject:FindPrefab<TunnelingVignetteController>("Assets/Samples/XR Interaction Toolkit/3.4.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab","TunnelingVignette");
            if(vignetteSource!=null)
            {
                liveVignette=Object.Instantiate(vignetteSource,walkCamera.transform).GetComponent<TunnelingVignetteController>();liveVignette.gameObject.hideFlags=HideFlags.HideAndDontSave;liveVignette.enabled=false;liveVignette.transform.localPosition=Vector3.zero;liveVignette.transform.localRotation=Quaternion.identity;
            }
            if(!VignetteAvailable)Debug.Log("WIM：未找到可用的官方 TunnelingVignette，黑边选项已停用，其余效果可继续使用。");
            sun=RenderSettings.sun;sunIntensity=sun.intensity;sunShadow=sun.shadowStrength;sunShadows=sun.shadows;sunRotation=sun.transform.rotation;sunColor=sun.color;
            ambientColor=RenderSettings.ambientLight;ambientIntensity=RenderSettings.ambientIntensity;ambientProbe=RenderSettings.ambientProbe;reflection=RenderSettings.reflectionIntensity;
            foreach(var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                if(!v.isGlobal||v.sharedProfile==null)continue;
                var copy=ScriptableObject.CreateInstance<VolumeProfile>();copy.hideFlags=HideFlags.HideAndDontSave;
                foreach(var c in v.sharedProfile.components){var cc=Object.Instantiate(c);cc.hideFlags=HideFlags.HideAndDontSave;copy.components.Add(cc);}
                volumes.Add((v,v.sharedProfile,copy));v.sharedProfile=copy;
            }
            transparent=Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r=>r.sharedMaterials.Any(m=>m!=null&&m.renderQueue>2500)&&r.GetComponentInParent<TunnelingVignetteController>()==null).ToArray();
            initialized=true;SetMode(4);SetControl(true);Teleport(0);Log("started");
        }
        static T LoadPackageAsset<T>(string relative,string filter) where T:Object
        {
            var asset=AssetDatabase.LoadAssetAtPath<T>(PackageRoot+"/"+relative);if(asset!=null)return asset;
            foreach(var guid in AssetDatabase.FindAssets(filter).OrderBy(g=>AssetDatabase.GUIDToAssetPath(g),StringComparer.Ordinal))
            {
                var path=AssetDatabase.GUIDToAssetPath(guid);
                if(Path.GetFileName(path)!=Path.GetFileName(relative))continue;
                asset=AssetDatabase.LoadAssetAtPath<T>(path);if(asset!=null)return asset;
            }
            return null;
        }
        static GameObject FindPrefab<T>(string preferred,string name) where T:Component
        {
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(preferred);
            if(asset!=null&&asset.GetComponent<T>()!=null)return asset;
            foreach(var guid in AssetDatabase.FindAssets(name+" t:Prefab").OrderBy(g=>AssetDatabase.GUIDToAssetPath(g),StringComparer.Ordinal))
            {
                asset=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if(asset!=null&&asset.GetComponent<T>()!=null)return asset;
            }
            return null;
        }
        Camera MakeCamera(string name,Transform parent)
        {
            var go=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};go.transform.SetParent(parent,false);var c=go.AddComponent<Camera>();c.CopyFrom(xrCamera);c.targetTexture=null;c.rect=new Rect(0,0,1,1);c.nearClipPlane=.01f;c.fieldOfView=60;
            var data=go.AddComponent<UniversalAdditionalCameraData>();var old=xrCamera.GetComponent<UniversalAdditionalCameraData>();if(old!=null)EditorUtility.CopySerialized(old,data);
            data.SetRenderer(0);data.renderType=CameraRenderType.Base;data.allowXRRendering=false;data.renderPostProcessing=true;data.requiresDepthTexture=true;return c;
        }
        public void Log(string action){if(events.Count>3000)events.RemoveAt(0);events.Add(new{frame=Time.frameCount,time=Time.unscaledTime,action,mode,keyboardWalk,panelOpen,uiHidden,alwaysPreview,envelope,speed,lightMode});}
        public void SetMode(int value)
        {
            int next=(value%7+7)%7;
            if(next!=6){flatten=next>=3;blur=next==4;flat2D=next==5;desaturate=next>=4;vignette=next==1||next==2;softVignette=next!=1;}
            if(!VignetteAvailable)vignette=false;
            mode=next;if(next==6)SetPanel(true);else SetPanel(false);Log("preset");
        }
        public void SetPanel(bool open)
        {
            panelOpen=open;uiHidden=false;lightingPanelOpen=false;
            if(open){mode=6;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            else if(keyboardWalk){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
            RefreshControls();Log(open?"panel opened":"panel closed");
        }
        void RefreshControls()
        {
            if(simulator!=null)simulator.enabled=!keyboardWalk&&!panelOpen&&oldSimEnabled;
            foreach(var p in providers)if(p.item!=null)p.item.enabled=!keyboardWalk&&!panelOpen&&p.enabled;
            foreach(var r in rigVisuals)if(r.item!=null)r.item.enabled=!keyboardWalk&&r.enabled;
            foreach(var c in rigColliders)if(c.item!=null)c.item.enabled=!keyboardWalk&&c.enabled;
            if(character!=null)character.enabled=keyboardWalk;
            // Prototype supplies its own compact HUD. Original canvases return on shutdown.
            foreach(var c in canvases)if(c.canvas!=null)c.canvas.enabled=false;
            if(official!=null)official.gameObject.SetActive(false);
        }
        public void SetControl(bool keyboard)
        {
            if(!initialized)return;
            if(!keyboard&&simulator==null){Debug.Log("WIM：XR 模拟器尚未就绪。停止 Play 后使用菜单 WIM Prototype > Prepare XR Simulator，再点 Play。");return;}
            var source=ActiveCamera;var position=source.transform.position;var euler=source.transform.eulerAngles;
            keyboardWalk=keyboard;xrCamera.enabled=!keyboard&&oldCameraEnabled;walkCamera.enabled=keyboard;
            if(keyboard){SetWalkPose(position,euler);}
            else{SetSimulatorPose(position,euler);}
            RefreshControls();ResetMotion();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;Log("control switched");
        }
        void SetWalkPose(Vector3 eye,Vector3 euler)
        {
            character.enabled=false;body.transform.position=eye-Vector3.up*1.6f;character.enabled=true;
            yaw=euler.y;pitch=Mathf.DeltaAngle(0,euler.x);body.transform.rotation=Quaternion.Euler(0,yaw,0);walkCamera.transform.localRotation=Quaternion.Euler(pitch,0,0);gravity=0;
        }
        void SetSimulatorPose(Vector3 eye,Vector3 euler)
        {
            var rotation=Quaternion.Euler(euler);origin.transform.position+=eye-xrCamera.transform.position;
            var parent=xrCamera.transform.parent;var localPos=parent.InverseTransformPoint(eye);var localRot=Quaternion.Inverse(parent.rotation)*rotation;
            if(simulator!=null)
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var field=simulator.GetType().GetField("m_HMDState",flags);var state=(XRSimulatedHMDState)field.GetValue(simulator);
                state.centerEyePosition=state.devicePosition=localPos;state.centerEyeRotation=state.deviceRotation=localRot;field.SetValue(simulator,state);simulator.GetType().GetField("m_CenterEyeEuler",flags).SetValue(simulator,localRot.eulerAngles);
            }
            xrCamera.transform.SetPositionAndRotation(eye,rotation);
        }
        public void Teleport(int index)
        {
            if(!initialized)return;
            bool newer=SceneManager.GetActiveScene().name=="S01 1";
            var eye=newer?new Vector3(-452,34.134884f,-149.5f):Spots[Mathf.Clamp(index,0,3)];float heading=newer?-167.16698f:SpotYaw[Mathf.Clamp(index,0,3)];
            var hits=Physics.RaycastAll(eye+Vector3.up*2,Vector3.down,6,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.7f&&h.collider!=character&&h.collider.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>()==null).OrderBy(h=>Mathf.Abs(h.point.y-(eye.y-1.6f))).ToArray();
            if(hits.Length>0)eye.y=hits[0].point.y+1.6f;
            if(keyboardWalk)SetWalkPose(eye,new Vector3(0,heading,0));else SetSimulatorPose(eye,new Vector3(0,heading,0));ResetMotion();Log("teleport "+index);
        }
        void OnTeleport(LocomotionProvider provider){if(initialized)ResetMotion();}
        public void ResetMotion(){speed=envelope=0;ignoreMotionUntil=Time.unscaledTime+.1f;if(ActiveCamera!=null)lastEye=ActiveCamera.transform.position;}
        void Update()
        {
            if(!initialized)return;
            float dt=Time.unscaledDeltaTime;fpsDt=Mathf.Lerp(fpsDt,dt,1-Mathf.Exp(-dt/.4f));
            var k=Keyboard.current;var mouse=Mouse.current;
            if(k!=null&&!BenchmarkRunning)
            {
                for(int i=0;i<6;i++)if(k[(Key)((int)Key.F5+i)].wasPressedThisFrame||(keyboardWalk&&k[(Key)((int)Key.Digit1+i)].wasPressedThisFrame))SetMode(i);
                if(k.f11Key.wasPressedThisFrame||(keyboardWalk&&k.digit7Key.wasPressedThisFrame))SetPanel(!panelOpen);
                if(k.leftBracketKey.wasPressedThisFrame)SetMode(mode-1);if(k.rightBracketKey.wasPressedThisFrame)SetMode(mode+1);
                if(k.f12Key.wasPressedThisFrame||(keyboardWalk&&k.pKey.wasPressedThisFrame)){alwaysPreview=!alwaysPreview;Log("preview");}
                if(k.digit0Key.wasPressedThisFrame||(keyboardWalk&&k.hKey.wasPressedThisFrame)){bool hide=!uiHidden;if(hide&&panelOpen)SetPanel(false);uiHidden=hide;Log("hide UI");}
                if(k.semicolonKey.wasPressedThisFrame||(keyboardWalk&&k.lKey.wasPressedThisFrame))SetLighting(lightMode+1);
                for(int i=0;i<4;i++)if(k[(Key)((int)Key.F1+i)].wasPressedThisFrame)Teleport(i);
                if(k.escapeKey.wasPressedThisFrame){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            }
            if(!keyboardWalk||panelOpen||BenchmarkRunning)return;
            if(mouse!=null&&mouse.leftButton.wasPressedThisFrame&&Cursor.lockState!=CursorLockMode.Locked&&!PointerOverHUD()){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
            if(Cursor.lockState!=CursorLockMode.Locked)return;
            if(mouse!=null){var d=mouse.delta.ReadValue();yaw+=d.x*.1f;pitch=Mathf.Clamp(pitch-d.y*.1f,-80,80);body.transform.rotation=Quaternion.Euler(0,yaw,0);walkCamera.transform.localRotation=Quaternion.Euler(pitch,0,0);}
            var input=ReadWalkInput();var world=body.transform.TransformDirection(new Vector3(input.x,0,input.y))*2.5f;
            gravity=character.isGrounded?-1.5f:Mathf.Max(-25,gravity-15*Time.deltaTime);
            character.Move((world+Vector3.up*gravity)*Time.deltaTime);
        }
        Vector2 ReadWalkInput(){var k=Keyboard.current;if(k==null)return Vector2.zero;return Vector2.ClampMagnitude(new Vector2((k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0)),1);}
        void LateUpdate()
        {
            if(!initialized)return;
            var c=ActiveCamera;float dt=Mathf.Max(Time.unscaledDeltaTime,.0001f);var delta=c.transform.position-lastEye;lastEye=c.transform.position;delta.y=0;
            bool intent=false;
            if(keyboardWalk)intent=!panelOpen&&Cursor.lockState==CursorLockMode.Locked&&ReadWalkInput().sqrMagnitude>.0001f;
            else if(!panelOpen)
            {
                intent=providers.Any(p=>p.item is ContinuousMoveProvider move&&move.enabled&&(move.leftHandMoveInput.ReadValue()+move.rightHandMoveInput.ReadValue()).sqrMagnitude>.0001f);
                intent|=simulator!=null&&((simulator.axis2DTargets&XRDeviceSimulator.Axis2DTargets.Position)!=0||simulator.manipulatingFPS)&&ReadWalkInput().sqrMagnitude>.0001f;
            }
            speed=intent&&Time.unscaledTime>=ignoreMotionUntil?delta.magnitude/dt:0;
            float t=Mathf.Clamp01((speed-.05f)/1.95f);float target=t*t*(3-2*t);
            envelope=alwaysPreview?1:Mathf.MoveTowards(envelope,target,dt/.30f);if(envelope<.001f&&target==0)envelope=0;
            innerRadius=Mathf.Clamp(innerRadius,.15f,.4f);strength=Mathf.Clamp(strength,.2f,1.5f);
            feature.mainCamera=c;feature.auxCamera=auxCamera;feature.envelope=envelope;feature.deltaTime=dt;feature.flatten=flatten;feature.desaturate=desaturate;feature.blur=blur;feature.flat2D=flat2D;
            foreach(var m in kernels.Append(material)){m.SetFloat("_Envelope",envelope);m.SetFloat("_InnerRadius",innerRadius);m.SetFloat("_OuterRadius",Mathf.Min(.85f,innerRadius+.31f));m.SetFloat("_Strength",strength);m.SetFloat("_Flatten",flatten?1:0);m.SetFloat("_Desaturate",desaturate?1:0);}
            for(int i=0;i<8;i++){kernels[i].SetFloat("_AxisX",i%2==0?1:0);kernels[i].SetFloat("_AxisY",i%2==1?1:0);kernels[i].SetFloat("_KernelRadius",8+(i/2)*4);kernels[i].SetFloat("_GaussianScale",flat2D?.55f:1);}
            if(VignetteAvailable)
            {
                liveVignette.transform.SetParent(c.transform,false);liveVignette.transform.localPosition=Vector3.zero;liveVignette.transform.localRotation=Quaternion.identity;liveVignette.gameObject.SetActive(vignette&&envelope>.001f);
                if(vignette&&envelope>.001f){vignetteParams.CopyFrom(liveVignette.defaultParameters);vignetteParams.apertureSize=.7f;vignetteParams.featheringEffect=softVignette?.5f:.2f;var a=vignetteParams.vignetteColor;var b=vignetteParams.vignetteColorBlend;a.a*=envelope;b.a*=envelope;vignetteParams.vignetteColor=a;vignetteParams.vignetteColorBlend=b;vignetteUpdate.Invoke(liveVignette,new object[]{vignetteParams});}
            }
            var dimensions=GameDimensions();EnsureBuffers(dimensions.x,dimensions.y);
            if(flatten&&envelope>.001f)CaptureAlbedo(c);
            if(flatten&&envelope>.001f&&!readbackPending&&Time.unscaledTime>nextReadback)
            {
                readbackPending=true;nextReadback=Time.unscaledTime+.25f;
                AsyncGPUReadback.Request(reference,r=>{readbackPending=false;if(this==null||disposed||r.hasError)return;var v=r.GetData<Vector4>()[0];ratioEMA=v.x;rawRatio=v.y;validSamples=v.z;referenceReadbacks++;});
            }
        }
        public static Vector2Int GameDimensions()
        {
            var t=typeof(EditorWindow).Assembly.GetType("UnityEditor.PlayModeView");
            var flags=BindingFlags.Public|BindingFlags.NonPublic;
            var view=t.GetMethod("GetMainPlayModeView",flags|BindingFlags.Static)?.Invoke(null,null);
            var rt=view==null?null:t.GetField("m_TargetTexture",flags|BindingFlags.Instance)?.GetValue(view) as RenderTexture;
            return rt!=null?new Vector2Int(rt.width,rt.height):new Vector2Int(Screen.width,Screen.height);
        }
        void EnsureBuffers(int w,int h)
        {
            w=Mathf.Max(w,16);h=Mathf.Max(h,16);if(auxTarget!=null&&auxTarget.width==w&&auxTarget.height==h)return;
            ReleaseBuffers();
            RTHandle Buffer(string name){var r=new RenderTexture(w,h,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){name=name,hideFlags=HideFlags.HideAndDontSave};r.Create();return RTHandles.Alloc(r);}
            albedo=Buffer("WIM Live Albedo");opaque=Buffer("WIM Live opaque");auxTarget=new RenderTexture(w,h,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear){hideFlags=HideFlags.HideAndDontSave,antiAliasing=4};auxTarget.Create();feature.albedo=albedo;feature.opaque=opaque;
        }
        void CaptureAlbedo(Camera source)
        {
            var debug=UniversalRenderPipelineDebugDisplaySettings.Instance;var old=debug.materialSettings.materialDebugMode;var active=RenderTexture.active;bool vg=liveVignette!=null&&liveVignette.gameObject.activeSelf;
            auxCamera.CopyFrom(source);auxCamera.enabled=false;auxCamera.targetTexture=auxTarget;auxCamera.clearFlags=CameraClearFlags.SolidColor;auxCamera.backgroundColor=Color.clear;auxCamera.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);
            var data=auxCamera.GetUniversalAdditionalCameraData();data.renderPostProcessing=false;data.allowXRRendering=false;
            hiddenTransparent.Clear();
            try{foreach(var r in transparent)if(r!=null&&r.enabled){hiddenTransparent.Add(r);r.enabled=false;}if(liveVignette!=null)liveVignette.gameObject.SetActive(false);debug.materialSettings.materialDebugMode=DebugMaterialMode.Albedo;auxCamera.Render();albedoCaptures++;}
            finally{debug.materialSettings.materialDebugMode=old;foreach(var r in hiddenTransparent)if(r!=null)r.enabled=true;if(liveVignette!=null)liveVignette.gameObject.SetActive(vg);RenderTexture.active=active;}
        }
        void ReleaseBuffers()
        {
            if(auxCamera!=null)auxCamera.targetTexture=null;
            foreach(var handle in new[]{albedo,opaque})if(handle!=null){var texture=handle.rt;if(RenderTexture.active==texture)RenderTexture.active=null;handle.Release();Object.DestroyImmediate(texture);}
            albedo=opaque=null;if(auxTarget!=null){if(RenderTexture.active==auxTarget)RenderTexture.active=null;auxTarget.Release();Object.DestroyImmediate(auxTarget);auxTarget=null;}
        }
        public object Status()=>new{initialized,mode,name=Names[mode],keyboardWalk,panelOpen,uiHidden,alwaysPreview,envelope,speed,innerRadius,outerRadius=innerRadius+.31f,strength,flatten,blur,flat2D,desaturate,vignette,lightMode,ratioEMA,rawRatio,validSamples,albedoCaptures,referenceReadbacks,lighting=LightingStatus(),frame=Time.frameCount,time=Time.unscaledTime,width=GameDimensions().x,height=GameDimensions().y,fps=1/fpsDt,tooltip=currentTooltip,position=ActiveCamera!=null?new[]{ActiveCamera.transform.position.x,ActiveCamera.transform.position.y,ActiveCamera.transform.position.z}:new float[3]};
        public void StartBenchmark(){if(initialized&&!BenchmarkRunning)StartCoroutine(Benchmark());}
        IEnumerator Benchmark()
        {
            BenchmarkRunning=true;bool oldPreview=alwaysPreview;int oldMode=mode;bool oldPanel=panelOpen;var rows=new List<object>();alwaysPreview=true;SetPanel(false);
            for(int i=0;i<6;i++)
            {
                SetMode(i);yield return new WaitForSecondsRealtime(1);var samples=new List<float>();float end=Time.realtimeSinceStartup+3;
                while(Time.realtimeSinceStartup<end){yield return null;samples.Add(Time.unscaledDeltaTime*1000);}
                samples.Sort();float mean=samples.Average();rows.Add(new{preset=Names[i],frames=samples.Count,meanMs=mean,fps=1000/mean,medianMs=samples[samples.Count/2],p95Ms=samples[Mathf.Min(samples.Count-1,(int)(samples.Count*.95f))],width=GameDimensions().x,height=GameDimensions().y,lightMode,envelope});
            }
            Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"live-benchmark.json"),JsonConvert.SerializeObject(new{time=DateTimeOffset.Now,method="actual unpaused Editor Update delta, 1s warmup+3s sample per preset, vsync/editor cap included; not Quest or GPU-only",rows},Formatting.Indented));
            alwaysPreview=oldPreview;SetMode(oldMode);if(oldPanel)SetPanel(true);BenchmarkRunning=false;Log("benchmark completed");Debug.Log("WIM 帧率自测已保存："+Path.Combine(Output,"live-benchmark.json"));
        }
        void OnApplicationFocus(bool focus){if(!focus&&keyboardWalk){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}}
        void OnDestroy(){Shutdown();}
        public void Shutdown()
        {
            if(disposed)return;disposed=true;initialized=false;StopAllCoroutines();
            if(feature!=null)feature.envelope=0;
            if(pipeline!=null)QualitySettings.renderPipeline=oldPipeline;
            if(sun!=null){sun.intensity=sunIntensity;sun.color=sunColor;sun.transform.rotation=sunRotation;sun.shadowStrength=sunShadow;sun.shadows=sunShadows;RenderSettings.ambientLight=ambientColor;RenderSettings.ambientIntensity=ambientIntensity;RenderSettings.reflectionIntensity=reflection;RenderSettings.ambientProbe=ambientProbe;}
            foreach(var v in volumes){if(v.volume!=null)v.volume.sharedProfile=v.original;foreach(var c in v.clone.components)Object.DestroyImmediate(c);Object.DestroyImmediate(v.clone);}
            foreach(var c in rigColliders)if(c.item!=null)c.item.enabled=c.enabled;
            foreach(var p in providers)if(p.item is TeleportationProvider tp){tp.locomotionStarted-=OnTeleport;tp.locomotionEnded-=OnTeleport;}
            if(simulator!=null)simulator.enabled=oldSimEnabled;foreach(var p in providers)if(p.item!=null)p.item.enabled=p.enabled;foreach(var r in rigVisuals)if(r.item!=null)r.item.enabled=r.enabled;foreach(var c in canvases)if(c.canvas!=null)c.canvas.enabled=c.enabled;
            if(official!=null)official.gameObject.SetActive(oldOfficialActive);if(xrCamera!=null)xrCamera.enabled=oldCameraEnabled;
            if(origin!=null){origin.transform.SetPositionAndRotation(originalRigPosition,originalRigRotation);xrCamera.transform.SetPositionAndRotation(originalCameraPosition,originalCameraRotation);}
            Application.runInBackground=oldRunInBackground;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            ReleaseBuffers();reference?.Dispose();reference=null;
            if(liveVignette!=null)Object.DestroyImmediate(liveVignette.gameObject);if(body!=null)Object.DestroyImmediate(body);if(auxCamera!=null)Object.DestroyImmediate(auxCamera.gameObject);
            foreach(var m in kernels??Array.Empty<Material>())Object.DestroyImmediate(m);if(material!=null)Object.DestroyImmediate(material);if(feature!=null)Object.DestroyImmediate(feature);if(liveRenderer!=null)Object.DestroyImmediate(liveRenderer);if(pipeline!=null)Object.DestroyImmediate(pipeline);if(ownsFont&&font!=null)Object.DestroyImmediate(font);
            if(ownedSimulator!=null)Object.DestroyImmediate(ownedSimulator);
            if(Instance==this)Instance=null;
        }
        // IMGUI is intentionally local to this temporary runtime object: no Canvas or scene asset edits.
        bool PointerOverHUD()
        {
            if(uiHidden||Mouse.current==null)return false;var p=Mouse.current.position.ReadValue();p.y=Screen.height-p.y;p/=guiScale;
            return panelOpen?panelRect.Contains(p):new Rect(12,12,Mathf.Min(1260,Screen.width/guiScale-24),34).Contains(p);
        }
        void Styles()
        {
            if(stylesReady)return;stylesReady=true;
            // Unity may keep the first missing family and log every frame instead of falling
            // through the supplied list. Filter against the actual OS families first.
            var installed=new HashSet<string>(Font.GetOSInstalledFontNames(),StringComparer.OrdinalIgnoreCase);
            var families=Application.platform==RuntimePlatform.OSXEditor
                ?new[]{"Arial Unicode MS","Heiti SC","PingFang SC","Hiragino Sans GB","Microsoft YaHei UI","Microsoft YaHei","SimHei"}
                :new[]{"Microsoft YaHei UI","Microsoft YaHei","SimHei","PingFang SC","Hiragino Sans GB","Heiti SC","Arial Unicode MS"};
            var candidates=families.Where(installed.Contains).ToArray();
            font=candidates.Length>0?Font.CreateDynamicFontFromOSFont(candidates,18):null;ownsFont=font!=null;if(font==null)font=GUI.skin.font;
            labelStyle=new GUIStyle(GUI.skin.label){font=font,fontSize=17,normal={textColor=new Color(.9f,.94f,.98f)}};
            titleStyle=new GUIStyle(labelStyle){fontSize=23,fontStyle=FontStyle.Bold};buttonStyle=new GUIStyle(GUI.skin.button){font=font,fontSize=16};toggleStyle=new GUIStyle(GUI.skin.toggle){font=font,fontSize=18};
            helpStyle=new GUIStyle(labelStyle){fontSize=16,wordWrap=true};capsuleStyle=new GUIStyle(labelStyle){fontSize=17,alignment=TextAnchor.MiddleLeft};
        }
        void Hover(Rect r,string tip)
        {
            if(Mouse.current==null)return;var p=Mouse.current.position.ReadValue();p.y=Screen.height-p.y;p/=guiScale;
            if(r.Contains(p))currentTooltip=tip;
        }
        bool Button(Rect r,string text,string tip){Hover(r,tip);return GUI.Button(r,text,buttonStyle);}
        bool Toggle(Rect r,bool value,string text,string tip){Hover(r,tip);return GUI.Toggle(r,value,text,toggleStyle);}
        float Slider(float y,string text,float value,float lo,float hi,string tip)
        {
            var r=new Rect(30,y,650,52);Hover(r,tip);GUI.Label(new Rect(30,y,560,24),text,labelStyle);GUI.Label(new Rect(594,y,86,24),value.ToString("0.00"),labelStyle);
            return GUI.HorizontalSlider(new Rect(35,y+31,632,18),value,lo,hi);
        }
        void OnGUI()
        {
            if(!initialized||uiHidden)return;Styles();guiScale=Mathf.Clamp(Screen.height/900f,.65f,1.5f);var oldMatrix=GUI.matrix;GUI.matrix=Matrix4x4.Scale(Vector3.one*guiScale);currentTooltip="";
            if(!panelOpen)
            {
                var r=new Rect(12,12,Mathf.Min(1260,Screen.width/guiScale-24),34);GUI.color=new Color(.07f,.09f,.12f,.86f);GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=Color.white;
                string keys=keyboardWalk?"1–6 预设  7 混搭  [/] 切换  H 藏UI":"F5–F10 预设  F11 混搭  [/] 切换  0 藏UI";
                GUI.Label(new Rect(24,12,r.width-20,34),$"[{(keyboardWalk?(mode+1).ToString():mode<6?"F"+(mode+5):"F11")}] {Names[mode]}  |  {(alwaysPreview?"常开预览":"仅平动")}  |  {speed:0.0} m/s  |  光照 {LightingShortLabel}  |  {1/fpsDt:0} FPS  ·  {keys}"+(BenchmarkRunning?"  自测中…":""),capsuleStyle);
                if(Event.current.type==EventType.MouseDown&&r.Contains(Event.current.mousePosition)){SetPanel(true);Event.current.Use();}
                GUI.matrix=oldMatrix;return;
            }
            panelRect=new Rect(12,12,698,lightingPanelOpen?(advancedLighting?832:610):772);GUI.color=new Color(.045f,.065f,.09f,.97f);GUI.DrawTexture(panelRect,Texture2D.whiteTexture);GUI.color=Color.white;
            if(lightingPanelOpen){DrawLightingPanel();GUI.matrix=oldMatrix;return;}
            GUI.Label(new Rect(30,25,380,35),"周边视野 · 自由混搭",titleStyle);
            if(Button(new Rect(420,27,155,30),"光照调节 →","打开独立光照面板。可连续调亮度、暗部补光和阴影，三档预设仍可作为起点；不改变当前周边效果。"))OpenLightingPanel();
            if(Button(new Rect(587,27,96,30),"收起", "收起面板；键鼠模式恢复鼠标视角控制。按7（模拟器F11）可再次打开。"))SetPanel(false);
            for(int i=0;i<6;i++)
            {
                bool enabled=GUI.enabled;GUI.enabled=enabled&&(VignetteAvailable||i!=1&&i!=2);
                if(Button(new Rect(30+i*108,72,102,32),(i+1)+" "+new[]{"原图","黑框","柔羽化","保清晰","柔虚化","保边平涂"}[i],!GUI.enabled?"缺少官方 TunnelingVignette，当前不可用。":Names[i]+"：切换整套预设。预设适合快速比较，自由混搭可把模块任意组合。效果只在平移时淡入；尚未证明防晕改善。"))SetMode(i);
                GUI.enabled=enabled;
            }
            GUI.Label(new Rect(30,117,640,24),"勾选即生效 · 中心保持清楚，外圈自由组合",labelStyle);
            flatten=Toggle(new Rect(32,155,640,27),flatten,"去 3D 光影（真实 Albedo 压平）","用材质原色减弱光照明暗，保留原生纹理。暗部有提亮上限，避免白环；不会移除贴图本来画入的阴影。视觉取舍需亲自比较，尚无防晕结论。");
            blur=Toggle(new Rect(32,190,640,27),blur,"周边柔和虚化（Blur）","柔化外圈砖缝、木纹与树叶细节，中心仍清楚。可能减少周边细节刺激，但也会损失可读性；与平涂同时开启时先平涂再轻度虚化。");
            flat2D=Toggle(new Rect(32,225,640,27),flat2D,"2D 保边平涂（Depth 2D Block）","把周边物体内部纹理融成更平的色块，优先保留深度外轮廓。靠近平面的颜色边缘也可能变软；并不是让整座场景变成真正二维。");
            desaturate=Toggle(new Rect(32,260,640,27),desaturate,"周边轻度去饱和（Desaturate）","温和降低外圈颜色鲜艳程度，中心颜色保留。不遮视野；可能让外围更安静，具体舒适感仍需实测。");
            bool wasEnabled=GUI.enabled;GUI.enabled=wasEnabled&&VignetteAvailable;
            vignette=Toggle(new Rect(32,295,640,27),vignette,"叠加官方柔羽化黑边（Vignette）",VignetteAvailable?"叠加XRI官方周边遮挡，可与其他效果混搭。它会缩小可见视野；纯保清晰/虚化/平涂预设不会自动混入黑边。":"未找到官方 TunnelingVignette，黑边功能停用；其他效果不受影响。");
            if(Button(new Rect(48,330,620,29),softVignette?"黑边样式：.70 / .50 柔羽化":"黑边样式：.70 / .20 原版黑框","仅切换黑边边缘软硬：.50过渡更宽柔，.20遮挡更集中。孔径始终.70；需勾选黑边才有效。"))softVignette=!softVignette;
            GUI.enabled=wasEnabled;
            innerRadius=Slider(373,"中心清晰圈大小",innerRadius,.15f,.4f,"数值越大，保留原图的中心区域越大。外沿自动保持相差.31的宽过渡，避免硬接缝；中心半径内逐像素保护。");
            strength=Slider(430,"外圈处理力度",strength,.2f,1.5f,"统一调整压平/褪色强度和虚化/平涂半径。越大外围变化越强，可能损失细节。不会改变官方黑边孔径或中心保护范围。");
            if(Button(new Rect(30,491,172,32),"一键恢复默认","恢复中心.25、力度1.00，并以M2的去光影+柔和虚化+轻度褪色作为混搭起点；关闭黑边和常开预览。")){innerRadius=.25f;strength=1;flatten=blur=desaturate=true;flat2D=vignette=alwaysPreview=false;softVignette=true;}
            if(Button(new Rect(213,491,216,32),alwaysPreview?"常开预览：开":"仅平动触发", "常开时即使站住也保持满强度，方便停在暗墙旁慢慢比较。关闭后只有连续水平平移才触发，转头、瞬移和Snap Turn不触发。P / F12。"))alwaysPreview=!alwaysPreview;
            if(Button(new Rect(440,491,243,32),keyboardWalk?"切到 XR 模拟器":"切到键鼠漫游","键鼠模式：点击画面锁鼠，WASD走路、鼠标转头、Esc释放。切回模拟器恢复原键位；其预设快捷键改用F5–F10，F11面板，避免抢占1–7。"))SetControl(!keyboardWalk);
            if(Button(new Rect(30,538,398,32),"测试灯光："+LightingLabel,"仅临时切换测试场景光照，不属于防晕算法本身，退出自动还原原场景。白天/强阴影/暖色黄昏三档循环；L（模拟器用分号）。每档调整在本次Play内分别保留；点击上方光照调节可自由调整。"))SetLighting(lightMode+1);
            if(Button(new Rect(440,538,243,32),BenchmarkRunning?"帧率自测中…":"运行六档帧率自测","自动逐档常开测试：每档1秒预热、3秒采样，约24秒。统计当前Game View真实帧间隔，含Editor与垂直同步开销；不是Quest帧率。"))StartBenchmark();
            bool newer=SceneManager.GetActiveScene().name=="S01 1";
            for(int i=0;i<(newer?1:4);i++)if(Button(new Rect(30+i*166,585,newer?650:157,32),newer?"新城石铺平台":"F"+(i+1)+" "+SpotNames[i],"仅在当前场景内传送到安全测试点，眼高约1.6米；不会保存场景。传送本身不触发效果。"))Teleport(i);
            GUI.Label(new Rect(30,627,650,56),keyboardWalk?"WASD 走路 · 鼠标转头 · Esc 释放 · 7 收起 · H 隐藏\n[ / ] 前后切 · P 常开 · L 灯光 · F1–F4 传送":"保留原模拟器操作 · F5–F10 预设 · F11 收起\n[ / ] 前后切 · F12 常开 · 0 隐藏 · ; 灯光 · F1–F4 传送",helpStyle);
            GUI.color=new Color(.13f,.19f,.24f,1);GUI.DrawTexture(new Rect(28,687,655,78),Texture2D.whiteTexture);GUI.color=Color.white;
            GUI.Label(new Rect(40,697,632,62),string.IsNullOrEmpty(currentTooltip)?"把鼠标停在任意选项、按钮或滑块上，这里会解释画面变化和取舍。打开面板时暂停走动，防止点击误触移动。":currentTooltip,helpStyle);
            GUI.matrix=oldMatrix;
        }
    }
}
#endif
