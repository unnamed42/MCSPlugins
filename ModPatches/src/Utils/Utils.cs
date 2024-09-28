using System;
using System.Collections.Generic;

namespace Unnamed42.ModPatches.Utils;

public static class Extensions
{
    public static T Get<K, T>(this IDictionary<K, T> dict, K k, T fallback = default) =>
        dict.TryGetValue(k, out var result) ? result : fallback;

    public static void AddIfAbsent<K, T>(this IDictionary<K, T> dict, K k, T t)
    {
        if (!dict.ContainsKey(k)) dict[k] = t;
    }

    public static U Let<T, U>(this T value, Func<T, U> let) => let(value);

    public static T Also<T>(this T value, Action<T> action) { action(value); return value; }

    public static string Join<T>(this IEnumerable<T> list, string sep) =>
        list == null ? "" : string.Join(sep, list);

    public static void ForEach<T>(this IEnumerable<T> list, Action<T> action)
    {
        foreach (var item in list) action(item);
    }

    public static T FirstIn<K, T>(this IEnumerable<K> list, IDictionary<K, T> dict, T fallback = default)
    {
        foreach (var item in list)
        {
            if (dict.TryGetValue(item, out var result)) return result;
        }
        return fallback;
    }
}
