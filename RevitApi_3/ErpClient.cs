using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace RevitApi_3
{
    internal static class ErpClient
    {
        // TODO: подставь реальные адреса/эндпоинты
        private const string TreeUrl = "http://localhost:8000/types";
        private const string CodesUrl = "http://localhost:8000/nomens";
        private const string ExportUrl = "http://localhost:8000/accept";

        // DTO для дерева (как приходит с REST)
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
            public string Extra { get; set; }
            public string Unit { get; set; }
        }

        /// <summary>
        /// Загружает дерево ERP (Ref_Key / Parent_Key / Description) и собирает его в иерархию.
        /// </summary>
        public static List<ErpTreeNode> LoadErpTree()
        {
            var requestObj = new { action = "get_classifier_tree" };
            string json = JsonConvert.SerializeObject(requestObj);

            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                string response = wc.UploadString(TreeUrl, "POST", json);

                var flat = JsonConvert.DeserializeObject<List<ErpTreeItemDto>>(response) ?? new List<ErpTreeItemDto>();

                // Собираем дерево
                var dict = new Dictionary<string, ErpTreeNode>();
                foreach (var dto in flat)
                {
                    if (string.IsNullOrEmpty(dto.Ref_Key))
                        continue;

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
                    {
                        roots.Add(node);
                    }
                    else
                    {
                        dict[node.ParentKey].Children.Add(node);
                    }
                }

                return roots;
            }
        }

        /// <summary>
        /// Загружает список номенклатур для выбранного узла дерева (по Ref_Key).
        /// </summary>
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

            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                string response = wc.UploadString(CodesUrl, "POST", json);

                var dtos = JsonConvert.DeserializeObject<List<ErpItemDto>>(response) ?? new List<ErpItemDto>();

                return dtos.Select(d => new ErpItem
                {
                    Code = d.Code,
                    Name = d.Name,
                    Extra = d.Extra,
                    Unit = d.Unit
                }).ToList();
            }
        }

        /// <summary>
        /// Выгружает агрегированную ресурсную в 1C-ERP.
        /// </summary>
        public static string ExportResources(string title,
                                             string context,
                                             IEnumerable<ExportRow> rows)
        {
            if (rows == null)
                throw new ArgumentNullException(nameof(rows));

            var rowList = rows.Select(r => new
            {
                r.ScheduleName,
                r.FamilyName,
                r.TypeName,
                r.DisplayName,
                r.ErpCode,
                r.Unit,
                Quantity = r.Quantity,
                MassPerItem = r.MassPerItem,
                TotalMass = r.TotalMass
            }).ToList();

            var payload = new
            {
                action = "upload_resource_map",
                title = title,
                context = context,
                rows = rowList
            };

            string json = JsonConvert.SerializeObject(payload);

            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                string response = wc.UploadString(ExportUrl, "POST", json);
                return response;
            }
        }
    }
}
