using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitApi_3
{
    /// <summary>
    /// Семантическая выгрузка спецификации:
    /// 1) элементы берём через FilteredElementCollector(doc, scheduleView.Id)
    /// 2) сортировку/группировку воспроизводим через ViewSchedule.Definition
    /// 3) значения полей читаем из параметров (без чтения ячеек)
    /// </summary>
    internal static class ScheduleSemanticExport
    {
        private class FieldSpec
        {
            public ScheduleFieldId FieldId;
            public ScheduleField Field;
            public string Header; // как в UI (по возможности)
        }

        private class SortSpec
        {
            public ScheduleFieldId FieldId;
            public ScheduleSortOrder SortOrder;
        }

        private class ElementRecord
        {
            public Element Inst;
            public Element Type;
            public Dictionary<ScheduleFieldId, string> FieldText = new Dictionary<ScheduleFieldId, string>();
            public Dictionary<ScheduleFieldId, string> SortKeyText = new Dictionary<ScheduleFieldId, string>();
            public string ErpCode;
            public string Unit;

            // fallback, если в спецификации нет колонок Семейство/Тип/Наименование
            public string BaseFamilyName;
            public string BaseTypeName;
            public string BaseDisplayName;
        }


        public static List<ExportRow> BuildExportRows(Document doc, ViewSchedule vs)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (vs == null) throw new ArgumentNullException(nameof(vs));

            ScheduleDefinition def = vs.Definition;

            List<FieldSpec> visibleFields = GetVisibleFields(def);

            List<SortSpec> sortSpecs = GetSortSpecs(def);

            var items = RevitCollectors.CollectFromSchedule(doc, vs); 
            var records = new List<ElementRecord>(items.Count);

            foreach (var it in items)
            {
                var inst = doc.GetElement(it.ElementId);
                if (inst == null) continue;

                Element type = null;
                if (it.TypeId != ElementId.InvalidElementId)
                    type = doc.GetElement(it.TypeId);
                if (it.ErpCode == "00-qwe") {
                    Console.WriteLine("");
                }
                
                var rec = new ElementRecord
                {
                    Inst = inst,
                    Type = type,

                    // ERP/Unit: можно взять из items, но оставим твой приоритет "тип->экземпляр"
                    ErpCode = string.IsNullOrWhiteSpace(it.ErpCode) ? GetErpCode(type, inst) : (it.ErpCode ?? "").Trim(),
                    Unit = string.IsNullOrWhiteSpace(it.Unit) ? GetStringParam(inst, type, "ADSK_Единица измерения") : (it.Unit ?? "").Trim(),

                    BaseFamilyName = it.FamilyName ?? "",
                    BaseTypeName = it.TypeName ?? "",
                    BaseDisplayName = it.DisplayName ?? ""
                };

                foreach (var fs in visibleFields)
                    rec.FieldText[fs.FieldId] = ScheduleSemanticExport.GetFieldText(doc, inst, type, fs.Field);

                foreach (var ss in sortSpecs)
                {
                    var f = def.GetField(ss.FieldId);
                    rec.SortKeyText[ss.FieldId] = GetSortKeyText(doc, inst, type, f) ?? "";
                }

                records.Add(rec);
            }


            // 5) Сортировка как в спецификации (по цепочке sort/group fields)
            records.Sort(new RecordComparer(sortSpecs));

            // 6) Если itemize включён — каждая запись = строка
            // Если itemize выключён — агрегируем по ключу сортировки (группы)
            if (def.IsItemized) // :contentReference[oaicite:3]{index=3}
                return BuildItemizedRows(vs, visibleFields, records);

            return BuildGroupedRows(vs, def, visibleFields, sortSpecs, records);
        }
        private static string GetSortKeyText(Document doc, Element inst, Element type, ScheduleField field)
        {
            if (doc == null || inst == null || field == null) return "";

            // 1) сначала штатно (как раньше)
            string v = GetFieldText(doc, inst, type, field);
            if (!string.IsNullOrWhiteSpace(v)) return v;

            // 2) fallback по имени поля/заголовку: пробуем И inst, И type (в любом порядке)
            string n = "";
            try { n = field.GetName(); } catch { }

            if (!string.IsNullOrWhiteSpace(n))
            {
                var p = inst.LookupParameter(n) ?? type?.LookupParameter(n);
                v = ParamToText(doc, p);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }

            try
            {
                string h = field.ColumnHeading;
                if (!string.IsNullOrWhiteSpace(h) && !string.Equals(h, n, StringComparison.OrdinalIgnoreCase))
                {
                    var p = inst.LookupParameter(h) ?? type?.LookupParameter(h);
                    v = ParamToText(doc, p);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            catch { }

            return "";
        }

        private static string ParamToText(Document doc, Parameter p)
        {
            if (p == null) return "";

            try
            {
                var s = p.AsValueString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            catch { }

            try
            {
                switch (p.StorageType)
                {
                    case StorageType.String: return (p.AsString() ?? "").Trim();
                    case StorageType.Integer: return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                    case StorageType.Double: return p.AsDouble().ToString(CultureInfo.InvariantCulture);
                    case StorageType.ElementId:
                        var id = p.AsElementId();
                        if (id == ElementId.InvalidElementId) return "";
                        var e = doc.GetElement(id);
                        return e != null ? (e.Name ?? "").Trim() : id.IntegerValue.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch { }

            return "";
        }


        private static List<FieldSpec> GetVisibleFields(ScheduleDefinition def)
        {
            var list = new List<FieldSpec>();

            // GetFieldOrder возвращает реальные IDs в UI-порядке
            IList<ScheduleFieldId> order = def.GetFieldOrder();
            foreach (var fid in order)
            {
                ScheduleField f = def.GetField(fid);
                if (f == null) continue;

                // скрытые поля в UI не экспортируем как колонки
                // (но их можно будет отдельно учесть позже, если нужно для payload)
                //if (f.IsHidden) continue; // :contentReference[oaicite:4]{index=4}

                string header = SafeHeader(f);
                list.Add(new FieldSpec { FieldId = fid, Field = f, Header = header });
            }

            return list;
        }

        private static List<SortSpec> GetSortSpecs(ScheduleDefinition def)
        {
            var list = new List<SortSpec>();

            int n = def.GetSortGroupFieldCount(); // :contentReference[oaicite:5]{index=5}
            for (int i = 0; i < n; i++)
            {
                ScheduleSortGroupField sg = def.GetSortGroupField(i); // :contentReference[oaicite:6]{index=6}
                if (sg == null) continue;

                list.Add(new SortSpec
                {
                    FieldId = sg.FieldId,          // :contentReference[oaicite:7]{index=7}
                    SortOrder = sg.SortOrder       // ctor/docs :contentReference[oaicite:8]{index=8}
                });
            }

            return list;
        }

        private static List<ExportRow> BuildItemizedRows(
            ViewSchedule vs,
            List<FieldSpec> fields,
            List<ElementRecord> records)
        {
            // Здесь мы делаем “ресурсные” строки (как у вас ExportRow),
            // а поля берём по заголовкам через токены.
            return BuildExportRowsFromRecords(vs, fields, records);
        }

        private static List<ExportRow> BuildGroupedRows(
            ViewSchedule vs,
            ScheduleDefinition def,
            List<FieldSpec> fields,
            List<SortSpec> sortSpecs,
            List<ElementRecord> records)
        {
            // Группа = одинаковые значения по всем sort/group полям (в порядке)
            var groups = new List<List<ElementRecord>>();
            List<ElementRecord> current = null;
            string lastKey = null;

            foreach (var r in records)
            {
                if (r.ErpCode == "00-qwe") {
                    Console.WriteLine(" ");
                }
                string key = MakeGroupKey(sortSpecs, r);
                if (current == null || !string.Equals(lastKey, key, StringComparison.Ordinal))
                {
                    current = new List<ElementRecord>();
                    groups.Add(current);
                    lastKey = key;
                }
                current.Add(r);
            }

            // Для каждой группы делаем “сводную запись”:
            // - Standard: если значения разные → MultipleValuesText
            // - Totals: суммируем (если числа)
            // см. DisplayType :contentReference[oaicite:9]{index=9}
            var aggRecords = new List<ElementRecord>(groups.Count);

            foreach (var g in groups)
            {
                var agg = new ElementRecord();

                agg.ErpCode = Merge(g.Select(x => x.ErpCode), "-");
                agg.Unit = Merge(g.Select(x => x.Unit));
                agg.BaseDisplayName = Merge(g.Select(x => x.BaseDisplayName));

                foreach (var fs in fields)
                {
                    var f = fs.Field;

                    if (f.IsCalculatedField || f.IsCombinedParameterField) // || f.IsCombinedParameterField
                    {
                        agg.FieldText[fs.FieldId] = "";
                        continue;
                    }

                    string v = AggregateField(def, fs, g);
                    agg.FieldText[fs.FieldId] = v ?? "";
                }

                aggRecords.Add(agg);
            }

            return BuildExportRowsFromRecords(vs, fields, aggRecords);
        }

        private static string AggregateField(ScheduleDefinition def, FieldSpec fs, List<ElementRecord> group)
        {
            ScheduleField f = fs.Field;

            // Totals: попробуем суммировать
            if (f.DisplayType == ScheduleFieldDisplayType.Totals) // :contentReference[oaicite:11]{index=11}
            {
                // суммируем по текстам не выйдет; поэтому берём raw числовые параметры
                // через Parameter.StorageType (int/double). Если не число — пусто.
                double sum = 0.0;
                bool any = false;

                foreach (var r in group)
                {
                    double? val = TryGetNumericFieldValue(r, f);
                    if (val.HasValue)
                    {
                        sum += val.Value;
                        any = true;
                    }
                }

                if (!any) return "";
                return sum.ToString(CultureInfo.InvariantCulture);
            }

            // Standard: если все одинаковые — показываем значение, иначе MultipleValuesText
            string first = group[0].FieldText.TryGetValue(fs.FieldId, out var v0) ? v0 : "";
            for (int i = 1; i < group.Count; i++)
            {
                string vi = group[i].FieldText.TryGetValue(fs.FieldId, out var vx) ? vx : "";
                if (!string.Equals(first, vi, StringComparison.Ordinal))
                {
                    // MultipleValuesText доступен у ScheduleField (в UI это “<varies>”)
                    try { return f.MultipleValuesText ?? ""; }
                    catch { return ""; }
                }
            }

            return first;
        }

        private static double? TryGetNumericFieldValue(ElementRecord r, ScheduleField f)
        {
            // Для totals нам нужен “сырой” числовой параметр.
            // Поддерживаем только параметрные поля (ParameterId).
            ElementId pid = f.ParameterId;
            if (pid == null || pid == ElementId.InvalidElementId) return null;

            Parameter p = GetParameterByElementId(r.Inst, r.Type, f.FieldType, pid, r.Inst.Document);
            if (p == null) return null;

            if (p.StorageType == StorageType.Integer)
                return p.AsInteger();

            if (p.StorageType == StorageType.Double)
                return p.AsDouble();

            return null;
        }

        private static string MakeGroupKey(List<SortSpec> specs, ElementRecord r)
        {
            if (specs == null || specs.Count == 0) return ""; // без группировки

            // Важно: ключ строим по “текстам сортировки” в порядке полей
            // (т.е. ровно как Revit группирует после сортировки)
            var parts = new string[specs.Count];
            for (int i = 0; i < specs.Count; i++)
            {
                var fid = specs[i].FieldId;
                r.SortKeyText.TryGetValue(fid, out var s);
                parts[i] = s ?? "";
            }
            return string.Join("\u001F", parts); // редкий разделитель
        }

        private class RecordComparer : IComparer<ElementRecord>
        {
            private readonly List<SortSpec> _specs;
            public RecordComparer(List<SortSpec> specs) { _specs = specs ?? new List<SortSpec>(); }

            public int Compare(ElementRecord a, ElementRecord b)
            {
                if (_specs.Count == 0) return 0;

                foreach (var s in _specs)
                {
                    a.SortKeyText.TryGetValue(s.FieldId, out var sa);
                    b.SortKeyText.TryGetValue(s.FieldId, out var sb);

                    int cmp = string.Compare(sa ?? "", sb ?? "", StringComparison.CurrentCultureIgnoreCase);
                    if (cmp != 0)
                    {
                        if (s.SortOrder == ScheduleSortOrder.Descending)
                            cmp = -cmp;
                        return cmp;
                    }
                }
                return 0;
            }
        }

        // ======== ExportRow projection (как сейчас в UI/ERP клиенте) ========

        private static List<ExportRow> BuildExportRowsFromRecords(
            ViewSchedule vs,
            List<FieldSpec> fields,
            List<ElementRecord> records)
        {
            // Находим “важные” колонки по заголовкам (как у вас раньше),
            // но значения берём не из ячеек, а из FieldText.
            int cFam = FindField(fields, "Семейство", "Family");
            int cTyp = FindField(fields, "Тип", "Type");
            int cName = FindField(fields, "Наименование", "ADSK_Наименование", "Имя", "Name");
            int cQty = FindField(fields, "Кол", "Количество", "Qty", "Count");
            int cM1 = FindField(fields, "Масса / шт", "Масса/шт", "Масса шт", "Масса/ед");
            int cMT = FindField(fields, "Масса всего", "Масса итого", "Итого масса");
            int cUnit = FindField(fields, "Ед", "Ед.", "Единица", "Unit", "Ед. изм.", "Единица измерения", "ADSK_Единица измерения");
            int cErp = FindField(fields, "Код 1C-ERP", "Код 1С-ERP", "ERP", "Код ERP");

            var outRows = new List<ExportRow>(records.Count);

            foreach (var r in records)
            {
                string family = GetByIndex(fields, r, cFam);
                string type = GetByIndex(fields, r, cTyp);
                string name = GetByIndex(fields, r, cName);

                if (string.IsNullOrWhiteSpace(family)) family = r.BaseFamilyName;
                if (string.IsNullOrWhiteSpace(type)) type = r.BaseTypeName;
                if (string.IsNullOrWhiteSpace(name)) name = r.BaseDisplayName;

                string erp = GetByIndex(fields, r, cErp);
                if (string.IsNullOrWhiteSpace(erp))
                    erp = r.ErpCode ?? "";

                string unit = GetByIndex(fields, r, cUnit);
                if (string.IsNullOrWhiteSpace(unit))
                    unit = r.Unit ?? "";

                outRows.Add(new ExportRow
                {
                    Stage = "",
                    ScheduleName = vs.Name,
                    FamilyName = family,
                    TypeName = type,
                    DisplayName = name,
                    ErpCode = erp,
                    Unit = unit,
                    QuantityText = GetByIndex(fields, r, cQty),
                    MassPerItemText = GetByIndex(fields, r, cM1),
                    TotalMassText = GetByIndex(fields, r, cMT)
                });
            }

            return outRows;
        }

        private static int FindField(List<FieldSpec> fields, params string[] tokens)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                string h = fields[i].Header ?? "";
                foreach (var t in tokens)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    if (h.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                        return i;
                }
            }
            return -1;
        }

        private static string GetByIndex(List<FieldSpec> fields, ElementRecord rec, int idx)
        {
            if (idx < 0 || idx >= fields.Count) return "";
            var fid = fields[idx].FieldId;
            rec.FieldText.TryGetValue(fid, out var v);
            return v ?? "";
        }

        // ======== Field reading ========

        public static string GetFieldText(Document doc, Element inst, Element type, ScheduleField field)
        {
            if (field == null) return "";

            // Формулы/combined пока не считаем (без ячеек)
            if (field.IsCalculatedField || field.IsCombinedParameterField) // :contentReference[oaicite:12]{index=12}
                return "";

            ElementId pid = field.ParameterId;
            if (pid == null || pid == ElementId.InvalidElementId)
                return "";

            Parameter p = GetParameterByElementId(inst, type, field.FieldType, pid, doc);
            if (p == null)
            {
                string n = "";
                try { n = field.GetName(); } catch { }
                if (!string.IsNullOrWhiteSpace(n))
                    p = inst.LookupParameter(n);

                if (p == null)
                {
                    try
                    {
                        var h = field.ColumnHeading;
                        if (!string.IsNullOrWhiteSpace(h) && !string.Equals(h, n, StringComparison.OrdinalIgnoreCase))
                            p = inst.LookupParameter(h);
                    }
                    catch { }
                }
            }
            if (p == null) return "";

            // сначала “как в UI”
            string s = p.AsValueString();
            if (!string.IsNullOrWhiteSpace(s))
                return (s ?? "").Trim();

            // иначе по StorageType
            switch (p.StorageType)
            {
                case StorageType.String:
                    return (p.AsString() ?? "").Trim();
                case StorageType.Integer:
                    return p.AsInteger().ToString(CultureInfo.InvariantCulture);
                case StorageType.Double:
                    return p.AsDouble().ToString(CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    ElementId id = p.AsElementId();
                    if (id == ElementId.InvalidElementId) return "";
                    Element e = doc.GetElement(id);
                    return e != null ? (e.Name ?? "").Trim() : id.IntegerValue.ToString(CultureInfo.InvariantCulture);
                default:
                    return "";
            }
        }

        private static Parameter GetParameterByElementId(Element inst, Element type, ScheduleFieldType fieldType, ElementId pid, Document doc)
        {
            Element src = inst;

            // Если поле берётся с типа — читаем с type
            if (fieldType == ScheduleFieldType.ElementType)
                src = type ?? inst;

            if (src == null) return null;

            // pid может быть BuiltInParameter (отрицательный) или ParameterElement (положительный)
            if (pid.IntegerValue < 0)
                return src.get_Parameter((BuiltInParameter)pid.IntegerValue);

            ParameterElement pe = doc.GetElement(pid) as ParameterElement;
            if (pe != null)
            {
                Definition def = pe.GetDefinition();
                if (def != null)
                    return src.get_Parameter(def);
            }

            return null;
        }

        // ======== ERP/unit helpers ========

        private static string GetErpCode(Element type, Element inst)
        {
            // как у вас в RevitCollectors: тип приоритетнее экземпляра :contentReference[oaicite:13]{index=13}
            Parameter pType = type != null ? type.LookupParameter(ErpParameters.ErpCodeParamName) : null;
            if (pType != null && pType.StorageType == StorageType.String)
            {
                var s = pType.AsString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }

            Parameter pInst = inst != null ? inst.LookupParameter(ErpParameters.ErpCodeParamName) : null;
            if (pInst != null && pInst.StorageType == StorageType.String)
                return (pInst.AsString() ?? "").Trim();

            return "";
        }

        private static string GetStringParam(Element inst, Element type, string name)
        {
            Parameter p = inst != null ? inst.LookupParameter(name) : null;
            if (p != null && p.StorageType == StorageType.String)
            {
                string s = p.AsString();
                if (!string.IsNullOrEmpty(s)) return s;
            }

            if (type != null)
            {
                Parameter pt = type.LookupParameter(name);
                if (pt != null && pt.StorageType == StorageType.String)
                {
                    string s = pt.AsString();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }

            return "";
        }

        private static string MergeErp(IEnumerable<string> codes)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in codes)
            {
                var s = (c ?? "").Trim();
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }
            if (set.Count == 0) return "";
            if (set.Count == 1) return set.First();
            return "-";
        }

        private static string Merge(IEnumerable<string> units, string defaultValue = "")
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in units)
            {
                var s = (c ?? "").Trim();
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }
            if (set.Count == 0) return defaultValue;
            if (set.Count == 1) return set.First();
            return defaultValue;
        }

        private static string SafeHeader(ScheduleField f)
        {
            if (f == null) return "";

            // Revit 2022: ColumnHeading — свойство, не метод
            try
            {
                string h = f.ColumnHeading;
                if (!string.IsNullOrWhiteSpace(h))
                    return h.Trim();
            }
            catch { }

            // fallback: имя поля
            try
            {
                string n = f.GetName();
                if (!string.IsNullOrWhiteSpace(n))
                    return n.Trim();
            }
            catch { }

            return "";
        }

    }
}
