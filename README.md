# Current contract summary

AdminActionProvider is the current extension point for interactive C# actions used by driver descriptors, component manifests and Web Administrator features. The browser sends componentCode, scope, actionId, context paths and typed input. Web Admin resolves the provider assembly and entry point, invokes a public method named by actionId, and expects AdminActionResult or Task<AdminActionResult>.

For Communicator drivers, descriptors are the primary source for forms and action metadata. Runtime DLLs should be resolved from Drivers\\<DriverCode> via actionProvider.assemblyFile; fallback to old training/source paths is compatibility only. Driver-specific CSS and JS belong in descriptor assets.styles and assets.scripts.

Use this document for the stable contract. Use ADMIN_ACTION_PROVIDER_AUDIT.md only as historical research background.

# AdminActionProvider guide

Дата: 2026-06-17

Этот документ описывает, как драйвер, модуль, tool, plugin или extension может дать веб-администратору C#-действия, которые нельзя выполнить только JSON/XML-формой.

## Общий принцип

Web UI собирает значения формы по descriptor, отправляет их в `ScadaAdminWebJP`, а `ScadaAdminWebJP` вызывает публичный метод в DLL компонента.

Компонент не разбирает HTTP и JSON. Компонент подключает библиотеку `Scada.Admin.Actions` и работает с C#-типами:

| Тип | Назначение |
|---|---|
| `AdminActionContext` | Аргументы, instance-контекст, пути проекта, культура, пользователь. |
| `AdminActionResult` | Единый ответ: `message`, `tree`, `list`, `table`, `formPatch`, `xmlPatch`, `operation`. |
| `AdminActionAttribute` | Связывает метод с `actionId`, если имя метода отличается. |
| `AdminActionArgsAttribute` | Объявляет DTO аргументов для проверки descriptor `input`. |
| `AdminActionIds` | Стандартные имена действий. |

Значения формы передаются с естественными JSON-типами: Boolean-поля уходят как `true`/`false`, числовые константы из descriptor остаются числами, а строки вида `values.<category>.<field>` подставляются из формы.

## Правило класса

В View/конфигурационной DLL компонента нужно создать публичный класс:

```csharp
using Scada.Admin.Actions;

namespace Scada.Comm.Drivers.DrvOpcUa.View;

public sealed class AdminActionProvider
{
    public Task<AdminActionResult> TestConnection(
        AdminActionContext context,
        CancellationToken cancellationToken)
    {
        string serverUrl = context.GetString("serverUrl");

        if (string.IsNullOrWhiteSpace(serverUrl))
            return Task.FromResult(AdminActionResult.Error("OPC UA server URL is not specified."));

        return Task.FromResult(AdminActionResult.Ok("Connection parameters are valid."));
    }
}
```

Provider создается через ASP.NET DI. Поэтому допустим как пустой конструктор, так и конструктор с зарегистрированными сервисами:

```csharp
public sealed class AdminActionProvider
{
    private readonly ILogger<AdminActionProvider> logger;

    public AdminActionProvider(ILogger<AdminActionProvider> logger)
    {
        this.logger = logger;
    }
}
```

Для внешней DLL зависимости provider должны быть доступны рядом с DLL или в runtime-папке компонента.

Поддерживаемая сигнатура метода:

```csharp
AdminActionResult Method(AdminActionContext context, CancellationToken cancellationToken)
Task<AdminActionResult> Method(AdminActionContext context, CancellationToken cancellationToken)
```

Если имя метода не совпадает с `actionId`, используйте атрибут:

```csharp
[AdminAction("BrowseTags")]
public Task<AdminActionResult> BrowseOpcServer(AdminActionContext context, CancellationToken cancellationToken)
{
    ...
}
```

Для новых actions рекомендуется объявлять DTO аргументов. Это не меняет сигнатуру метода: provider всё равно получает `AdminActionContext`, а DTO создается через `context.GetArgs<T>()`.

```csharp
[AdminAction(AdminActionIds.BrowseTags)]
[AdminActionArgs(typeof(BrowseTagsArgs))]
public Task<AdminActionResult> BrowseTags(AdminActionContext context, CancellationToken cancellationToken)
{
    BrowseTagsArgs args = context.GetArgs<BrowseTagsArgs>();
    ...
}

private sealed class BrowseTagsArgs
{
    public string EndpointUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string NodeId { get; set; } = "";
}
```

`GET /api/admin/actions/catalog/validate` сверяет `input` из driver descriptor или `component.json` с публичными set-свойствами DTO, если метод помечен `AdminActionArgs`. Сравнение регистронезависимое: `endpointUrl` в JSON соответствует `EndpointUrl` в C#. Отсутствие некоторых свойств DTO в descriptor допустимо, потому что поля могут быть опциональными или заполняться provider-ом по умолчанию.

## Descriptor

В descriptor указывается, где находится provider и какие действия доступны UI.

```json
{
  "driverCode": "DrvOpcUa",
  "actionProvider": {
    "assemblyFile": "$(AppDir)/Drivers/DrvOpcUa/DrvOpcUa.View.dll",
    "entryPoint": "Scada.Comm.Drivers.DrvOpcUa.View.AdminActionProvider"
  },
  "actions": [
    {
      "actionId": "TestConnection",
      "name": "Проверить подключение",
      "placement": "categoryToolbar",
      "categoryId": "connection",
      "input": {
        "serverUrl": "values.connection.serverUrl",
        "username": "values.connection.username",
        "password": "values.connection.password"
      },
      "result": {
        "type": "message"
      }
    }
  ]
}
```

Для tools, modules и plugins та же идея записывается в `component.json`. В этом случае `assembly` берется из component manifest и должен указывать на отдельную DLL компонента, а `actionProvider.entryPoint` указывает только класс provider:

```json
{
  "id": "mod-example-jp",
  "type": "module",
  "name": "ModExampleJP",
  "version": "1.0.0",
  "assembly": "ModExampleJP.dll",
  "entryPoint": "Scada.Admin.WebJP.Modules.ModExampleJP.ModExampleModule",
  "actionProvider": {
    "entryPoint": "Scada.Admin.WebJP.Modules.ModExampleJP.AdminActionProvider"
  },
  "actions": [
    {
      "actionId": "TestConnection",
      "name": "Проверить подключение",
      "input": {
        "serverUrl": "values.connection.serverUrl"
      },
      "result": {
        "type": "message"
      }
    }
  ]
}
```

Для `component.json` web-администратор проверяет, что `actionProvider.entryPoint` задан, `actionId` не пустой и не повторяется, `result.type` известен, а `result.applyMode`, если указан, равен `replace` или `append`.

`input` задает, какие значения формы передаются provider и под какими именами. Например:

```json
"input": {
  "serverUrl": "values.connection.serverUrl"
}
```

В provider это читается так:

```csharp
string serverUrl = context.GetString("serverUrl");
```

Для list-категорий можно передать выбранную строку TreeView/list и её вложенные таблицы:

```json
"input": {
  "readCommand": "listValues.readCommands.selected",
  "readCommandRegisters": "nestedGridValues.readCommands.selected",
  "registers": "nestedGridValues.readCommands.selected.registers"
}
```

`selected` берется из выбранного элемента TreeView/list на странице. Если выбранной строки нет, используется строка `0`.

Поддерживаемые пути `input`:

| Input path | Что передается |
|---|---|
| `values.<category>` | Все простые поля категории. |
| `values.<category>.<field>` | Одно поле категории. |
| `listValues.<category>` | Все строки list-категории. |
| `listValues.<category>.selected` | Выбранная строка TreeView/list-категории. |
| `listValues.<category>.selected.<field>` | Одно поле выбранной строки. |
| `gridValues.<category>.<gridField>` | Строки grid-поля singleton-категории. |
| `nestedGridValues.<listCategory>` | Все вложенные grid-значения всех строк list-категории. |
| `nestedGridValues.<listCategory>.selected` | Все вложенные grid-значения выбранной строки. |
| `nestedGridValues.<listCategory>.selected.<gridField>` | Строки вложенного grid-поля выбранной строки. |
| `nestedGridValues.<listCategory>.<rowIndex>.<gridField>` | Строки вложенного grid-поля указанной строки, где `rowIndex` начинается с `0`. |
| `nestedGridValues.<listCategory>.<gridField>` | Строки вложенного grid-поля первой строки. |

Descriptor проверяется при загрузке: если путь указывает на неизвестную категорию, поле или grid-поле, веб-администратор покажет ошибку descriptor до выполнения action.

Если XML/descriptor использует одни имена полей, а DTO provider-а ожидает другие имена, используйте объектный input с `source` и `map`. Это важно для случаев, когда конфигурация одного драйвера временно или постоянно вызывает общий action-provider другого компонента.

```json
"input": {
  "queries": {
    "source": "listValues.importCommands",
    "map": {
      "active": "enabled",
      "name": "name",
      "sql": "query"
    }
  }
}
```

В этом примере UI читает строки из `listValues.importCommands`, но в provider передает массив объектов с полями `active`, `name`, `sql`. Исходные поля формы и XML не переименовываются. Путь `source` проверяется как обычный input-путь; значения `map` без префиксов `values.`, `listValues.`, `gridValues.`, `nestedGridValues.` считаются относительными полями исходного объекта.

Если provider ожидает список, а UI должен передать только выбранную строку TreeView/list, используйте `array=true`. Тогда одиночный объект из `source` будет отправлен как массив из одной строки:

```json
"input": {
  "queries": {
    "source": "listValues.importCommands.selected",
    "array": true
  }
}
```

Такой вариант используется для `DrvDbImportPlus.DBQuery`: пользователь выбирает команду импорта в TreeView, а provider получает `Queries` как список из выбранной команды.

Если настройки драйвера лежат в нескольких XML-файлах, категория может указать свой `configFile`:

```json
{
  "id": "connection",
  "configFile": "DriverConfigs/DrvDbImport_line001.xml",
  "xmlPath": "/DbLineConfig/ConnectionOptions"
}
```

Для зашифрованных XML-значений, например `Password` или `ConnectionString`, поле помечается так:

```json
{
  "id": "password",
  "type": "password",
  "xmlPath": "Password",
  "encrypted": true
}
```

Web-администратор читает такое значение через `ScadaUtils.Decrypt`, а при сохранении снова пишет через `ScadaUtils.Encrypt`.

## Полный маршрут адаптации драйвера

Для WebAdmin-адаптации драйвера нужны три независимых файла. Они решают разные задачи и не должны смешиваться.

| Файл | Где хранить в исходниках | Куда попадает при сборке | Назначение |
|---|---|---|---|
| `driver.json` | `Drivers/<DriverFolder>/driver.json` | Не публикуется как runtime-контракт | Манифест сборки: откуда взять DLL, descriptor и assets драйвера. |
| `DriverDescriptor.json` | Обычно `Drivers/<DriverFolder>/<SourceProject>/AdminWeb/DriverDescriptor.json` | `DriverDescriptors/<DriverCode>.json` | Web-схема формы, XML-маппинг, действия и provider. |
| `AdminActionProvider.cs` | В View/конфигурационной DLL драйвера | `Drivers/<DriverCode>/<DriverViewDll>.dll` | C#-логика интерактивных действий: прочитать теги, сформировать команды, проверить соединение. |

Практический порядок работы:

1. Определить `driverCode`. Он должен совпадать с кодом драйвера в проекте Rapid SCADA и с именем runtime-папки `Drivers/<DriverCode>`.
2. Добавить `driver.json`, чтобы `Build/SyncDrivers.ps1` мог скопировать runtime-файлы и descriptor без правки `ScadaAdminWebJP.csproj`.
3. Создать `AdminWeb/DriverDescriptor.json`: описать категории, поля, XML-пути, действия и `actionProvider`.
4. Добавить `AdminActionProvider` в View DLL драйвера только для логики, которую нельзя выразить descriptor-ом.
5. Собрать драйвер и выполнить `build-debug.bat` или `build-release.bat`. Синхронизация должна положить DLL в `Drivers/<DriverCode>` и descriptor в `DriverDescriptors`.
6. Открыть редактор линии/устройства в WebAdmin и проверить metadata API: `GET /api/admin/driver-schema/<DriverCode>`, `GET /api/admin/driver-schema/actions`.

Важно: web-проект больше не должен содержать `Content Include` на конкретные драйверы. Добавление или отключение драйвера делается через `driver.json`.

## `driver.json`

`driver.json` описывает инвентаризацию и публикацию драйвера. Это build-манифест, а не UI-схема.

```json
{
  "enabled": true,
  "code": "DrvModbus",
  "name": "Modbus",
  "owner": "web-adapted",
  "origin": "OpenDrivers3",
  "sourcePath": "Drivers/OtherVendors/DrvModbus/OpenDrivers3/DrvModbus.View",
  "buildMode": "build-list",
  "runtime": {
    "source": "Drivers/OtherVendors/DrvModbus/OpenDrivers3/DrvModbus.View/bin/{Configuration}/net8.0-windows",
    "target": "Drivers/DrvModbus"
  },
  "webDescriptor": {
    "source": "Drivers/OtherVendors/DrvModbus/OpenDrivers3/DrvModbus.View/AdminWeb/DriverDescriptor.json",
    "target": "DriverDescriptors/DrvModbus.json"
  },
  "assets": [],
  "extraFiles": [],
  "releasePackages": [],
  "notes": []
}
```

| Свойство | Назначение |
|---|---|
| `enabled` | Если `false`, драйвер не копируется в debug/release publish. |
| `code` | Код драйвера. Должен совпадать с `driverCode` descriptor-а и runtime-папкой. |
| `name` | Человекочитаемое имя для инвентаризации и проверок. |
| `owner` | Происхождение поддержки: например `custom`, `upstream`, `web-adapted`. |
| `origin` | Откуда взят драйвер: `custom`, `OtherVendors`, старый `OpenDrivers3` и т.п. |
| `sourcePath` | Путь к исходному View/driver-проекту относительно корня репозитория. |
| `buildMode` | Режим участия в сборке. `build-list` означает активную публикацию; `inventory-only` оставляет запись только для учета. |
| `runtime.source` | Папка с собранными DLL. `{Configuration}` заменяется на `Debug` или `Release`. |
| `runtime.target` | Runtime-папка внутри WebAdmin. Для драйвера используйте `Drivers/<DriverCode>`. |
| `webDescriptor.source` | Исходный `DriverDescriptor.json`. |
| `webDescriptor.target` | Runtime-путь descriptor-а. Обычно `DriverDescriptors/<DriverCode>.json`. |
| `assets` | Дополнительные web-ресурсы драйвера, если они копируются отдельным шагом. |
| `extraFiles` | Дополнительные runtime-файлы, которые не попадают в `runtime.source`. |
| `releasePackages` | Описание отдельных архивов/пакетов драйвера, если они нужны пользователям отдельно от WebAdmin. |
| `notes` | Короткие технические примечания. Не используйте вместо документации. |

Если у драйвера есть отдельный zip-архив для пользователя, его структура должна повторять runtime-раскладку WebAdmin: DLL в `Drivers/<DriverCode>`, descriptor в `DriverDescriptors/<DriverCode>.json`, assets в тех путях, на которые ссылается descriptor.

## `DriverDescriptor.json`

`DriverDescriptor.json` задает форму редактирования, XML-маппинг и действия. Он читается `DriverConfigWorkspace`.

Верхний уровень:

| Свойство | Назначение |
|---|---|
| `driverCode` | Код драйвера. Используется для поиска descriptor-а, runtime-папки и связки с устройством Communicator. |
| `driverName` | Имя драйвера в UI. |
| `version` | Версия descriptor-а, а не обязательно версия DLL. |
| `description` | Описание для диагностики и будущих экранов компонентов. |
| `configFile` | Относительный путь XML-конфигурации драйвера, если файл общий для всего descriptor-а. |
| `configSource` | Источник конфигурации, если используется нестандартная модель хранения. Обычно пусто. |
| `categories` | Разделы формы. |
| `actions` | Кнопки/команды, вызывающие C# provider. |
| `actionProvider` | Где лежит DLL и какой класс вызывать. |
| `assets.styles` | CSS-файлы, загружаемые при открытии редактора этого драйвера. |
| `assets.scripts` | JS-файлы, загружаемые при открытии редактора этого драйвера. |

`actionProvider` для драйвера должен указывать на runtime-копию, а не на исходный проект:

```json
"actionProvider": {
  "assemblyFile": "$(AppDir)/Drivers/DrvModbus/DrvModbus.View.dll",
  "entryPoint": "Scada.Comm.Drivers.DrvModbus.View.AdminActionProvider"
}
```

`$(AppDir)` раскрывается в папку запущенного WebAdmin. Это позволяет не зашивать абсолютные пути разработчика.

### Категории

Категория описывает один раздел редактора.

| Свойство | Назначение |
|---|---|
| `id` | Стабильный идентификатор. Используется в `values.<category>`, `listValues.<category>` и action `categoryId`. |
| `name` | Заголовок раздела в UI. |
| `isSingleton` | Один объект настроек. Например `/DriverConfig/Connection`. |
| `isList` | Список однотипных строк. Например `/DriverConfig/Devices/Device`. |
| `itemName` | Название строки списка или grid-строки. |
| `viewMode` | Визуальный режим редактора, если нужен нестандартный вид. |
| `columns`, `columnCount` | Количество колонок формы. `columnCount` оставлен как альтернативное имя. |
| `configFile` | Отдельный XML-файл только для этой категории. |
| `xmlPath` | XPath/путь к XML-узлу категории. |
| `fields` | Поля категории. |

`isSingleton` и `isList` задают модель данных. Для одного XML-раздела используйте `isSingleton`; для повторяющихся XML-узлов используйте `isList`.

### Поля

Поле описывает UI-контрол и XML-маппинг.

| Свойство | Назначение |
|---|---|
| `id` | Стабильный идентификатор поля. Используется в action input, `dependsOn` и DTO. |
| `name` | Подпись поля. |
| `type` | Тип редактора: `string`, `text`, `integer`, `float`, `boolean`, `password`, `select`, `multiselect`, `time`, `datetime`, `color`, `file`, `grid`. |
| `required` | Обязательное поле. |
| `hidden` | Скрыть поле в визуальном редакторе, но оставить в модели. |
| `min`, `max` | Числовые ограничения. |
| `order` | Порядок отображения. |
| `colSpan`, `columnSpan`, `rowSpan` | Размер поля в layout. |
| `xmlPath` | Относительный XML-путь внутри категории или grid-строки. |
| `isXmlList` | Значение хранится как XML-список. |
| `encrypted` | Значение шифруется при сохранении и расшифровывается при чтении. |
| `default` | JSON-значение по умолчанию. Может быть строкой, числом, bool или массивом. |
| `valueSeparator` | Разделитель для многострочных/мультизначений. |
| `itemName` | Имя элемента для вложенного `grid`. |
| `dependsOn` | Условие видимости поля. |
| `options` | Варианты для `select` и `multiselect`. |
| `fields` | Вложенные поля для `grid`. |

`options` задаются так:

```json
"options": [
  { "value": "HoldingRegisters", "label": "Holding registers" },
  { "value": "InputRegisters", "label": "Input registers" }
]
```

`dependsOn` поддерживает ссылку на другое поле этой же формы:

```json
"dependsOn": {
  "fieldId": "pollModes",
  "operator": "contains",
  "value": "diagnostic",
  "valueSeparator": ","
}
```

| Свойство `dependsOn` | Назначение |
|---|---|
| `fieldId` | Поле, от значения которого зависит видимость. |
| `operator` | Оператор проверки. На практике используются `equals` и `contains`. |
| `value` | Одно ожидаемое значение. |
| `values` | Несколько ожидаемых значений. |
| `valueSeparator` | Разделитель, если исходное поле хранит несколько значений одной строкой. |

### Действия

Действие связывает кнопку в UI с C#-методом provider-а.

| Свойство | Назначение |
|---|---|
| `actionId` | Идентификатор действия. Обычно совпадает с методом provider-а или с константой `AdminActionIds`. |
| `name` | Текст кнопки. |
| `description` | Tooltip/описание действия. |
| `placement` | Где показать кнопку. По умолчанию `categoryToolbar`. Для кнопки рядом с полем используется placement с `fieldId`. |
| `categoryId` | Категория, в которой показывается action. |
| `fieldId` | Поле, рядом с которым показывается action, если placement привязан к полю. |
| `input` | Карта аргументов provider-а. |
| `result` | Как UI должен применить результат. |

`result`:

| Свойство | Назначение |
|---|---|
| `type` | Ожидаемый тип результата: `message`, `validation`, `list`, `tree`, `table`, `formPatch`, `xmlPatch`, `filePreview`, `operation`. |
| `target` | Путь, куда применить данные результата. Например `values.readCommands.tags`. |
| `applyMode` | `replace` или `append`. Если пусто, используется стандартное поведение для типа результата. |
| `placement`, `label`, `name` | Отображение результата, если UI показывает отдельную область результата. |

## `AdminActionProvider` API

Provider не должен работать с HTTP. Его контракт - `AdminActionContext` и `AdminActionResult`.

`AdminActionContext`:

| Свойство/метод | Назначение |
|---|---|
| `ComponentCode` | Код компонента или драйвера. |
| `Scope` | Область вызова: driver/component/tool/module/plugin/extension. |
| `ActionId` | Выполняемое действие. |
| `ProjectDir` | Корневая папка текущего проекта. |
| `ConfigDir` | Папка конфигурации проекта. |
| `Culture` | Текущая культура UI. |
| `User` | Имя пользователя, если доступно. |
| `Args` | Аргументы из descriptor `input`. Ключи регистронезависимые. |
| `Instance` | Контекст экземпляра/узла, если action вызван из Communicator/Server/Webstation. |
| `GetString`, `GetInt`, `GetBool`, `GetValue<T>` | Безопасное чтение отдельных аргументов с default-значением. |
| `GetArgs<T>()` | Десериализация всех аргументов в DTO. |

`GetValue<T>` понимает обычные JSON-значения, enum без учета регистра, числа, bool, строки и сложные объекты. Для сложных действий предпочтительнее DTO через `GetArgs<T>()`: так проще валидировать descriptor через `GET /api/admin/actions/catalog/validate`.

`AdminActionResult`:

| Factory | `ResultType` | Когда использовать |
|---|---|---|
| `AdminActionResult.Ok(message)` | `message` | Простое успешное действие. |
| `AdminActionResult.Error(message)` | `message`, `Success=false` | Ошибка действия без падения WebAdmin. |
| `AdminActionResult.Validation(errors)` | `validation` | Ошибки по полям формы. |
| `AdminActionResult.List(data)` | `list` | Вернуть список строк/вариантов. |
| `AdminActionResult.Tree(data)` | `tree` | Вернуть иерархию `AdminActionTreeNode`. |
| `AdminActionResult.Table(data)` | `table` | Вернуть таблицу `{ columns, rows }`. |
| `AdminActionResult.FormPatch(data, message)` | `formPatch` | Заменить несколько частей формы согласованным результатом. |
| `AdminActionResult.XmlPatch(data)` | `xmlPatch` | Вернуть изменение XML-представления. |

Если action может выполняться долго, регулярно вызывайте `cancellationToken.ThrowIfCancellationRequested()`.

Ошибки лучше возвращать через `AdminActionResult.Error(...)`, а не пробрасывать наружу. Тогда UI покажет понятное сообщение, а не общую ошибку API.

## Пример адаптации: `DrvModbus`

`DrvModbus` сейчас является хорошим примером WebAdmin-адаптации стороннего драйвера.

Исходники и манифест:

```text
Drivers/OtherVendors/DrvModbus/driver.json
Drivers/OtherVendors/DrvModbus/OpenDrivers3/DrvModbus.View/AdminWeb/DriverDescriptor.json
Drivers/OtherVendors/DrvModbus/OpenDrivers3/DrvModbus.View/AdminActionProvider.cs
```

Почему `driver.json` сделан именно так:

| Решение | Причина |
|---|---|
| `code = DrvModbus` | Код совпадает с именем runtime-папки и descriptor-а. |
| `owner = web-adapted` | Это не исходный upstream-вариант, а версия, доработанная под WebAdmin. |
| `origin = OpenDrivers3` | Сохраняет информацию, из какой промежуточной папки пришла адаптация. |
| `runtime.source = .../bin/{Configuration}/net8.0-windows` | `SyncDrivers.ps1` может одинаково работать для Debug и Release. |
| `runtime.target = Drivers/DrvModbus` | WebAdmin загружает provider из runtime-папки, а не из исходников. |
| `webDescriptor.target = DriverDescriptors/DrvModbus.json` | API `/api/admin/driver-schema` ищет published descriptors именно там. |
| `assets = []` | Для текущего Modbus-редактора хватает общей формы; отдельный CSS/JS не нужен. |

Почему descriptor разделен на категории:

| Категория | Модель | Почему так |
|---|---|---|
| `connection` | `isSingleton`, `/DriverConfig/Connection` | Параметры порта существуют в одном экземпляре. |
| `devices` | `isList`, `/DriverConfig/Devices/Device` | Устройств может быть несколько, поэтому нужен список. |
| `advanced` | `isSingleton`, `/DriverConfig/Advanced` | Дополнительные настройки относятся ко всему драйверу. |
| `readCommands` | `isList`, `/DriverConfig/ElemGroups/ElemGroup` | Команды чтения повторяются и содержат вложенный grid регистров. |
| `commands` | `isList`, `/DriverConfig/Commands/Command` | Команды управления повторяются и затем преобразуются в таблицу команд. |

Примеры выбранных полей:

- `connection.port`, `baudRate`, `parity` сделаны `select`, потому что набор значений ограничен и ошибка ввода руками нежелательна.
- `timeout`, `retries`, `slaveId`, `pollInterval` сделаны `integer` с `min`/`max`, чтобы не сохранять недопустимые значения в XML.
- `advanced.pollModes` сделан `multiselect`, потому что режимов может быть несколько.
- `advanced.diagnosticFile` имеет `dependsOn` от `pollModes contains diagnostic`, чтобы не показывать обязательный файл диагностики, пока диагностический режим не включен.
- `readCommands.registers` сделан `grid`, потому что один read command содержит набор регистров.
- `commands.functionCode` зависит от `dataBlock == Custom`, потому что для стандартных блоков код функции вычисляется provider-ом.

Почему action `GetTags` получает именно такие входные данные:

```json
"input": {
  "readCommands": "listValues.readCommands",
  "readCommandRegisters": "nestedGridValues.readCommands"
}
```

`readCommands` передает все строки команд чтения. `nestedGridValues.readCommands` передает вложенные grids для каждой строки списка. Provider получает эти данные как DTO:

```csharp
private sealed class GetTagsArgs
{
    public List<ReadCommandRow> ReadCommands { get; set; } = [];
    public List<Dictionary<string, List<RegisterRow>>> ReadCommandRegisters { get; set; } = [];
}
```

Такой формат нужен, потому что теги рассчитываются не из одной строки, а из всей таблицы команд чтения и всех вложенных регистров. Provider проходит только по активным read commands, берет `dataBlock`, начальный `address`, затем для каждого регистра вычисляет `tagCode` и увеличивает адрес на ширину типа (`float` занимает больше одного регистра).

Результат:

```json
"result": {
  "type": "table",
  "target": "values.readCommands.tags"
}
```

`table` выбран потому, что результат удобно показать как расчетную таблицу. `target` привязан к категории `readCommands`, чтобы UI понимал, где применить/показать результат.

Почему action `GetCommands` проще:

```json
"input": {
  "commands": "listValues.commands"
}
```

Команды управления не имеют вложенного grid. Provider получает список `CommandRow`, вычисляет `functionCode` по `dataBlock`, `multiple`, `elementCount` и возвращает таблицу. Для `Custom` можно использовать явно заданный `functionCode`, а для стандартных блоков код выбирается автоматически.

Минимальные правила для нового драйвера на базе этого примера:

1. Не копируйте `DrvModbus` descriptor механически. Сначала нарисуйте реальную XML-структуру своего драйвера.
2. Для каждого повторяющегося XML-узла делайте `isList`; для одиночного узла - `isSingleton`.
3. Если C#-логика нужна только для расчета таблицы или подключения к устройству, оставляйте форму в descriptor, а provider делайте маленьким.
4. `actionProvider.assemblyFile` всегда должен указывать на `$(AppDir)/Drivers/<DriverCode>/<ViewDll>.dll`.
5. Проверяйте `GET /api/admin/actions/catalog/validate`: он поймает несовпадение `input` и DTO раньше, чем пользователь нажмет кнопку.

## AdminWebOptions.json для Communicator

`AdminActionProvider` нужен, когда UI должен вызвать C#-код: подключиться к серверу, прочитать теги, выполнить SQL-запрос или вернуть команды.

Для простого описания формы параметров источника данных или канала связи provider не нужен. Такие формы описываются файлом:

```text
Drivers/<DriverCode>/AdminWebOptions.json
```

Исходный файл удобно хранить рядом с View-проектом драйвера:

```text
ScadaComm/OpenDrivers/DrvCnlBasic.View/AdminWebOptions.json
ScadaComm/OpenDrivers/DrvDsMqtt.View/AdminWebOptions.json
```

В `.csproj` драйвера файл нужно копировать в output:

```xml
<ItemGroup>
  <None Update="AdminWebOptions.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

После сборки WebJP копирует output драйвера в `ScadaAdminWebJP/bin/Debug/net8.0/Drivers/<DriverCode>`, и endpoint `GET /api/admin/communicator/config/driver-catalog` отдаёт эти descriptors в браузер.

Если `AdminWebOptions.json` отсутствует или не содержит нужного раздела, WebJP использует встроенный fallback для стандартных драйверов.

### Источник данных

Для `DrvDs*` используется массив `dataSourceOptions`:

```json
{
  "dataSourceOptions": [
    {
      "group": "Подключение",
      "name": "Server",
      "label": "Сервер",
      "type": "text",
      "defaultValue": ""
    },
    {
      "group": "Подключение",
      "name": "UseTls",
      "label": "Использовать TLS",
      "type": "checkbox",
      "defaultValue": "false"
    },
    {
      "group": "Подключение",
      "name": "ProtocolVersion",
      "label": "Версия протокола",
      "type": "select",
      "defaultValue": "Unknown",
      "options": [
        { "value": "Unknown", "text": "Default" },
        { "value": "V311", "text": "3.1.1" }
      ]
    }
  ]
}
```

Поддерживаемые `type`: `text`, `number`, `password`, `checkbox`, `select`.

`name` должен совпадать с именем `Option` в `ScadaCommConfig.xml`:

```xml
<Option name="Server" value="localhost" />
```

### Канал связи

Для `DrvCnl*` используется массив `channelTypes`. Один драйвер может описать несколько типов каналов:

```json
{
  "channelTypes": [
    {
      "typeCode": "TcpClient",
      "options": [
        {
          "name": "Behavior",
          "labelKey": "ChannelFieldBehavior",
          "inputType": "select",
          "defaultValue": "Master",
          "options": [
            { "value": "Master", "labelKey": "ChannelBehaviorMaster" },
            { "value": "Slave", "labelKey": "ChannelBehaviorSlave" }
          ]
        },
        {
          "name": "TcpPort",
          "labelKey": "ChannelFieldTcpPort",
          "inputType": "number",
          "defaultValue": "502",
          "min": 1,
          "max": 65535
        }
      ]
    }
  ]
}
```

`typeCode` должен совпадать с кодом типа канала из `DriverView.ChannelTypes`.

Поддерживаемые `inputType`: `text`, `number`, `password`, `checkbox`, `select`, `checkboxGroup`.

Для `checkboxGroup` используются вложенные `fields`:

```json
{
  "inputType": "checkboxGroup",
  "fields": [
    { "name": "DtrEnable", "labelKey": "ChannelFieldDtrEnable", "inputType": "checkbox", "defaultValue": "false" },
    { "name": "RtsEnable", "labelKey": "ChannelFieldRtsEnable", "inputType": "checkbox", "defaultValue": "false" }
  ]
}
```

`labelKey` берется из локализации WebJP. Если ключа нет, браузер покажет сам ключ. Для новых сторонних драйверов можно сначала использовать понятные ключи-строки, а затем добавить локализацию.

### Что выбирать

| Задача | Механизм |
|---|---|
| Редактор XML-конфига устройства | `DriverDescriptors/<DriverCode>.json` |
| Кнопка, которая вызывает C#-код | `AdminActionProvider` + `actions` в descriptor |
| Генерация каналов устройства | `AdminActionProvider.GetTags` |
| Параметры источника данных `DrvDs*` | `Drivers/<DriverCode>/AdminWebOptions.json`, `dataSourceOptions` |
| Параметры канала `DrvCnl*` | `Drivers/<DriverCode>/AdminWebOptions.json`, `channelTypes` |
| Особый внешний вид редактора устройства | `assets.styles` и `assets.scripts` в driver descriptor |

## Driver CSS

Если драйверу нужен свой внешний вид формы или небольшая клиентская логика, descriptor может подключить CSS и JS-файлы:

```json
{
  "driverCode": "DrvModbusTree",
  "assets": {
    "styles": [
      "/drivers/DrvModbusTree/driver.css"
    ],
    "scripts": [
      "/drivers/DrvModbusTree/driver.js"
    ]
  }
}
```

CSS и script-теги загружаются только пока открыт этот драйвер и удаляются при переключении на другой драйвер. Это позволяет делать TreeView, специальные панели, плотные таблицы или driver-specific улучшения без добавления частных правил в темы администратора.

Рекомендуемая структура:

```text
wwwroot/drivers/<DriverCode>/driver.css
wwwroot/drivers/<DriverCode>/driver.js
```

Ограничения:

- путь должен быть внутри текущего сайта;
- CSS/JS-файл должен существовать под `wwwroot`, иначе descriptor не загрузится;
- внешние URL, например `https://...`, не разрешены;
- `..`, обратные слэши, `?` и `#` в пути не разрешены;
- файлы в `assets.styles` должны иметь расширение `.css`, файлы в `assets.scripts` - `.js`;
- общие элементы формы должны по возможности использовать базовые классы администратора, а driver CSS должен описывать только частную компоновку драйвера.

Для driver JS предусмотрены события жизненного цикла:

```js
document.addEventListener("scada-admin-driver-rendered", function (event) {
    if (event.detail.driverCode !== "DrvExample") {
        return;
    }

    // event.detail.root содержит DOM формы драйвера.
});

document.addEventListener("scada-admin-driver-assets-unload", function (event) {
    if (event.detail.driverCode === "DrvExample") {
        // Здесь нужно снять MutationObserver, таймеры и внешние обработчики.
    }
});
```

## Result target

`result.target` указывает, куда UI должен применить или где показать результат.

| Target | Поведение |
|---|---|
| `values.<category>.<field>` | Записывает значение в поле формы, если такое поле существует. Если поле виртуальное, target используется только как панель отображения результата в указанной категории. |
| `listValues.<category>` | Заменяет строки list-категории. |
| `gridValues.<category>.<gridField>` | Заменяет строки grid-поля. |
| `nestedGridValues.<listCategory>.<gridField>` | Заменяет строки вложенного grid-поля в первой строке list-категории. Если строка списка отсутствует, UI создаёт её автоматически. |
| `nestedGridValues.<listCategory>.selected.<gridField>` | Заменяет строки вложенного grid-поля в выбранной строке TreeView/list-категории. Если выбранной строки нет, используется первая строка. |
| `nestedGridValues.<listCategory>.<rowIndex>.<gridField>` | Заменяет строки вложенного grid-поля в указанной строке list-категории. `rowIndex` начинается с `0`. |
| `formPatch` result | Может заменить сразу несколько секций `values`, `listValues`, `gridValues` и `nestedGridValues`. Для него `result.target` не требуется. |

`result.applyMode` управляет применением результата к `listValues`, `gridValues` и `nestedGridValues`:

| applyMode | Поведение |
|---|---|
| `replace` или пусто | Заменить текущие строки результатом действия. |
| `append` | Добавить строки результата к текущим строкам. |

Если target указывает на категорию, но не на существующее поле, результат показывается в панели этой категории и не меняет XML-конфигурацию. Это удобно для browse-деревьев, предпросмотра SQL-запросов и диагностических таблиц:

```json
{
  "actionId": "BrowseTags",
  "categoryId": "connection",
  "result": {
    "type": "tree",
    "target": "values.browser.nodes"
  }
}
```

Кнопка находится в `connection`, а дерево результата показывается в категории `browser`.

Для таблицы предпросмотра SQL-запроса можно использовать такой же display-only target:

```json
{
  "actionId": "DBQuery",
  "categoryId": "importCommands",
  "result": {
    "type": "table",
    "target": "values.importCommands.queryResult"
  }
}
```

Такой target показывает таблицу в категории `importCommands`, но не создает поле `queryResult` и не помечает форму измененной.

## Стандартные actionId

Рекомендуемые имена:

| actionId | Назначение |
|---|---|
| `Connect` | Открыть подключение или долгоживущую сессию. |
| `Disconnect` | Закрыть подключение или сессию. |
| `TestConnection` | Проверить параметры подключения без долгоживущей сессии. |
| `DBQuery` | Выполнить SQL-запрос к базе данных и вернуть результат. |
| `GetTags` | Получить плоский список тегов. |
| `BrowseTags` | Получить дерево тегов/узлов. |
| `GetTagInfo` | Получить подробную информацию по тегу. |
| `SetTags` | Сохранить выбранные теги в конфигурацию. |
| `GetCommands` | Получить список команд. |
| `SetCommands` | Сохранить выбранные команды в конфигурацию. |
| `ReadSchema` | Прочитать внешнюю схему, структуру БД или структуру устройства. |
| `PreviewConfig` | Предварительно показать изменения конфигурации. |
| `GenerateConfig` | Сгенерировать конфигурацию или патч. |
| `GenerateChannels` | Сгенерировать каналы Rapid SCADA. |
| `ImportConfig` | Импортировать настройки. |
| `ExportConfig` | Экспортировать настройки. |
| `ValidateConfig` | Проверить конфигурацию. |
| `EncryptPassword` | Зашифровать пароль или секретное значение. |
| `AddNode` | Добавить узел древовидной конфигурации. |
| `RemoveNode` | Удалить узел древовидной конфигурации. |
| `MoveNode` | Переместить узел древовидной конфигурации. |
| `GetNodeProperties` | Получить свойства выбранного узла. |
| `SetNodeProperties` | Записать свойства выбранного узла. |
| `Deploy` | Выполнить развертывание или передачу данных. |
| `RestartService` | Перезапустить сервис. |
| `SyncLine` | Синхронизировать линию связи. |
| `PollDevice` | Опросить устройство. |

Можно использовать свои `actionId`, но descriptor и provider должны совпадать. Если descriptor содержит `actionId`, а provider не содержит публичный метод или `[AdminAction]` с таким именем, web-администратор вернет понятную ошибку:

```text
Action "GetTags" is not implemented by component "DrvOpcUa".
```

## Что проверяет web-администратор

При загрузке descriptor проверяется:

- `actionId` не пустой;
- нет дубликатов `actionId`;
- `categoryToolbar`, `listToolbar` и `afterField` ссылаются на существующий `categoryId`; для `afterField` дополнительно нужен существующий `fieldId`;
- `fieldId`, если задан, ссылается на существующее поле;
- `result.type` входит в известный список;
- `result.applyMode`, если задан, равен `replace` или `append`;
- `result.target`, если задан, ссылается на существующую категорию; для `listValues`, `gridValues` и `nestedGridValues` проверяется подходящий список/grid, а `values.<category>.<virtualKey>` допускается как display-only target;
- строковые `input`-пути с префиксами `values`, `listValues`, `gridValues` и `nestedGridValues` ссылаются на существующие категории и поля.

При вызове действия проверяется:

- файл DLL существует;
- класс provider найден;
- `actionId` реализован публичным методом или атрибутом;
- сигнатура метода поддерживается.

## Metadata API

Для диагностики и будущего конструктора форм доступны metadata endpoints:

| Endpoint | Назначение |
|---|---|
| `GET /api/admin/components/actions` | Действия tools/modules/plugins из `component.json`. |
| `GET /api/admin/driver-schema/actions` | Действия driver descriptors. |
| `GET /api/admin/actions/catalog` | Единый каталог component и driver actions. |
| `GET /api/admin/actions/catalog/validate` | Проверка provider-классов, action-методов, сигнатур и типов возврата без выполнения действий. |

Каталог возвращает owner-компонент или драйвер, provider entry point и список действий с `input`, `resultType`, `target` и `applyMode`.

Validation endpoint возвращает по одной строке на каждое объявленное действие. Если `component.json` или descriptor содержит `actionId`, но provider не содержит публичный метод с таким именем или `[AdminAction]`, строка вернется с `success=false` и текстом ошибки.

## formPatch

`formPatch` нужен, когда одно действие меняет несколько частей формы. Например, OPC UA `SetTags` может создать подписку, добавить в неё выбранный node и вернуть найденные свойства узла:

```csharp
return AdminActionResult.FormPatch(new
{
    listValues = new Dictionary<string, object>
    {
        ["subscriptions"] = subscriptions
    },
    nestedGridValues = new Dictionary<string, object>
    {
        ["subscriptions"] = subscriptionItems
    }
});
```

Форма `data` у `formPatch` повторяет модель редактора:

```json
{
  "values": {
    "connection": {
      "serverUrl": "opc.tcp://localhost:4840"
    }
  },
  "listValues": {
    "subscriptions": [
      { "active": true, "displayName": "Subscription 1", "publishingInterval": 1000 }
    ]
  },
  "gridValues": {
    "commands": {
      "items": [
        { "name": "Command 1" }
      ]
    }
  },
  "nestedGridValues": {
    "subscriptions": [
      {
        "items": [
          { "active": true, "nodeId": "ns=2;i=1001", "displayName": "Tag 1" }
        ]
      }
    ]
  }
}
```

Правила применения:

- patch заменяет только те категории, которые указаны в `data`;
- внутри указанной категории значение заменяется целиком, а не сливается построчно;
- после patch форма перерисовывается и помечается измененной;
- выбранная строка TreeView/list сохраняется, если после patch такая строка ещё существует;
- при сохранении формы web-администратор нормализует значения в строки для XML-маппера, поэтому provider может возвращать естественные JSON-типы: `true`, `false`, числа и строки.

Для частичного добавления строк без полной перерисовки используйте обычный `list`, `table` или `tree` result с `result.target` и `applyMode=append`. `formPatch` лучше использовать, когда C# provider пересчитал согласованное состояние нескольких секций формы.

## XML-режим редактора драйвера

Редактор драйвера всегда работает с конкретным `relativeConfigFile`. Для устройства Communicator этот путь вычисляется из `cmdLine`, а если `cmdLine` пустой, используется стандартное имя:

```text
Instances/<InstanceName>/ScadaComm/Config/<DriverCode>_<DeviceNumber:D3>.xml
```

Если файл существует, кнопка `XML` читает его через `GET /api/admin/project-files/{relativePath}`.

Если файл ещё не создан, кнопка `XML` не блокируется. Web-администратор вызывает предпросмотр:

```http
POST /api/admin/driver-schema/{driverCode}/xml-preview
POST /api/admin/driver-schema/device/xml-preview?instanceName=Default&lineNumber=1&deviceNumber=2
```

Preview использует тот же descriptor, device file context и validation rules, что обычное сохранение, но возвращает только:

```json
{
  "success": true,
  "relativePath": "Instances/Default/ScadaComm/Config/DrvModbus_099.xml",
  "content": "<?xml version=\"1.0\" encoding=\"utf-8\"?>..."
}
```

Такой XML показывается как несохранённый. Файл создаётся только после явного нажатия `Сохранить` в XML-режиме.

## Диагностика provider DLL

Для драйверов путь DLL обычно указывает на runtime-копию в `$(AppDir)/Drivers/<DriverCode>`. Fallback на `ScadaComm/OpenDrivers3` нужен только для старых descriptors без `actionProvider.assemblyFile`.

Чтобы автор descriptor видел, откуда реально загружается provider, API возвращает диагностические поля:

| Поле | Где возвращается | Назначение |
|---|---|---|
| `resolvedAssemblyFile` | `GET /api/admin/driver-schema/actions`, `GET /api/admin/actions/catalog` | Полный путь DLL, которую будет загружать web-администратор. |
| `assemblySource` | `GET /api/admin/driver-schema/actions`, `GET /api/admin/actions/catalog` | `descriptor`, если путь взят из `actionProvider.assemblyFile`; `OpenDrivers3 fallback`, если сработала совместимость со старым descriptor. |

Для новых drivers/modules/tools/plugins нужно задавать явный runtime-путь или component assembly в manifest/descriptor. Fallback нужен только как временная совместимость во время разработки. Tools, modules и plugins не должны ссылаться на host assembly `ScadaAdminWebJP.dll` как на реализацию компонента.

Для `DrvDbImportPlus` runtime provider в текущей рабочей схеме собирается из внешнего проекта `C:\Projects\SCADA\RAPIDSCADA_V6\DRIVERS\DrvDbImportPlus_v6`. Путь к готовому `DrvDbImportPlus.View` можно переопределить при сборке web-администратора:

```powershell
dotnet build ScadaAdminWebJP\ScadaAdminWebJP.csproj -p:DrvDbImportPlusViewDir="D:\Drivers\DrvDbImportPlus.View\bin\Release\net8.0-windows"
```

Descriptor всё равно должен ссылаться на runtime-копию внутри web-приложения: `$(AppDir)/Drivers/DrvDbImportPlus/DrvDbImportPlus.View.dll`.

