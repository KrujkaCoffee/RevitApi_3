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
        // TODO: подставь реальные адреса/методы из 1С
        private const string BaseUrl = "http://srv-mes:20011";
        //private const string BaseUrl = "http://pow18-08:8000";

        private static string TreeUrl = $"{BaseUrl}/api/v1/revit/types/";
        private static string CodesUrl = $"{BaseUrl}/api/v1/revit/nomens";
        private static string TypesUrl = $"{BaseUrl}/api/v1/revit/nomen/kind/form/";
        private static string UnitsUrl = $"{BaseUrl}/api/v1/revit/nomen/units/form/";
        private static string CreateUrl = $"{BaseUrl}/api/v1/revit/nomen/create/";
        private static string ExportResourcesUrl = $"{BaseUrl}/api/v1/revit/resource/create/";
        private static string ValidateUrl = $"{BaseUrl}/api/v1/revit/resource/validate/";
        private static string ValidateNomenclatureUrl = $"{BaseUrl}/api/v1/revit/nomen/validate/";
        private static string StagesUrl = $"{BaseUrl}/api/v1/revit/nomen/stages/form/";
        private static string LinkCheckUrl = $"{BaseUrl}/api/v1/revit/resource/link_exists/";


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
            string response = PostJson(StagesUrl, json, out code); // StagesUrl добавь как const

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
            string response = PostJson(CodesUrl, json, out _);

            var dtos = JsonConvert.DeserializeObject<List<ErpItemDto>>(response)
                       ?? new List<ErpItemDto>();

            return dtos.Select(d => new ErpItem
            {
                Code = d.Code,
                Name = d.Name,
                Unit = d.Unit
            }).ToList();
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
                // предполагаем, что вернётся { Code, Name, ... }
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
            IEnumerable<ExportRow> rows,
            ErpItem outputProduct)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (outputProduct == null)
                throw new ArgumentNullException(nameof(outputProduct));

            var rowList = rows.Select(r => new
            {
                Stage = r.Stage, // RefKey этапа (из ComboBox)
                r.FamilyName,
                r.TypeName,
                r.DisplayName,
                r.ErpCode,
                r.Unit,
                Quantity = r.QuantityText,
            }).ToList();

            var payload = new
            {
                action = "upload_resource_map",
                title = title,
                context = context,
                creator = authorFullName,
                start_date = startDate,
                end_date = endDate,
                output_product = new
                {
                    code = outputProduct.Code,
                    name = outputProduct.Name,
                    unit = outputProduct.Unit
                },
                rows = rowList
            };

            string json = JsonConvert.SerializeObject(payload);
            HttpStatusCode statusCode;
            string response = PostJson(ExportResourcesUrl, json, out statusCode);
            if (statusCode != HttpStatusCode.OK) {
                return null;
            }
            return response;
        }

        internal class ResourceTableError
        {
            public int Row { get; set; }
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
            IEnumerable<ExportRow> rows,
            ErpItem outputProduct)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));
            if (outputProduct == null)
                throw new ArgumentNullException(nameof(outputProduct));

            var rowList = rows.Select(r => new
            {
                Stage = r.Stage,
                r.FamilyName,
                r.TypeName,
                r.DisplayName,
                r.ErpCode,
                r.Unit,
                Quantity = r.QuantityText,
            }).ToList();

            var payload = new
            {
                action = "upload_resource_map", // как при экспорте (по твоему требованию)
                title = title,
                context = context,
                creator = authorFullName,
                start_date = startDate,
                end_date = endDate,
                output_product = new
                {
                    code = outputProduct.Code,
                    name = outputProduct.Name,
                    unit = outputProduct.Unit
                },
                rows = rowList
            };

            string json = JsonConvert.SerializeObject(payload);

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
                        string msg = "";

                        var o = x as JObject;
                        if (o != null)
                        {
                            row = o["row"] != null ? (int)o["row"] : 0;
                            msg = (o["msg"] ?? "").ToString();
                        }

                        if (row > 0 && !string.IsNullOrWhiteSpace(msg))
                            result.TableErrors.Add(new ResourceTableError { Row = row, Msg = msg });
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
