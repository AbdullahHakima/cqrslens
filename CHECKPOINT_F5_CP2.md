# Stage F5 Checkpoint 2 — Cross-Corpus Rendering, Docs Export & HTML Parity

- **Date:** 2026-11-17
- **Status:** `DONE`
- **Branch:** `foundations/f5-spine-reports` (base F4 HEAD `f8902e9e75ac62d936d285b5e42f2be536228f25`)
- **Corpus Pin:** `/data/workspace/corpora/nopcommerce` (`release-4.90.0` at `e7a52b9199f9c7651839e92826df47f668f3c767`, clean read-only)

---

## 1. Corpus Scope on Box #4

Per stage instructions, CP2 executed in **both** `text` and `roslyn` modes (`--generated-at 2026-01-01T00:00:00Z`) on:
- **`nopCommerce`** (`/data/workspace/corpora/nopcommerce`)
- **`SampleShop`** (`samples/SampleShop`)
- **`GoldenCorpus`** (`tests/fixtures/GoldenCorpus`)
- Plus the remaining four in-repo fixtures (`Adversarial`, `DiffPair/base`, `DiffPair/head`, `StrictFailure`) for complete local fixture coverage.

**Verification-side external corpora note:** The remaining verification corpora (`ABP`, `modular-monolith`, `northwind`, `eshop`, `ardalis`, `jasontaylor`) are **not** present on this machine, were **not** cloned or fetched, and remain covered verification-side on Mariam's VM.

---

## 2. CP2 Multi-Corpus Results (`text` & `roslyn`)

Every pair below was compared directly against the F4 baseline binary (`/tmp/cqrslens-f4base`) and verified for:
1. `0`-byte JSON diff (`D5`).
2. Insertion-only diff in Markdown (`## Architecture Spine` between `## Layers` and `## CQRS Map`) and HTML (`<h2>Architecture Spine</h2>` between `<h2>Layers</h2>` and `<h2>CQRS Map</h2>`, in both `mermaid` and `svg` render modes) (`D1`, `H2`).
3. Identical table headers, row counts, cell values, and row ordering between Markdown and HTML (`mermaid` and `svg`) (`D2`).
4. Extended `02-building-blocks.md` (`### Endpoint Inventory` and `### Component Index` inserted after `### Relations by Kind & Evidence Rule` and before `## Building Blocks by Layer`), unchanged docs file set, and `0`-byte diff on pages `01`, `03`, `04`, `05`, `06` and `diagrams/*.svg` (`D4`).

| Corpus | Mode | Components / Endpoints / Relations | JSON (`F4 == F5` B) | MD (`F4 -> F5` B) | HTML SVG (`F4 -> F5` B) | Docs `02` (`F4 -> F5` B, Files) | MD/HTML Parity | `--check-docs` Fresh / Mutated |
|---|---|---:|---:|---:|---:|---:|---|---|
| `nopCommerce` | `text` | `516 / 1,151 / 8,764` | `33,747,841` (`==`) | `11,836 -> 300,072` | `46,823 -> 444,381` | `148,504 -> 433,952` (`8`) | **PASS** (`6` tables) | exit `0` / exit `4` |
| `nopCommerce` | `roslyn` | `516 / 1,151 / 8,764` | `33,747,843` (`==`) | `11,838 -> 300,074` | `46,825 -> 444,383` | `148,504 -> 433,952` (`8`) | **PASS** (`6` tables) | exit `0` / exit `4` |
| `SampleShop` | `text` | `16 / 0 / 14` | `58,604` (`==`) | `18,491 -> 23,430` | `79,086 -> 86,312` | `6,948 -> 9,609` (`17`) | **PASS** (`5` tables) | exit `0` / exit `4` |
| `SampleShop` | `roslyn` | `16 / 0 / 14` | `58,606` (`==`) | `18,493 -> 23,432` | `79,088 -> 86,314` | `6,948 -> 9,609` (`17`) | **PASS** (`5` tables) | exit `0` / exit `4` |
| `GoldenCorpus` | `text` | `21 / 6 / 27` | `88,082` (`==`) | `20,949 -> 27,469` | `93,936 -> 103,820` | `8,184 -> 12,116` (`17`) | **PASS** (`6` tables) | exit `0` / exit `4` |
| `GoldenCorpus` | `roslyn` | `21 / 6 / 27` | `88,084` (`==`) | `20,951 -> 27,471` | `93,938 -> 103,822` | `8,184 -> 12,116` (`17`) | **PASS** (`6` tables) | exit `0` / exit `4` |
| `Adversarial` | `text` | `9 / 0 / 6` | `22,259` (`==`) | `13,964 -> 16,532` | `49,320 -> 53,650` | `3,922 -> 4,883` (`16`) | **PASS** (`5` tables) | — |
| `Adversarial` | `roslyn` | `9 / 0 / 6` | `22,261` (`==`) | `13,966 -> 16,534` | `49,322 -> 53,652` | `3,922 -> 4,883` (`16`) | **PASS** (`5` tables) | — |
| `DiffPair/base` | `text` | `2 / 0 / 1` | `4,785` (`==`) | `8,213 -> 9,617` | `28,564 -> 31,094` | `2,023 -> 2,304` (`11`) | **PASS** (`5` tables) | — |
| `DiffPair/base` | `roslyn` | `2 / 0 / 1` | `4,787` (`==`) | `8,215 -> 9,619` | `28,566 -> 31,096` | `2,023 -> 2,304` (`11`) | **PASS** (`5` tables) | — |
| `DiffPair/head` | `text` | `1 / 0 / 0` | `2,365` (`==`) | `6,333 -> 7,552` | `22,337 -> 24,469` | `1,637 -> 1,852` (`9`) | **PASS** (`4` tables) | — |
| `DiffPair/head` | `roslyn` | `1 / 0 / 0` | `2,367` (`==`) | `6,335 -> 7,554` | `22,339 -> 24,471` | `1,637 -> 1,852` (`9`) | **PASS** (`4` tables) | — |
| `StrictFailure` | `text` | `1 / 0 / 0` | `2,510` (`==`) | `6,488 -> 7,743` | `22,632 -> 24,800` | `1,698 -> 1,937` (`9`) | **PASS** (`4` tables) | — |
| `StrictFailure` | `roslyn` | `0 / 0 / 0` | `1,376` (`==`) | `4,546 -> 5,624` | `16,598 -> 18,441` | `1,312 -> 1,440` (`8`) | **PASS** (`2` tables) | — |

---

## 3. Text vs Roslyn Mode Parity (`D6`, `H8`)

- `nopCommerce`: MD `## Architecture Spine` **identical**, HTML `<h2>Architecture Spine</h2>` **identical**, `02-building-blocks.md` **identical** (`text == roslyn`).
- `SampleShop`: MD `## Architecture Spine` **identical**, HTML `<h2>Architecture Spine</h2>` **identical**, `02-building-blocks.md` **identical** (`text == roslyn`).
- `GoldenCorpus`: MD `## Architecture Spine` **identical**, HTML `<h2>Architecture Spine</h2>` **identical**, `02-building-blocks.md` **identical** (`text == roslyn`).
- `Adversarial`, `DiffPair/base`, `DiffPair/head`: MD/HTML Spine and `02-building-blocks.md` **identical** (`text == roslyn`).
- `StrictFailure`: underlying F1–F4 model intentionally differs between `text` (`1` component) and `roslyn` (`0` components), and the rendered Spine/docs reflect that underlying model faithfully.

---

## 4. `nopCommerce` Spine Output Highlights

- **Summary:** `516` unique components, `1,151` endpoints (`spineEndpoints`), `8,764` relations, `17,422` relation evidence occurrences, `340` unproven items.
- **Role breakdown:** `validator = 105 (105 occ)`, `controller = 128 (128 occ)`, `service = 282 (6,216 occ)`, `repository = 1 (1 occ)`, `endpoint (mapping-derived) = 1,151 (1,151 occ)`.
- **Relations summary:** `references = 12 (12 occ)`, `resolvesTo = 787 (1,020 occ)`, `calls = 6,203 (14,549 occ)`, `touchesData = 611 (690 occ)`, `exposes = 1,151 (1,151 occ)`.
- **Top 5 most-connected components:**
  1. ``Nop.Data.EntityRepository`1`` (`622` in, `8` out, `630` total)
  2. `Nop.Services.Customers.CustomerService` (`316` in, `41` out, `357` total)
  3. `Nop.Services.Catalog.ProductService` (`243` in, `85` out, `328` total)
  4. `Nop.Services.Localization.LocalizationService` (`285` in, `27` out, `312` total)
  5. `Nop.Web.Areas.Admin.Controllers.ProductController` (`0` in, `281` out, `281` total)
- **--check-docs mutation check:** Mutating `02-building-blocks.md` under `### Component Index` exited `4` with `02-building-blocks.md [modified]: section changed (Component Index)` on `nopCommerce`, `SampleShop`, and `GoldenCorpus` in both `text` and `roslyn` modes.
