using System;
using System.Collections.Generic;

namespace ParliamentGame
{
    public class ActionLog
    {
        public event Action Changed;

        private readonly List<string> entries = new List<string>();
        private readonly int maxEntries;

        public IReadOnlyList<string> Entries => entries;

        public ActionLog(int maxEntries = 20)
        {
            this.maxEntries = maxEntries;
        }

        public void Add(string message)
        {
            entries.Add(message);

            while (entries.Count > maxEntries)
                entries.RemoveAt(0);

            Changed?.Invoke();
        }

        public void Clear()
        {
            entries.Clear();
            Changed?.Invoke();
        }
    }
}
