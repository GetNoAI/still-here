using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StillHere.EditorTools
{
    /// Creates the (nearly empty) game scene and the particle material. Everything else is built at runtime by Game.
    public static class StillHereSetup
    {
        public const string ScenePath = "Assets/StillHere/StillHere.unity";

        [MenuItem("StillHere/Create Scene")]
        public static void CreateSceneMenu() { Debug.Log(CreateScene()); }

        public static string CreateScene()
        {
            System.IO.Directory.CreateDirectory("Assets/StillHere/Resources");
            var sh = Shader.Find("StillHere/TouchParticle");
            if (!sh) return "shader not found";
            const string matPath = "Assets/StillHere/Resources/TouchParticleMat.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(matPath)) AssetDatabase.CreateAsset(new Material(sh) { name = "TouchParticleMat" }, matPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("StillHere");
            go.AddComponent<Game>();
            RenderSettings.skybox = null; RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = Color.black; RenderSettings.fog = false;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            return "scene saved at " + ScenePath;
        }
    }
}
