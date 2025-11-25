using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace RevitApi_3
{
    public static class ErpClient
    {
        /// <summary>
        /// Загружает список номенклатур из 1С-ERP.
        /// Сейчас payload простой: { action = "get_nomenclature_list" }.
        /// При необходимости модель запроса легко поменять.
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
    }
}
