using System;
using System.Text;

namespace ParliamentGame
{
    public static class RoomCodeSystem
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly Random Random = new Random();

        /// <summary>
        /// Генерирует короткий код комнаты для подключения клиентов.
        /// </summary>
        public static string GenerateRoomCode(int length = 6)
        {
            StringBuilder builder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                builder.Append(Alphabet[Random.Next(0, Alphabet.Length)]);

            return builder.ToString();
        }

        /// <summary>
        /// Нормализует код комнаты к безопасному каноническому виду.
        /// </summary>
        public static string Normalize(string roomCode)
        {
            return string.IsNullOrWhiteSpace(roomCode) ? string.Empty : roomCode.Trim().ToUpperInvariant();
        }
    }
}
