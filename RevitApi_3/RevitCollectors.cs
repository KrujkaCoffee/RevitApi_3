using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    public static class RevitCollectors
    {
        public static List<ViewSchedule> GetSpecsByPrefix(Document doc, string prefix)
        {
            IList<Element> schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .ToElements();

            var result = new List<ViewSchedule>();
            foreach (Element el in schedules)
            {
                var vs = el as ViewSchedule;
                if (vs == null) continue;
                if (!vs.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(vs);
            }
            return result;
        }

        public static List<RevitItem> CollectFromAllSpecs(Document doc)
        {
            var specs = GetSpecsByPrefix(doc, "Спецификация_");
            return CollectItemsFromSchedules(doc, specs);
        }

        public static List<RevitItem> CollectFromSchedule(Document doc, ViewSchedule vs)
        {
            return CollectItemsFromSchedules(doc, new[] { vs });
        }

        public static List<RevitItem> CollectItemsFromSchedules(Document doc, IEnumerable<ViewSchedule> schedules)
        {
            var result = new List<RevitItem>();

            foreach (var vs in schedules)
            {
                if (vs == null) continue;

                var col = new FilteredElementCollector(doc, vs.Id);
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

                    var fs = type as FamilySymbol;
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

                    var item = new RevitItem
                    {
                        ElementId = inst.Id,
                        TypeId = inst.GetTypeId(),
                        ScheduleName = vs.Name,
                        FamilyName = familyName,
                        TypeName = typeName,
                        DisplayName = dispName,
                        ErpCode = erpCode
                    };

                    result.Add(item);
                }
            }

            // Больше без группировки! Команды сами решат, нужно ли по типам.
            return result;
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
