using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    public static class RevitCollectors
    {
        /// <summary>
        /// Все спецификации, чьи имена начинаются с "Спецификация_".
        /// </summary>
        public static List<ViewSchedule> GetSpecsByPrefix(Document doc, string prefix)
        {
            IList<Element> schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .ToElements();

            List<ViewSchedule> result = new List<ViewSchedule>();
            foreach (Element el in schedules)
            {
                ViewSchedule vs = el as ViewSchedule;
                if (vs == null) continue;
                if (!vs.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(vs);
            }
            return result;
        }

        /// <summary>
        /// Собирает номенклатуры из всех спецификаций с префиксом "Спецификация_".
        /// </summary>
        public static List<RevitItem> CollectFromAllSpecs(Document doc)
        {
            List<ViewSchedule> specs = GetSpecsByPrefix(doc, "Спецификация_");
            return CollectItemsFromSchedules(doc, specs);
        }

        /// <summary>
        /// Собирает номенклатуры только из одной спецификации.
        /// </summary>
        public static List<RevitItem> CollectFromSchedule(Document doc, ViewSchedule vs)
        {
            return CollectItemsFromSchedules(doc, new[] { vs });
        }

        /// <summary>
        /// Универсальный сборщик из набора спецификаций.
        /// Оставляет по одному RevitItem на TypeId.
        /// </summary>
        public static List<RevitItem> CollectItemsFromSchedules(Document doc, IEnumerable<ViewSchedule> schedules)
        {
            List<RevitItem> result = new List<RevitItem>();

            foreach (ViewSchedule vs in schedules)
            {
                if (vs == null) continue;

                FilteredElementCollector col = new FilteredElementCollector(doc, vs.Id);
                ICollection<ElementId> ids = col
                    .WhereElementIsNotElementType()
                    .ToElementIds();

                foreach (ElementId id in ids)
                {
                    Element inst = doc.GetElement(id);
                    if (inst == null) continue;

                    Element type = doc.GetElement(inst.GetTypeId());

                    string familyName = "";
                    string typeName = "";

                    FamilySymbol fs = type as FamilySymbol;
                    if (fs != null)
                    {
                        familyName = fs.Family != null ? fs.Family.Name : "";
                        typeName = fs.Name;
                    }
                    else if (type != null)
                    {
                        typeName = type.Name;
                    }

                    string dispName = GetDisplayName(inst, type);

                    string erpCode = null;
                    Parameter pType = type != null ? type.LookupParameter(ErpParameters.ErpCodeParamName) : null;
                    Parameter pInst = inst.LookupParameter(ErpParameters.ErpCodeParamName);

                    if (pType != null && pType.StorageType == StorageType.String)
                        erpCode = pType.AsString();
                    else if (pInst != null && pInst.StorageType == StorageType.String)
                        erpCode = pInst.AsString();

                    RevitItem item = new RevitItem();
                    item.ElementId = inst.Id;
                    item.TypeId = inst.GetTypeId();
                    item.ScheduleName = vs.Name;
                    item.FamilyName = familyName;
                    item.TypeName = typeName;
                    item.DisplayName = dispName;
                    item.ErpCode = erpCode;

                    result.Add(item);
                }
            }

            // Наследование по типу — один RevitItem на TypeId
            List<RevitItem> grouped = new List<RevitItem>();
            foreach (var g in result.GroupBy(r => r.TypeId.IntegerValue))
            {
                grouped.Add(g.First());
            }

            return grouped;
        }

        private static string GetDisplayName(Element inst, Element type)
        {
            Parameter p = inst.LookupParameter("ADSK_Наименование");
            if (p != null && p.StorageType == StorageType.String && !string.IsNullOrEmpty(p.AsString()))
                return p.AsString();

            if (type != null)
            {
                Parameter pt = type.LookupParameter("ADSK_Наименование");
                if (pt != null && pt.StorageType == StorageType.String && !string.IsNullOrEmpty(pt.AsString()))
                    return pt.AsString();
            }

            if (type != null)
                return type.Name;

            return inst.Name;
        }
    }
}
