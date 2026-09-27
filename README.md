# Тайм2гаджет (Time2Gadget)

Компактный настольный таймер для Windows 10/11 в виде виджета: круглый циферблат с пресетами
(5–90 минут), сегментные цифры, звонок и визуальные эффекты по окончании, компактный режим,
живая иконка в трее с прогрессом.

## Установка

Скачайте `Time2Gadget-win-Setup.exe` со страницы [Releases](https://github.com/alexan-moscow/time2gadget/releases)
и запустите. Программа ставится для текущего пользователя, права администратора не нужны.
Если на компьютере нет .NET 8 Desktop Runtime, установщик предложит его установить.

Файлы пока не подписаны цифровой подписью, поэтому Windows SmartScreen может показать
предупреждение: «Подробнее» → «Выполнить в любом случае».

## Сборка из исходников

Нужен .NET 8 SDK.

```
dotnet build src/Time2Gadget/Time2Gadget.csproj -c Release
```

**Звонки в репозиторий не входят.** Встроенные звонки — записи с [Pixabay](https://pixabay.com/sound-effects/);
их лицензия разрешает использовать звуки внутри программы, но не раздавать файлы отдельно.
Поэтому mp3 лежат только локально (`src/Time2Gadget/Assets/Ringtones/`, см. `.gitignore`) и попадают
внутрь exe и установщика. Сборка из чистой копии репозитория работает, но без встроенных звонков
(можно выбрать свой файл в настройках).

Установщик собирается [Velopack](https://velopack.io):

```
dotnet publish src/Time2Gadget/Time2Gadget.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -o publish
vpk pack --packId Time2Gadget --packVersion <версия> --packDir publish --mainExe Time2Gadget.exe --framework net8.0-x64-desktop -o Releases
```

## Документация

Архитектура, решения и контракт интерфейса — в папке [`docs/`](docs/).

## Шрифт

Цифры — шрифт [DSEG](https://github.com/keshikan/DSEG) (keshikan), лицензия SIL Open Font License 1.1
(`src/Time2Gadget/Assets/Fonts/DSEG-LICENSE.txt`).
