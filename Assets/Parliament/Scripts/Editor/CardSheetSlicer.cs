using System.Collections.Generic;
using System.IO;
using ParliamentGame;
using UnityEditor;
using UnityEngine;

namespace ParliamentGame.EditorTools
{
    // Нарезает общий лист карт на отдельные PNG без верхней полосы с названием.
    public static class CardSheetSlicer
    {
        private const int Columns = 5;
        private const float TitleStripRatio = 0.18f;
        private const float RepairTitleStripRatio = 0.24f;
        private const float CellPaddingRatio = 0.006f;
        private const float Repair41To45BottomShiftRatio = 0.06f;
        private const string OutputFolder = "Assets/Parliament/images";
        private const string CardArtLibraryPath = "Assets/Parliament/CardArtLibrary.asset";

        private static readonly string[] AllFileNames =
        {
            "1Propaganda",
            "2Skandal",
            "3Lobbirovanie",
            "4PokupkaNeitralov",
            "5Kompromat",
            "6Agitatsiya",
            "7PerehvatElektorata",
            "8PodderzhkaSMI",
            "9YuridicheskiiShchit",
            "10ByurokraticheskayaZaderzhka",
            "11BolshayaRech",
            "12YadovityiSluh",
            "13FinansovayaBlokada",
            "14ToksichnayaPovestka",
            "15SlivByudzheta",
            "16PartiinayaDistsiplina",
            "17Miting",
            "18ShtabVolonterov",
            "19PeregovorySElitami",
            "20ProverkaSchetov",
            "21OhotaZaGolosami",
            "22AntikrizisnyiShtab",
            "23VolnaNedoveriya",
            "24PolevyeAgitatory",
            "25ListovkiUMetro",
            "26NochnayaKampaniya",
            "27MelkayaIntriga",
            "28PartiinayaKassa",
            "29SrochnayaSvodka",
            "30ChernyiPiar",
            "31FalshivyiOpros",
            "32SekretnyiShtab",
            "33OhranaShtaba",
            "34DogovorSNeitralami",
            "35PodkupChinovnika",
            "36UlichnyeProtesty",
            "37MediinyiShtorm",
            "38UtechkaDokumentov",
            "39ZakulisnayaSdelka",
            "40NarodnyiTribun",
            "41SryvDebatov",
            "42KontrolPovestki",
            "43KrizisDoveriya",
            "44PartiinyiSezd",
            "45SoyuzSPromyshlennikami",
            "46AdministrativnyiResurs",
            "47SilovoeDavlenie",
            "48BolshoiPerehvat",
            "49Antireiting",
            "50KoalitsionnyiTorg",
            "51YuristyNagotove",
            "52VneocherednoeSlushanie",
            "53ObvalReitinga",
            "54FederalnyiEfir",
            "55TenevayaBuhgalteriya",
            "56ShirokiiFront",
            "57PanikaVShtabah",
            "58StavkaVaBank",
            "59NatsionalnayaKoalitsiya",
            "60BolshayaChistka",
            "61IstoricheskayaRech",
            "62NochnoiArest",
            "63VotumNedoveriya",
            "64RazvorotElit",
            "65PoliticheskoeTsunami",
            "66TotalnayaMobilizatsiya",
            "67InformatsionnayaBlokada",
            "68NeprikosnovennyiLider"
        };

        [MenuItem("Parliament/Slice Selected Card Sheet/1-25")]
        public static void SliceSelectedCardSheet1To25()
        {
            SliceSelectedCardSheet(1, 1, 25, 5, TitleStripRatio, CellPaddingRatio, false);
        }

        [MenuItem("Parliament/Slice Selected Card Sheet/26-50")]
        public static void SliceSelectedCardSheet26To50()
        {
            SliceSelectedCardSheet(26, 26, 25, 5, TitleStripRatio, CellPaddingRatio, false);
        }

        [MenuItem("Parliament/Slice Selected Card Sheet/51-68")]
        public static void SliceSelectedCardSheet51To68()
        {
            SliceSelectedCardSheet(51, 51, 18, 4, TitleStripRatio, CellPaddingRatio, false);
        }

        [MenuItem("Parliament/Slice Selected Card Sheet/Repair 41-45")]
        public static void RepairSelectedCardSheet41To45()
        {
            SliceSelectedCardSheet(26, 41, 5, 5, TitleStripRatio, Repair41To45BottomShiftRatio, true);
        }

        [MenuItem("Parliament/Slice Selected Card Sheet/Repair 61-68")]
        public static void RepairSelectedCardSheet61To68()
        {
            SliceSelectedCardSheet(51, 61, 8, 4, RepairTitleStripRatio, CellPaddingRatio, false);
        }

        private static void SliceSelectedCardSheet(int sheetStartCardId, int firstCardId, int cardCount, int rows, float titleStripRatio, float bottomOffsetRatio, bool keepSizeWhenShiftingBottom)
        {
            string sourcePath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                Debug.LogError("Выдели исходную картинку-лист в Project и снова нажми нужный пункт Parliament > Slice Selected Card Sheet.");
                return;
            }

            Texture2D source = LoadTexture(sourcePath);
            if (source == null)
            {
                Debug.LogError($"Не удалось прочитать картинку: {sourcePath}");
                return;
            }

            Directory.CreateDirectory(OutputFolder);
            Dictionary<int, Sprite> generatedSprites = new Dictionary<int, Sprite>();

            for (int i = 0; i < cardCount; i++)
            {
                int cardId = firstCardId + i;
                int zeroBasedCell = cardId - sheetStartCardId;
                int row = zeroBasedCell / Columns;
                int col = zeroBasedCell % Columns;
                string fileName = AllFileNames[cardId - 1] + ".png";
                string outputPath = $"{OutputFolder}/{fileName}";

                Texture2D cardTexture = CropCard(source, row, col, rows, titleStripRatio, bottomOffsetRatio, keepSizeWhenShiftingBottom);
                File.WriteAllBytes(outputPath, cardTexture.EncodeToPNG());
                Object.DestroyImmediate(cardTexture);

                AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
                ConfigureAsSprite(outputPath);
                generatedSprites[cardId] = AssetDatabase.LoadAssetAtPath<Sprite>(outputPath);
            }

            UpdateCardArtLibrary(generatedSprites);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Object.DestroyImmediate(source);
            Debug.Log($"Готово: нарезано {cardCount} картинок с id {firstCardId}-{firstCardId + cardCount - 1} в {OutputFolder}.");
        }

        private static Texture2D LoadTexture(string sourcePath)
        {
            byte[] bytes = File.ReadAllBytes(sourcePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(bytes) ? texture : null;
        }

        private static Texture2D CropCard(Texture2D source, int row, int col, int rows, float titleStripRatio, float bottomOffsetRatio, bool keepSizeWhenShiftingBottom)
        {
            int cellX0 = Mathf.RoundToInt(source.width * col / (float)Columns);
            int cellX1 = Mathf.RoundToInt(source.width * (col + 1) / (float)Columns);
            int cellTop = Mathf.RoundToInt(source.height * row / (float)rows);
            int cellBottom = Mathf.RoundToInt(source.height * (row + 1) / (float)rows);

            int cellWidth = cellX1 - cellX0;
            int cellHeight = cellBottom - cellTop;
            int paddingX = Mathf.RoundToInt(cellWidth * CellPaddingRatio);
            int bottomOffset = Mathf.RoundToInt(cellHeight * bottomOffsetRatio);
            int titleStrip = Mathf.RoundToInt(cellHeight * titleStripRatio);
            int cropWidth = cellWidth - paddingX * 2;
            int cropHeight = keepSizeWhenShiftingBottom
                ? cellHeight - titleStrip - Mathf.RoundToInt(cellHeight * CellPaddingRatio)
                : cellHeight - titleStrip - bottomOffset;

            int sourceX = cellX0 + paddingX;
            int sourceY = source.height - cellBottom + bottomOffset;
            Texture2D output = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false);
            Color[] pixels = source.GetPixels(sourceX, sourceY, cropWidth, cropHeight);
            output.SetPixels(pixels);
            output.Apply();
            return output;
        }

        private static void ConfigureAsSprite(string assetPath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        private static void UpdateCardArtLibrary(Dictionary<int, Sprite> spritesByCardId)
        {
            CardArtLibrary library = AssetDatabase.LoadAssetAtPath<CardArtLibrary>(CardArtLibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CardArtLibrary>();
                AssetDatabase.CreateAsset(library, CardArtLibraryPath);
            }

            SerializedObject serializedLibrary = new SerializedObject(library);
            SerializedProperty entries = serializedLibrary.FindProperty("cardArt");

            foreach (KeyValuePair<int, Sprite> pair in spritesByCardId)
            {
                int index = FindEntryIndex(entries, pair.Key);
                if (index < 0)
                {
                    entries.arraySize++;
                    index = entries.arraySize - 1;
                }

                SerializedProperty entry = entries.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("cardId").intValue = pair.Key;
                entry.FindPropertyRelative("sprite").objectReferenceValue = pair.Value;
            }

            serializedLibrary.ApplyModifiedProperties();
            EditorUtility.SetDirty(library);
        }

        private static int FindEntryIndex(SerializedProperty entries, int cardId)
        {
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("cardId").intValue == cardId)
                    return i;
            }

            return -1;
        }
    }
}
