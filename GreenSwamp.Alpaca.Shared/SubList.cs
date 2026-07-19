using GreenSwamp.Alpaca.Shared;
using System;
using System.Collections;
using System.Collections.Generic;

namespace GreenSwamp.Alpaca.Shared
{
    /// <summary>
    /// A read-only list that represents a sublist of a backing list, starting from 
    /// a specified index. The sublist reflects changes made to the backing list, 
    /// such as adding or removing elements, while maintaining the specified start index.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class SubList<T> : IReadOnlyList<T>
        where T : class
    {
        private readonly List<T> _source;
        private int _startIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="SubList{T}"/> class with the 
        /// specified backing list and start index.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="startIndex"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public SubList(List<T> source, int startIndex = 0)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));

            if (_source.Count == 0)
            {
                if (startIndex != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(startIndex));
                }

                _startIndex = 0;
                return;
            }

            SetStartIndex(startIndex);
        }

        /// <summary>
        /// Gets the current start index of the sublist within the backing list.
        /// </summary>
        public int StartIndex
        {
            get
            {
                EnsureStartIndexValid();
                return _startIndex;
            }
        }

        /// <summary>
        /// Gets the number of elements in the sublist.
        /// </summary>
        public int Count
        {
            get
            {
                EnsureStartIndexValid();

                if (_source.Count == 0)
                {
                    return 0;
                }

                return _source.Count - _startIndex;
            }
        }

        /// <summary>
        /// Gets the element at the specified index in the sublist. The index is relative to the start index of the sublist.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get.</param>
        /// <returns>The element at the specified index.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the index is out of range.</exception>
        public T this[int index]
        {
            get
            {
                EnsureStartIndexValid();

                if (index < 0 || index >= Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _source[_startIndex + index];
            }
        }

        /// <summary>
        /// Sets the start index of the sublist within the backing list. The start index must be within the bounds of the backing list.
        /// </summary>
        /// <param name="startIndex">The zero-based index to set as the start of the sublist.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the start index is out of range.</exception>
        public void SetStartIndex(int startIndex)
        {
            if (_source.Count == 0)
            {
                if (startIndex != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(startIndex));
                }

                _startIndex = 0;
                return;
            }

            if (startIndex < 0 || startIndex >= _source.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            _startIndex = startIndex;
        }

        /// <summary>
        /// Sets the start index of the sublist to the index of the specified item reference in the backing list.
        /// </summary>
        /// <param name="startItem">The item reference to set as the start of the sublist.</param>
        /// <exception cref="InvalidOperationException">Thrown when the specified start item reference is not found in the backing list.</exception>
        public void SetStartReference(T startItem)
        {
            ArgumentNullException.ThrowIfNull(startItem);

            for (var index = 0; index < _source.Count; index++)
            {
                if (ReferenceEquals(_source[index], startItem))
                {
                    _startIndex = index;
                    return;
                }
            }

            throw new InvalidOperationException("The specified start item reference was not found in the backing list.");
        }

        /// <summary>
        /// Sets the start index of the sublist to the index of the first element in the backing list that matches the specified predicate.
        /// </summary>
        /// <param name="predicate">The predicate to match elements in the backing list.</param>
        /// <exception cref="InvalidOperationException">Thrown when no backing-list element matches the specified predicate.</exception>
        public void SetStart(Predicate<T> predicate)
        {
            ArgumentNullException.ThrowIfNull(predicate);

            for (var index = 0; index < _source.Count; index++)
            {
                if (predicate(_source[index]))
                {
                    _startIndex = index;
                    return;
                }
            }

            throw new InvalidOperationException("No backing-list element matched the specified start predicate.");
        }
        
        /// <summary>
        /// Adds an item to the end of the backing list.
        /// </summary>
        /// <param name="item">The item to add to the end of the backing list.</param>
        /// <exception cref="ArgumentNullException">Thrown when the item is null.</exception>
        public void AddAtEnd(T item)
        {
            ArgumentNullException.ThrowIfNull(item);

            var wasEmpty = _source.Count == 0;

            _source.Add(item);

            if (!wasEmpty)
            {
                _startIndex++;
            }

            EnsureStartIndexValid();
        }

        /// <summary>
        /// Removes the first item from the backing list. If the backing list is empty, this method does nothing.
        /// </summary>
        public void RemoveFromStart()
        {
            RemoveFromStart(1);
        }
        
        /// <summary>
        /// Removes the specified number of items from the start of the backing list. If the backing list has fewer items than the specified count, all items are removed.
        /// </summary>
        /// <param name="count">The number of items to remove from the start of the backing list.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative or greater than the number of items in the backing list.</exception>
        public void RemoveFromStart(int count)
        {
            if (count < 0 || count > _source.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0)
            {
                return;
            }

            _startIndex += count;
            EnsureStartIndexValid();
        }

        /// <summary>
        /// Notifies the sublist that an item has been inserted at the end of the backing list. 
        /// This method adjusts the start index of the sublist accordingly.
        /// </summary>
        public void NotifyInsertedAtEnd ()
        {
            NotifyInsertedAtEnd(1);
        }

        /// <summary>
        /// Notifies the sublist that a specified number of items have been inserted at the end of the backing list. This method adjusts the start index of the sublist accordingly.
        /// </summary>
        /// <param name="count">The number of items inserted at the end of the backing list.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is negative.</exception>
        public void NotifyInsertedAtEnd(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0 || _source.Count == 0)
            {
                EnsureStartIndexValid();
                return;
            }

            _startIndex -= count;
            EnsureStartIndexValid();
        }

        /// <summary>
        /// Notifies the sublist that the first item has been removed from the backing list. This method adjusts 
        /// the start index of the sublist accordingly.
        /// </summary>
        /// <param name="count">The number of items removed from the start of the backing list.</param> 
        public void NotifyRemovedFromStart(int count = 0)
        {
            RemoveFromStart(count);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the sublist, starting from the current start index of the backing list.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the sublist.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            EnsureStartIndexValid();

            for (var index = _startIndex; index < _source.Count; index++)
            {
                yield return _source[index];
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through the sublist, starting from the current start index of the backing list.
        /// </summary>
        /// <returns>An enumerator that can be used to iterate through the sublist.</returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// Ensures that the start index is valid with respect to the current state of the backing list. 
        /// If the backing list is empty, the start index is set to 0. If the start index is less than 0, it is set to 0. 
        /// If the start index is greater than or equal to the count of the backing list, it is set to the last valid index.
        /// </summary>
        private void EnsureStartIndexValid()
        {
            if (_source.Count == 0)
            {
                _startIndex = 0;
                return;
            }

            if (_startIndex < 0)
            {
                _startIndex = 0;
                return;
            }

            if (_startIndex >= _source.Count)
            {
                _startIndex = _source.Count - 1;
            }
        }
    }
}