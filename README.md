# ACadSmartLeader
Autocad создание умной выноски, которая считывает XDATA с объекта на который указывает и выводит их в полки

## Документы

- [ТЗ на умную выноску](docs/TZ-smart-leader.md): выбор языка (C#), архитектура, данные, правила подписей,
  привязка к объекту, обновление по кнопке и по `REGENALL`, этапы и приёмка.
- [Пример правил подписей](examples/label-rules.json): двигатель (номер / мощность), кабель (группа / марка).

## Эксперименты

- [`experiments/LabelFormatCheck`](experiments/LabelFormatCheck/Program.cs) вычисляет полки из
  `examples/label-rules.json` настоящим Core [acadPaletteSetXDATA](https://github.com/veb86/acadPaletteSetXDATA).
  AutoCAD не нужен. Результат: [`output.txt`](experiments/LabelFormatCheck/output.txt).
- [`experiments/AcadApiProbe`](experiments/AcadApiProbe/Program.cs) читает метаданные сборок AutoCAD .NET API
  (типы, значения перечислений, члены) без загрузки кода. Им проверены факты из приложения А ТЗ.
