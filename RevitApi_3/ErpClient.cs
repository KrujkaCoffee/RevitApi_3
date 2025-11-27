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
        // TODO: подставь свои реальные адреса
        private const string CodesUrl = "http://localhost:8000/";
        private const string ExportUrl = "http://localhost:8000/accept";

        /// <summary>
        /// Загружает список номенклатур из 1C-ERP.
        /// </summary>
        public static List<ErpItem> LoadErpItems()
        {
            var requestObj = new { action = "get_nomenclature_list" };
            string json = JsonConvert.SerializeObject(requestObj);

            using (WebClient wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";

                string response = wc.UploadString(CodesUrl, "POST", json);
                List<ErpItem> items = JsonConvert.DeserializeObject<List<ErpItem>>(response);
                return items ?? new List<ErpItem>();
            }
        }

        /// <summary>
        /// Выгружает ресурсную (маппинг Revit → ERP) в 1C-ERP.
        /// </summary>
        public static string ExportResources(string title,
                                             string context,
                                             IEnumerable<RevitItem> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

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
                string response = wc.UploadString(ExportUrl, "POST", json);
                return response;
            }
        }
    }
}
