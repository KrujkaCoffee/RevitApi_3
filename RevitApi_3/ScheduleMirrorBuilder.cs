using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace RevitApi_3
{
    /// <summary>
    /// Читает Header/Body непосредственно из ViewSchedule. Значения Values — это
    /// GetCellText без повторного расчёта, поэтому превью и payload совпадают с
    /// тем, что пользователь видит в открытой спецификации.
    /// </summary>
    internal static class ScheduleMirrorBuilder
    {
        private sealed class ElementProfile
        {
            public RevitItem Item { get; set; }
            public Dictionary<int, string> Values { get; } = new Dictionary<int, string>();
            public List<string> HiddenIdentityValues { get; } = new List<string>();
        }

        private sealed class ProfileClusterMatch
        {
            public List<int> ElementIds { get; set; }
            public string ErpCode { get; set; }
        }

        public static ScheduleMirrorTable Build(Document doc, ViewSchedule schedule, Guid erpParameterGuid)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));

            var result = new ScheduleMirrorTable
            {
                ScheduleElementId = schedule.Id.IntegerValue,
                ScheduleName = schedule.Name ?? ""
            };

            TableData tableData = schedule.GetTableData();
            TableSectionData header = tableData.GetSectionData(SectionType.Header);
            TableSectionData body = tableData.GetSectionData(SectionType.Body);
            if (body == null)
                throw new InvalidOperationException("Revit не вернул секцию Body активной спецификации.");

            int columnCount = body.NumberOfColumns;
            List<ScheduleField> visibleFields = GetVisibleFields(schedule.Definition);
            result.HasExactFieldMapping = visibleFields.Count == columnCount;

            ReadHeaderRows(schedule, header, columnCount, result.HeaderRows);
            BuildColumns(doc, visibleFields, columnCount, erpParameterGuid, result);
            ReadBodyRows(schedule, body, columnCount, result.Rows);
            AssociateRowsWithElements(doc, schedule, result);

            int unmatched = result.Rows.Count(x => x.IsResourceRow && !x.CanWriteErpCode);
            if (!result.HasExactFieldMapping)
            {
                result.Diagnostic =
                    $"Revit вернул {columnCount} видимых колонок, а Definition — {visibleFields.Count}. " +
                    "Ячейки показаны точно, но запись кода для неоднозначных строк заблокирована.";
            }
            else if (unmatched > 0)
            {
                result.Diagnostic =
                    $"{unmatched} строк показаны точно, но не удалось однозначно связать их с ElementId. " +
                    "Для них запись кода в модель заблокирована.";
            }

            return result;
        }

        private static List<ScheduleField> GetVisibleFields(ScheduleDefinition definition)
        {
            var fields = new List<ScheduleField>();
            foreach (ScheduleFieldId fieldId in definition.GetFieldOrder())
            {
                ScheduleField field = definition.GetField(fieldId);
                if (field != null && !field.IsHidden) fields.Add(field);
            }
            return fields;
        }

        private static void ReadHeaderRows(
            ViewSchedule schedule,
            TableSectionData header,
            int columnCount,
            List<List<string>> target)
        {
            if (header == null) return;
            for (int row = 0; row < header.NumberOfRows; row++)
            {
                var values = new List<string>(columnCount);
                for (int column = 0; column < columnCount; column++)
                    values.Add(SafeGetCellText(schedule, SectionType.Header, row, column));
                target.Add(values);
            }
        }

        private static void BuildColumns(
            Document doc,
            List<ScheduleField> fields,
            int columnCount,
            Guid erpParameterGuid,
            ScheduleMirrorTable target)
        {
            var usedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < columnCount; index++)
            {
                ScheduleField field = fields.Count == columnCount ? fields[index] : null;
                string header = SafeColumnHeading(field);
                if (string.IsNullOrWhiteSpace(header))
                    header = FindBottomHeader(target.HeaderRows, index);
                if (string.IsNullOrWhiteSpace(header))
                    header = "Колонка " + (index + 1);

                string key = MakeUniqueKey(header, index, usedKeys);
                int parameterId = ElementId.InvalidElementId.IntegerValue;
                string parameterGuid = "";

                if (field != null)
                {
                    try
                    {
                        ElementId id = field.ParameterId;
                        if (id != null)
                        {
                            parameterId = id.IntegerValue;
                            SharedParameterElement shared = doc.GetElement(id) as SharedParameterElement;
                            if (shared != null) parameterGuid = shared.GuidValue.ToString("D");
                        }
                    }
                    catch { }
                }

                bool isErp = (!string.IsNullOrWhiteSpace(parameterGuid) &&
                              string.Equals(parameterGuid, erpParameterGuid.ToString("D"),
                                  StringComparison.OrdinalIgnoreCase)) || IsErpHeader(header);

                target.Columns.Add(new ScheduleMirrorColumn
                {
                    Index = index,
                    Key = key,
                    Header = header,
                    ParameterId = parameterId,
                    ParameterGuid = parameterGuid,
                    FieldType = field?.FieldType.ToString() ?? "",
                    IsCalculated = SafeIsCalculated(field),
                    IsCombined = SafeIsCombined(field),
                    IsErpCode = isErp,
                    IsQuantity = ContainsAny(header, "количество", "кол-во", "quantity", "qty", "count"),
                    IsUnit = ContainsAny(header, "единица измерения", "ед. изм", "unit"),
                    RevitField = field
                });
            }
        }

        private static void ReadBodyRows(
            ViewSchedule schedule,
            TableSectionData body,
            int columnCount,
            List<ScheduleMirrorRow> target)
        {
            for (int row = 0; row < body.NumberOfRows; row++)
            {
                var mirrorRow = new ScheduleMirrorRow { SourceRowNumber = row + 1 };
                for (int column = 0; column < columnCount; column++)
                    mirrorRow.Values.Add(SafeGetCellText(schedule, SectionType.Body, row, column));
                target.Add(mirrorRow);
            }
        }

        private static void AssociateRowsWithElements(
            Document doc,
            ViewSchedule schedule,
            ScheduleMirrorTable table)
        {
            List<RevitItem> items = RevitCollectors.CollectFromSchedule(doc, schedule);
            List<int> identityColumns = GetIdentityColumns(table.Columns);
            List<ScheduleField> hiddenIdentityFields = GetHiddenIdentityFields(schedule.Definition);
            var profiles = new List<ElementProfile>(items.Count);

            foreach (RevitItem item in items)
            {
                Element instance = doc.GetElement(item.ElementId);
                if (instance == null) continue;
                Element type = doc.GetElement(item.TypeId);

                var profile = new ElementProfile { Item = item };
                foreach (int columnIndex in identityColumns)
                {
                    ScheduleField field = table.Columns[columnIndex].RevitField;
                    profile.Values[columnIndex] = ScheduleFieldValueReader.GetText(doc, instance, type, field);
                }
                foreach (ScheduleField field in hiddenIdentityFields)
                    profile.HiddenIdentityValues.Add(
                        ScheduleFieldValueReader.GetText(doc, instance, type, field));
                profiles.Add(profile);
            }

            var clusters = profiles
                .GroupBy(x => MakeProfileKey(x, identityColumns), StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToList();

            var rowMatches = new Dictionary<ScheduleMirrorRow, List<ProfileClusterMatch>>();
            foreach (IGrouping<string, ElementProfile> cluster in clusters)
            {
                ElementProfile sample = cluster.First();
                int bestScore = 0;
                var bestRows = new List<ScheduleMirrorRow>();

                foreach (ScheduleMirrorRow row in table.Rows)
                {
                    int score = ScoreRow(row, sample, identityColumns);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestRows.Clear();
                        bestRows.Add(row);
                    }
                    else if (score > 0 && score == bestScore)
                    {
                        bestRows.Add(row);
                    }
                }

                if (bestScore == 0) continue;

                List<int> elementIds = cluster
                    .Select(x => x.Item.ElementId.IntegerValue)
                    .Distinct()
                    .ToList();
                List<string> codes = cluster
                    .Select(x => (x.Item.ErpCode ?? "").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                string code = codes.Count == 1 ? codes[0] : codes.Count > 1 ? "-" : "";
                var match = new ProfileClusterMatch { ElementIds = elementIds, ErpCode = code };

                // Несколько одинаковых itemized-строк — одна номенклатурная сущность.
                // Код у них обязан быть одинаковым, поэтому связываем весь набор.
                foreach (ScheduleMirrorRow row in bestRows)
                {
                    if (!rowMatches.TryGetValue(row, out List<ProfileClusterMatch> matches))
                    {
                        matches = new List<ProfileClusterMatch>();
                        rowMatches[row] = matches;
                    }
                    matches.Add(match);
                }
            }

            foreach (KeyValuePair<ScheduleMirrorRow, List<ProfileClusterMatch>> pair in rowMatches)
            {
                ScheduleMirrorRow row = pair.Key;
                List<ProfileClusterMatch> matches = pair.Value;
                row.IsResourceRow = true;

                if (matches.Count != 1)
                {
                    int erpColumn = table.ErpCodeColumnIndex;
                    string visibleCode = erpColumn >= 0 ? row.GetValue(erpColumn).Trim() : "";
                    row.ErpCode = visibleCode;
                    row.OriginalErpCode = visibleCode;
                    row.MatchInfo =
                        $"Неоднозначная связь: строке соответствуют {matches.Count} группы элементов";
                    continue;
                }

                ProfileClusterMatch single = matches[0];
                row.ElementIds.AddRange(single.ElementIds);
                row.ErpCode = single.ErpCode;
                row.OriginalErpCode = single.ErpCode;
                row.MatchInfo = $"Связано элементов Revit: {row.ElementIds.Count}";
            }

            int erpColumn = table.ErpCodeColumnIndex;
            foreach (ScheduleMirrorRow row in table.Rows)
            {
                if (row.IsResourceRow) continue;

                int populatedIdentity = identityColumns.Count(i =>
                    !string.IsNullOrWhiteSpace(row.GetValue(i)));
                int populatedTotal = row.Values.Count(x => !string.IsNullOrWhiteSpace(x));
                string visibleErpCode = erpColumn >= 0 ? row.GetValue(erpColumn).Trim() : "";

                // Строки итогов/заголовков обычно содержат одно значение. Две и
                // более номенклатурные ячейки — консервативный fallback для данных.
                if (populatedIdentity >= 2 || (!string.IsNullOrWhiteSpace(visibleErpCode) && visibleErpCode != "-"))
                {
                    row.IsResourceRow = true;
                    row.ErpCode = visibleErpCode;
                    row.OriginalErpCode = visibleErpCode;
                    row.MatchInfo = "Строка данных не связана однозначно с ElementId";
                }
                else if (schedule.Definition.IsItemized && populatedTotal >= 2 && populatedIdentity > 0)
                {
                    row.IsResourceRow = true;
                    row.ErpCode = visibleErpCode;
                    row.OriginalErpCode = visibleErpCode;
                    row.MatchInfo = "Itemized-строка не связана однозначно с ElementId";
                }
            }
        }

        private static List<int> GetIdentityColumns(IList<ScheduleMirrorColumn> columns)
        {
            var result = new List<int>();
            foreach (ScheduleMirrorColumn column in columns)
            {
                if (column.RevitField == null || column.IsCalculated || column.IsCombined ||
                    column.IsErpCode || column.IsQuantity || column.IsUnit)
                    continue;

                try
                {
                    if (column.RevitField.DisplayType == ScheduleFieldDisplayType.Totals) continue;
                }
                catch { }

                string header = column.Header ?? "";
                if (IsContextOrAggregateHeader(header))
                    continue;

                result.Add(column.Index);
            }
            return result;
        }

        private static List<ScheduleField> GetHiddenIdentityFields(ScheduleDefinition definition)
        {
            var result = new List<ScheduleField>();
            int count = definition.GetSortGroupFieldCount();
            for (int index = 0; index < count; index++)
            {
                ScheduleSortGroupField sort = definition.GetSortGroupField(index);
                ScheduleField field = sort == null ? null : definition.GetField(sort.FieldId);
                if (field == null || !field.IsHidden || SafeIsCalculated(field) || SafeIsCombined(field))
                    continue;

                string header = SafeColumnHeading(field);
                if (IsErpHeader(header) ||
                    ContainsAny(header, "количество", "кол-во", "quantity", "qty", "count",
                        "единица измерения", "ед. изм", "unit") ||
                    IsContextOrAggregateHeader(header))
                    continue;

                result.Add(field);
            }
            return result;
        }

        private static string MakeProfileKey(ElementProfile profile, IList<int> identityColumns)
        {
            var builder = new StringBuilder();
            bool hasValue = false;
            int typeId = profile.Item?.TypeId?.IntegerValue ?? ElementId.InvalidElementId.IntegerValue;
            builder.Append("type=").Append(typeId).Append('\u001F');
            foreach (int index in identityColumns)
            {
                profile.Values.TryGetValue(index, out string value);
                string normalized = Normalize(value);
                if (!string.IsNullOrWhiteSpace(normalized)) hasValue = true;
                builder.Append(index).Append('=').Append(normalized).Append('\u001F');
            }
            for (int index = 0; index < profile.HiddenIdentityValues.Count; index++)
            {
                string normalized = Normalize(profile.HiddenIdentityValues[index]);
                if (!string.IsNullOrWhiteSpace(normalized)) hasValue = true;
                builder.Append("hidden_").Append(index).Append('=').Append(normalized).Append('\u001F');
            }
            return hasValue ? builder.ToString().Trim('\u001F') : "";
        }

        private static int ScoreRow(
            ScheduleMirrorRow row,
            ElementProfile profile,
            IList<int> identityColumns)
        {
            int matches = 0;
            int mismatches = 0;

            foreach (int index in identityColumns)
            {
                profile.Values.TryGetValue(index, out string expectedRaw);
                string expected = Normalize(expectedRaw);
                string actual = Normalize(row.GetValue(index));

                if (string.IsNullOrEmpty(actual)) continue;
                if (string.IsNullOrEmpty(expected))
                {
                    mismatches++;
                    continue;
                }

                if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) matches++;
                else mismatches++;
            }

            return mismatches == 0 ? matches : 0;
        }

        private static string SafeGetCellText(ViewSchedule schedule, SectionType section, int row, int column)
        {
            try { return schedule.GetCellText(section, row, column) ?? ""; }
            catch { return ""; }
        }

        private static string SafeColumnHeading(ScheduleField field)
        {
            if (field == null) return "";
            try
            {
                string heading = field.ColumnHeading;
                if (!string.IsNullOrWhiteSpace(heading)) return heading.Trim();
            }
            catch { }
            try { return (field.GetName() ?? "").Trim(); }
            catch { return ""; }
        }

        private static bool SafeIsCalculated(ScheduleField field)
        {
            try { return field != null && field.IsCalculatedField; }
            catch { return false; }
        }

        private static bool SafeIsCombined(ScheduleField field)
        {
            try { return field != null && field.IsCombinedParameterField; }
            catch { return false; }
        }

        private static string FindBottomHeader(IList<List<string>> rows, int column)
        {
            for (int row = rows.Count - 1; row >= 0; row--)
            {
                if (column < rows[row].Count && !string.IsNullOrWhiteSpace(rows[row][column]))
                    return rows[row][column].Trim();
            }
            return "";
        }

        private static string MakeUniqueKey(
            string header,
            int index,
            IDictionary<string, int> used)
        {
            string baseKey = string.IsNullOrWhiteSpace(header) ? "column_" + (index + 1) : header.Trim();
            if (!used.TryGetValue(baseKey, out int count))
            {
                used[baseKey] = 1;
                return baseKey;
            }

            count++;
            used[baseKey] = count;
            return baseKey + "__" + count;
        }

        private static bool IsErpHeader(string header)
        {
            string normalized = Normalize(header).Replace('с', 'c');
            return normalized.Contains("1c-erp") || normalized.Contains("erp code") ||
                   normalized == "erp";
        }

        private static bool IsContextOrAggregateHeader(string header)
        {
            return ContainsAny(header,
                "система", "system", "этап", "stage", "группирован", "group",
                "позиция", "position", "масса", "mass", "примечание", "note",
                "категория", "category", "не учитывать");
        }

        private static bool ContainsAny(string source, params string[] tokens)
        {
            string normalized = Normalize(source);
            return tokens.Any(token => normalized.Contains(Normalize(token)));
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var builder = new StringBuilder(value.Length);
            bool lastWasSpace = false;
            foreach (char ch in value.Trim().ToLowerInvariant())
            {
                bool isSpace = char.IsWhiteSpace(ch) || ch == '\u00A0';
                if (isSpace)
                {
                    if (!lastWasSpace) builder.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    builder.Append(ch);
                    lastWasSpace = false;
                }
            }
            return builder.ToString();
        }
    }
}
