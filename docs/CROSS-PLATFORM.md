# SteadyClock 2.0 — кроссплатформенная версия

Ветка `feature/cross-platform` содержит новый клиент на **Avalonia 12 + .NET 10**.
Стабильная WPF-версия для Windows остаётся в `main` до завершения проверки.

## Состояние платформ

| Возможность | Windows | Linux X11 | Linux Wayland | macOS |
|---|---|---|---|---|
| Таймер, история, ручные записи | проверено | сборка | сборка | сборка |
| Автопауза по простою | проверено | X11 | нет данных о простое | CoreGraphics |
| Блокировка экрана / сон | проверено | через простой | через простой | через простой |
| Трей | проверено | зависит от рабочего стола | зависит от рабочего стола | зависит от рабочего стола |
| Глобальные горячие клавиши | проверено | X11 | недоступны | пока недоступны |
| Автозапуск | проверено | XDG autostart | XDG autostart | LaunchAgent |
| Режим «По приложению» | проверено | `/proc` | ограничен песочницей | список процессов |
| Звуки уведомлений | проверено | `paplay` / `pw-play` / `aplay` | как в X11 | `afplay` |

«Проверено» означает ручной smoke-тест в Windows. Сборки Linux и macOS
компилируются и публикуются, но ещё не проверялись на реальных системах.
Wayland-ограничения относятся к определению простоя и глобальным горячим
клавишам: без XWayland эти API недоступны.

## Данные

Windows сохраняет данные в прежней папке, поэтому история WPF-версии
подхватывается автоматически:

```
%LOCALAPPDATA%\MyTimeTracker\
  data.db
  settings.json
  logs\
```

Другие платформы:

```
macOS:  ~/Library/Application Support/SteadyClock/
Linux:  $XDG_DATA_HOME/steadyclock/  (или ~/.local/share/steadyclock/)
```

Переменная `STEADYCLOCK_DATA_DIR` позволяет запускать отдельный профиль данных.
В этом режиме приложение не синхронизирует системный автозапуск.

## Автозапуск

- **Windows:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, значение
  `SteadyClock`, без прав администратора.
- **Linux:** `~/.config/autostart/steadyclock.desktop`.
- **macOS:** `~/Library/LaunchAgents/com.steadyclock.app.plist`.

## Сборка

Нужен .NET 10 SDK.

```powershell
dotnet build src/SteadyClock.Desktop/SteadyClock.Desktop.csproj -c Release
dotnet test tests/MyTimeTracker.Tests/MyTimeTracker.Tests.csproj -c Release
```

Публикация всех поддерживаемых платформ:

```powershell
./build/publish-desktop.ps1
```

Готовые папки появятся в `publish/SteadyClock-<rid>`. Для Linux и macOS после
распаковки zip может потребоваться `chmod +x SteadyClock` — zip не сохраняет
Unix-права на исполнение.

## Известные ограничения

- Глобальные горячие клавиши на macOS пока не реализованы; интерфейс показывает
  предупреждение и оставляет кнопки в окне и трее.
- Wayland не даёт приложению определить системный простой и зарегистрировать
  глобальные горячие клавиши. Остальные функции работают.
- Flatpak/Snap могут скрывать процессы хоста, поэтому режим «По приложению»
  видит только процессы внутри песочницы.
- macOS-сборка не подписана. Gatekeeper может потребовать разрешение при
  первом запуске.
- Системный трей на Linux зависит от наличия AppIndicator/StatusNotifier в
  рабочем столе.
