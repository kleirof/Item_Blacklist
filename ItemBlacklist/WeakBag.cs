using System;
using System.Collections;
using System.Collections.Generic;

namespace ItemBlacklist
{
    public sealed class WeakBag<T> : IEnumerable<T>
    where T : class
    {
        private WeakReference[] _items;
        private int _count;
        private readonly bool _isUnityObject;

        public WeakBag(int initialCapacity = 4)
        {
            if (initialCapacity < 1) initialCapacity = 1;
            _items = new WeakReference[initialCapacity];
            _isUnityObject = typeof(UnityEngine.Object).IsAssignableFrom(typeof(T));
        }

        public int Capacity => _items.Length;

        public int Count
        {
            get
            {
                int alive = 0;
                for (int i = 0; i < _count; i++)
                {
                    if (_items[i] != null && IsAlive(_items[i]))
                        alive++;
                }
                return alive;
            }
        }

        public bool Add(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            EnsureCapacity(_count + 1);

            for (int i = 0; i < _count; i++)
            {
                if (_items[i] == null) continue;

                var target = _items[i].Target;
                if (target != null && IsAlive(_items[i]) &&
                    ReferenceEquals(target, item))
                    return false;
            }

            int index = FindFreeSlot();
            _items[index] = new WeakReference(item);
            if (index >= _count) _count = index + 1;

            return true;
        }

        public int AddRange(IEnumerable<T> items)
        {
            int added = 0;
            foreach (var item in items)
            {
                if (Add(item)) added++;
            }
            return added;
        }

        public int CleanDeadItems()
        {
            int cleaned = 0;
            int newCount = 0;

            for (int i = 0; i < _count; i++)
            {
                if (_items[i] == null || !IsAlive(_items[i]))
                {
                    _items[i] = null;
                    cleaned++;
                    continue;
                }

                if (newCount != i)
                {
                    _items[newCount] = _items[i];
                    _items[i] = null;
                }
                newCount++;
            }

            _count = newCount;
            return cleaned;
        }

        public bool NeedsCleanup()
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i] != null && !IsAlive(_items[i]))
                    return true;
            }
            return false;
        }

        public T[] ToArray()
        {
            CleanDeadItems();
            var result = new T[_count];
            for (int i = 0; i < _count; i++)
            {
                result[i] = (T)_items[i].Target;
            }
            return result;
        }

        public List<T> ToList()
        {
            CleanDeadItems();
            var list = new List<T>(_count);
            for (int i = 0; i < _count; i++)
            {
                list.Add((T)_items[i].Target);
            }
            return list;
        }

        public bool Contains(T item)
        {
            if (item == null) return false;

            for (int i = 0; i < _count; i++)
            {
                if (_items[i] == null) continue;

                var target = _items[i].Target;
                if (target != null && IsAlive(_items[i]) &&
                    ReferenceEquals(target, item))
                    return true;
            }
            return false;
        }

        public bool Remove(T item)
        {
            if (item == null) return false;

            for (int i = 0; i < _count; i++)
            {
                if (_items[i] == null) continue;

                var target = _items[i].Target;
                if (target != null && IsAlive(_items[i]) &&
                    ReferenceEquals(target, item))
                {
                    _items[i] = null;
                    return true;
                }
            }
            return false;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _count);
            _count = 0;
        }

        public struct Enumerator : IEnumerator<T>
        {
            private readonly WeakBag<T> _bag;
            private int _index;
            private T _current;

            internal Enumerator(WeakBag<T> bag)
            {
                _bag = bag;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                while (++_index < _bag._count)
                {
                    if (_bag._items[_index] != null && _bag.IsAlive(_bag._items[_index]))
                    {
                        _current = (T)_bag._items[_index].Target;
                        return true;
                    }
                }
                return false;
            }

            public T Current => _current;
            object IEnumerator.Current => _current;
            public void Dispose() { }
            public void Reset() => _index = -1;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private void EnsureCapacity(int min)
        {
            if (_items.Length >= min) return;

            int newCapacity = Math.Max(_items.Length * 2, min);
            Array.Resize(ref _items, newCapacity);
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _count; i++)
            {
                if (_items[i] == null)
                    return i;
            }

            return _count;
        }

        private bool IsAlive(WeakReference wr)
        {
            var target = wr.Target;
            if (target == null) return false;

            if (_isUnityObject)
            {
                var unityObj = target as UnityEngine.Object;
                return unityObj != null;
            }

            return true;
        }

        public int GetTotalSlots() => _count;
        public int GetAliveCount() => Count;
        public int GetDeadCount()
        {
            int dead = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_items[i] != null && !IsAlive(_items[i]))
                    dead++;
            }
            return dead;
        }

        public int GetFreeSlotCount()
        {
            int free = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_items[i] == null) free++;
            }
            return free;
        }

        public float GetLoadFactor() => (float)GetAliveCount() / _items.Length;
    }
}