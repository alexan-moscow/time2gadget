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

## Антивирусы и ложные срабатывания

Файлы программы пока не подписаны цифровой подписью. Неподписанные самораспаковывающиеся установщики
(установщик Velopack распаковывает программу в профиль пользователя и запускает её) иногда помечаются
эвристиками машинного обучения — это видно по суффиксу `!ml` / `ml.score` в названии срабатывания, например
`Trojan:Win32/Wacatac.C!ml`. Это не найденный вирус, а «похоже на» по формальным признакам.

Для каждого выпуска в его описании есть ссылка на отчёт VirusTotal; о ложных срабатываниях сообщается
производителю антивируса, номер заявки и результат указываются там же. Исходный код полностью открыт —
его можно проверить и собрать самостоятельно (ниже). Кнопка «Проверка на VirusTotal» в настройках программы
открывает отчёт именно по установщику вашей версии.

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

## Как выпустить новую версию

1. Поднять `<Version>` в `src/Time2Gadget/Time2Gadget.csproj`, закоммитить и отправить.
2. В папке `Releases/` должен лежать `full.nupkg` предыдущей версии — тогда Velopack соберёт дельта-обновление
   (иначе сначала `vpk download github --repoUrl https://github.com/alexan-moscow/time2gadget -o Releases`).
3. Собрать (команды выше, `--packVersion` = новая версия, звонки должны лежать в `Assets/Ringtones/`).
4. Опубликовать: `vpk upload github --outputDir Releases --repoUrl https://github.com/alexan-moscow/time2gadget --token <токен gh> --publish --releaseName "Тайм2гаджет X.Y.Z" --tag vX.Y.Z --targetCommitish main`,
   затем добавить описание выпуска (`gh release edit vX.Y.Z --notes-file …`).
5. Загрузить `Time2Gadget-win-Setup.exe` нового выпуска на VirusTotal — кнопка в программе найдёт отчёт сама
   по отпечатку файла из выпуска.

Установленные копии найдут выпуск сами (раз в неделю) или по кнопке «Проверить обновления».

## Документация

Архитектура, решения и контракт интерфейса — в папке [`docs/`](docs/).

## Шрифт

Цифры — шрифт [DSEG](https://github.com/keshikan/DSEG) (keshikan), лицензия SIL Open Font License 1.1
(`src/Time2Gadget/Assets/Fonts/DSEG-LICENSE.txt`).
