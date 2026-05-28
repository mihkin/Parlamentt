using ParliamentGame;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace ParliamentGame.EditorTools
{
    public static class MainMenuSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/MainMenuScene.unity";

        static MainMenuSceneBuilder()
        {
            EditorApplication.delayCall += TryRebuildOpenMainMenuSceneIfNeeded;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        [MenuItem("Parliament/Rebuild Main Menu Scene")]
        public static void RebuildMainMenuScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RebuildAndSave(scene, logResult: true);
        }

        private static void TryRebuildOpenMainMenuSceneIfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryRebuildOpenMainMenuSceneIfNeeded;
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
                return;

            MainMenuSceneBootstrap bootstrap = Object.FindObjectOfType<MainMenuSceneBootstrap>();
            if (bootstrap == null)
                return;

            bool hasCanvas = bootstrap.transform.Find("Canvas") != null;
            bool hasServices = bootstrap.transform.Find("MenuServices") != null;
            bool hasController = Object.FindObjectOfType<MainMenuController>() != null;
            if (hasCanvas && hasServices && hasController)
                return;

            RebuildAndSave(scene, logResult: true);
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene.path != ScenePath)
                return;

            TryRebuildOpenMainMenuSceneIfNeeded();
        }

        private static void RebuildAndSave(Scene scene, bool logResult)
        {
            MainMenuSceneBootstrap bootstrap = Object.FindObjectOfType<MainMenuSceneBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("MainMenuSceneBootstrap was not found in MainMenuScene.");
                return;
            }

            bootstrap.RebuildSceneLayout();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (logResult)
                Debug.Log("Main menu scene was rebuilt and saved.");
        }
    }
}
