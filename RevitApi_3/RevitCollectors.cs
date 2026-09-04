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

                if (vs.IsTemplate) continue;                 // ВАЖНО: шаблоны
                if (!string.IsNullOrWhiteSpace(prefix) &&
                    !vs.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(vs);
            }
            return result;
        }

        public static List<RevitItem> CollectFromAllSpecs(Document doc)
        {
            var specs = GetSpecsByPrefix(doc, "Спецификация");
            return CollectItemsFromSchedules(doc, specs);
        }

        public static List<RevitItem> CollectFromSchedule(Document doc, ViewSchedule vs)
        {
            return CollectItemsFromSchedules(doc, new[] { vs });
        }

        public static List<RevitItem> CollectItemsFromSchedules(Document doc, IEnumerable<ViewSchedule> schedules)
        {
            var result = new List<RevitItem>();
            Guid erpParameterGuid = ErpParameters.ResolveErpCodeGuid(doc);

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

                    // Новая схема — экземплярная. Чтение с типа остаётся только
                    // как миграционный fallback для документов старых версий.
                    string erpCode = ErpParameters.ReadCode(inst, erpParameterGuid);
                    if (string.IsNullOrWhiteSpace(erpCode))
                        erpCode = ErpParameters.ReadCode(type, erpParameterGuid);

                    var item = new RevitItem
                    {
                        ElementId = inst.Id,
                        TypeId = inst.GetTypeId(),
                        ErpCode = erpCode
                    };

                    result.Add(item);
                }
            }
            return result;
        }

    }
}
