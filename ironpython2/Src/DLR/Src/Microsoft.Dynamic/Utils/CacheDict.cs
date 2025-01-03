﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;

namespace AnyPrefix.Microsoft.Scripting.Utils {
    /// <summary>
    /// Provides a dictionary-like object used for caches which holds onto a maximum
    /// number of elements specified at construction time.
    /// 
    /// This class is not thread safe.
    /// </summary>
    public class CacheDict<TKey, TValue> {
        private readonly MyDictionary<TKey, KeyInfo> _dict = new MyDictionary<TKey, KeyInfo>();
        private readonly LinkedList<TKey> _list = new LinkedList<TKey>();
        private readonly int _maxSize;

        /// <summary>
        /// Creates a dictionary-like object used for caches.
        /// </summary>
        /// <param name="maxSize">The maximum number of elements to store.</param>
        public CacheDict(int maxSize) {
            _maxSize = maxSize;
        }

        /// <summary>
        /// Tries to get the value associated with 'key', returning true if it's found and
        /// false if it's not present.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value) {
            if (_dict.TryGetValue(key, out KeyInfo storedValue)) {
                LinkedListNode<TKey> node = storedValue.List;
                if (node.Previous != null) {
                    // move us to the head of the list...
                    _list.Remove(node);
                    _list.AddFirst(node);
                }

                value = storedValue.Value;
                return true;
            }

            value = default(TValue);
            return false;
        }

        /// <summary>
        /// Adds a new element to the cache, replacing and moving it to the front if the
        /// element is already present.
        /// </summary>
        public void Add(TKey key, TValue value) {
            if (_dict.TryGetValue(key, out KeyInfo keyInfo)) {
                // remove original entry from the linked list
                _list.Remove(keyInfo.List);
            } else if (_list.Count == _maxSize) {
                // we've reached capacity, remove the last used element...
                LinkedListNode<TKey> node = _list.Last;
                _list.RemoveLast();
                bool res = _dict.Remove(node.Value);
                Debug.Assert(res);
            }
            
            // add the new entry to the head of the list and into the dictionary
            LinkedListNode<TKey> listNode = new LinkedListNode<TKey>(key);
            _list.AddFirst(listNode);
            _dict[key] = new CacheDict<TKey, TValue>.KeyInfo(value, listNode);
        }

        /// <summary>
        /// Returns the value associated with the given key, or throws KeyNotFoundException
        /// if the key is not present.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1065:DoNotRaiseExceptionsInUnexpectedLocations")]
        public TValue this[TKey key] {
            get {
                if (TryGetValue(key, out TValue res)) {
                    return res;
                }
                throw new KeyNotFoundException();
            }
            set {
                Add(key, value);
            }
        }

        private struct KeyInfo {
            internal readonly TValue Value;
            internal readonly LinkedListNode<TKey> List;

            internal KeyInfo(TValue value, LinkedListNode<TKey> list) {
                Value = value;
                List = list;
            }
        }
    }
}
