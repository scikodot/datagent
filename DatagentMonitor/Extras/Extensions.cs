using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DatagentMonitor
{
    internal static class StringExtensions
    {
        public static int StartsWithCount(this string str, char value)
        {
            int count = 0;
            foreach (var ch in str)
            {
                if (ch != value)
                    break;

                count++;
            }

            return count;
        }
    }

    internal static class StringBuildExtensions
    {
        public static IEnumerable<T> Wrap<T, S>(this StringBuilder builder, IEnumerable<T> collection, Func<T, S> selector)
        {
            int len;
            foreach (var item in collection)
            {
                len = builder.Length;
                builder.Append(selector(item));
                yield return item;
                builder.Remove(len, builder.Length - len);
            }
        }
    }
}
