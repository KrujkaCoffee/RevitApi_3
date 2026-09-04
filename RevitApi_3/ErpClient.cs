using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitApi_3
{
    internal static class ErpClient
    {
        private const string DefaultBaseUrl = "http://srv-mes:20011";
        private const int RequestTimeoutMs = 45000;
        private static string BaseUrl =>
            (Environment.GetEnvironmentVariable("REVIT_ERP_BASE_URL") ?? DefaultBaseUrl).TrimEnd('/');

        private static string TreeUrl => $"{BaseUrl}/api/v1/revit/types/";
        private static string CodesUrl => $"{BaseUrl}/api/v1/revit/nomens";
        private static string NamesByCodeArrayUrl => $"{BaseUrl}/api/v1/revit/nomens/bycodearray/";
        private static string TypesUrl => $"{BaseUrl}/api/v1/revit/nomen/kind/form/";
        private static string UnitsUrl => $"{BaseUrl}/api/v1/revit/nomen/units/form/";
        private static string CreateUrl => $"{BaseUrl}/api/v1/revit/nomen/create/";
        private static string ExportResourcesUrl => $"{BaseUrl}/api/v1/revit/resource/create/";
        private static string ValidateUrl => $"{BaseUrl}/api/v1/revit/resource/validate/";
        private static string ValidateNomenclatureUrl => $"{BaseUrl}/api/v1/revit/nomen/validate/";
        private static string StagesUrl => $"{BaseUrl}/api/v1/revit/nomen/stages/form/";
        private static string LinkCheckUrl => $"{BaseUrl}/api/v1/revit/resource/link_exists/";


        // DTO для дерева
        private class ErpTreeItemDto
        {
            public string Ref_Key { get; set; }
            public string Parent_Key { get; set; }
            public string Description { get; set; }
        }

        private class ErpItemDto
        {
            public string Code { get; set; }
            public string Name { get; set; }
            public string Unit { get; set; }
        }


        /// <summary>
        /// Проверить, существует ли сущность по link.
        /// Возвращает:
        ///  - true  : сервер явно подтвердил существование
        ///  - false : сервер явно сказал, что ссылка недействительна
        ///  - null  : не булево/ошибка/сбой — ничего не удаляем
        /// </summary>
        public static bool? CheckResourceLinkAlive(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
                return null;

            var payload = new
            {
                action = "check_resource_link",
                link = link
            };

            string json = JsonConvert.SerializeObject(payload);

            try
            {
                HttpStatusCode status;
                string body = PostJson(LinkCheckUrl, json, out status);

                if (status != HttpStatusCode.OK)
                    return null;

                return TryParseBoolFromBody(body);
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryParseBoolFromBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            string t = body.Trim();

            if (bool.TryParse(t, out bool b))
                return b;

            try
            {
                var tok = JToken.Parse(t);

                if (tok.Type == JTokenType.Boolean)
                    return tok.Value<bool>();

                if (tok is JObject o)
                {
                    // поддержим несколько возможных ключей
                    foreach (var key in new[] { "exists", "ok", "valid", "result" })
                    {
                        var v = o[key];
                        if (v != null && v.Type == JTokenType.Boolean)
                            return v.Value<bool>();
                    }
                }
            }
            catch { }

            return null;
        }


        private class RefNamedItemDto
        {
            public string Ref_Key { get; set; }
            public string Description { get; set; }
        }
        public static List<RefNamedItem> LoadStages()
        {
            var requestObj = new { action = "get_stages" };
            string json = JsonConvert.SerializeObject(requestObj);
            HttpStatusCode code;
            string response = PostJson(StagesUrl, json, out code);

            var dtos = JsonConvert.DeserializeObject<List<RefNamedItemDto>>(response)
                       ?? new List<RefNamedItemDto>();

            var result = new List<RefNamedItem>();
            foreach (var dto in dtos)
            {
                if (string.IsNullOrEmpty(dto.Ref_Key)) continue;
                result.Add(new RefNamedItem { RefKey = dto.Ref_Key, Name = dto.Description });
            }
            return result;
        }

        /// <summary>Загрузка дерева классификатора.</summary>
        public static List<ErpTreeNode> LoadErpTree()
        {
            var requestObj = new { action = "get_classifier_tree" };
            string json = JsonConvert.SerializeObject(requestObj);

            string response = PostJson(TreeUrl, json, out _);

            var flat = JsonConvert.DeserializeObject<List<ErpTreeItemDto>>(response)
                       ?? new List<ErpTreeItemDto>();

            var dict = new Dictionary<string, ErpTreeNode>();
            foreach (var dto in flat)
            {
                if (string.IsNullOrEmpty(dto.Ref_Key)) continue;

                var node = new ErpTreeNode
                {
                    RefKey = dto.Ref_Key,
                    ParentKey = dto.Parent_Key,
                    Description = dto.Description
                };
                dict[dto.Ref_Key] = node;
            }

            var roots = new List<ErpTreeNode>();
            foreach (var node in dict.Values)
            {
                if (string.IsNullOrEmpty(node.ParentKey) || !dict.ContainsKey(node.ParentKey))
                    roots.Add(node);
                else
                    dict[node.ParentKey].Children.Add(node);
            }

            return roots;
        }

        /// <summary>Коды (номенклатура) по выбранному узлу дерева.</summary>
        public static List<ErpItem> LoadErpItems(string refKey)
        {
            if (string.IsNullOrEmpty(refKey))
                throw new ArgumentException("RefKey не задан.", nameof(refKey));

            var requestObj = new
            {
                action = "get_nomenclature_list",
                parent_ref = refKey
            };

            string json = JsonConvert.SerializeObject(requestObj);
            HttpStatusCode status;
            string response = PostJson(CodesUrl, json, out status);
            if (status != HttpStatusCode.OK)
                throw new Exception(
                    "Загрузка номенклатуры ERP: статус " + (int)status + ", тело: " + response);

            return ParseErpItems(response);
        }

        /// <summary>
        /// Глобальный поиск без parent_ref. Контракт намеренно использует тот же
        /// /nomens endpoint и action-подход, что существующая выборка по дереву.
        /// </summary>
        public static List<ErpItem> SearchErpItems(string query, int limit)
        {
            query = (query ?? "").Trim();
            if (query.Length < 2) return new List<ErpItem>();
            if (limit < 1) limit = 1;
            if (limit > 500) limit = 500;

            var requestObj = new
            {
                action = "search_nomenclature",
                query = query,
                limit = limit,
                offset = 0
            };

            HttpStatusCode status;
            string response = PostJson(CodesUrl, JsonConvert.SerializeObject(requestObj), out status);
            if (status != HttpStatusCode.OK)
                throw new Exception(
                    "Глобальный поиск ERP: статус " + (int)status + ", тело: " + response);
            return ParseErpItems(response);
        }

        private static List<ErpItem> ParseErpItems(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return new List<ErpItem>();

            JToken token = JToken.Parse(response);
            JArray array = token as JArray;
            if (array == null && token is JObject obj)
            {
                array = obj["items"] as JArray ?? obj["results"] as JArray ??
                        obj["data"] as JArray ?? obj["nomenclature"] as JArray;
            }
            if (array == null) return new List<ErpItem>();

            var result = new List<ErpItem>();
            foreach (JToken item in array)
            {
                string code = FirstString(item, "Code", "code", "Код");
                string name = FirstString(item, "Name", "name", "Description", "description", "Наименование");
                if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name)) continue;

                result.Add(new ErpItem
                {
                    Code = code,
                    Name = name,
                    Unit = FirstString(item, "Unit", "unit", "UnitName", "unit_name", "Единица"),
                    Extra = FirstString(item, "Extra", "extra", "Article", "article", "Артикул")
                });
            }
            return result;
        }

        private static string FirstString(JToken token, params string[] names)
        {
            if (!(token is JObject obj)) return "";
            foreach (string name in names)
            {
                JToken value = obj[name];
                if (value != null && value.Type != JTokenType.Null)
                    return value.ToString().Trim();
            }
            return "";
        }


        /// <summary>
        /// Пакетно получить наименования номенклатур по массиву кодов 1C-ERP.
        /// Ожидаемый ответ (пример):
        /// {
        ///   "00-01": { "name": "..." },
        ///   "00-02": { "name": "..." }
        /// }
        /// </summary>
        public static Dictionary<string, string> LoadNomenclatureNamesByCodes(IEnumerable<string> codes)
        {
            var list = (codes ?? Enumerable.Empty<string>())
                .Select(x => (x ?? "").Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x) && x != "-")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (list.Count == 0) return result;

            var requestObj = new
            {
                action = "get_nomenclature_names_by_codes",
                codes = list
            };

            string json = JsonConvert.SerializeObject(requestObj);

            string response = PostJson(NamesByCodeArrayUrl, json, out _);

            try
            {
                var jo = JObject.Parse(response);
                foreach (var p in jo.Properties())
                {
                    string code = (p.Name ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(code)) continue;

                    string name = "";
                    if (p.Value is JObject o)
                    {
                        name = (o["name"] ?? o["Name"] ?? o["description"] ?? o["Description"])?.ToString() ?? "";
                    }
                    else if (p.Value != null)
                    {
                        // иногда могут вернуть просто строку
                        name = p.Value.ToString();
                    }

                    if (!string.IsNullOrWhiteSpace(name))
                        result[code] = name;
                }

                return result;
            }
            catch
            {
                // 2) альтернативный формат: массив [{code:"", name:""}]
                try
                {
                    var arr = JArray.Parse(response);
                    foreach (var it in arr)
                    {
                        if (it is JObject o)
                        {
                            string code = (o["code"] ?? o["Code"])?.ToString()?.Trim() ?? "";
                            string name = (o["name"] ?? o["Name"] ?? o["description"] ?? o["Description"])?.ToString() ?? "";
                            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(name))
                                result[code] = name;
                        }
                    }
                }
                catch
                {
                    // игнор — вернём пустой/частичный result
                }

                return result;
            }
        }

        /// <summary>Типы номенклатуры.</summary>
        public static List<RefNamedItem> LoadNomenclatureTypes()
        {
            var requestObj = new { action = "get_nomenclature_types" };
            string json = JsonConvert.SerializeObject(requestObj);
            string response = PostJson(TypesUrl, json, out _);

            var dtos = JsonConvert.DeserializeObject<List<RefNamedItemDto>>(response)
                       ?? new List<RefNamedItemDto>();

            var result = new List<RefNamedItem>();
            foreach (var dto in dtos)
            {
                if (string.IsNullOrEmpty(dto.Ref_Key)) continue;
                result.Add(new RefNamedItem
                {
                    RefKey = dto.Ref_Key,
                    Name = dto.Description
                });
            }
            return result;
        }

        /// <summary>Единицы измерения.</summary>
        public static List<RefNamedItem> LoadUnits()
        {
            var requestObj = new { action = "get_units" };
            string json = JsonConvert.SerializeObject(requestObj);
            string response = PostJson(UnitsUrl, json, out _);

            var dtos = JsonConvert.DeserializeObject<List<RefNamedItemDto>>(response)
                       ?? new List<RefNamedItemDto>();

            var result = new List<RefNamedItem>();
            foreach (var dto in dtos)
            {
                if (string.IsNullOrEmpty(dto.Ref_Key)) continue;
                result.Add(new RefNamedItem
                {
                    RefKey = dto.Ref_Key,
                    Name = dto.Description
                });
            }
            return result;
        }

        /// <summary>Создание новой номенклатуры (выходного изделия).</summary>
        /// <returns>Созданный объект (имя+код).</returns>
        public static ErpItem CreateNomenclature(
            string kindRef, string typeRef, string unitRef,
            string name, string article)
        {
            var payload = new
            {
                action = "create_nomenclature",
                kind_ref = kindRef,
                type_ref = typeRef,
                unit_ref = unitRef,
                name = name,
                article = article
            };

            string json = JsonConvert.SerializeObject(payload);

            HttpStatusCode statusCode;
            string response = PostJson(CreateUrl, json, out statusCode);

            if (statusCode == HttpStatusCode.Created) // 201
            {
                var dto = JsonConvert.DeserializeObject<ErpItemDto>(response);
                if (dto == null)
                    throw new Exception("Пустой ответ от сервера при создании номенклатуры.");

                return new ErpItem
                {
                    Code = dto.Code,
                    Name = dto.Name,
                    Unit = dto.Unit,
                };
            }

            if (statusCode == HttpStatusCode.BadRequest) // 400
            {
                // ожидаем: { "field": ["err1","err2"], ... }
                var dictRaw = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(response)
                              ?? new Dictionary<string, string[]>();

                var dict = new Dictionary<string, List<string>>();
                foreach (var kv in dictRaw)
                    dict[kv.Key] = new List<string>(kv.Value ?? new string[0]);

                throw new ErpValidationException(dict);
            }

            throw new Exception("Неуспешный код ответа при создании номенклатуры: " + (int)statusCode);
        }

        /// <summary>Экспорт ресурсной с учётом выходного изделия.</summary>
        public static string ExportResources(
            string title,
            string context,
            string startDate,
            string endDate,
            string authorFullName,
            ScheduleMirrorTable table,
            ErpItem outputProduct)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            if (outputProduct == null)
                throw new ArgumentNullException(nameof(outputProduct));

            JObject payload = BuildResourcePayload(
                title, context, startDate, endDate, authorFullName, table, outputProduct);
            string json = payload.ToString(Formatting.None);
            HttpStatusCode statusCode;
            string response = PostJson(ExportResourcesUrl, json, out statusCode);
            if (statusCode != HttpStatusCode.OK && statusCode != HttpStatusCode.Created)
                throw new Exception(
                    "ExportResources: неожиданный статус " + (int)statusCode + ", тело: " + response);
            return response;
        }

        internal class ResourceTableError
        {
            public int Row { get; set; }
            public int SourceRow { get; set; }
            public string Msg { get; set; }
        }

        internal class ResourceValidationResult
        {
            public Dictionary<string, string> FieldErrors { get; set; } = new Dictionary<string, string>();
            public List<ResourceTableError> TableErrors { get; set; } = new List<ResourceTableError>();
            public bool HasErrors =>
                (FieldErrors != null && FieldErrors.Count > 0) ||
                (TableErrors != null && TableErrors.Count > 0);
        }

        /// <summary>
        /// Проверка ресурсной (тот же payload что экспорт), ожидаем:
        /// { field_errors: {..}, table_errors: [{row:2,msg:".."}, ...] }
        /// </summary>
        public static ResourceValidationResult ValidateResources(
            string title,
            string context,
            string startDate,
            string endDate,
            string authorFullName,
            ScheduleMirrorTable table,
            ErpItem outputProduct)
        {
            if (table == null)
                throw new ArgumentNullException(nameof(table));
            if (outputProduct == null)
                throw new ArgumentNullException(nameof(outputProduct));

            JObject payload = BuildResourcePayload(
                title, context, startDate, endDate, authorFullName, table, outputProduct);
            string json = payload.ToString(Formatting.None);

            HttpStatusCode status;
            string body = PostJson(ValidateUrl, json, out status);

            var result = new ResourceValidationResult();

            // если сервер вернул JSON с ошибками — распарсим
            try
            {
                var jo = JObject.Parse(body);

                var fe = jo["field_errors"] as JObject;
                if (fe != null)
                {
                    foreach (var p in fe.Properties())
                        result.FieldErrors[p.Name] = (p.Value ?? "").ToString();
                }

                var te = jo["table_errors"] as JArray;
                if (te != null)
                {
                    foreach (var x in te)
                    {
                        int row = 0;
                        int sourceRow = 0;
                        string msg = "";

                        var o = x as JObject;
                        if (o != null)
                        {
                            row = o["row"] != null ? (int)o["row"] : 0;
                            sourceRow = o["source_row"] != null ? (int)o["source_row"] : 0;
                            msg = (o["msg"] ?? "").ToString();
                        }

                        if ((row > 0 || sourceRow > 0) && !string.IsNullOrWhiteSpace(msg))
                            result.TableErrors.Add(new ResourceTableError
                            {
                                Row = row,
                                SourceRow = sourceRow,
                                Msg = msg
                            });
                    }
                }
            }
            catch
            {
                if (status == HttpStatusCode.OK)
                    return result;

                throw new Exception("ValidateResources: сервер вернул не-JSON: " + body);
            }

            if (status == HttpStatusCode.OK)
                return result;

            if (status == HttpStatusCode.BadRequest)
                return result;

            throw new Exception("ValidateResources: неожиданный статус " + (int)status + ", тело: " + body);
        }

        /// <summary>
        /// Единственный конструктор payload для validate/create. В contract v2
        /// передаётся полный снимок таблицы и одновременно сохраняются legacy-поля
        /// rows, чтобы сервер можно было обновлять поэтапно.
        /// </summary>
        private static JObject BuildResourcePayload(
            string title,
            string context,
            string startDate,
            string endDate,
            string authorFullName,
            ScheduleMirrorTable table,
            ErpItem outputProduct)
        {
            var columns = new JArray(table.Columns
                .OrderBy(x => x.Index)
                .Select(column => JObject.FromObject(new
                {
                    order = column.Index,
                    key = column.Key,
                    header = column.Header,
                    parameter_id = column.ParameterId,
                    parameter_guid = column.ParameterGuid,
                    field_type = column.FieldType,
                    is_calculated = column.IsCalculated,
                    is_combined = column.IsCombined,
                    is_erp_code = column.IsErpCode,
                    is_quantity = column.IsQuantity,
                    is_unit = column.IsUnit
                })));

            var bodyRows = new JArray();
            foreach (ScheduleMirrorRow row in table.Rows)
            {
                bodyRows.Add(new JObject
                {
                    ["source_row"] = row.SourceRowNumber,
                    ["is_resource_row"] = row.IsResourceRow,
                    ["cells"] = JArray.FromObject(row.Values)
                });
            }

            var resourceRows = new JArray();
            int exportRow = 0;
            foreach (ScheduleMirrorRow row in table.ResourceRows)
            {
                exportRow++;
                JObject values = BuildValuesObject(table, row);
                resourceRows.Add(new JObject
                {
                    ["row"] = exportRow,
                    ["source_row"] = row.SourceRowNumber,
                    ["stage"] = row.Stage ?? "",
                    ["Stage"] = row.Stage ?? "",
                    ["erp_code"] = row.ErpCode ?? "",
                    ["ErpCode"] = row.ErpCode ?? "",
                    ["unit"] = FindValue(table, row, "единица измерения", "ед. изм", "unit"),
                    ["Unit"] = FindValue(table, row, "единица измерения", "ед. изм", "unit"),
                    ["quantity"] = FindValue(table, row, "количество", "кол-во", "quantity", "qty", "count"),
                    ["Quantity"] = FindValue(table, row, "количество", "кол-во", "quantity", "qty", "count"),
                    ["FamilyName"] = FindValue(table, row, "семейство", "family"),
                    ["TypeName"] = FindValue(table, row, "тип", "type"),
                    ["DisplayName"] = FindValue(table, row, "наименование", "name"),
                    ["values"] = values,
                    ["cells"] = JArray.FromObject(row.Values),
                    ["element_ids"] = new JArray(row.ElementIds)
                });
            }

            return new JObject
            {
                ["action"] = "upload_resource_map",
                ["contract_version"] = 2,
                ["title"] = title ?? "",
                ["context"] = context ?? "",
                ["creator"] = authorFullName ?? "",
                ["start_date"] = startDate ?? "",
                ["end_date"] = endDate ?? "",
                ["output_product"] = JObject.FromObject(new
                {
                    code = outputProduct.Code,
                    name = outputProduct.Name,
                    unit = outputProduct.Unit
                }),
                ["schedule"] = new JObject
                {
                    ["element_id"] = table.ScheduleElementId,
                    ["name"] = table.ScheduleName ?? "",
                    ["field_mapping_exact"] = table.HasExactFieldMapping,
                    ["diagnostic"] = table.Diagnostic ?? "",
                    ["header_rows"] = JArray.FromObject(table.HeaderRows),
                    ["columns"] = columns,
                    ["body_rows"] = bodyRows
                },
                ["rows"] = resourceRows
            };
        }

        private static JObject BuildValuesObject(ScheduleMirrorTable table, ScheduleMirrorRow row)
        {
            var result = new JObject();
            foreach (ScheduleMirrorColumn column in table.Columns.OrderBy(x => x.Index))
                result[column.Key] = row.GetValue(column.Index);
            return result;
        }

        private static string FindValue(
            ScheduleMirrorTable table,
            ScheduleMirrorRow row,
            params string[] tokens)
        {
            foreach (ScheduleMirrorColumn column in table.Columns)
            {
                string header = (column.Header ?? "").ToLowerInvariant();
                if (tokens.Any(token => header.Contains((token ?? "").ToLowerInvariant())))
                    return row.GetValue(column.Index);
            }
            return "";
        }


        public static Dictionary<string, string> ValidateNomenclature(
            string kindRef, string typeRef, string unitRef,
            string name, string article)
        {
            // Отправляем “алиасы” (как ты просил) + дублируем ref-ключи
            var payload = new
            {
                action = "validate_nomenclature",

                kind_ref = kindRef,
                type_ref = typeRef,
                unit_ref = unitRef,
                name = name,
                article = article,
            };

            string json = JsonConvert.SerializeObject(payload);

            HttpStatusCode status;
            string body = PostJson(ValidateNomenclatureUrl, json, out status);

            if (status == HttpStatusCode.OK)
                return new Dictionary<string, string>(); // всё ок

            if (status == HttpStatusCode.BadRequest)
            {
                // ожидаем { "FieldName": "error text", ... }
                var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(body)
                           ?? new Dictionary<string, string>();
                return dict;
            }

            throw new Exception("ValidateNomenclature: неожиданный статус " + (int)status);
        }


        /// <summary>
        /// Вспомогательный метод POST JSON с возвратом тела и кода статуса.
        /// </summary>
        private static string PostJson(string url, string json, out HttpStatusCode statusCode)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Timeout = RequestTimeoutMs;
            request.ReadWriteTimeout = RequestTimeoutMs;

            byte[] data = Encoding.UTF8.GetBytes(json);
            request.ContentLength = data.Length;

            using (var reqStream = request.GetRequestStream())
            {
                reqStream.Write(data, 0, data.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    statusCode = response.StatusCode;
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string body = reader.ReadToEnd();
                        statusCode = resp.StatusCode;
                        return body;
                    }
                }

                statusCode = 0;
                throw;
            }
        }
    }
}
