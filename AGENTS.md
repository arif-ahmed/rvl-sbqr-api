# BQR Secure Manager — Agent Charter (spec-only)

Source of truth: `docs/bb-banglaqr-p2p-specification.md` (Chapter 2 + Annexes A–E).
If this file conflicts with the spec, the spec wins. Do not invent controls.

## Scope

Issue and verify BanglaQR P2P codes per spec §§2.2–2.6. Stack: .NET (C#),
PostgreSQL (EF Core/Npgsql). No other gates apply.

## 1. Payload (Tables 3A/3B/4A)

Root: `00(M=01)`, `01(O: 11=static, 12=dynamic)`, `26(M)`, `52(M=4829)`,
`53(M, e.g. 050)`, `54(C: dynamic only)`, `58(M=BD)`, `59(M≤25)`, `60(M≤15)`,
`61(O)`, `62(O: 06=FT, 08=***→prompt purpose)`, `64(O)`, `65–79(RFU)`,
`80–81(C: signature)`, `82–99(unreserved)`, `63(M: CRC)`.

`26`: `00(M=bd.org.bb.npsb)`, `01(M: 00–05)`, `02(M: Annex A ID)`, `03(M: PAN≤19)`.

## 2. Signature (§2.5, Tables 5A/5B, Annex E)

* Payload = `Tag59 + Tag26.Sub03`, UTF-8, no separator/space.
  e.g. `AreebaNawar` + `01711111111` = `AreebaNawar01711111111`.
* Sign with QR-generating institution private key, **Ed25519 only** →
  64 bytes → Base64 88 chars → `80`: GUID + first 44, `81`: GUID + last 44.
  GUID in both = `bd.org.bb.npsb`.
* Never transmit signature in ISO8583; initiate transfer only after valid verify.

## 3. Verification (Annex B — exact order)

1. Parse all TLV, validate CRC (`63`).
2. `Institution_ID = concat(Tag26.01, Tag26.02)` (e.g. `03`+`1008`=`031008`);
   retrieve `{Institution_ID}-public.pem` from BB trust store.
3. `payload = Tag59 + 26.03`; `Verify(public_key, payload, signature)`.
4. Decision: Valid → proceed | Invalid → reject & block | revoked/not-found → reject & block.
5. After valid: validate beneficiary/amount, obtain user authorization (§2.2, Annex C).
   Route by MCC: `52=4829` → P2P, else P2M rules (§2.6).

## 4. Keys & trust store (Annexes B/C)

* Each institution: `openssl genpkey -algorithm Ed25519 -out {ID}-private.pem`;
  `openssl pkey -in {ID}-private.pem -pubout -out {ID}-public.pem`; share public
  with Bangladesh Bank. Recipient institution signs with its private key.
* Periodically sync + securely store BB trust-store keys for offline/real-time verify.

## 5. Transaction rules (Annexes C/D)

* Static QR = repeated-use; update QR payment status after successful credit;
  existing IBFT daily limits, AML, velocity controls apply.
* `F112` carries `IBFT` or `MFSFT` + Tag 62 data, end-to-end to switch/NPSB.
* ISO8583: F2 = `26.01+26.02` + 10-digit zero-pad (e.g. `0001450000000000`);
  F4 ← Tag54; F18 ← Tag52; F43.01/02/03 ← Tags 59/60/58;
  F47 ← sender account; F103 ← 26.03; F112 ← type.

## 6. App behavior (§2.6)

Support QR download/share, scan **and** image upload, and pre-transaction review
screen showing at minimum Tag59 name, 26.03 account, institution name from 26.02.

## Definition of Done

* TLV round-trip + CRC byte-for-byte vs spec vectors; payload example verifies.
* Invalid/revoked/unknown-key QRs rejected & blocked; no transfer without valid verify.
* Static/dynamic, `52` routing, `62.08=***` prompt, F112/ISO8583 mapping covered.

## Explicitly out of scope (not in spec — do not enforce)

No replay window, rotation period, `kid`, `TenantId`, rate-limit, audit hash-chain,
TLS/NTP/deny-by-default/SAST/SBOM/backup gates. Add only via separate approved doc.
