using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Reflection;
using System.Linq;
using UnityEditor.PackageManager.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace WIM.Prototype.Editor
{
    [InitializeOnLoad]
    public static class LiveWalkMenu
    {
        const string Item="WIM Prototype/Enable Live Walk Prototype";
        static LiveWalkMenu()
        {
            EditorApplication.delayCall+=PrepareWhenReady;
            EditorApplication.playModeStateChanged+=s=>{
                if(s==PlayModeStateChange.ExitingEditMode){SessionState.SetBool("WIM.LiveWalk.WasDirty",SceneManager.GetActiveScene().isDirty);SessionState.SetString("WIM.LiveWalk.Scene",SceneManager.GetActiveScene().path);}
                if(s==PlayModeStateChange.ExitingPlayMode&&LiveWalkPrototype.Instance!=null)LiveWalkPrototype.Instance.Shutdown();
                if(s==PlayModeStateChange.EnteredPlayMode)LiveWalkPrototype.AutoStart();
                if(s==PlayModeStateChange.EnteredEditMode)EditorApplication.delayCall+=()=>{
                    var scene=SceneManager.GetActiveScene();
                    if(scene.path==SessionState.GetString("WIM.LiveWalk.Scene","")&&!SessionState.GetBool("WIM.LiveWalk.WasDirty",true))
                        typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)?.Invoke(null,new object[]{scene});
                };
            };
            AssemblyReloadEvents.beforeAssemblyReload+=()=>{if(LiveWalkPrototype.Instance!=null)LiveWalkPrototype.Instance.Shutdown();};
        }
        static void PrepareWhenReady()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||!EditorPrefs.GetBool(LiveWalkPrototype.EnabledKey,true))return;
            if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=PrepareWhenReady;return;}
            if(SessionState.GetBool("WIM.LiveWalk.SimulatorChecked",false))return;
            SessionState.SetBool("WIM.LiveWalk.SimulatorChecked",true);PrepareSimulator();
        }
        // The pristine project has no Simulator sample. Import the official dependency once,
        // without overriding existing samples, saving a scene, or changing the XRI switch.
        [MenuItem("WIM Prototype/Prepare XR Simulator")]
        static void PrepareSimulator()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            var package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(XRDeviceSimulator).Assembly);
            if(package==null)return;
            var sample=Sample.FindByPackage(package.name,package.version).FirstOrDefault(s=>s.displayName=="XR Device Simulator");
            if(string.IsNullOrEmpty(sample.displayName)||sample.isImported)return;
            if(sample.Import(Sample.ImportOptions.None))Debug.Log("WIM：已准备官方 XR Device Simulator Sample；Play 时临时启用，原场景与 XRI 开关保持原样。");
            else Debug.Log("WIM：XR Device Simulator Sample 未导入。可在 Package Manager 的 XRI Samples 中手动导入，键鼠漫游仍可使用。");
        }
        [MenuItem(Item)]static void Toggle()=>EditorPrefs.SetBool(LiveWalkPrototype.EnabledKey,!EditorPrefs.GetBool(LiveWalkPrototype.EnabledKey,true));
        [MenuItem(Item,true)]static bool Check(){Menu.SetChecked(Item,EditorPrefs.GetBool(LiveWalkPrototype.EnabledKey,true));return !EditorApplication.isPlaying;}
        [MenuItem("WIM Prototype/Live Walk Status")]
        static void Status(){if(LiveWalkPrototype.Instance!=null)Debug.Log(Newtonsoft.Json.JsonConvert.SerializeObject(LiveWalkPrototype.Instance.Status()));}
    }
}
