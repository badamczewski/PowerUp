using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PowerUp.Core
{
    public static class Extensions
    {
        public static Dictionary<TKey, TElement> ToUniqueDictionary<TSource, TKey, TElement>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TElement> elementSelector) where TKey : notnull
        {
            Dictionary<TKey, TElement> dict = new Dictionary<TKey, TElement>();
            foreach (var element in source)
            {
                var key = keySelector(element); 
                var value = elementSelector(element);

                dict.TryAdd(key, value);
            }
            return dict;
        }


    }
}
