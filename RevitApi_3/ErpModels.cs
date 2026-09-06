using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace RevitApi_3
{
    public class RevitItem
    {
        public ElementId ElementId { get; set; }
        public ElementId TypeId { get; set; }
        public string ErpCode { get; set; }
    }


    public class ErpItem
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public string Extra { get; set; }
        public string Unit { get; set; }
    }

    // Узел дерева классификатора
    public class ErpTreeNode
    {
        public string RefKey { get; set; }
        public string ParentKey { get; set; }
        public string Description { get; set; }

        public List<ErpTreeNode> Children { get; set; } = new List<ErpTreeNode>();

        public override string ToString() => Description;
    }

    // Элемент справочника (тип номенклатуры, единица и т.п.)
    public class RefNamedItem
    {
        public string RefKey { get; set; }
        public string Name { get; set; }

        public override string ToString() => Name;
    }

    /// <summary>
    /// Одна видимая колонка спецификации. Порядок и заголовок берутся из Revit,
    /// а не восстанавливаются по заранее известному набору полей.
    /// </summary>
    public sealed class ScheduleMirrorColumn
    {
        public int Index { get; set; }
        public string Key { get; set; }
        /// <summary>Нижний заголовок поля из ScheduleField.ColumnHeading.</summary>
        public string Header { get; set; }
        /// <summary>Имя исходного параметра независимо от пользовательской шапки.</summary>
        public string FieldName { get; set; }
        /// <summary>Все уровни пользовательской шапки для показа в WPF.</summary>
        public List<string> HeaderPath { get; } = new List<string>();
        public string DisplayHeader => HeaderPath.Count > 0
            ? string.Join("\n", HeaderPath)
            : Header ?? "";
        public int ParameterId { get; set; }
        public string ParameterGuid { get; set; }
        public string FieldType { get; set; }
        public bool IsCalculated { get; set; }
        public bool IsCombined { get; set; }
        public bool IsErpCode { get; set; }
        public bool IsQuantity { get; set; }
        public bool IsUnit { get; set; }
        public bool IsStrongIdentity { get; set; }

        // Нужен только внутри Revit-клиента для безопасного сопоставления строки
        // с экземплярами. В HTTP payload объект ScheduleField не попадает.
        internal ScheduleField RevitField { get; set; }
    }

    /// <summary>
    /// Строка тела спецификации ровно в том виде, в котором её показывает Revit.
    /// Служебные поля (этап, код для записи, ElementId) хранятся отдельно и не
    /// подменяют исходные ячейки.
    /// </summary>
    public sealed class ScheduleMirrorRow : INotifyPropertyChanged
    {
        private string _stage = "";
        private string _erpCode = "";
        private string _erpName1c = "";

        public int SourceRowNumber { get; set; }
        public List<string> Values { get; } = new List<string>();
        public List<int> ElementIds { get; } = new List<int>();

        public bool IsResourceRow { get; set; }
        public bool CanWriteErpCode => ElementIds.Count > 0;
        /// <summary>
        /// Одинаковый ключ означает, что строки неразличимы по номенклатурным
        /// полям и обязаны получать один ERP-код.
        /// </summary>
        public string AssociationKey { get; set; } = "";
        public string MatchState { get; set; } = "not_resource";
        public int MatchedFieldCount { get; set; }
        public string MatchInfo { get; set; } = "";
        public string OriginalErpCode { get; set; } = "";

        public string Stage
        {
            get => _stage;
            set { _stage = value ?? ""; OnPropertyChanged(); }
        }

        /// <summary>Код, который будет записан в экземпляры или отправлен в ERP.</summary>
        public string ErpCode
        {
            get => _erpCode;
            set
            {
                _erpCode = (value ?? "").Trim();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMissingErpCode));
            }
        }

        public string ErpName1c
        {
            get => _erpName1c;
            set { _erpName1c = value ?? ""; OnPropertyChanged(); }
        }

        public bool HasMissingErpCode =>
            IsResourceRow && (string.IsNullOrWhiteSpace(ErpCode) || ErpCode == "-");

        public string GetValue(int index)
        {
            return index >= 0 && index < Values.Count ? Values[index] ?? "" : "";
        }

        public void SetValue(int index, string value)
        {
            if (index < 0 || index >= Values.Count) return;
            Values[index] = value ?? "";
            OnPropertyChanged("Values[]");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public sealed class ScheduleMirrorTable
    {
        public int ScheduleElementId { get; set; }
        public string ScheduleName { get; set; }
        public List<ScheduleMirrorColumn> Columns { get; } = new List<ScheduleMirrorColumn>();
        public List<List<string>> HeaderRows { get; } = new List<List<string>>();
        public List<ScheduleMirrorRow> Rows { get; } = new List<ScheduleMirrorRow>();
        public bool HasExactFieldMapping { get; set; }
        public string Diagnostic { get; set; } = "";

        public int ErpCodeColumnIndex => Columns.FindIndex(x => x.IsErpCode);
        public int QuantityColumnIndex => Columns.FindIndex(x => x.IsQuantity);
        public int UnitColumnIndex => Columns.FindIndex(x => x.IsUnit);
        public IEnumerable<ScheduleMirrorRow> ResourceRows => Rows.Where(x => x.IsResourceRow);
    }
}
