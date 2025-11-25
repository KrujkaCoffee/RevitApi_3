using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitErpIntegration
{
    public static class RevitCollectors
    {
        public static List<RevitItem> CollectItemsFromSpecs(Document doc)
        {
            List<RevitItem> result = new List<RevitItem>();

            IList<Element> schedules = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .ToElements();

            foreach (Element el in schedules)
            {
                ViewSchedule vs = el as ViewSchedule;
                if (vs == null) continue;

                if (!vs.Name.StartsWith("Спецификация_", StringComparison.OrdinalIgnoreCase))
                    continue;

                // ВАЖНО: для спецификации берём элементы через FilteredElementCollector по Id вида
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

            // сгруппировать по TypeId, чтобы не дублировать одну и ту же номенклатуру
            List<RevitItem> grouped = result
                .GroupBy(r => r.TypeId.IntegerValue)
                .Select(g => g.First())
                .ToList();

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
