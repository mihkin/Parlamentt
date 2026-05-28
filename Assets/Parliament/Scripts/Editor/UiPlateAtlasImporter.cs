using UnityEditor;
using UnityEngine;

namespace ParliamentGame.EditorTools
{
    public static class UiPlateAtlasImporter
    {
        private const string AtlasAssetPath = "Assets/Resources/UI/ui_plate_atlas.png";

        [MenuItem("Parliament/UI/Select Plate Atlas")]
        private static void SelectPlateAtlas()
        {
            Object atlas = AssetDatabase.LoadMainAssetAtPath(AtlasAssetPath);
            if (atlas == null)
            {
                Debug.LogWarning("UI atlas not found at Assets/Resources/UI/ui_plate_atlas.png.");
                return;
            }

            Selection.activeObject = atlas;
            EditorGUIUtility.PingObject(atlas);
            Debug.Log("UI plate atlas selected. Sprite slicing is stored in ui_plate_atlas.png.meta; automatic re-slicing is disabled to avoid Unity importer crashes.");
        }
    }
}
