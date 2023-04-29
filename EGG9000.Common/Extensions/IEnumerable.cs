using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Extensions {
    public static class IEnumerableExtentions {
        //https://stackoverflow.com/a/1287572
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random rng) {
            T[] elements = source.ToArray();
            for(int i = elements.Length - 1; i >= 0; i--) {
                // Swap element "i" with a random earlier element it (or itself)
                // ... except we don't really need to swap it fully, as we can
                // return it immediately, and afterwards it's irrelevant.
                int swapIndex = rng.Next(i + 1);
                yield return elements[swapIndex];
                elements[swapIndex] = elements[i];
            }
        }
    }
}
