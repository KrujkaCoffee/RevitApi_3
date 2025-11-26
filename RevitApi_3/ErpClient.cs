using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace RevitApi_3
{
    public static class ErpClient
    {
        /// <summary>
        /// Загружает список номенклатур из 1C-ERP.
        /// Сейчас payload простой: { action = "get_nomenclature_list" }.
        /// </summary>
        public static List<ErpItem> LoadErpItems(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL не задан.", "url");

            var requestObj = new { action = "get_nomenclature_list" };
            string json = JsonConvert.SerializeObject(requestObj);

            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                string response = wc.UploadString(url, "POST", json);

                List<ErpItem> items = JsonConvert.DeserializeObject<List<ErpItem>>(response);
                if (items == null) items = new List<ErpItem>();
                return items;
            }
        }

        /// <summary>
        /// Выгружает ресурсную (маппинг Revit → ERP) в 1C-ERP.
        /// Отправляет title, context и строки с кодами ERP.
        /// </summary>
        public static string ExportResources(string url,
                                             string title,
                                             string context,
                                             IEnumerable<RevitItem> items)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("URL не задан.", "url");
            if (items == null)
                throw new ArgumentNullException("items");

            var rows = items.Select(i => new
            {
                ScheduleName = i.ScheduleName,
                FamilyName = i.FamilyName,
                TypeName = i.TypeName,
                DisplayName = i.DisplayName,
                ErpCode = i.ErpCode
            }).ToList();

            var payload = new
            {
                action = "upload_resource_map",
                title = title,
                context = context,
                rows = rows
            };

            string json = JsonConvert.SerializeObject(payload);

            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                string response = wc.UploadString(url, "POST", json);
                return response;
            }
        }
    }
}
