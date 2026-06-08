# Contract: Резервная стратегия применения правил брандмауэра

Эта фича не добавляет внешних API/UI-контрактов (никаких новых HTTP-эндпоинтов, CLI-команд или экранов). "Контракты" здесь — это внутренние программные интерфейсы между новыми и существующими компонентами `Steamoff.Infrastructure`/`Steamoff.App`, которые должны оставаться стабильными, чтобы существующие вызывающие стороны (ViewModel'и, диагностика, тесты) продолжали работать без изменений.

## C1. `IFirewallService` — без изменений (сохраняемый контракт)

```csharp
public interface IFirewallService
{
    Task<ActualFirewallState> GetCurrentStateAsync(CancellationToken ct = default);
    Task ApplyBlockAsync(IReadOnlyList<FirewallTarget> targets, DirectionMode directionMode, CancellationToken ct = default);
    Task RemoveOrDisableAsync(IReadOnlyList<FirewallTarget> targets, RuleCleanupMode cleanupMode, CancellationToken ct = default);
    bool IsManagedBySteamoff(FirewallRuleState rule);
}
```

**Гарантия**: и `NetSecurityFirewallService`, и `FallbackAwareFirewallService` реализуют этот же интерфейс без расширений. Любой существующий код, работающий с `AppServices.Firewall as IFirewallService`, продолжает работать буквально без изменений — оркестратор полностью прозрачен для вызывающей стороны (FR-001 satisfied: основной путь и публичный контракт не меняются).

## C2. `NetSecurityFirewallService : IFirewallService` (новый, секундарная стратегия)

**Назначение**: Реализует те же три операции (`GetCurrentStateAsync`/`ApplyBlockAsync`/`RemoveOrDisableAsync`/`IsManagedBySteamoff`) через `New-NetFirewallRule`/`Get-NetFirewallRule`/`Remove-NetFirewallRule`/`Set-NetFirewallRule` (модуль `NetSecurity`), запускаемые через elevated `powershell.exe` с аргументами в виде массива (см. R1 в research.md).

**Контракт поведения** (помимо буквального соответствия сигнатуре `IFirewallService`):
- Каждое создаваемое/изменяемое правило ИМЕНУЕТСЯ и ГРУППИРУЕТСЯ исключительно через `FirewallConstants.RuleGroup` и `FirewallRuleNameBuilder.Build(displayName, direction)` — выходной набор правил после вызова `NetSecurityFirewallService.ApplyBlockAsync(...)` должен быть структурно неотличим (имена, группа, направление, действие, профиль, путь к программе) от набора, который произвёл бы `ComFirewallService.ApplyBlockAsync(...)` для тех же `targets`/`directionMode`.
- `GetCurrentStateAsync()` возвращает `ActualFirewallState`, построенный из `Get-NetFirewallRule -Group "Steamoff"` (плюс связанные `Get-NetFirewallApplicationFilter`/адресные/портовые фильтры по необходимости для заполнения `FirewallRuleState`) — формат идентичен тому, что возвращает COM-стратегия, чтобы оркестратор/ViewModel могли сравнивать состояния независимо от того, какая стратегия их произвела (FR-011 — кросс-стратегийное распознавание правил без дублирования).
- `IsManagedBySteamoff(rule)` использует ту же проверку имени/группы, что и `ComFirewallService` (в идеале — общую вспомогательную логику, чтобы не дублировать критерий "это наше правило").
- Каждая цель (`FirewallTarget`) обрабатывается в собственном try/catch — отказ по одной цели логируется как предупреждение и не прерывает обработку остальных (зеркально per-target резильентности `ComFirewallService`, FR-009).
- Любая ошибка запуска `powershell.exe` (ненулевой код возврата, таймаут, неожиданный вывод) перехватывается как ошибка ЭТОЙ цели/операции — никогда не приводит к падению процесса приложения.
- Никогда не строит команду PowerShell через прямую конкатенацию пользовательских/файловых строк — все значения передаются как элементы `ProcessStartInfo.ArgumentList` и/или через явное безопасное экранирование одиночных кавычек для значений, обязательных внутри `-Command`.

## C3. `FallbackAwareFirewallService : IFirewallService` (новый, оркестратор)

**Конструктор**: `FallbackAwareFirewallService(IFirewallService primary, IFirewallService secondary, ILogService log, ILocalizedLogService localizedLog)`

**Контракт поведения**:
- `GetCurrentStateAsync()`: читает через `primary`, затем точечно обогащает результат. **Обновлено по факту обнаруженной проблемы:** `ComFirewallService.ToRuleState` молча проглатывает `COMException` при чтении `rule.ApplicationName` для правил, чей фильтр приложения был задан через провайдер NetSecurity/CIM (т.е. для правил, созданных резервной стратегией) — из-за этого `StatusEvaluator.PathsMatch` никогда не совпадает с `null`, и панель показывает «0% покрытия» для правил, которые на деле существуют, включены и активно блокируют. Поэтому, если среди управляемых Steamoff-правил из `primary` встречаются записи с пустым `ApplicationName`, оркестратор дополнительно запрашивает состояние через `secondary` (он читает фильтр приложения напрямую через `Get-NetFirewallApplicationFilter` и не подвержен этой проблеме чтения) и подставляет известный `ApplicationName` по совпадению `RuleName`+`Direction`. Это расширение исключительно для повышения точности отображения состояния — оно не меняет логику выбора целей и не используется для решений о резерве при операциях изменения правил (см. ниже).
- `ApplyBlockAsync(targets, directionMode, ct)` и `RemoveOrDisableAsync(targets, cleanupMode, ct)`:
  1. Выполнить операцию через `primary`.
  2. Если выброшено исключение → `FailureReason = Exception`; перейти к шагу 4.
  3. Иначе верифицировать через `primary.GetCurrentStateAsync()` (или эквивалентный быстрый запрос состояния группы `Steamoff`): если ожидавшиеся правила не появились/не обновились → `FailureReason = NoRulesProduced`; перейти к шагу 4. Если появились — записать обычное событие завершения (`FirewallBlockCompleted`/`FirewallUnblockCompleted`, БЕЗ упоминания стратегии — см. R4) и завершить с `StrategyUsed = Primary`.
  4. Выполнить операцию через `secondary`. Если она тоже завершилась неуспехом (исключение ИЛИ верификация пуста) → записать `FirewallBothStrategiesFailed` (единое сообщение в обоих журналах) и пробросить понятную ошибку вызывающей стороне. Если успешна → записать `FirewallStrategyFallbackUsed` (с `FailureReason` и итоговым резюме) в технический лог и краткое резюме в локализованный пользовательский журнал, затем обычное событие завершения операции; завершить с `StrategyUsed = Fallback`.
- `IsManagedBySteamoff(rule)`: делегирует в `primary` (оба должны давать одинаковый ответ по построению — C2; делегирование в `primary` выбрано как канонический источник для согласованности с уже существующим поведением до этой фичи).
- Резерв запускается ТОЛЬКО если основная стратегия признана неуспешной — на "счастливом пути" (подавляющее большинство запусков) `secondary` не вызывается вообще, обеспечивая нулевые накладные расходы (FR-010).

**Зависимости от существующих абстракций**: `ILogService` (технический лог, `Warning`/`Info` — те же уровни, что заданы в `LogEventTemplates` для новых ключей) и `ILocalizedLogService` (пользовательский локализованный журнал, `LogAsync(LogEventKey, params object[] args)`).

## C4. Точка внедрения зависимостей: `Steamoff.App/AppServices.cs`

**Было**:
```csharp
Firewall = new ComFirewallService(Log);
```

**Станет**:
```csharp
Firewall = new FallbackAwareFirewallService(
    new ComFirewallService(Log),
    new NetSecurityFirewallService(Log),
    Log,
    LocalizedLog);
```

Это единственное изменение в production-коде вне `Steamoff.Infrastructure/Firewall/` и `Steamoff.Core/Logging/`+локализации. Тип публичного свойства `AppServices.Firewall` (`IFirewallService`) не меняется.

## C5. Расширение `LogEventKey`/`LogEventTemplates` (контракт логирования)

Два новых значения добавляются в конец существующего перечисления `LogEventKey` (не нарушая порядок/значения существующих — enum используется как идентификатор, не как сериализуемое числовое значение для внешнего хранилища, проверить по текущему файлу при реализации):

| `LogEventKey` | Ключ локализации | Уровень | Когда пишется |
|---|---|---|---|
| `FirewallStrategyFallbackUsed` | `log.event.firewallStrategyFallbackUsed` | `Warning` | Резерв сработал успешно вместо основной стратегии |
| `FirewallBothStrategiesFailed` | `log.event.firewallBothStrategiesFailed` | `Error` | Ни основная, ни резервная стратегия не смогли создать/изменить ожидаемые правила |

Локализационные строки добавляются параллельно во все 9 файлов (`ru`, `en`, `de`, `es`, `fr`, `it`, `pl`, `pt`, `zh`) — формат и тон соответствуют уже существующим `log.event.firewallBlock*`/`firewallUnblock*` записям; существующий тест паритета локализации (`tests/Steamoff.Tests/.../Localization`) должен продолжать проходить и автоматически проверит полноту покрытия по всем языкам.
