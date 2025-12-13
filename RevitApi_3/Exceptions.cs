using System;
using System.Collections.Generic;

namespace RevitApi_3
{
    public class ErpValidationException : Exception
    {
        public Dictionary<string, List<string>> Errors { get; private set; }

        public ErpValidationException(Dictionary<string, List<string>> errors)
            : base("Ошибка валидации данных 1C-ERP")
        {
            Errors = errors ?? new Dictionary<string, List<string>>();
        }
    }
}
