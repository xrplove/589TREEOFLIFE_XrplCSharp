# Гайд: Спонсируемые комиссии и резервы (XLS-68)

Как использовать XRPL Sponsored Fees & Reserves через XrplCSharp SDK. Спонсорство позволяет одному аккаунту (**спонсору**) платить комиссии транзакций и/или резервы объектов за другой аккаунт (**спонсируемого**) — базовый механизм для «безгазового» онбординга и кастодиальных сценариев.

> **Важно:** требуется амендмент `Sponsor` (XLS-68). На середину 2026 он существует только в ветке rippled `develop` — не входит ни в один релиз и не активен ни в mainnet, ни в testnet. Для экспериментов используйте nightly-стенд из этого репозитория (см. [Тестирование](#тестирование)). Фича в статусе draft и может меняться.

## Содержание

- [Обзор](#обзор)
- [Ключевые понятия](#ключевые-понятия)
- [Типы транзакций](#типы-транзакций)
- [Пошагово: создание спонсорства](#пошагово-создание-спонсорства)
- [Отправка спонсируемой транзакции](#отправка-спонсируемой-транзакции)
- [Сценарии подписания (V1/V2/V3)](#сценарии-подписания-v1v2v3)
- [Спонсорство внутри Batch](#спонсорство-внутри-batch)
- [Комиссии](#комиссии)
- [Объекты леджера](#объекты-леджера)
- [Тестирование](#тестирование)
- [Типичные ошибки](#типичные-ошибки)

---

## Обзор

```text
Спонсор (rSponsor...)                   Спонсируемый (rUser...)
┌───────────────────────┐               ┌────────────────────────┐
│ SponsorshipSet        │──────────────►│ может слать транзакции │
│   Sponsee: rUser...   │               │   Sponsor: rSponsor... │
│   FeeAmount: 5 XRP    │               │   SponsorFlags: fee    │
│   RemainingOwnerCount │               │ комиссию платит спонсор│
└───────────────────────┘               └────────────────────────┘
        создаёт
┌───────────────────────┐
│ Sponsorship (леджер)  │
│   Owner / Sponsee     │
│   бюджет FeeAmount    │
│   RemainingOwnerCount │
└───────────────────────┘
```

Спонсируются два независимых измерения:

| Измерение | Значение `SponsorFlags` | Что платит спонсор |
|---|---|---|
| Комиссии | `SponsorCoverage.spfSponsorFee` (= 1) | `Fee` транзакций спонсируемого |
| Резервы | `SponsorCoverage.spfSponsorReserve` (= 2) | Резервы за спонсируемого: owner-резервы создаваемых им объектов **и** резервы аккаунтов (включая спонсируемое создание аккаунта) |

У каждого типа транзакций появляются три общих поля: `Sponsor`, `SponsorFlags` и (когда спонсорство этого требует) `SponsorSignature` — вложенный not-signing STObject поверх **той же транзакции, что и основная подпись, но под собственным hash-префиксом спонсора** (rippled `fixCleanup3_4_0`; до него все роли подписывали одинаковые байты). Допустимы две альтернативные формы: одиночная подпись (`SigningPubKey` + `TxnSignature`) либо мультисиг спонсора (вложенный массив `Signers`).

---

## Ключевые понятия

### Объект леджера Sponsorship

Создаётся транзакцией `SponsorshipSet`, один на пару спонсор/спонсируемый:

| Поле | Значение |
|---|---|
| `Owner` | Спонсирующий аккаунт |
| `Sponsee` | Спонсируемый аккаунт |
| `FeeAmount` | Остаток XRP-бюджета на спонсируемые комиссии |
| `RemainingOwnerCount` | Сколько ещё объектов спонсор покроет резервами |

Оба поля существуют только у объекта леджера. Транзакция меняет их знаковыми полями
`FeeAmountDelta` / `RemainingOwnerCountDelta`: положительная дельта пополняет бюджет,
отрицательная возвращает его спонсору. Абсолютные имена в транзакции нода отвергает
(`Field 'FeeAmount' found in disallowed location`).

### Режим обязательной подписи

По умолчанию спонсируемый тратит бюджет без участия спонсора. Флаги `SponsorshipSet` переключают это по-измеренно:

- `tfSponsorshipSetRequireSignForFee` / `tfSponsorshipClearRequireSignForFee`
- `tfSponsorshipSetRequireSignForReserve` / `tfSponsorshipClearRequireSignForReserve`

Если подпись обязательна, транзакция спонсируемого должна нести валидный `SponsorSignature` — см. [Сценарии подписания](#сценарии-подписания-v1v2v3).

### Учётные поля

- `AccountRoot`: `SponsoredOwnerCount`, `SponsoringOwnerCount`, `SponsoringAccountCount`
- `RippleState`: `HighSponsor` / `LowSponsor` — кто покрывает резерв каждой стороны трастлайна

---

## Типы транзакций

### SponsorshipSet

Создаёт/обновляет отношение либо удаляет его флагом `tfDeleteObject`. По rippled `SponsorshipSet::preflight` присутствует ровно одно из полей `Sponsee` / `CounterpartySponsor` (отправитель — другая сторона); **создавать и обновлять может только спонсор**, а **удалять — любая из сторон**:

```csharp
// Спонсор создаёт/обновляет (указывает спонсируемого):
var setup = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmountDelta = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCountDelta = 3,
};
setup = await client.Autofill(setup);
await client.SubmitAndWait(setup, sponsor, true);

// Спонсируемый сам удаляет своё спонсорство (указывает спонсора);
// при удалении запрещены модификационные флаги и FeeAmountDelta/MaxFee/RemainingOwnerCountDelta:
var deletion = new SponsorshipSet
{
    Account = sponsee.ClassicAddress,
    CounterpartySponsor = sponsor.ClassicAddress,
    Flags = SponsorshipSetFlags.tfDeleteObject,
};
deletion = await client.Autofill(deletion);
await client.SubmitAndWait(deletion, sponsee, true);
```

### SponsorshipTransfer

Создаёт, передаёт или завершает спонсорство резерва уже существующих объектов леджера. Обязателен ровно один режимный флаг (по rippled `SponsorshipTransfer::preflight`; для Create/Reassign целевой владелец — `Account`, поле `Sponsee` там запрещено):

| Флаг | Кто отправляет | Что делает | Дополнительные поля |
|---|---|---|---|
| `tfSponsorshipCreate` | спонсируемый (владелец неспонсируемых объектов) | устанавливает спонсорство резерва существующих объектов | `Sponsor` (новый спонсор) + `spfSponsorReserve` обязательны; спонсор со-подписывает |
| `tfSponsorshipReassign` | спонсируемый (владелец спонсируемых объектов) | переносит резерв с текущего спонсора на нового | `Sponsor` (новый спонсор) + `spfSponsorReserve` обязательны; новый спонсор со-подписывает |
| `tfSponsorshipEnd` | спонсируемый или спонсор | снимает спонсорство резерва (цель = `Sponsee`, если указан, иначе `Account`) | `Sponsor` и `SponsorFlags` запрещены |

---

## Пошагово: создание спонсорства

```csharp
XrplWallet sponsor = XrplWallet.Generate();
XrplWallet sponsee = XrplWallet.Generate();
// оба кошелька предварительно пополнить

var tx = new SponsorshipSet
{
    Account = sponsor.ClassicAddress,
    Sponsee = sponsee.ClassicAddress,
    FeeAmountDelta = new Currency { ValueAsXrp = 5m },
    RemainingOwnerCountDelta = 3,
};
tx = await client.Autofill(tx);
TransactionSummary result = await client.SubmitAndWait(tx, sponsor, true);
// result.Meta.TransactionResult == "tesSUCCESS"
```

Проверка через `account_objects`:

```csharp
var request = new AccountObjectsRequest(sponsor.ClassicAddress) { Type = LedgerEntryType.Sponsorship };
var objects = await client.AccountObjects(request);
LOSponsorship sponsorship = objects.AccountObjectList.OfType<LOSponsorship>().First();
```

---

## Отправка спонсируемой транзакции

Спонсируемый отправляет любую обычную транзакцию с полями спонсорства:

```csharp
var payment = new Payment
{
    Account = sponsee.ClassicAddress,
    Destination = destination.ClassicAddress,
    Amount = new Currency { ValueAsXrp = 1m },
    Sponsor = sponsor.ClassicAddress,
    SponsorFlags = SponsorCoverage.spfSponsorFee,
};
payment = await client.Autofill(payment);
```

Если спонсорство **не** требует co-подписи — подписывайте и отправляйте как обычно. Если требует — один из сценариев ниже.

---

## Сценарии подписания (V1/V2/V3)

`SponsorSignature` покрывает ту же транзакцию, что и основная подпись, но под собственным hash-префиксом спонсора, введённым `fixCleanup3_4_0` (аналогично counterparty-паттерну LoanSet). Префикс SDK выбирает по роли, передавать ничего не нужно.

### Простой путь — стандартные Sign/Submit (10.8.0+)

Выбирать хелпер не нужно: стандартный API маршрутизирует по роли. Кошелёк, совпадающий с `tx.Sponsor`, создаёт подпись спонсора; кошелёк отправителя подписывает основную подпись, сохраняя уже присутствующий `SponsorSignature`; умный `SubmitAndWait` компонует, сверяет require-sign флаги спонсорства с леджером и отправляет.

```csharp
// Декодирует переданный blob обратно в транзакцию для подписи
static Dictionary<string, object> Reparse(string blob) =>
    JsonSerializer.Deserialize<Dictionary<string, object>>(
        XrplBinaryCodec.Decode(blob).ToJsonString(), XrplJsonOptions.Default);

// Оба ключа локально — один вызов:
await client.SubmitAndWaitSponsored(payment, sponseeWallet, sponsorWallet);

// Ключи на разных устройствах — каждая сторона просто вызывает Sign:
var sponsorPart = sponsorWallet.Sign(preparedTx);               // добавит SponsorSignature
var final = sponseeWallet.Sign(Reparse(sponsorPart.TxBlob));    // добавит основную подпись
await client.SubmitRequest(final.TxBlob, true);

// Либо отправляющая сторона финализирует всё сама:
await client.SubmitAndWait(partiallySignedTx, sponsorWallet); // спонсор доводит tx, подписанную спонсируемым
```

При отсутствии обязательной подписи `SubmitAndWait` падает сразу с "transaction is not signed by all participants" вместо ошибки от ноды. Мультисиг любой из сторон остаётся переносимым: устройства подписывают с `multisign: true`, а `client.ComposeSignatures(parts)` раскладывает Signer-записи по секциям согласно SignerList'ам леджера (с пре-чеком кворума по весам).

### Advanced — явные хелперные сценарии

**V1 — Автоматический (оба ключа в одном процессе):**

```csharp
JsonObject prepared = SponsorSigningHelper.PrepareForSigning(payment, sponseeWallet);
SignatureResult signed = SponsorSigningHelper.SignSponsored(prepared, sponseeWallet, sponsorWallet);
await client.SubmitRequest(signed.TxBlob, true);
```

**V2 — Параллельный (ключи на разных устройствах, объединение позже):**

```csharp
var sponsorSig  = sponsorWallet.SignAsSponsor(prepared);
var submitterSig = sponseeWallet.Sign(prepared);
var combined = SponsorSigningHelper.CombineSponsorSignatures(submitterSig.TxBlob, sponsorSig.TxBlob);
await client.SubmitRequest(combined.TxBlob, true);
```

**V3 — Последовательный (сначала спонсор, затем отправитель финализирует):**

```csharp
var withSponsor = sponsorWallet.SignAsSponsor(prepared);
var final = SponsorSigningHelper.SubmitterSign(withSponsor.TxBlob, sponseeWallet);
await client.SubmitRequest(final.TxBlob, true);
```

---

## Спонсорство внутри Batch

Спонсорство сочетается с Batch (XLS-56); правила зеркалят rippled `Batch::preflight`:

- **Спонсирование резерва внутренней транзакции**: на inner ставятся `Sponsor` + `spfSponsorReserve` и **пустой** объект `SponsorSignature` как маркер. Маркер делает спонсора *обязательным batch-подписантом* — спонсор авторизует весь батч через стандартный `Sign`: одиночной подписью или **через свой SignerList** (вложенный мультисиг `BatchSigner.Signers`). Подписные данные внутрь маркера не кладутся никогда.
- **Спонсирование комиссии внешнего батча**: `Sponsor` + `spfSponsorFee` на внешнем Batch; спонсор со-подписывает сам батч обычной `SponsorSignature` через стандартный `Sign`.
- **Запрещено протоколом** (`ValidateBatch` отсекает на клиенте): `spfSponsorReserve` на внешнем Batch, спонсирование комиссии на inner-транзакциях, подписные данные внутри маркеров.
- **Inner-транзакции Vault, LoanBroker и Loan** нода отклоняет (rippled `kDisabledTxTypes` → `temINVALID_INNER_BATCH`), пока не включён амендмент `LendingProtocolV1_2`, и принимает после его включения. `ValidateBatch` этого не проверяет: правил леджера у него нет, решает нода. После включения амендмента inner `LoanSet` делает свой `Counterparty` обязательным подписантом batch, как и любая другая inner-транзакция.

```csharp
// Внутренний TrustSet со спонсированным резервом; спонсор подписывает через
// свой SignerList (Reparse — см. «Простой путь» выше)
SignatureResult holderPart  = holder.Sign(batchDict);
SignatureResult signer1Part = signer1.Sign(Reparse(holderPart.TxBlob),  multisign: true, signingFor: sponsor.ClassicAddress);
SignatureResult signer2Part = signer2.Sign(Reparse(signer1Part.TxBlob), multisign: true, signingFor: sponsor.ClassicAddress);
SignatureResult final       = root.Sign(Reparse(signer2Part.TxBlob));
```

---

## Комиссии

По rippled `Transactor::calculateBaseFee` **одиночная** `SponsorSignature` не добавляет к базовой комиссии ничего. Надбавка возникает только при **мультисиге** спонсора: каждый подписант, вложенный в `SponsorSignature.Signers`, добавляет одну базовую комиссию. `Autofill` SDK учитывает это автоматически.

`GetFeePayer` показывает, с кого списана комиссия. `tx.GetFeePayer()` читает только транзакцию: спонсор, если в `SponsorFlags` есть `spfSponsorFee`, иначе `Delegate`, иначе `Account`. `tx.GetFeePayer(meta)` читает ещё и метаданные и возвращает `null`, если комиссия списана с предоплаченного `FeeAmount` объекта `Sponsorship`: тогда она не отражается на балансе ни одного счёта. Существующий объект `Sponsorship` имеет приоритет над счётом спонсора, даже если спонсор поставил подпись.

---

## Объекты леджера

- `LOSponsorship` — объект отношения (см. [Ключевые понятия](#ключевые-понятия))
- `LOAccountRoot.SponsoredOwnerCount` / `SponsoringOwnerCount` / `SponsoringAccountCount`
- `LORippleState.HighSponsor` / `LowSponsor`
- `BaseLedgerEntry.Sponsor` — спонсор резерва любого другого объекта, включая `AccountRoot`, созданный с `tfSponsorCreatedAccount`: там это `Account` платежа

---

## Тестирование

Амендмент есть только на develop. Репозиторий содержит nightly-стенд со включённым на генезисе `Sponsor`:

```bash
docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestISponsorship"
```

Интеграционные тесты защищены `AmendmentGuard`: на ноде без амендмента они завершаются как *inconclusive*, а не падают. Полные рабочие примеры — `Tests/Xrpl.Tests/Integration/transactions/TestISponsorship.cs`; детали стендов — [гайд по standalone-ноде](StandaloneNode-Guide.ru.md).

---

## Типичные ошибки

| Ошибка | Значение |
|---|---|
| `temDISABLED` | Амендмент `Sponsor` не активен на ноде |
| `tecNO_SPONSOR_PERMISSION` | Спонсорство не покрывает это измерение, бюджет исчерпан либо отсутствует/невалиден обязательный `SponsorSignature` |
| `terNO_PERMISSION` | Состояние отношения не допускает операцию (например, передача чужого спонсорства) |
| Валидация: "invalid Sponsor" | Поле `Sponsor` — не строковый адрес (клиентская проверка) |

*English version: [Sponsorship-Guide](Sponsorship-Guide.md)*
