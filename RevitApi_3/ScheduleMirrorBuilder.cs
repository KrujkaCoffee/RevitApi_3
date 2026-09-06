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
        private const string ElementMarkerPrefix = "__ERP_REVIT_ELEMENT__";

        private sealed class ElementProfile
        {
            public RevitItem Item { get; set; }
            public Dictionary<int, string> Values { get; } = new Dictionary<int, string>();
        }

        private sealed class ProfileCluster
        {
            public ElementProfile Sample { get; set; }
            public List<ElementProfile> Profiles { get; } = new List<ElementProfile>();
        }

        private sealed class RowComparison
        {
            public int Matches { get; set; }
            public int Mismatches { get; set; }
            public List<int> MismatchColumns { get; } = new List<int>();
            public Dictionary<int, string> ExpectedValues { get; } = new Dictionary<int, string>();
            public bool IsExact => Matches > 0 && Mismatches == 0;
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
            BuildColumns(doc, schedule, header, visibleFields, columnCount, erpParameterGuid, result);
            ReadBodyRows(schedule, body, columnCount, result.Rows);
            AssociateRowsWithElements(doc, schedule, result, erpParameterGuid);

            int matched = result.Rows.Count(x => x.IsResourceRow && x.CanWriteErpCode);
            int unmatched = result.Rows.Count(x => x.IsResourceRow && !x.CanWriteErpCode);
            int noExactMatch = result.Rows.Count(x => x.MatchState == "no_exact_match");
            int insufficientIdentity = result.Rows.Count(x => x.MatchState == "insufficient_identity");
            var diagnosticParts = new List<string>();
            if (!result.HasExactFieldMapping)
            {
                diagnosticParts.Add(
                    $"Revit вернул {columnCount} видимых колонок, а Definition — {visibleFields.Count}. " +
                    "Текст ячеек показан точно, но метаданные колонок сопоставлены не полностью.");
            }
            if (unmatched > 0 && !string.IsNullOrWhiteSpace(result.AssociationDiagnostic))
                diagnosticParts.Add(result.AssociationDiagnostic);
            if (unmatched > 0)
            {
                diagnosticParts.Add(
                    $"Связано строк: {matched}; заблокировано: {unmatched} " +
                    $"(нет точного совпадения: {noExactMatch}, недостаточно признаков: {insufficientIdentity}). " +
                    "Причина каждой строки показана в колонке «Связь с Revit».");
            }
            result.Diagnostic = string.Join(" ", diagnosticParts);

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
            int firstRow = header.FirstRowNumber;
            int firstColumn = header.FirstColumnNumber;
            for (int rowOffset = 0; rowOffset < header.NumberOfRows; rowOffset++)
            {
                var values = new List<string>(columnCount);
                int row = firstRow + rowOffset;
                for (int columnOffset = 0; columnOffset < columnCount; columnOffset++)
                    values.Add(SafeGetCellText(
                        schedule,
                        SectionType.Header,
                        row,
                        firstColumn + columnOffset));
                target.Add(values);
            }
        }

        private static void BuildColumns(
            Document doc,
            ViewSchedule schedule,
            TableSectionData headerSection,
            List<ScheduleField> fields,
            int columnCount,
            Guid erpParameterGuid,
            ScheduleMirrorTable target)
        {
            var usedKeys = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < columnCount; index++)
            {
                ScheduleField field = fields.Count == columnCount ? fields[index] : null;
                string fieldName = SafeFieldName(field);
                string header = SafeColumnHeading(field);
                if (string.IsNullOrWhiteSpace(header))
                    header = FindBottomHeader(target.HeaderRows, index);
                if (string.IsNullOrWhiteSpace(header))
                    header = "Колонка " + (index + 1);

                List<string> headerPath = BuildHeaderPath(
                    schedule, headerSection, index, columnCount, fieldName, header);
                string semanticText = string.Join(" ",
                    headerPath.Concat(new[] { fieldName, header })
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                string keySource = IsOrdinalHeader(header) && !string.IsNullOrWhiteSpace(fieldName)
                    ? fieldName
                    : header;
                string key = MakeUniqueKey(keySource, index, usedKeys);
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
                                  StringComparison.OrdinalIgnoreCase)) || IsErpHeader(semanticText);
                bool isQuantity = ContainsAny(
                    semanticText, "количество", "кол-во", "quantity", "qty", "count");
                bool isUnit = ContainsAny(
                    semanticText, "единица измерения", "ед. изм", "unit");
                bool isStrongIdentity = !isErp && !isQuantity && !isUnit &&
                    ContainsAny(semanticText,
                        "наименование", "name", "марка", "mark", "тип", "type",
                        "семейство", "family", "код изделия", "product code",
                        "артикул", "article", "обозначение", "designation",
                        "размер", "size", "диаметр", "diameter", "гост");

                var mirrorColumn = new ScheduleMirrorColumn
                {
                    Index = index,
                    Key = key,
                    Header = header,
                    FieldName = fieldName,
                    ParameterId = parameterId,
                    ParameterGuid = parameterGuid,
                    FieldType = field?.FieldType.ToString() ?? "",
                    IsCalculated = SafeIsCalculated(field),
                    IsCombined = SafeIsCombined(field),
                    IsErpCode = isErp,
                    IsQuantity = isQuantity,
                    IsUnit = isUnit,
                    IsStrongIdentity = isStrongIdentity,
                    RevitField = field
                };
                mirrorColumn.HeaderPath.AddRange(headerPath);
                target.Columns.Add(mirrorColumn);
            }
        }

        private static void ReadBodyRows(
            ViewSchedule schedule,
            TableSectionData body,
            int columnCount,
            List<ScheduleMirrorRow> target)
        {
            int firstRow = body.FirstRowNumber;
            int firstColumn = body.FirstColumnNumber;
            for (int rowOffset = 0; rowOffset < body.NumberOfRows; rowOffset++)
            {
                int row = firstRow + rowOffset;
                var mirrorRow = new ScheduleMirrorRow { SourceRowNumber = rowOffset + 1 };
                for (int columnOffset = 0; columnOffset < columnCount; columnOffset++)
                    mirrorRow.Values.Add(SafeGetCellText(
                        schedule,
                        SectionType.Body,
                        row,
                        firstColumn + columnOffset));
                target.Add(mirrorRow);
            }
        }

        private static void AssociateRowsWithElements(
            Document doc,
            ViewSchedule schedule,
            ScheduleMirrorTable table,
            Guid erpParameterGuid)
        {
            List<RevitItem> items = RevitCollectors.CollectFromSchedule(doc, schedule);
            List<int> identityColumns = GetIdentityColumns(table.Columns);
            List<ElementProfile> profiles = BuildElementProfiles(
                doc, schedule, table, items, identityColumns, erpParameterGuid,
                out string profileDiagnostic);
            table.AssociationDiagnostic = profileDiagnostic;

            List<ProfileCluster> clusters = profiles
                .GroupBy(x => MakeProfileKey(x, identityColumns), StringComparer.OrdinalIgnoreCase)
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .Select(group =>
                {
                    var cluster = new ProfileCluster
                    {
                        Sample = group.First()
                    };
                    cluster.Profiles.AddRange(group);
                    return cluster;
                })
                .ToList();

            int erpColumn = table.ErpCodeColumnIndex;
            foreach (ScheduleMirrorRow row in table.Rows)
            {
                string visibleCode = erpColumn >= 0 ? row.GetValue(erpColumn).Trim() : "";
                int populatedIdentity = identityColumns.Count(index =>
                    !string.IsNullOrWhiteSpace(row.GetValue(index)));
                bool hasStrongIdentity = identityColumns.Any(index =>
                    table.Columns[index].IsStrongIdentity &&
                    !string.IsNullOrWhiteSpace(row.GetValue(index)));
                int minimumEvidence = hasStrongIdentity || schedule.Definition.IsItemized ? 1 : 2;

                int bestExactScore = 0;
                var bestClusters = new List<ProfileCluster>();
                RowComparison nearest = null;

                foreach (ProfileCluster cluster in clusters)
                {
                    RowComparison comparison = CompareRow(row, cluster.Sample, identityColumns);
                    if (nearest == null || comparison.Matches > nearest.Matches ||
                        (comparison.Matches == nearest.Matches &&
                         comparison.Mismatches < nearest.Mismatches))
                    {
                        nearest = comparison;
                    }

                    if (!comparison.IsExact) continue;
                    if (comparison.Matches > bestExactScore)
                    {
                        bestExactScore = comparison.Matches;
                        bestClusters.Clear();
                        bestClusters.Add(cluster);
                    }
                    else if (comparison.Matches == bestExactScore)
                    {
                        bestClusters.Add(cluster);
                    }
                }

                if (bestExactScore >= minimumEvidence && bestClusters.Count > 0)
                {
                    List<ElementProfile> matchedProfiles = bestClusters
                        .SelectMany(x => x.Profiles)
                        .ToList();
                    List<int> elementIds = matchedProfiles
                        .Select(x => x.Item.ElementId.IntegerValue)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    List<string> codes = matchedProfiles
                        .Select(x => (x.Item.ErpCode ?? "").Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    row.IsResourceRow = true;
                    row.MatchState = "matched";
                    row.MatchedFieldCount = bestExactScore;
                    row.ElementIds.AddRange(elementIds);
                    row.AssociationKey = string.Join(",", elementIds);
                    row.ErpCode = codes.Count == 1
                        ? codes[0]
                        : codes.Count > 1 ? "-" : visibleCode;
                    row.OriginalErpCode = row.ErpCode;
                    row.MatchInfo = bestClusters.Count == 1
                        ? $"Связано элементов Revit: {elementIds.Count}; совпавших полей: {bestExactScore}."
                        : $"Связано элементов Revit: {elementIds.Count}; объединено визуально " +
                          $"неразличимых групп: {bestClusters.Count}; совпавших полей: {bestExactScore}.";
                    continue;
                }

                int populatedTotal = row.Values.Count(x => !string.IsNullOrWhiteSpace(x));
                bool looksLikeAggregate = IsAggregateRow(row);
                bool looksLikeResource = !looksLikeAggregate &&
                    (populatedIdentity >= 2 || hasStrongIdentity ||
                     !string.IsNullOrWhiteSpace(visibleCode) ||
                     (schedule.Definition.IsItemized && populatedTotal >= 2 && populatedIdentity > 0));

                if (!looksLikeResource)
                {
                    row.MatchState = "not_resource";
                    row.MatchInfo = looksLikeAggregate
                        ? "Заголовок или итоговая строка; запись ERP-кода не требуется."
                        : "Служебная строка спецификации; номенклатурные признаки отсутствуют.";
                    continue;
                }

                row.IsResourceRow = true;
                row.ErpCode = visibleCode;
                row.OriginalErpCode = visibleCode;
                row.MatchedFieldCount = bestExactScore;

                if (bestExactScore > 0 && bestExactScore < minimumEvidence)
                {
                    row.MatchState = "insufficient_identity";
                    row.MatchInfo =
                        $"Недостаточно признаков для безопасной связи: совпало {bestExactScore}, " +
                        $"требуется {minimumEvidence}.";
                }
                else
                {
                    row.MatchState = "no_exact_match";
                    row.MatchInfo = BuildNoMatchInfo(table, row, nearest);
                }
            }
        }

        /// <summary>
        /// Основной источник профилей — сама спецификация, временно переведённая
        /// в itemized-режим. В экземплярный ERP-параметр записывается служебная
        /// метка с ElementId, после чтения GetCellText вся транзакция откатывается.
        /// Это позволяет получить те же значения, которые вычисляет Revit для
        /// shared-, типовых, материальных и связанных полей, не угадывая место
        /// хранения параметра.
        /// </summary>
        private static List<ElementProfile> BuildElementProfiles(
            Document doc,
            ViewSchedule schedule,
            ScheduleMirrorTable table,
            IList<RevitItem> items,
            IList<int> identityColumns,
            Guid erpParameterGuid,
            out string diagnostic)
        {
            var result = new List<ElementProfile>();
            var capturedIds = new HashSet<int>();
            int distinctItemCount = items
                .Where(x => x?.ElementId != null)
                .Select(x => x.ElementId.IntegerValue)
                .Distinct()
                .Count();

            string itemizedFailure;
            List<ElementProfile> itemizedProfiles = TryBuildItemizedProfiles(
                doc, schedule, table, items, identityColumns, erpParameterGuid,
                out itemizedFailure);
            result.AddRange(itemizedProfiles);
            foreach (ElementProfile profile in itemizedProfiles)
                capturedIds.Add(profile.Item.ElementId.IntegerValue);

            // Резервный путь нужен для редких видов, где Revit запрещает менять
            // IsItemized, либо для отдельных элементов с недоступным ERP-параметром.
            foreach (RevitItem item in items)
            {
                if (item?.ElementId == null || capturedIds.Contains(item.ElementId.IntegerValue))
                    continue;

                Element instance = doc.GetElement(item.ElementId);
                if (instance == null) continue;
                Element type = doc.GetElement(item.TypeId);

                var profile = new ElementProfile { Item = item };
                foreach (int columnIndex in identityColumns)
                {
                    ScheduleField field = table.Columns[columnIndex].RevitField;
                    profile.Values[columnIndex] =
                        ScheduleFieldValueReader.GetText(doc, instance, type, field);
                }
                result.Add(profile);
            }

            if (capturedIds.Count == distinctItemCount && distinctItemCount > 0)
            {
                diagnostic = "";
            }
            else if (capturedIds.Count > 0)
            {
                diagnostic =
                    $"Точное чтение itemized-строк охватило {capturedIds.Count} из " +
                    $"{distinctItemCount} элементов; для остальных использовано чтение параметров.";
            }
            else
            {
                diagnostic = string.IsNullOrWhiteSpace(itemizedFailure)
                    ? "Revit не вернул itemized-строки с ElementId; использовано резервное чтение параметров."
                    : "Точное чтение itemized-строк недоступно: " + itemizedFailure +
                      " Использовано резервное чтение параметров.";
            }

            return result;
        }

        private static List<ElementProfile> TryBuildItemizedProfiles(
            Document doc,
            ViewSchedule schedule,
            ScheduleMirrorTable table,
            IList<RevitItem> items,
            IList<int> identityColumns,
            Guid erpParameterGuid,
            out string failure)
        {
            var result = new List<ElementProfile>();
            failure = "";

            int markerColumn = table.ErpCodeColumnIndex;
            if (markerColumn < 0 || markerColumn >= table.Columns.Count)
            {
                failure = "в спецификации нет видимой колонки «Код 1C-ERP».";
                return result;
            }

            var itemsByMarker = new Dictionary<string, RevitItem>(StringComparer.Ordinal);
            using (var transaction = new Transaction(doc, "ERP: временная связь строк с ElementId"))
            {
                try
                {
                    if (transaction.Start() != TransactionStatus.Started)
                    {
                        failure = "не удалось начать временную транзакцию.";
                    }
                    else
                    {
                        schedule.Definition.IsItemized = true;

                        foreach (RevitItem item in items
                            .Where(x => x?.ElementId != null)
                            .GroupBy(x => x.ElementId.IntegerValue)
                            .Select(x => x.First()))
                        {
                            Element element = doc.GetElement(item.ElementId);
                            Parameter parameter = ErpParameters.GetCodeParameter(
                                element, erpParameterGuid);
                            if (parameter == null || parameter.IsReadOnly ||
                                parameter.StorageType != StorageType.String)
                                continue;

                            string marker = ElementMarkerPrefix + item.ElementId.IntegerValue;
                            try
                            {
                                if (parameter.Set(marker)) itemsByMarker[marker] = item;
                            }
                            catch { }
                        }

                        if (itemsByMarker.Count == 0)
                        {
                            failure = "ERP-параметр экземпляров недоступен для временной разметки.";
                        }
                        else
                        {
                            doc.Regenerate();
                            TableSectionData body = schedule.GetTableData()
                                .GetSectionData(SectionType.Body);
                            if (body == null || body.NumberOfColumns != table.Columns.Count)
                            {
                                failure = body == null
                                    ? "Revit не вернул секцию Body после itemized-развёртки."
                                    : $"после itemized-развёртки изменилось число колонок " +
                                      $"({body.NumberOfColumns} вместо {table.Columns.Count}).";
                            }
                            else
                            {
                                int firstRow = body.FirstRowNumber;
                                int firstColumn = body.FirstColumnNumber;
                                for (int rowOffset = 0; rowOffset < body.NumberOfRows; rowOffset++)
                                {
                                    int row = firstRow + rowOffset;
                                    string marker = SafeGetCellText(
                                        schedule, SectionType.Body,
                                        row, firstColumn + markerColumn).Trim();
                                    if (!itemsByMarker.TryGetValue(
                                            marker, out RevitItem item))
                                        continue;

                                    var profile = new ElementProfile { Item = item };
                                    foreach (int columnIndex in identityColumns)
                                    {
                                        profile.Values[columnIndex] = SafeGetCellText(
                                            schedule, SectionType.Body,
                                            row, firstColumn + columnIndex);
                                    }
                                    result.Add(profile);
                                }

                                if (result.Count == 0)
                                    failure = "служебные ElementId не появились в строках спецификации.";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failure = Shorten(ex.Message);
                    result.Clear();
                }
                finally
                {
                    if (transaction.GetStatus() == TransactionStatus.Started)
                    {
                        TransactionStatus rollbackStatus = transaction.RollBack();
                        if (rollbackStatus != TransactionStatus.RolledBack)
                            throw new InvalidOperationException(
                                "Revit не подтвердил откат временных ElementId. " +
                                "Операция остановлена; сохранение документа не выполняйте.");
                    }
                }
            }

            bool markerRemained = itemsByMarker.Count > 0 && items
                .Where(x => x?.ElementId != null)
                .Select(x => doc.GetElement(x.ElementId))
                .Where(x => x != null)
                .Select(x => ErpParameters.ReadCode(x, erpParameterGuid))
                .Any(x => (x ?? "").StartsWith(ElementMarkerPrefix, StringComparison.Ordinal));
            if (markerRemained)
                throw new InvalidOperationException(
                    "После отката обнаружена служебная метка ElementId. " +
                    "Операция остановлена; сохранение документа не выполняйте.");

            return result;
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

                string semanticText = GetColumnSemanticText(column);
                if (IsContextOrAggregateHeader(semanticText))
                    continue;

                result.Add(column.Index);
            }
            return result;
        }

        private static string MakeProfileKey(ElementProfile profile, IList<int> identityColumns)
        {
            var builder = new StringBuilder();
            bool hasValue = false;
            foreach (int index in identityColumns)
            {
                profile.Values.TryGetValue(index, out string value);
                string normalized = Normalize(value);
                if (!string.IsNullOrWhiteSpace(normalized)) hasValue = true;
                builder.Append(index).Append('=').Append(normalized).Append('\u001F');
            }
            return hasValue ? builder.ToString().Trim('\u001F') : "";
        }

        private static RowComparison CompareRow(
            ScheduleMirrorRow row,
            ElementProfile profile,
            IList<int> identityColumns)
        {
            var result = new RowComparison();

            foreach (int index in identityColumns)
            {
                profile.Values.TryGetValue(index, out string expectedRaw);
                string expected = Normalize(expectedRaw);
                string actual = Normalize(row.GetValue(index));

                if (string.IsNullOrEmpty(actual)) continue;
                if (string.IsNullOrEmpty(expected))
                {
                    result.Mismatches++;
                    result.MismatchColumns.Add(index);
                    result.ExpectedValues[index] = expectedRaw ?? "";
                    continue;
                }

                if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    result.Matches++;
                }
                else
                {
                    result.Mismatches++;
                    result.MismatchColumns.Add(index);
                    result.ExpectedValues[index] = expectedRaw ?? "";
                }
            }

            return result;
        }

        private static string BuildNoMatchInfo(
            ScheduleMirrorTable table,
            ScheduleMirrorRow row,
            RowComparison nearest)
        {
            if (nearest == null)
                return "Не найдено ни одного профиля элементов для сравнения.";

            string fields = string.Join("; ", nearest.MismatchColumns
                .Take(3)
                .Where(index => index >= 0 && index < table.Columns.Count)
                .Select(index =>
                {
                    nearest.ExpectedValues.TryGetValue(index, out string expected);
                    string header = table.Columns[index].DisplayHeader.Replace('\n', ' ');
                    return $"{header}: «{Shorten(row.GetValue(index))}» ≠ «{Shorten(expected)}»";
                }));
            if (string.IsNullOrWhiteSpace(fields)) fields = "сопоставляемые поля";

            return $"Нет точного совпадения. Лучший кандидат: совпало {nearest.Matches}, " +
                   $"различается {nearest.Mismatches}; поля: {fields}.";
        }

        private static string Shorten(string value)
        {
            value = (value ?? "").Trim();
            return value.Length <= 48 ? value : value.Substring(0, 45) + "…";
        }

        private static bool IsAggregateRow(ScheduleMirrorRow row)
        {
            foreach (string value in row.Values)
            {
                string normalized = Normalize(value);
                if (normalized == "итого" || normalized.StartsWith("итого ") ||
                    normalized == "всего" || normalized.StartsWith("всего ") ||
                    normalized == "total" || normalized.StartsWith("grand total"))
                    return true;
            }
            return false;
        }

        private static List<string> BuildHeaderPath(
            ViewSchedule schedule,
            TableSectionData header,
            int logicalColumn,
            int bodyColumnCount,
            string fieldName,
            string leafHeader)
        {
            var result = new List<string>();
            if (header != null && logicalColumn < header.NumberOfColumns)
            {
                int firstRow = header.FirstRowNumber;
                int firstColumn = header.FirstColumnNumber;
                int lastColumn = firstColumn + header.NumberOfColumns - 1;
                int actualColumn = firstColumn + logicalColumn;

                for (int rowOffset = 0; rowOffset < header.NumberOfRows; rowOffset++)
                {
                    int actualRow = firstRow + rowOffset;
                    string text = SafeGetCellText(
                        schedule, SectionType.Header, actualRow, actualColumn);
                    bool coversWholeHeader = false;

                    try
                    {
                        using (TableMergedCell merged = header.GetMergedCell(actualRow, actualColumn))
                        {
                            if (merged != null)
                            {
                                coversWholeHeader = merged.Left <= firstColumn &&
                                                    merged.Right >= lastColumn;
                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    text = SafeGetCellText(
                                        schedule, SectionType.Header, merged.Top, merged.Left);
                                }
                            }
                        }
                    }
                    catch { }

                    // Первая объединённая строка — заголовок всей спецификации,
                    // а не конкретной колонки. Не дублируем его над каждым полем.
                    if ((rowOffset == 0 && coversWholeHeader && bodyColumnCount > 1) ||
                        string.Equals(
                            Normalize(text), Normalize(schedule?.Name),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    AddHeaderPart(result, text);
                }
            }

            // В некоторых шаблонах содержательные подписи сделаны объединёнными
            // ячейками, а ColumnHeading содержит только номер 1, 2, 3... Если API
            // не вернул верхний уровень, показываем имя исходного поля над номером.
            if ((result.Count == 0 || result.All(IsOrdinalHeader)) &&
                !string.IsNullOrWhiteSpace(fieldName) && !IsOrdinalHeader(fieldName))
                result.Insert(0, fieldName.Trim());

            AddHeaderPart(result, leafHeader);
            if (result.Count == 0) result.Add("Колонка " + (logicalColumn + 1));
            return result;
        }

        private static void AddHeaderPart(ICollection<string> target, string value)
        {
            value = (value ?? "").Trim();
            if (string.IsNullOrWhiteSpace(value)) return;
            if (target.Any(existing => string.Equals(
                    Normalize(existing), Normalize(value), StringComparison.OrdinalIgnoreCase)))
                return;
            target.Add(value);
        }

        private static bool IsOrdinalHeader(string value)
        {
            string normalized = (value ?? "").Trim().TrimEnd('.', ')');
            normalized = normalized.TrimStart('№', '#').Trim();
            return int.TryParse(normalized, out _);
        }

        private static string GetColumnSemanticText(ScheduleMirrorColumn column)
        {
            if (column == null) return "";
            return string.Join(" ", new[]
            {
                column.FieldName,
                column.Header,
                column.DisplayHeader
            }.Where(x => !string.IsNullOrWhiteSpace(x)));
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

        private static string SafeFieldName(ScheduleField field)
        {
            if (field == null) return "";
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
