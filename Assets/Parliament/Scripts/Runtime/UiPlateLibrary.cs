using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParliamentGame
{
    public static class UiPlateLibrary
    {
        private const string AtlasResourcePath = "UI/ui_plate_atlas";

        private static Dictionary<string, Sprite> spritesByName;
        private static Dictionary<string, string> aliasMap;

        public static Sprite Get(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return null;

            EnsureLoaded();
            if (!spritesByName.TryGetValue(spriteName, out Sprite sprite) && TryResolveAlias(spriteName, out string aliasName))
                spritesByName.TryGetValue(aliasName, out sprite);
            return sprite;
        }

        private static void EnsureLoaded()
        {
            if (spritesByName != null)
                return;

            spritesByName = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "button_text_plate", "button_frame_plate" },
                { "profile_text_plate", "input_plate" },
                { "collection_text_plate", "title_plate" },
                { "lobby_profile_plate", "light_panel_plate" },
                { "connection_text_plate", "input_plate" },
                { "participant_panel_plate", "light_panel_plate" },
                { "participant_text_plate", "input_plate" }
            };
            Sprite[] sprites = Resources.LoadAll<Sprite>(AtlasResourcePath);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null || spritesByName.ContainsKey(sprite.name))
                    continue;

                spritesByName.Add(sprite.name, sprite);
            }
        }

        public static Sprite Get(string spriteName, string fallbackSpriteName)
        {
            Sprite sprite = Get(spriteName);
            return sprite != null ? sprite : Get(fallbackSpriteName);
        }

        private static bool TryResolveAlias(string spriteName, out string resolvedName)
        {
            resolvedName = spriteName;
            return aliasMap != null && aliasMap.TryGetValue(spriteName, out resolvedName);
        }
    }
}
