using System;
using System.Collections;
using System.Collections.Generic;

namespace ItemBlacklist
{
    public sealed class WeakStrongDictionary<TKey, TValue> :
    IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : class
    {
        private const float LoadFactor = 0.9f;
        private const int MinCapacity = 16;

        private struct Entry
        {
            public int Hash;
            public int Next;
            public WeakReference Key;
            public TValue Value;
        }

        private int[] _buckets;
        private Entry[] _entries;
        private int _count;
        private int _freeList;
        private int _liveCount;

        private readonly IEqualityComparer<TKey> _comparer;
        private readonly bool _isUnityObject;

        public WeakStrongDictionary(int capacity = MinCapacity,
            IEqualityComparer<TKey> comparer = null)
        {
            capacity = Math.Max(capacity, MinCapacity);
            capacity = GetPrime(capacity);

            _buckets = new int[capacity];
            _entries = new Entry[capacity];
            _freeList = -1;
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            _isUnityObject = typeof(UnityEngine.Object).IsAssignableFrom(typeof(TKey));
        }

        public int Count => _liveCount;
        public int Capacity => _entries.Length;

        public bool ContainsKey(TKey key)
            => TryGetValue(key, out _);

        public TValue this[TKey key]
        {
            get
            {
                if (TryGetValue(key, out var value))
                    return value;
                throw new KeyNotFoundException();
            }
            set => AddOrUpdate(key, value);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            value = default;
            if (key == null) return false;

            int hash = GetHash(key);
            int bucket = hash % _buckets.Length;

            int i = _buckets[bucket] - 1;
            int prev = -1;

            while (i >= 0)
            {
                Entry e = _entries[i];

                if (!TryGetAliveKey(e, out TKey aliveKey))
                {
                    RemoveEntry(bucket, i, prev);
                    i = (prev < 0) ? _buckets[bucket] - 1 : _entries[prev].Next;
                    continue;
                }

                if (e.Hash == hash && _comparer.Equals(aliveKey, key))
                {
                    value = e.Value;
                    return true;
                }

                prev = i;
                i = e.Next;
            }

            return false;
        }

        public void AddOrUpdate(TKey key, TValue value)
        {
            if (key == null)
                throw new ArgumentNullException(nameof(key));

            int hash = GetHash(key);

            if (FindEntry(hash, key, out int bucket, out int index, out _))
            {
                _entries[index].Value = value;
                return;
            }

            if (_count == _entries.Length ||
                _liveCount >= _entries.Length * LoadFactor)
            {
                Resize(_entries.Length * 2);
                bucket = hash % _buckets.Length;
            }

            int entryIndex = AllocateEntry();

            _entries[entryIndex].Hash = hash;
            _entries[entryIndex].Key = new WeakReference(key);
            _entries[entryIndex].Value = value;
            _entries[entryIndex].Next = _buckets[bucket] - 1;
            _buckets[bucket] = entryIndex + 1;

            _liveCount++;
        }

        public bool TryRemove(TKey key, out TValue value)
        {
            value = default;
            if (key == null) return false;

            int hash = GetHash(key);
            int bucket = hash % _buckets.Length;

            int i = _buckets[bucket] - 1;
            int prev = -1;

            while (i >= 0)
            {
                Entry e = _entries[i];

                if (!TryGetAliveKey(e, out TKey aliveKey))
                {
                    RemoveEntry(bucket, i, prev);
                    i = (prev < 0) ? _buckets[bucket] - 1 : _entries[prev].Next;
                    continue;
                }

                if (e.Hash == hash && _comparer.Equals(aliveKey, key))
                {
                    value = e.Value;
                    RemoveEntry(bucket, i, prev);
                    return true;
                }

                prev = i;
                i = e.Next;
            }

            return false;
        }

        public int CleanDeadKeys()
        {
            int cleaned = 0;

            for (int bucket = 0; bucket < _buckets.Length; bucket++)
            {
                int prev = -1;
                int i = _buckets[bucket] - 1;

                while (i >= 0)
                {
                    Entry e = _entries[i];

                    if (TryGetAliveKey(e, out _))
                    {
                        prev = i;
                        i = e.Next;
                        continue;
                    }

                    RemoveEntry(bucket, i, prev);
                    cleaned++;

                    i = (prev < 0) ? _buckets[bucket] - 1 : _entries[prev].Next;
                }
            }

            return cleaned;
        }

        public void EnsureCapacity(int capacity)
        {
            if (capacity <= _liveCount) return;

            int required = (int)(capacity / LoadFactor);
            if (required > _entries.Length)
                Resize(required);
        }

        public void TrimExcess()
        {
            if (_liveCount == 0)
            {
                Clear();
                return;
            }

            int target = GetPrime((int)(_liveCount / LoadFactor));
            target = Math.Max(target, MinCapacity);

            if (target < _entries.Length)
                Resize(target);
        }

        public void Clear()
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            Array.Clear(_entries, 0, _entries.Length);
            _count = 0;
            _liveCount = 0;
            _freeList = -1;
        }

        private void Resize(int newSize)
        {
            newSize = GetPrime(Math.Max(newSize, _liveCount * 2));

            int[] newBuckets = new int[newSize];
            Entry[] newEntries = new Entry[newSize];

            int newCount = 0;
            int newLiveCount = 0;

            for (int i = 0; i < _count; i++)
            {
                Entry e = _entries[i];
                if (!TryGetAliveKey(e, out _))
                    continue;

                int bucket = e.Hash % newSize;
                e.Next = newBuckets[bucket] - 1;
                newBuckets[bucket] = newCount + 1;
                newEntries[newCount++] = e;
                newLiveCount++;
            }

            _buckets = newBuckets;
            _entries = newEntries;
            _count = newCount;
            _liveCount = newLiveCount;
            _freeList = -1;
        }

        private int AllocateEntry()
        {
            if (_freeList >= 0)
            {
                int i = _freeList;
                _freeList = _entries[i].Next;
                return i;
            }
            return _count++;
        }

        private void RemoveEntry(int bucket, int index, int prev)
        {
            if (prev < 0)
                _buckets[bucket] = _entries[index].Next + 1;
            else
                _entries[prev].Next = _entries[index].Next;

            _entries[index].Key = null;
            _entries[index].Value = default;
            _entries[index].Next = _freeList;
            _freeList = index;
            _liveCount--;
        }

        private bool TryGetAliveKey(Entry e, out TKey key)
        {
            key = null;
            var wr = e.Key;
            if (wr == null) return false;

            var obj = wr.Target;
            if (obj == null) return false;

            if (_isUnityObject)
            {
                var unityObj = obj as UnityEngine.Object;
                if (IsUnityObjectDestroyed(unityObj))
                    return false;
            }

            key = (TKey)obj;
            return true;
        }

        private static bool IsUnityObjectDestroyed(UnityEngine.Object obj)
        {
            return obj == null;
        }

        private bool FindEntry(int hash, TKey key,
            out int bucket, out int index, out int prev)
        {
            bucket = hash % _buckets.Length;
            index = _buckets[bucket] - 1;
            prev = -1;

            while (index >= 0)
            {
                Entry e = _entries[index];

                if (!TryGetAliveKey(e, out TKey aliveKey))
                {
                    RemoveEntry(bucket, index, prev);
                    index = (prev < 0) ? _buckets[bucket] - 1 : _entries[prev].Next;
                    continue;
                }

                if (e.Hash == hash && _comparer.Equals(aliveKey, key))
                    return true;

                prev = index;
                index = e.Next;
            }

            return false;
        }

        private int GetHash(TKey key)
            => _comparer.GetHashCode(key) & 0x7FFFFFFF;

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly Entry[] _entries;
            private readonly int _end;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;

            internal Enumerator(WeakStrongDictionary<TKey, TValue> dict)
            {
                _entries = dict._entries;
                _end = dict._count;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                while (++_index < _end)
                {
                    Entry e = _entries[_index];
                    var wr = e.Key;
                    if (wr == null) continue;

                    var obj = wr.Target;
                    if (obj == null) continue;

                    if (obj is UnityEngine.Object unityObj && unityObj == null)
                        continue;

                    _current = new KeyValuePair<TKey, TValue>((TKey)obj, e.Value);
                    return true;
                }
                return false;
            }

            public KeyValuePair<TKey, TValue> Current => _current;
            object IEnumerator.Current => _current;
            public void Dispose() { }
            public void Reset() => _index = -1;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IEnumerable<TKey> Keys
        {
            get
            {
                for (int i = 0; i < _count; i++)
                {
                    if (TryGetAliveKey(_entries[i], out TKey key))
                        yield return key;
                }
            }
        }

        public IEnumerable<TValue> Values
        {
            get
            {
                for (int i = 0; i < _count; i++)
                {
                    if (TryGetAliveKey(_entries[i], out _))
                        yield return _entries[i].Value;
                }
            }
        }

        public int GetTotalEntryCount() => _count;
        public int GetDeadKeyCount() => _count - _liveCount - GetFreeSlotCount();
        public int GetFreeSlotCount()
        {
            int freeCount = 0;
            int index = _freeList;
            while (index >= 0)
            {
                freeCount++;
                index = _entries[index].Next;
            }
            return freeCount;
        }

        public float GetLoadFactor() => (float)_liveCount / _entries.Length;

        private static readonly int[] Primes =
        {
        3,7,11,17,23,29,37,47,59,71,89,107,131,163,197,239,293,
        353,431,521,631,761,919,1103,1327,1597,1931,2333,2801,
        3371,4049,4861,5839,7013,8419,10103,12143,14591,17519,
        21023,25229,30293,36353,43627,52361,62851,75431,90523,
        108631,130363,156437,187751,225307,270371,324449,389357,
        467237,560689,672827,807403,968897
    };

        private static int GetPrime(int min)
        {
            foreach (int p in Primes)
                if (p >= min) return p;

            for (int i = (min | 1); i < int.MaxValue; i += 2)
                if (IsPrime(i)) return i;

            return min;
        }

        private static bool IsPrime(int v)
        {
            if ((v & 1) == 0) return v == 2;
            int limit = (int)Math.Sqrt(v);
            for (int i = 3; i <= limit; i += 2)
                if (v % i == 0) return false;
            return true;
        }
    }
}