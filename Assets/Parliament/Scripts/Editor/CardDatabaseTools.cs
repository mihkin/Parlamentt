using UnityEditor;
using UnityEngine;

namespace ParliamentGame.EditorTools
{
    public static class CardDatabaseTools
    {
        private const string CardsJsonPath = "Assets/StreamingAssets/cards.json";

        /// <summary>
        /// Selects the runtime card JSON database in the Unity Project window.
        /// </summary>
        [MenuItem("Parliament/Select Card Database JSON")]
        public static void SelectCardDatabaseJson()
        {
            TextAsset cardsJson = AssetDatabase.LoadAssetAtPath<TextAsset>(CardsJsonPath);
            if (cardsJson == null)
            {
                Debug.LogError($"Card database JSON not found: {CardsJsonPath}");
                return;
            }

            Selection.activeObject = cardsJson;
            EditorGUIUtility.PingObject(cardsJson);
            Debug.Log($"Card database selected: {CardsJsonPath}");
        }
    }
}
