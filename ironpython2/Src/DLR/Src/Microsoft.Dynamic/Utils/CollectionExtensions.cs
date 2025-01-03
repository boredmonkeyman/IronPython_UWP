﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace AnyPrefix.Microsoft.Scripting.Utils {
    internal static class CollectionExtensions {
        /// <summary>
        /// Wraps the provided enumerable into a ReadOnlyCollection{T}
        /// 
        /// Copies all of the data into a new array, so the data can't be
        /// changed after creation. The exception is if the enumerable is
        /// already a ReadOnlyCollection{T}, in which case we just return it.
        /// </summary>
        internal static ReadOnlyCollection<T> ToReadOnly<T>(this IEnumerable<T> enumerable) {
            if (enumerable == null) {
                return EmptyReadOnlyCollection<T>.Instance;
            }

            if (enumerable is ReadOnlyCollection<T> roCollection) {
                return roCollection;
            }

            if (enumerable is ICollection<T> collection) {
                int count = collection.Count;
                if (count == 0) {
                    return EmptyReadOnlyCollection<T>.Instance;
                }

                T[] array = new T[count];
                collection.CopyTo(array, 0);
                return new ReadOnlyCollection<T>(array);
            }

            // ToArray trims the excess space and speeds up access
            return new ReadOnlyCollection<T>(new List<T>(enumerable).ToArray());
        }

        // We could probably improve the hashing here
        internal static int ListHashCode<T>(this IEnumerable<T> list) {
            var cmp = EqualityComparer<T>.Default;
            int h = 6551;
            foreach (T t in list) {
                h ^= (h << 5) ^ cmp.GetHashCode(t);
            }
            return h;
        }

        internal static bool ListEquals<T>(this ICollection<T> first, ICollection<T> second) {
            if (first.Count != second.Count) {
                return false;
            }

            var cmp = EqualityComparer<T>.Default;

            using (var f = first.GetEnumerator()) {
                using (var s = second.GetEnumerator()) {
                    while (f.MoveNext()) {
                        s.MoveNext();

                        if (!cmp.Equals(f.Current, s.Current)) {
                            return false;
                        }
                    }

                    return true;
                }
            }
        }

        // Name needs to be different so it doesn't conflict with Enumerable.Select
        internal static U[] Map<T, U>(this ICollection<T> collection, Func<T, U> select) {
            int count = collection.Count;
            U[] result = new U[count];
            count = 0;
            foreach (T t in collection) {
                result[count++] = select(t);
            }
            return result;
        }

        internal static T[] RemoveFirst<T>(this T[] array) {
            T[] result = new T[array.Length - 1];
            Array.Copy(array, 1, result, 0, result.Length);
            return result;
        }

        internal static T[] RemoveLast<T>(this T[] array) {
            T[] result = new T[array.Length - 1];
            Array.Copy(array, 0, result, 0, result.Length);
            return result;
        }

        internal static T[] AddFirst<T>(this IList<T> list, T item) {
            T[] res = new T[list.Count + 1];
            res[0] = item;
            list.CopyTo(res, 1);
            return res;
        }

        internal static T[] AddLast<T>(this IList<T> list, T item) {
            T[] res = new T[list.Count + 1];
            list.CopyTo(res, 0);
            res[list.Count] = item;
            return res;
        }

        internal static T[] RemoveAt<T>(this T[] array, int indexToRemove) {
            Debug.Assert(array != null);
            Debug.Assert(indexToRemove >= 0 && indexToRemove < array.Length);

            T[] result = new T[array.Length - 1];
            if (indexToRemove > 0) {
                Array.Copy(array, 0, result, 0, indexToRemove);
            }
            int remaining = array.Length - indexToRemove - 1;
            if (remaining > 0) {
                Array.Copy(array, array.Length - remaining, result, result.Length - remaining, remaining);
            }
            return result;
        }

        internal static T[] RotateRight<T>(this T[] array, int count) {
            Debug.Assert(count >= 0 && count <= array.Length);

            T[] result = new T[array.Length];
            // The head of the array is shifted, and the tail will be rotated to the head of the resulting array
            int sizeOfShiftedArray = array.Length - count;
            Array.Copy(array, 0, result, count, sizeOfShiftedArray);
            Array.Copy(array, sizeOfShiftedArray, result, 0, count);
            return result;
        }
    }


    internal static class EmptyReadOnlyCollection<T> {
        internal static readonly ReadOnlyCollection<T> Instance = new ReadOnlyCollection<T>(new T[0]);
    }

    internal static class EmptyReadOnlyDictionary<TKey, TValue> {
        internal static readonly IMyReadOnlyDictionary<TKey, TValue> Instance
#if NETCOREAPP
            = System.Collections.Immutable.ImmutableDictionary.Create<TKey, TValue>();
#else
            = new MyReadOnlyDictionary<TKey, TValue>(new MyDictionary<TKey, TValue>());
#endif
    }

    internal static class EmptyArray<T> {
        internal static readonly T[] Instance = new T[0];
    }
}
