**BANGLA QR — NATIONAL QR CODE STANDARD | VERSION 3.0, AUGUST 2026**

# Business Requirements Document

## Bangla QR P2P Payment SDK — v2.0

*A drop-in integration toolkit for Banks, MFS providers and PSPs on the NPSB network*

| Field | Detail |
|---|---|
| Document title | Business Requirements Document — Bangla QR P2P SDK |
| Version | 2.0 |
| Date | 15 September 2026 |
| Status | Draft — pending stakeholder sign-off |
| Supersedes | v1.0 Draft (15 Sep 2026) |
| Regulatory basis | Bangla QR National QR Code Standard v3.0, August 2026 |
| Circular reference | PSD-2 (NPSSD)/5/2026-885, dated 12 August 2026 |
| Compliance deadline | 31 October 2026 — production ready |
| Ecosystem go-live | 1 November 2026 |
| Classification | Internal / Partner-confidential |

---

## 1. Purpose

This document defines the business requirements for a commercially licensed Bangla QR P2P SDK — a versioned, certified software toolkit offered to banks, MFS providers and PSPs on the NPSB network.

Bangladesh Bank's updated Bangla QR guideline (v3.0, August 2026) mandates Person-to-Person QR payment capability across every NPSB-connected institution by 31 October 2026. The SDK packages the full technical specification — payload construction, Ed25519 digital signing, trust store verification, MCC-based routing, and compliant user flows — into a single drop-in integration. Licensing institutions inherit correctness by construction rather than re-implementing cryptographic and protocol logic independently.

This document contains:

- Full end-to-end user journeys for both static and dynamic QR modes
- Expanded requirements for static QR generation and dynamic QR generation

## 2. Scope

### In scope

- Server SDK — P2P QR payload construction, Ed25519 signing, key management, trust Chain synchronisation
- Client SDK — Android, iOS, Web: QR rendering, share-sheet, camera scanning, image upload, parsing, CRC validation, signature verification, Tag 52 routing
- Static QR generation and handling — full lifecycle
- Dynamic QR generation and handling— full lifecycle
- Optional drop-in UI kit: scan screen, review screen, receive/request screen, static QR display screen

## 3. Static vs. Dynamic QR — Detailed Specification

The Bangla QR P2P standard defines two QR modes distinguished by the Point of Initiation field. The SDK must implement both modes fully and correctly, as they serve different use cases and carry different validation rules.

### STATIC QR

Generated once by the recipient's institution and reused for many transactions. Carries recipient and account details only — no fixed amount. The sender enters the amount at scan time. Ideal for personal profile QRs, printed stickers, and display on shared surfaces. Must be treated as repeated-use during validation — single-use replay rejection must never be applied.

#### Static QR — characteristics

| Attribute | Value / behaviour |
|---|---|
| Amount field | sender enters amount at scan time |
| Reusability | Unlimited — same QR used across many transactions |
| Expiry | No standard expiry; institution may define a rotation schedule |
| Signature scope | Name + Account numbe— same for every scan |
| Replay protection | Must NOT be applied — treat as repeated-use |
| Ideal for | Personal profile QR in app, printed sticker, reception desk display |

### DYNAMIC QR ·

Generated fresh for a specific payment request. Embeds the exact amount and transaction context. The sender reviews and authorises — no amount entry required. One QR equals one intended transaction. Ideal for Request Money flows and bill-style asks between individuals.

#### Dynamic QR — characteristics

| Attribute | Value / behaviour |
|---|---|
| Amount field | Mandatory — exact BDT amount embedded at generation time |
| Reusability | Single-transaction intent; institution should enforce one-use server-side |
| Expiry | Institution-defined; recommended maximum 15 minutes from generation |
| Signature scope | Name + Account number— amount NOT covered by signature |
| Replay protection | Should be enforced — institution hooks the SDK's single-use callback |
| Ideal for | Request Money, split-bill, invoice-style payment requests |

#### Comparison summary

| Dimension | Static QR | Dynamic QR |
|---|---|---|
| Generation frequency | Once; reused indefinitely | Fresh per payment request |
| Amount in payload | No | Yes — exact BDT amount |
| Sender action on amount | Enter amount at scan time | Review pre-filled amount |
| Single-use enforcement | Never | Recommended — via SDK hook |
| Expiry | Institution-defined schedule | Recommended ≤ 15 minutes |
| Share method | Profile display, sticker, saved image | Real-time share — app, WhatsApp, Messenger, email |
| Use case | Always-on receive persona | Specific payment request |

## 5. User Journeys

This section defines the complete end-to-end user journey for each QR mode. Both journeys follow the same three-stage structure: the recipient generates and shares the QR; the sender scans, verifies and pays; settlement occurs via NPSB.

### 5.1 Static QR journey — always-on receive persona

#### Scenario

Fatema wants to receive money from anyone without sharing her account number. She generates a personal static QR from her bank app and saves it as a profile image. Her colleague Rafiq owes her BDT 500 and pays by scanning the QR from Fatema's profile.

#### RECIPIENT — Fatema generates her static QR

1. **Fatema opens 'Receive Money'**  
   Navigates to the Receive / Request Money screen in her bank app. The SDK's Client SDK renders the entry point.

2. **Fatema selects Static QR**  
   Chooses 'My personal QR' — no amount is entered. The app calls Server SDK GenerateStaticQR() with her account name and number.

3. **Server SDK constructs and signs the payload**  
   Builds the TLV payload: Tag 01 = '11', Tag 52 = '4829', Tag 59 = name, Tag 26/Sub-03 = account. Signs with Ed25519 private key, splits into Tag 80 / Tag 81. Computes CRC (Tag 63). Returns encoded QR string.

4. **Fatema saves and shares her QR**  
   The Client SDK renders the QR image. Fatema saves it to her profile in the app. She can also download it, set it as a payment sticker, or display it on-screen.

#### SENDER — Rafiq scans and pays

5. **Rafiq scans or uploads Fatema's QR**  
   Opens his bank app and scans her QR with the camera, or uploads a saved image. Client SDK decodes the payload.

6. **Client SDK validates and routes**  
   Validates CRC (Tag 63). Reads Tag 52 = '4829' → P2P branch. Reads Tag 01 = '11' → static QR mode. Retrieves issuing institution public key from trust store. Verifies Ed25519 signature.

7. **Rafiq enters amount on review screen**  
   Because this is a static QR, the amount is blank. The review screen shows Fatema's name, masked account and institution, with a mandatory amount entry field. Rafiq types BDT 500.

8. **Rafiq selects funding source**  
   Chooses from his available accounts or wallets — bank account, MFS, or PSP. SDK surfaces the options; institution supplies the list.

9. **Rafiq authenticates**  
   Confirms with OTP or authenticator app (the institution's own auth, invoked via SDK hook). SDK does not implement authentication.

10. **Settlement NPSB routes and both parties are notified**  
    Rafiq's institution debits BDT 500. Routes via NPSB. Fatema's institution credits her account. Both apps show real-time success notification.

### 5.2 Dynamic QR journey — specific payment request

#### Scenario

Karim and three friends shared a meal. The bill was BDT 2,000 and Karim paid. He wants to collect BDT 500 from each friend. He uses the Request Money flow to generate a dynamic QR for each, sending it via WhatsApp. His friend Nasrin receives it and pays in under 30 seconds.

#### RECIPIENT — Karim generates a payment request

1. **Karim opens 'Request Money'**  
   Navigates to Request / Receive Money and chooses 'Request a specific amount'. The SDK's Client SDK renders the Request screen.

2. **Karim enters amount and optional note**  
   Types BDT 500. Optionally adds a purpose note ('Dinner at Momo restaurant'). Taps Generate.

3. **Server SDK constructs and signs the dynamic QR payload**  
   Builds payload: Tag 01 = '12', Tag 52 = '4829', Tag 54 = '500.00', Tag 59 = name, Tag 26/Sub-03 = account, Tag 62/Sub-08 = purpose. Signs with Ed25519. Returns encoded QR string with a generation timestamp for expiry tracking.

4. **Karim shares via WhatsApp**  
   Client SDK renders the QR and opens the share sheet. Karim selects WhatsApp and sends to Nasrin. The QR image is a self-contained artifact — no app link required.

#### SENDER — Nasrin receives and pays

5. **Nasrin saves and uploads the QR image**  
   Opens her bank app. Taps 'Pay' → 'Upload QR image'. Selects the WhatsApp image from her gallery. Client SDK decodes the payload.

6. **Client SDK validates and routes**  
   Validates CRC. Tag 52 = '4829' → P2P branch. Tag 01 = '12' → dynamic QR mode. Checks generation timestamp against institution expiry policy. Verifies Ed25519 signature. Checks single-use status via institution hook.

7. **Nasrin reviews the pre-filled amount**  
   Review screen shows Karim's name, masked account, institution, amount BDT 500 and purpose 'Dinner at Momo restaurant'. No amount entry required — she reviews and taps Confirm.

8. **Nasrin selects funding source and authenticates**  
   Selects her bank account. Confirms with OTP. Institution's auth layer processes the confirmation.

9. **Settlement NPSB routes and both parties are notified**  
   Nasrin's institution debits BDT 500. Routes via NPSB. Karim's institution credits his account. Both apps show real-time confirmation. The dynamic QR is marked as used (single-use enforced).

### 5.3 User journey edge cases and error flows

| Scenario | QR type | SDK behaviour |
|---|---|---|
| Signature verification fails | Both | Transaction blocked. Non-technical message shown: 'This QR could not be verified. Please ask the recipient to share a new one.' No retry — new QR required. |
| Information mismatch | Both | Payload rejected immediately. User told the QR appears damaged and asked to try again or request a fresh QR. |
| Dynamic QR expired | Dynamic | Institution expiry hook returns expired status. User shown: 'This request has expired. Ask the sender to create a new one.' |
| Dynamic QR already used | Dynamic | Single-use hook returns used status. User shown: 'This payment link has already been used.' Transaction blocked. |
| Static QR scanned by wrong institution's app | Static | Trust store lookup finds issuing institution's public key. Verification succeeds. P2P routing proceeds normally — full interoperability. |
| Unknown institution ID in QR | Both | SDK triggers on-demand trust store refresh before rejecting. If still unknown after refresh, transaction blocked with message to contact support. |
| Sender enters zero or negative amount (static) | Static | Client-side validation rejects before submission. Error: 'Please enter a valid amount.' |
| Low-light or damaged QR scan | Both | Camera module requests user to hold steady or move closer. If decode fails after retries, user offered gallery upload option. |
| Network offline during verification | Both | Trust store served from integrity-protected local cache. If cache is within max age, verification proceeds. If stale, transaction blocked with offline message. |

## 6. Solution Overview

### 6.1 Architectural boundary — key custody

The standard requires each institution to sign P2P QRs with its own private key. A private key cannot be distributed inside a mobile application binary — any app can be decompiled, and a leaked signing key lets an attacker mint QR codes every institution in the ecosystem would accept. This is not configurable; it is a hard security boundary.

| Component | Where it runs | What it does |
|---|---|---|
| CA Side | CA | Constructs P2P QR payloads, signs private key, manages key custody, synchronises trust store |
| Client SDK | Institution's mobile / web app | Renders QRs, handles share-sheet, scans and decodes, validates CRC, verifies signatures, routes TXN, presents review screen |
| Trust Store Client | Both tiers | Fetches, caches and refreshes Bangladesh Bank's central public key repository; integrity-protected local cache for offline resilience |

### 6.3 Signing algorithm

| Step | Action |
|---|---|
| 1 | Concatenate Account Name (Tag 59) + Account Number (Tag 26 Sub-03) — no space, no symbol, no separator |
| 2 | Sign with RVL CA private key |
| 3 | Base64-encode the 64-byte output into an 88-character string |

**Worked example:** Account Name AreebaNawar + Account Number 01711111111 → signature payload AreebaNawar01711111111 → signed → 88-char Base64 → Tag 80 (chars 1–44) + Tag 81 (chars 45–88).
