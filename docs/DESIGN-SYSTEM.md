# DESIGN-SYSTEM.md

Дизайн-токены проекта — WPF-эквивалент CSS custom properties из КЛЮЧ24. Источник истины для `src/TimerGadget/Resources/Theme.xaml`. Значения ниже — начальная, разумная отправная точка; финальная визуальная подгонка — по факту визуального ревью пользователем (см. `BRIEF.md` §23 UI FREEZE).

## Цвета

| Токен | Значение | Роль |
|---|---|---|
| `Color.Background` | `#0E1420` | фон окна — очень тёмный сине-серый |
| `Color.SurfaceSector` | `#1C2433` @ 70% opacity | обычная поверхность сектора |
| `Color.SurfaceSectorHover` | `#26314A` @ 85% opacity | сектор при hover |
| `Color.Accent` | `#3D8BFF` | синий акцент — выбранный сектор, progress-ring, Start-кнопка |
| `Color.AccentHover` | `#5C9EFF` | акцент при hover/press |
| `Color.TextPrimary` | `#F5F7FA` | основной текст (время) |
| `Color.TextSecondary` | `#8CA0BE` | вторичный текст (статус, подписи секторов) |
| `Color.BorderSubtle` | `#2A3547` @ 60% opacity | тонкие границы/разделители секторов |
| `Color.Finish` | `#FF6B4A` | завершение таймера — красно-оранжевый |
| `Color.ShadowSoft` | `#000000` @ 35% opacity, blur 24 | мягкая тень под панелью |

## Геометрия

Актуально на 2026-09-27 (см. `docs/DECISIONS.md` — «уменьшено на 25% + структурное центрирование»,
«убраны лишние padding по ушам», «Compact — скруглённый прямоугольник, не круг»).

| Токен | Normal | Compact |
|---|---|---|
| Размер окна (с учётом Chrome-margin 8 со всех сторон) | 273×341 | 236×156 |
| Форма Compact | — | скруглённый прямоугольник (CornerRadius 20), НЕ круг |
| Радиус скругления окна (Chrome) | 28 | 20 |
| Внешний радиус кольца секторов | 108 | — (скрыто) |
| Внутренний радиус кольца секторов | 84 | — (скрыто) |
| Зазор между секторами | 2° | — |
| Внешний радиус progress-ring | 78 | — (линейный прогресс-бар вместо кольца) |
| Внутренний радиус progress-ring | 74 | — |
| EffectOverlay (диаметр, для Pulse/Flash/ColorBreathe) | 225 | — |
| Центрирование времени | `CenterDisplay` — ребёнок ТОЙ ЖЕ Grid-ячейки, что и кольца (`RingGroup`), оба `HorizontalAlignment/VerticalAlignment=Center` без Margin — центр текста математически совпадает с центром колец при любых будущих изменениях радиусов | центр контейнера (Grid.Row=0, Stretch) |
| Отступы (padding) | единые 16px со ВСЕХ сторон — верх/бока/низ И зазор между кольцом и рядом кнопок (докладка 2026-09-27, дважды подтверждено пользователем) | единые 8px (см. `CompactContent` centering) |
| Радиус circle-кнопок управления | ~20 (PrimaryCircleButton крупнее) | 16 (мелкие Mute/Reset), Primary — как в Normal |
| Угловые кнопки (шестерёнка / крестик) | 28×28, `Style.CornerIconButton`, top-left/top-right, Margin 10 — одинаковое положение в обоих режимах (дети RootGrid, не NormalContent/CompactContent). Иконки — векторный `Path` (`Geometry.Gear`/`Geometry.Close`, Material Design, solid fill), НЕ символьный шрифт (докладка 2026-09-27) | то же |
| Регулятор громкости звонка | тонкий `Style.ThinVolumeSliderVertical` (трек 3px, thumb 10px), СЛЕВА от кнопки Mute, высота = высоте кнопки (40) | — (скрыт, компакт не показывает громкость) |

## Типографика

| Токен | Размер | Начертание | Использование |
|---|---|---|---|
| `Type.TimeDisplay` | 44px | SemiBold | центральное время (`23:47`) |
| `Type.TimeDisplayCompact` | 30px | SemiBold | время в Compact-режиме |
| `Type.StatusLabel` | 12px, letter-spacing 1.5 | Medium, UPPERCASE | `ОСТАЛОСЬ` / `ГОТОВ` |
| `Type.SectorValue` | 15px | SemiBold | число в секторе (`5`) |
| `Type.SectorUnit` | 9px, letter-spacing 0.5 | Medium, UPPERCASE | `МИН` |

Шрифт — системный `Segoe UI Variable` (доступен в Windows 10 через компонент, fallback `Segoe UI`), без внешних веб-шрифтов.

## Анимация

| Токен | Длительность | Easing |
|---|---|---|
| `Anim.Hover` | 120ms | ease-out |
| `Anim.SelectSector` | 150ms | ease-out |
| `Anim.ProgressTick` | continuous (не дискретно по секундам, а плавно — см. ARCHITECTURE.md Timer Engine) | linear |
| `Anim.FinishPulse` | 900ms, loop | ease-in-out |
| `Anim.ModeSwitch` (Normal↔Compact) | 200ms | ease-in-out |

## Принципы (обязательны)
- НЕ градиенты в стиле старых Windows-программ.
- НЕ массивные рамки, НЕ стандартные квадратные WPF-кнопки/чекбоксы.
- Один акцентный цвет (`Color.Accent`) — не вводить второй конкурирующий акцент без явного решения.
- Все геометрические/цветовые значения — только через `Theme.xaml` ресурсы, не хардкодить в code-behind/XAML напрямую (аналог "не дублировать design tokens" из КЛЮЧ24 UI-CONTRACT.md).
