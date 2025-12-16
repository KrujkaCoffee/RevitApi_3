using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace RevitApi_3
{
    internal static class ErpClient
    {
        // TODO: подставь реальные адреса/методы из 1С
        private const string TreeUrl = "http://pow18-08:8000/types";
        private const string CodesUrl = "http://pow18-08:8000/nomens";
        private const string TypesUrl = "http://pow18-08:8000/nomen/kind/form/";
        private const string UnitsUrl = "http://pow18-08:8000/nomen/units/form/";
        private const string CreateUrl = "http://pow18-08:8000/nomen/create/";
        private const string ExportResourcesUrl = "http://pow18-08:8000/accept";
        private const string ValidateUrl = "http://pow18-08:8000/resource/validate/";
        private const string ValidateNomenclatureUrl = "http://pow18-08:8000/nomen/validate/";

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

        private class RefNamedItemDto
        {
            public string Ref_Key { get; set; }
            public string Description { get; set; }
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
                r.ScheduleName,
                r.FamilyName,
                r.TypeName,
                r.DisplayName,
                r.ErpCode,
                r.Unit,
                Quantity = r.QuantityText,
                MassPerItem = r.MassPerItemText,
                TotalMass = r.TotalMassText
            }).ToList();

            var payload = new
            {
                action = "upload_resource_map",
                title = title,
                context = context,
                creator = authorFullName,
                start_date = startDate,
                output_product = new
                {
                    code = outputProduct.Code,
                    name = outputProduct.Name,
                    unit = outputProduct.Unit
                },
                rows = rowList
            };

            string json = JsonConvert.SerializeObject(payload);
            string response = PostJson(ExportResourcesUrl, json, out _);
            return response;
        }

        public static Dictionary<string, string> ValidateNomenclature(
            string kindRef, string typeRef, string unitRef,
            string name, string article)
        {
            // Отправляем “алиасы” (как ты просил) + дублируем ref-ключи
            var payload = new
            {
                action = "validate_nomenclature",

                // “тех” поля (ref)
                kind_ref = kindRef,
                type_ref = typeRef,
                unit_ref = unitRef,

                // “человекочитаемые” поля
                Наименование = name,
                Артикул = article,
                ВидНоменклатуры = kindRef,
                ТипНоменклатуры = typeRef,
                ЕдиницаИзмерения = unitRef
            };

            string json = JsonConvert.SerializeObject(payload);

            HttpStatusCode status;
            string body = PostJson(ValidateUrl, json, out status);

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
