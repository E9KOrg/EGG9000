using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers.AfxSets {
    public static class AfxSetsBuilder {
        // One output set per input saved set, in order. Drops unresolved (null) slots
        // within a set, but keeps empty sets so set indexes line up with in-game slots.
        public static List<List<T>> BuildSetsPreservingEmpty<T>(IEnumerable<IEnumerable<T>> resolvedSlotsPerSet) where T : class {
            return resolvedSlotsPerSet
                .Select(set => set.Where(x => x is not null).ToList())
                .ToList();
        }
    }
}
