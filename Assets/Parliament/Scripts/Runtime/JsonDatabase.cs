using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ParliamentGame
{
    // Загружает JSON-данные из StreamingAssets, чтобы карты и события можно было менять без кода.
    public static class JsonDatabase
    {
        // Читает cards.json и возвращает список карт для колоды.
        public static List<CardDefinition> LoadCards()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "cards.json");
            if (!File.Exists(path))
            {
                Debug.LogError($"Не найден файл карт: {path}");
                return new List<CardDefinition>();
            }

            CardDefinitionList list = JsonUtility.FromJson<CardDefinitionList>(File.ReadAllText(path, System.Text.Encoding.UTF8));
            return list?.cards == null ? new List<CardDefinition>() : new List<CardDefinition>(list.cards);
        }

        // Читает events.json и возвращает список случайных событий раунда.
        public static List<EventDefinition> LoadEvents()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "events.json");
            if (!File.Exists(path))
            {
                Debug.LogError($"Не найден файл событий: {path}");
                return new List<EventDefinition>();
            }

            EventDefinitionList list = JsonUtility.FromJson<EventDefinitionList>(File.ReadAllText(path, System.Text.Encoding.UTF8));
            return list?.events == null ? new List<EventDefinition>() : new List<EventDefinition>(list.events);
        }
    }
}
