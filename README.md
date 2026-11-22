<p align="center">
  <img src="docs/banner.svg" alt="SteadyClock — тихий офлайн-трекер времени для Windows" width="100%">
</p>

<p align="center">
  <strong>Тихая сосредоточенность. Один таймер. Локальная история.</strong>
</p>

<p align="center">
  <a href="https://github.com/GGadjiev/steadyclock/releases/latest"><img src="https://img.shields.io/badge/version-2.0.0-1E5B54?style=flat-square" alt="SteadyClock 2.0.0"></a>
  <img src="https://img.shields.io/badge/Windows-10%2F11-1E5B54?style=flat-square" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" alt=".NET 10">
  <img src="https://img.shields.io/badge/license-MIT-A9C6BB?style=flat-square" alt="MIT">
</p>

---

**SteadyClock 2.0** — спокойный офлайн-трекер времени для Windows. Интерфейс
«Quiet Focus» построен вокруг одного таймера: тёплый фарфоровый фон, глубокий
хвойный акцент, крупные цифры и ничего лишнего.

Приложение не обращается к интернету: история, настройки и логи остаются на
компьютере. Установка и права администратора не нужны.

## Что нового в 2.0

- Новый дизайн **Quiet Focus** со светлой и тёмной темами.
- Крупный таймер на Inter и JetBrains Mono.
- История по дням с итогами за сегодня, неделю и всё время.
- Семидневный спарклайн на главном экране.
- Два режима вычитания простоя: от последней активности и с момента обнаружения.
- Свой звук уведомлений до 5 секунд: WAV, MP3, M4A, WMA или AAC.
- Windows-only сборки для x64 и ARM64.

## Интерфейс

| Светлая тема | Тёмная тема |
|:---:|:---:|
| <img src="docs/screenshots/main-light.png" width="360" alt="Главный экран, светлая тема"> | <img src="docs/screenshots/main-dark.png" width="360" alt="Главный экран, тёмная тема"> |
| <img src="docs/screenshots/history-light.png" width="360" alt="История"> | <img src="docs/screenshots/settings-light.png" width="360" alt="Настройки"> |

## Возможности

- Один таймер: старт, пауза, продолжение, стоп.
- Автопауза по простою с настройкой порога, автовозобновления и блокировки экрана.
- История по дням с итогами за сегодня, неделю и всё время.
- Ручные записи и комментарий к активной сессии.
- Уведомления со встроенными и своими звуками, тихими часами и настройкой событий.
- Режим «По приложению»: автостарт и автостоп по выбранному процессу.
- Глобальные горячие клавиши и автозапуск при входе в Windows.
- Системный трей, закрытие в трей и диалог выхода с активной сессией.
- Светлая и тёмная темы.
- Одноэкземплярный запуск.

## Скачать

**Стабильная версия 2.0.0** · [все релизы](https://github.com/GGadjiev/steadyclock/releases/latest)

| Платформа | Архитектура | Файл |
|---|---|---|
| Windows | x64 | **[SteadyClock-win-x64.zip](https://github.com/GGadjiev/steadyclock/releases/latest/download/SteadyClock-win-x64.zip)** |
| Windows | ARM64 | **[SteadyClock-win-arm64.zip](https://github.com/GGadjiev/steadyclock/releases/latest/download/SteadyClock-win-arm64.zip)** |

Установка не требуется: распакуйте архив и запустите `SteadyClock.exe`.
Права администратора и установленный .NET не нужны.

Обновление с бета-версий безопасно: история и настройки в
`%LOCALAPPDATA%\MyTimeTracker` сохраняются.

## Где хранятся данные

```
%LOCALAPPDATA%\MyTimeTracker\
  data.db          SQLite: сессии и сегменты
  settings.json    настройки
  sounds\          свой звук уведомлений (custom.wav)
  logs\            локальные логи с ротацией
```

Папка `MyTimeTracker` сохранена для обратной совместимости с предыдущими
версиями. Для резервной копии закройте приложение и скопируйте папку целиком.

## Сборка

Нужен .NET 10 SDK.

```powershell
dotnet build src/SteadyClock.Desktop/SteadyClock.Desktop.csproj -c Release
dotnet test tests/MyTimeTracker.Tests/MyTimeTracker.Tests.csproj -c Release
```

Публикация Windows-сборок (x64 и ARM64):

```powershell
./build/publish-desktop.ps1
```

Готовые архивы появятся в `publish/`.

## Тесты

Покрыто 39 детерминированными сценариями:

- работа таймера, пауза, продолжение и восстановление после сбоя;
- простой, сон, блокировка экрана и системные разрывы;
- два режима вычитания простоя;
- ручные записи, пересечения и будущие даты;
- SQLite-репозиторий, сегменты и источники сессий;
- настройки, напоминания и уведомления.

## Дизайн

Концепция и исходные макеты находятся в [`design/`](design/README.md).

- Шрифты: Inter для интерфейса, JetBrains Mono для таймера.
- Палитра: Porcelain `#F5F2EA`, Pine `#1E5B54`, Sage `#A9C6BB`, Coral `#E08A6A`.
- Карточки 20–24 px, поля 12 px, кнопки pill/14 px.
- Анимации 100–200 ms ease-out.

## Приватность

- Нет аккаунтов, телеметрии и проверки обновлений.
- Нет сетевых запросов и CDN.
- Уведомления и звуки работают локально.
- Единственный IPC — локальный канал одноэкземплярного запуска.

## Лицензия

MIT. Copyright © GGadjiev.

Сторонние компоненты: шрифты Inter и JetBrains Mono (SIL OFL 1.1),
[NAudio](https://github.com/naudio/NAudio) (MS-PL) для импорта пользовательских звуков.
