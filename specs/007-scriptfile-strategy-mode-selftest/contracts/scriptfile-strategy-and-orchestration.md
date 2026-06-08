# Contract: Третья (ScriptFile) стратегия, режим выбора стратегии и первичное самотестирование

Эта фича не добавляет внешних API/UI-контрактов (никаких новых HTTP-эндпоинтов или CLI). Как и в feature 006,
"контракты" — это внутренние программные интерфейсы между новыми и существующими компонентами
`Steamoff.Infrastructure`/`Steamoff.App`, которые должны оставаться стабильными для существующих вызывающих сторон.

## C1. `IFirewallService` — без изменений (сохраняемый контракт)

Сигнатура идентична feature 006 (см. `specs/006-firewall-fallback-strategy/contracts/firewall-fallback.md` C1) и
**не меняется**. `ScriptFileFirewallService` реализует её наравне с `ComFirewallService`/`NetSecurityFirewallService`/
`FallbackAwareFirewallService` — любой существующий вызывающий код продолжает работать буквально без изменений
(FR-001, FR-015).

## C2. `ScriptFileFirewallService : IFirewallService` (новый, "Вариант 3")

**Конструктор**: `ScriptFileFirewallService(IFirewallScriptFileWriter scriptWriter, IPowerShellCommandRunner runner, ILogService log)`
(сигнатура повторяет уже проверенный паттерн внедрения зависимостей `NetSecurityFirewallService`/`PowerShellRuleInvoker`,
включая возможность подмены `IPowerShellCommandRunner` в тестах без реального процесса — продолжение конвенции
feature 006).

**Контракт поведения**:
- Перед каждой операцией проверяет (через `scriptWriter.EnsureUpToDateAsync()`) актуальность файла
  `steamoff-firewall.ps1` (создаёт/обновляет при необходимости — research.md R4, FR-005), затем запускает его как
  ОТДЕЛЬНЫЙ elevated-процесс:
  ```text
  powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "<scriptPath>"
  ```
  с данными операции, переданными исключительно через переменные окружения процесса —
  `STEAMOFF_OPERATION` (`Apply`/`Remove`/`Query`), `STEAMOFF_DISPLAY_NAME`, `STEAMOFF_RULE_GROUP`,
  `STEAMOFF_RULE_DIRECTION`, `STEAMOFF_PROGRAM`, `STEAMOFF_RULE_DESCRIPTION` — буквально тот же канал и те же пять
  переменных, что уже доказанно работают в `PowerShellRuleInvoker` (research.md R3), плюс новый селектор операции.
- НИКОГДА не использует `UseShellExecute = true`/`Verb = "runas"` — дочерний процесс наследует уже повышенный
  токен приложения (research.md R2); это сохраняет уже установленный в `ProcessPowerShellCommandRunner` паттерн
  без повторной реализации — `ScriptFileFirewallService` может использовать тот же `IPowerShellCommandRunner`,
  расширенный поддержкой `-File`-инвокаций (т.е. `PowerShellInvocation` с `Arguments` вида
  `["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath]`).
- Каждая создаваемая/изменяемая запись ИМЕНУЕТСЯ и ГРУППИРУЕТСЯ исключительно через `FirewallConstants.RuleGroup` и
  `FirewallRuleNameBuilder.Build(displayName, direction)` — выходной набор правил структурно неотличим от набора,
  который произвели бы `ComFirewallService`/`NetSecurityFirewallService` для тех же `targets`/`directionMode`
  (FR-002 — точное зеркало гарантии C2 из feature 006).
- `GetCurrentStateAsync()` возвращает `ActualFirewallState`, построенный из вывода `Get-NetFirewallRule -Group
  "Steamoff"` (плюс связанные фильтры приложения/адреса/порта) внутри того же script-файла — формат идентичен тому,
  что возвращают обе существующие стратегии.
- `IsManagedBySteamoff(rule)` использует ту же общую проверку имени/группы, что и остальные две реализации (общая
  вспомогательная логика — не дублируется заново).
- Каждая цель (`FirewallTarget`) обрабатывается в собственном try/catch внутри скрипта — отказ по одной цели не
  прерывает обработку остальных (FR-003, зеркально per-target резильентности feature 006 C2).
- Любая ошибка запуска/выполнения `powershell.exe -File` (ненулевой код возврата, таймаут, неожиданный вывод)
  перехватывается как ошибка ЭТОЙ операции — никогда не приводит к падению процесса приложения.
- Никогда не строит PowerShell-код через конкатенацию пользовательских строк — весь пользовательский ввод приходит
  исключительно через `STEAMOFF_*` переменные окружения, читаемые внутри статического содержимого скрипта.

## C3. `IFirewallScriptFileWriter` / `FirewallScriptFileWriter` (новый, управление файлом скрипта)

```csharp
public interface IFirewallScriptFileWriter
{
    /// <summary>Гарантирует, что файл скрипта присутствует на диске и содержит ожидаемое для текущей сборки содержимое;
    /// при необходимости атомарно (пере)записывает его. Возвращает абсолютный путь к готовому к использованию файлу.</summary>
    Task<string> EnsureUpToDateAsync(CancellationToken ct = default);
}
```

**Контракт поведения** (research.md R4):
- Канонический путь: `<applicationBaseDirectory>\Scripts\steamoff-firewall.ps1` — единственный, никогда не
  дублируется и не суффиксируется по версии (FR-005).
- "Актуально" определяется сравнением SHA-256 текущего содержимого файла с хэшем содержимого, ожидаемого ТЕКУЩЕЙ
  сборкой (встроенная константа); при отсутствии файла, ошибке чтения или несовпадении хэша — атомарная
  перезапись (временный файл в той же папке + `File.Move` с перезаписью), что исключает как "наполовину записанный"
  файл, так и накопление конфликтующих копий между обновлениями приложения (FR-005, Edge Cases).
- Содержимое скрипта ВСЕГДА начинается с `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force`
  (обёрнуто в `try/catch`, никогда не считается фатальным — research.md R1) и использует исключительно
  `FirewallConstants`/`FirewallRuleNameBuilder`-совместимые имена — НИКОГДА прототипные
  `"SteamOfflineToggle"`/`"Steam Offline IN/OUT - <exe>"` (связывающее ограничение из feature 006, перенесённое
  буквально).

## C4. `FallbackAwareFirewallService : IFirewallService` (повышен до режимо-зависимого 3-стороннего оркестратора)

**Новый конструктор**: `FallbackAwareFirewallService(IFirewallService primary, IFirewallService secondary,
IFirewallService scriptFile, Func<FirewallStrategyMode> currentModeProvider,
Func<FirewallStrategyVariant?, Task> rememberSuccessAsync, ILogService log, ILocalizedLogService localizedLog)`

> Передача режима/памяти через делегаты (а не прямую зависимость от `AppSettings`/`SettingsService`) сохраняет
> существующую тестируемость через простые fakes (см. `FallbackAwareFirewallServiceTests`/`ScriptedFirewallService`)
> без введения зависимости оркестратора `Steamoff.Infrastructure` от слоя настроек `Steamoff.App`/`Steamoff.Core.Models`
> сверх уже существующих — `currentModeProvider` читает текущее значение `AppSettings.FirewallStrategyMode` в момент
> старта операции (фиксируя его на всё время операции — FR-014), `rememberSuccessAsync` персистентно обновляет
> `AppSettings.LastSuccessfulFirewallStrategy`.

**Контракт поведения**:
- В начале каждой `ApplyBlockAsync`/`RemoveOrDisableAsync` вызывает `currentModeProvider()` РОВНО ОДИН РАЗ и
  фиксирует результат на всю операцию — последующее изменение режима пользователем не влияет на уже идущую
  операцию (FR-014, Edge Case "переключение режима во время операции").
- **Режим `Auto`**: строит порядок попыток как `[вспомненная-последняя-успешная, ...остальные в каноническом
  порядке Primary → Secondary → ScriptFile, без дублей]` — т.е. если `LastSuccessfulFirewallStrategy = ScriptFile`,
  порядок становится `[ScriptFile, Primary, Secondary]`; если память пуста (`null`), порядок — канонический
  `[Primary, Secondary, ScriptFile]` (предохраняет полную обратную совместимость с поведением feature 006 для
  пользователей, которые никогда не взаимодействовали с новой настройкой — FR-015). Пробует по порядку, как и в
  существующем `ExecuteWithFallbackAsync`/`TryStrategyAsync` (та же верификация через перечитывание состояния,
  тот же `VerificationAttempts`/`VerificationRetryDelay`); при первом успехе — вызывает
  `rememberSuccessAsync(этот вариант)` и завершает с `StrategyUsed = <этот вариант>`. Если ВСЕ три потерпели
  неудачу — записывает `FirewallAllStrategiesFailed` (расширенный аналог `FirewallBothStrategiesFailed` для
  3-стороннего случая) и пробрасывает `FirewallOperationException`; `rememberSuccessAsync` НЕ вызывается (память
  не портится единичным полным отказом — она остаётся последним известным успехом до следующего успеха, что и есть
  "self-healing": при следующей попытке снова пробуем именно её первой, поскольку именно так выглядит
  "самоисцеление" — временная неудача не должна стирать единственное известное хорошее воспоминание).
- **Режимы `ForcePrimary`/`ForceSecondary`/`ForceScriptFile`**: вызывает РОВНО ОДНУ соответствующую стратегию —
  никогда не пробует остальные (FR-008, "никакого тихого резерва"). При успехе — обычное событие завершения
  (`FirewallBlockCompleted`/`FirewallUnblockCompleted`) БЕЗ упоминания "резерва" (это не резерв — это
  единственный выбранный пользователем путь) и `rememberSuccessAsync(этот вариант)` (форсированный успех всё же
  достоин запоминания — он лучше, чем отсутствие данных, и именно этого ожидает пользователь, "запирающий" известный
  рабочий путь, от последующего поведения `Auto`, если он туда переключится). При неудаче — записывает новое
  событие `FirewallForcedStrategyFailed` (технический + локализованный журнал, с явным указанием, какой именно
  "Вариант N" был выбран и не сработал, и нейтральной подсказкой, что "Авто" или другой вариант могут сработать
  лучше — FR-008, Edge Case "форсированный вариант не работает на этой машине") и пробрасывает
  `FirewallOperationException` — БЕЗ обращения к двум другим стратегиям.
- `GetCurrentStateAsync()`: сохраняет существующее кросс-стратегийное обогащение `ApplicationName` из feature 006
  (делегирование в `primary`, точечное обогащение через `secondary` при обнаружении пустых значений — C3 из
  `firewall-fallback.md`, без изменений) — `scriptFile` не участвует в чтении состояния (его `GetCurrentStateAsync`
  реализация существует только для соответствия контракту `IFirewallService` и для собственной верификации внутри
  `TryStrategyAsync`, когда оно само является активной стратегией).
- `IsManagedBySteamoff(rule)`: делегирует в `primary`, без изменений (C3 из feature 006).

## C5. `FirewallSelfTestRunner` (новый, первичное самотестирование)

```csharp
public interface IFirewallSelfTestRunner
{
    /// <summary>Если ещё не запускалось (FirewallSelfTest.Outcome == NotYetRun) — безопасно проверяет все три
    /// стратегии вероятностным create→verify→remove зондом (research.md R5), записывает результат в AppSettings
    /// и в оба журнала, и сидирует LastSuccessfulFirewallStrategy. Идемпотентно — повторные вызовы после
    /// завершения являются no-op.</summary>
    Task RunIfNeededAsync(CancellationToken ct = default);
}
```

**Контракт поведения**:
- Вызывается один раз при старте приложения (после инициализации `AppServices`/`Firewall`, до первой реальной
  операции блокировки — FR-010), используя ТЕ ЖЕ ТРИ экземпляра `IFirewallService` (`primary`/`secondary`/
  `scriptFile`), что и оркестратор — "тестируем именно то, что реально будет работать" (research.md R5), НЕ
  отдельные probe-реализации.
- Зонд для каждой стратегии: создаёт ОДНО временное правило с группой `"Steamoff-SelfTest-Probe"` (НЕ
  `FirewallConstants.RuleGroup` — изоляция от реальных Steamoff-правил и от подсчёта покрытия, FR-011), нацеленное
  на безвредный путь, немедленно проверяет его наличие, затем немедленно удаляет и проверяет отсутствие — всё в
  `try/finally`, гарантирующем попытку удаления даже при исключении на любом шаге (research.md R5).
- Результат — список из 0..3 `FirewallStrategyVariant`, успешно прошедших полный цикл create→verify→remove→verify;
  записывается в `AppSettings.FirewallSelfTest` (`Outcome = CompletedWithResult`, `WorkingStrategies = <список>`,
  `CompletedAt = now`) и атомарно персистируется (существующая конвенция `AppSettings`).
- Если список непуст — `LastSuccessfulFirewallStrategy` сидируется первым найденным рабочим вариантом в
  каноническом порядке `Primary → Secondary → ScriptFile` (предсказуемый, воспроизводимый выбор при равных шансах
  — FR-010).
- Если зонд прерван (исключение выше уровня per-strategy try/catch, отмена и т.п.) — записывает
  `Outcome = Inconclusive` (отдельно от `NotYetRun`/`CompletedWithResult` — FR-013) и НЕ повторяет попытку при
  следующих запусках; `Auto` продолжает работать через полный канонический каскад без посева.
- Логирует результат в обоих журналах: технический (`ILogService.Info`, подробности по каждой стратегии) и
  локализованный пользовательский (новый `LogEventKey.FirewallSelfTestCompleted`/`FirewallSelfTestInconclusive` —
  человекочитаемая сводка "на вашем компьютере работает: …; не работает: …" — FR-012).

## C6. Точка внедрения зависимостей: `Steamoff.App/AppServices.cs`

**Было** (после feature 006):
```csharp
Firewall = new FallbackAwareFirewallService(
    new ComFirewallService(Log),
    new NetSecurityFirewallService(Log),
    Log,
    LocalizedLog);
```

**Станет**:
```csharp
var primary = new ComFirewallService(Log);
var secondary = new NetSecurityFirewallService(Log);
var scriptFile = new ScriptFileFirewallService(new FirewallScriptFileWriter(), new ProcessPowerShellCommandRunner(), Log);

Firewall = new FallbackAwareFirewallService(
    primary, secondary, scriptFile,
    () => Settings.Current.FirewallStrategyMode,
    variant => Settings.UpdateAsync(s => s.LastSuccessfulFirewallStrategy = variant),
    Log,
    LocalizedLog);

SelfTestRunner = new FirewallSelfTestRunner(primary, secondary, scriptFile, Settings, Log, LocalizedLog);
```

Тип публичного свойства `AppServices.Firewall` (`IFirewallService`) не меняется (C1). `AppServices.SelfTestRunner`
— новое свойство, вызываемое из `App.xaml.cs` при старте (см. quickstart.md).

## C7. Расширение `LogEventKey`/`LogEventTemplates` (контракт логирования)

Новые значения добавляются в конец существующего перечисления `LogEventKey` (после `FirewallBothStrategiesFailed`,
не нарушая порядок существующих — та же гарантия, что и в C5 feature 006):

| `LogEventKey` | Ключ локализации | Уровень | Когда пишется |
|---|---|---|---|
| `FirewallAllStrategiesFailed` | `log.event.firewallAllStrategiesFailed` | `Error` | Режим `Auto`: ни одна из трёх стратегий не смогла создать/изменить ожидаемые правила |
| `FirewallForcedStrategyFailed` | `log.event.firewallForcedStrategyFailed` | `Error` | Форсированный режим: единственная выбранная пользователем стратегия не справилась — без обращения к другим |
| `FirewallSelfTestCompleted` | `log.event.firewallSelfTestCompleted` | `Info` | Первичное самотестирование завершилось — сводка, какие варианты работают |
| `FirewallSelfTestInconclusive` | `log.event.firewallSelfTestInconclusive` | `Warning` | Первичное самотестирование не смогло завершиться чисто — записано отдельно от "никогда не запускалось" |

Локализационные строки добавляются параллельно во все 9 файлов (`ru`, `en`, `de`, `es`, `fr`, `it`, `pl`, `pt`,
`zh`) — формат и тон соответствуют уже существующим `log.event.firewall*`-записям; существующий тест паритета
локализации продолжает проходить и автоматически проверит полноту покрытия (та же гарантия, что в C5 feature 006).

## C8. Расширение Settings UI: `SettingsWindow`

Новая группа элементов управления "Стратегия применения правил брандмауэра" — четыре варианта (`RadioButton`/аналог уже
используемых в существующих группах выбора, в стиле Dark Orange Neumorphic, без `MessageBox` — принцип VI):
"Авто" / "Вариант 1" / "Вариант 2" / "Вариант 3", с кратким описанием под каждым (что он форсирует), индикацией
текущего выбора и применением изменения немедленно (без перезапуска — FR-007/Acceptance Scenario US2.4),
размещённая в существующей секции диагностики/брандмауэра рядом с уже существующими переключателями (никакой
новой top-level поверхности — Assumption из spec.md).
