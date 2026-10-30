using System;

namespace DSPQualitySystem
{
    /// <summary>
    /// M2 (spec 3.6/3.7/3.8): FIFO hand-item quality queue for one CreatePrebuilds batch.
    /// Player.UseHandItems hands out the per-item quality of the consumed building item;
    /// CreatePrebuilds then maps those entries to the freshly created prebuildIds in the same
    /// order they were consumed. Entries are only accepted while a batch is open, so unrelated
    /// UseHandItems calls (inserters, manual feeding, other build tools) never pollute the queue.
    /// A batch never crosses a frame; BeginBatch/EndBatch bracket every CreatePrebuilds call.
    /// </summary>
    public static class BuildQualityStash
    {
        private const int MaxEntries = 1024;

        private struct Entry
        {
            public int itemId;
            public int perItem;
        }

        private static Entry[] _entries = new Entry[32];
        private static int _count;
        private static bool _capturing;
        private static bool _warnedMismatch;

        /// <summary>True while a CreatePrebuilds batch is running.</summary>
        public static bool Capturing
        {
            get { return _capturing; }
        }

        public static void BeginBatch()
        {
            _count = 0;
            _capturing = true;
        }

        public static void EndBatch()
        {
            _capturing = false;
            _count = 0;
        }

        /// <summary>Record one consumed hand item. Called from the UseHandItems postfix.</summary>
        public static void Push(int itemId, int perItem)
        {
            if (!_capturing || itemId <= 0)
            {
                return;
            }

            if (perItem < 0)
            {
                perItem = 0;
            }
            else if (perItem > Cargo.kIncLevelMax)
            {
                perItem = Cargo.kIncLevelMax;
            }

            if (_count >= MaxEntries)
            {
                return;
            }
            if (_count == _entries.Length)
            {
                var bigger = new Entry[_entries.Length * 2];
                Array.Copy(_entries, bigger, _entries.Length);
                _entries = bigger;
            }

            _entries[_count].itemId = itemId;
            _entries[_count].perItem = perItem;
            _count++;
        }

        /// <summary>
        /// Pop the oldest entry for itemId. A mismatch means we cannot trust the mapping
        /// (spec 3.7: never give quality to the wrong building), so the rest of the batch is
        /// dropped and one diagnostic line is emitted.
        /// </summary>
        public static bool TryPopFor(int itemId, out int perItem)
        {
            perItem = 0;
            if (_count <= 0)
            {
                return false;
            }

            Entry head = _entries[0];
            if (head.itemId != itemId)
            {
                if (!_warnedMismatch)
                {
                    _warnedMismatch = true;
                    if (Plugin.Log != null)
                    {
                        Plugin.Log.LogWarning("[QSystem][build] stash item mismatch: preview=" + itemId
                            + " queueHead=" + head.itemId + "; dropping batch to avoid wrong quality");
                    }
                }
                _count = 0;
                return false;
            }

            perItem = head.perItem;
            for (int i = 0; i < _count - 1; i++)
            {
                _entries[i] = _entries[i + 1];
            }
            _count--;
            return true;
        }
    }
}
