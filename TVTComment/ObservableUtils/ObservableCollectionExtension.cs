using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace ObservableUtils
{
    static class ObservableCollectionExtension
    {
        public static Collection<T> AddRange<T>(this Collection<T> collection, IEnumerable<T> items)
        {
            ArgumentNullException.ThrowIfNull(collection);
            ArgumentNullException.ThrowIfNull(items);
            foreach (var item in items)
                collection.Add(item);
            return collection;
        }

        public static DisposableReadOnlyObservableCollection<TResult> MakeOneWayLinkedCollection<TSource, TCollection, TResult>(this TCollection source, Func<TSource, TResult> selector)
            where TCollection : INotifyCollectionChanged, IEnumerable<TSource>
        {
            var ret = new DisposableObservableCollection<TResult>(source.Select(selector));
            ret.AddDisposable(source.ObserveCollectionChanged<TCollection, TSource>((item, index) =>
             {
                 ret.Insert(index, selector(item));
             }, (item, index) =>
              {
                  ret.RemoveAt(index);
              }, () =>
              {
                  ret.Clear();
                  ret.AddRange(source.Select(selector));
              }));

            return new DisposableReadOnlyObservableCollection<TResult>(ret);
        }

        public static DisposableReadOnlyObservableCollection<TResult> MakeOneWayLinkedCollection<TSource, TResult>(this ObservableCollection<TSource> source, Func<TSource, TResult> selector)
        {
            return MakeOneWayLinkedCollection<TSource, ObservableCollection<TSource>, TResult>(source, selector);
        }
        public static DisposableReadOnlyObservableCollection<TResult> MakeOneWayLinkedCollection<TSource, TResult>(this ReadOnlyObservableCollection<TSource> source, Func<TSource, TResult> selector)
        {
            return MakeOneWayLinkedCollection<TSource, ReadOnlyObservableCollection<TSource>, TResult>(source, selector);
        }

        public static DisposableReadOnlyObservableCollection<TResult> MakeOneWayLinkedCollectionMany<TSource, TCollection, TResult>(this TCollection source, Func<TSource, IEnumerable<TResult>> selector)
            where TCollection : INotifyCollectionChanged, IEnumerable<TSource>
        {
            List<int> counts = new List<int>();
            var ret = new DisposableObservableCollection<TResult>(source.Select(selector).SelectMany(x => { counts.Add(x?.Count() ?? 0); return x ?? Array.Empty<TResult>(); }));
            ret.AddDisposable(source.ObserveCollectionChanged<TCollection, TSource>((item, index) =>
            {
                int idx = counts.Take(index).Sum();
                int lastIdx = idx;
                foreach (var newItem in selector(item) ?? Array.Empty<TResult>())
                    ret.Insert(lastIdx++, newItem);
                counts.Insert(index, lastIdx - idx);
            }, (item, index) =>
            {
                int idx = counts.Take(index).Sum();
                int lastIdx = idx + counts[index];
                for (; idx < lastIdx; idx++)
                    ret.RemoveAt(idx);
                counts.RemoveAt(index);
            }, () =>
            {
                counts.Clear();
                ret.Clear();
                ret.AddRange(source.Select(selector).SelectMany(x => { counts.Add(x?.Count() ?? 0); return x ?? Array.Empty<TResult>(); }));
            }));

            return new DisposableReadOnlyObservableCollection<TResult>(ret);
        }

        public static DisposableReadOnlyObservableCollection<TResult> MakeOneWayLinkedCollectionMany<TSource, TResult>(this ObservableCollection<TSource> source, Func<TSource, IEnumerable<TResult>> selector)
        {
            return MakeOneWayLinkedCollectionMany<TSource, ObservableCollection<TSource>, TResult>(source, selector);
        }
        public static DisposableReadOnlyObservableCollection<TResult> MakeOneWayLinkedCollectionMany<TSource, TResult>(this ReadOnlyObservableCollection<TSource> source, Func<TSource, IEnumerable<TResult>> selector)
        {
            return MakeOneWayLinkedCollectionMany<TSource, ReadOnlyObservableCollection<TSource>, TResult>(source, selector);
        }

        public static DisposableReadOnlyObservableCollection<TSource> ObservableConcat<TSource, TCollection>(this TCollection first, IEnumerable<TSource> second)
            where TCollection : INotifyCollectionChanged, IEnumerable<TSource>
        {
            var ret = new DisposableObservableCollection<TSource>(first.Concat(second));
            ret.AddDisposable(first.ObserveCollectionChanged<TCollection, TSource>((item, index) =>
                 ret.Insert(index, item)
                , (item, index) =>
                    ret.RemoveAt(index)
                , () =>
                {
                    ret.Clear();
                    ret.AddRange(first.Concat(second));
                }));
            return new DisposableReadOnlyObservableCollection<TSource>(ret);
        }
        public static DisposableReadOnlyObservableCollection<TSource> ObservableConcat<TSource>(this ObservableCollection<TSource> first, IEnumerable<TSource> second)
        {
            return ObservableConcat<TSource, ObservableCollection<TSource>>(first, second);
        }
        public static DisposableReadOnlyObservableCollection<TSource> ObservableConcat<TSource>(this ReadOnlyObservableCollection<TSource> first, IEnumerable<TSource> second)
        {
            return ObservableConcat<TSource, ReadOnlyObservableCollection<TSource>>(first, second);
        }
    }
}
