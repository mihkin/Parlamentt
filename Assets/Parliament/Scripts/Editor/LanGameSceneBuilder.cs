using System;
using System.Reflection;
using ParliamentGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ParliamentGame.EditorTools
{
    public static class LanGameSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/PvEGameScene.unity";
        private const string TargetScenePath = "Assets/Scenes/LanGameScene.unity";
        private const string MainMenuScenePath = "Assets/Scenes/MainMenuScene.unity";

        [MenuItem("Parliament/Build LAN Scene")]
        public static void BuildLanScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("LAN-сцену нельзя пересобирать во время Play Mode.");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                Debug.LogError($"Не найдена исходная PvE-сцена: {SourceScenePath}");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TargetScenePath) != null)
                AssetDatabase.DeleteAsset(TargetScenePath);

            if (!AssetDatabase.CopyAsset(SourceScenePath, TargetScenePath))
            {
                Debug.LogError("Не удалось создать копию PvE-сцены для LAN.");
                return;
            }

            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.OpenScene(TargetScenePath, OpenSceneMode.Single);
            GameManager gameManager = UnityEngine.Object.FindObjectOfType<GameManager>();
            if (gameManager == null)
            {
                Debug.LogError("На LAN-сцене не найден GameManager.");
                return;
            }

            SetPrivate(gameManager, "autoStartOnSceneLoad", false);

            LanGameSceneBootstrap bootstrap = gameManager.GetComponent<LanGameSceneBootstrap>();
            if (bootstrap == null)
                bootstrap = gameManager.gameObject.AddComponent<LanGameSceneBootstrap>();

            SetPrivate(bootstrap, "gameManager", gameManager);
            SetPrivate(bootstrap, "mainMenuSceneName", "MainMenuScene");

            EnsureSceneInBuildSettings(MainMenuScenePath);
            EnsureSceneInBuildSettings(TargetScenePath);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("LAN-сцена собрана: Assets/Scenes/LanGameScene.unity");
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
                return;

            EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes;
            for (int i = 0; i < currentScenes.Length; i++)
            {
                if (!string.Equals(currentScenes[i].path, scenePath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!currentScenes[i].enabled)
                {
                    currentScenes[i].enabled = true;
                    EditorBuildSettings.scenes = currentScenes;
                }

                return;
            }

            Array.Resize(ref currentScenes, currentScenes.Length + 1);
            currentScenes[currentScenes.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = currentScenes;
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, fieldName);

            field.SetValue(target, value);
            if (target is UnityEngine.Object unityObject)
                EditorUtility.SetDirty(unityObject);
        }
    }
}
