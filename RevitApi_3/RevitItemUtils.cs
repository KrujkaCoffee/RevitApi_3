using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    public static class RevitItemUtils
    {
        /// <summary>
        /// Группирует элементы по типу, оставляя по одному RevitItem на TypeId.
        /// </summary>
        public static List<RevitItem> GroupByType(List<RevitItem> items)
        {
            return items
                .GroupBy(i => i.TypeId.IntegerValue)
                .Select(g => g.First())
                .ToList();
        }
    }
}
