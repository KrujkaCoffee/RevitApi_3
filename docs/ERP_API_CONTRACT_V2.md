# ERP API contract v2

Клиент Revit 2022 использует прежние URL и `action`-маршрутизацию. Новых HTTP-маршрутов для v2 не требуется.

Базовый адрес по умолчанию: `http://srv-mes:20011`. Для другой среды его можно задать переменной процесса `REVIT_ERP_BASE_URL` без завершающего `/`.

## Глобальный поиск номенклатуры

`POST /api/v1/revit/nomens`

```json
{
  "action": "search_nomenclature",
  "query": "труба 108",
  "limit": 200,
  "offset": 0
}
```

Требования:

- поиск по коду, наименованию, артикулу и дополнительным реквизитам;
- регистр не должен влиять на результат;
- `limit` ограничивается клиентом диапазоном 1–500;
- ответ может быть массивом либо объектом с массивом в `items`, `results`, `data` или `nomenclature`.

```json
{
  "items": [
    {
      "Code": "00-00179666",
      "Name": "Труба 108×3 AISI",
      "Unit": "м",
      "Extra": "артикул или комментарий"
    }
  ]
}
```

Клиент также понимает имена полей в нижнем регистре и `Description` вместо `Name`.

## Проверка и создание ресурсной спецификации

- проверка: `POST /api/v1/revit/resource/validate/`;
- создание: `POST /api/v1/revit/resource/create/`;
- `action`: `upload_resource_map`;
- `contract_version`: `2`.

Обе операции получают один и тот же payload. Значения `schedule.header_rows`, `schedule.body_rows[].cells` и `rows[].cells` сняты через Revit `GetCellText` и не рассчитываются клиентом повторно.

```json
{
  "action": "upload_resource_map",
  "contract_version": 2,
  "title": "Ресурсная спецификация проекта",
  "context": "Спецификация: О_Спецификация сводная",
  "creator": "Иван Иванов",
  "start_date": "2026-09-04",
  "end_date": "2026-09-11",
  "output_product": {
    "code": "00-00000001",
    "name": "Выпускаемое изделие",
    "unit": "шт"
  },
  "schedule": {
    "element_id": 123456,
    "name": "О_Спецификация сводная",
    "field_mapping_exact": true,
    "diagnostic": "",
    "header_rows": [["Наименование", "Количество"]],
    "columns": [
      {
        "order": 0,
        "key": "Наименование",
        "header": "2",
        "display_header": "Наименование\n2",
        "header_path": ["Наименование", "2"],
        "field_name": "Наименование",
        "parameter_id": 123,
        "parameter_guid": "",
        "field_type": "Instance",
        "is_calculated": false,
        "is_combined": false,
        "is_erp_code": false,
        "is_quantity": false,
        "is_unit": false
      }
    ],
    "body_rows": [
      {
        "source_row": 1,
        "is_resource_row": true,
        "match_state": "matched",
        "match_info": "Связано элементов Revit: 1; совпавших полей: 2.",
        "cells": ["Труба 108×3 AISI", "12,50 м"]
      }
    ]
  },
  "rows": [
    {
      "row": 1,
      "source_row": 1,
      "stage": "stage-ref-key",
      "erp_code": "00-00179666",
      "unit": "м",
      "quantity": "12,50 м",
      "values": {
        "Наименование": "Труба 108×3 AISI",
        "Количество": "12,50 м"
      },
      "cells": ["Труба 108×3 AISI", "12,50 м"],
      "element_ids": [654321],
      "match_state": "matched",
      "match_info": "Связано элементов Revit: 1; совпавших полей: 2."
    }
  ]
}
```

Правила:

- порядок `cells` строго соответствует `schedule.columns[].order`;
- `source_row` — номер строки секции `Body` Revit, начиная с 1;
- `body_rows` содержит также заголовки групп и итоги для полного зеркала документа;
- `rows` содержит только строки ресурсов;
- `header_rows` хранит исходную многоуровневую шапку, `header_path` — путь шапки конкретной колонки, а `display_header` — этот путь через перевод строки;
- если нижний пользовательский заголовок является номером (`1`, `2`, ...), ключ `values` строится из исходного `field_name`; дубликаты получают суффиксы `__2`, `__3`;
- `match_state=matched` означает, что строка безопасно связана с перечисленными `element_ids`; диагностические состояния также передаются серверу, но назначение ERP-кода для них клиент блокирует;
- визуально неразличимые строки получают одинаковый набор `element_ids` и должны иметь один ERP-код;
- `Stage`, `ErpCode`, `Unit`, `Quantity`, `FamilyName`, `TypeName`, `DisplayName` дублируются в `rows` для совместимости с текущим обработчиком. Новая реализация должна предпочитать поля нижнего регистра и `values`.

Ответ проверки:

```json
{
  "field_errors": {
    "title": "Наименование обязательно"
  },
  "table_errors": [
    {
      "row": 1,
      "source_row": 7,
      "msg": "Код номенклатуры не найден"
    }
  ]
}
```

Для ошибки допустим `400`, для успешной проверки — `200`. При создании клиент принимает `200` или `201`. Ссылка перехода может находиться в любом строковом поле JSON, если значение начинается с `e1c://`.
