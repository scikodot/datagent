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
}
