# Shellwright — Web-to-Native App Platform

## Master Product & Engineering Specification

> **Working codename:** Shellwright (rename freely — see §16.1 for naming/trademark notes)
> **Document owner:** Lasa
> **Status:** v1.0 — Pre-build architecture & strategy document
> **Last updated:** 31 August 2026
> **Category:** Hybrid WebView app generation platform (Median.co / MobiLoud / GoodBarber competitor)

---

## Table of Contents

- [0. How to read this document](#0-how-to-read-this-document)
- [1. Executive Summary](#1-executive-summary)
- [Part I — Median.co Teardown](#part-i--medianco-teardown)
  - [2. Business model](#2-business-model)
  - [3. Pricing decomposed](#3-pricing-decomposed)
  - [4. Complete feature inventory](#4-complete-feature-inventory)
  - [5. Their architecture](#5-their-architecture-confirmed--inferred)
- [Part II — Market](#part-ii--market)
  - [6. Competitive landscape](#6-competitive-landscape)
- [Part III — The Gap](#part-iii--the-gap)
  - [7. What Median structurally cannot do](#7-what-median-structurally-cannot-do)
  - [8. What Median technically struggles with](#8-what-median-technically-struggles-with)
  - [9. Commercial pain points you can exploit](#9-commercial-pain-points-you-can-exploit)
- [Part IV — Product Definition](#part-iv--product-definition)
  - [10. Positioning & differentiation pillars](#10-positioning--differentiation-pillars)
  - [11. Personas & jobs-to-be-done](#11-personas--jobs-to-be-done)
- [Part V — Feature Specification](#part-v--feature-specification)
  - [12. Full feature catalogue](#12-full-feature-catalogue)
- [Part VI — Architecture](#part-vi--architecture)
  - [13. System architecture](#13-system-architecture)
- [Part VII — Engineering](#part-vii--engineering)
  - [14. Tech stack](#14-tech-stack)
  - [15. Services & third-party dependencies](#15-services--third-party-dependencies)
- [Part VIII — Business](#part-viii--business)
  - [16. Unit economics & cost model](#16-unit-economics--cost-model)
  - [17. Pricing & packaging — what to give away free](#17-pricing--packaging--what-to-give-away-free)
- [Part IX — Execution](#part-ix--execution)
  - [18. Hardest engineering problems, ranked](#18-hardest-engineering-problems-ranked)
  - [19. Security, legal & compliance](#19-security-legal--compliance)
  - [20. Roadmap](#20-roadmap)
  - [21. Risk register](#21-risk-register)
  - [22. Success metrics](#22-success-metrics)
  - [23. Open decisions](#23-open-decisions)
- [Appendices](#appendices)

---

## 0. How to read this document

This is the single source of truth for the platform. It is written to be handed to an engineer (or to future-you) and acted upon without further context.

**Reading order by role:**

| If you are...                         | Read                |
| ------------------------------------- | ------------------- |
| Deciding whether to build this at all | §1, §7–§9, §16, §21 |
| Designing the system                  | §13, §14, §15, §18  |
| Defining scope for a sprint           | §12, §20            |
| Setting prices                        | §3, §16, §17        |
| Handling store submissions            | §7.1, §7.2, §19     |

**Conventions used:**

- `P0` = must exist for first paying customer. `P1` = needed within 6 months. `P2` = differentiator / later.
- **Marginal cost** = what it costs _you_ each time a user uses a feature. This drives §17 entirely.
- Anything marked ⚠️ is a hard constraint imposed by Apple or Google, not a design choice.

---

## 1. Executive Summary

### 1.1 What this product is

A cloud platform that takes a URL plus a configuration, and produces signed, store-ready iOS and Android binaries whose UI chrome is native and whose content is a live web view — plus a JavaScript bridge that lets the website drive native device capability.

The user never installs Xcode, never installs Android Studio, never touches a certificate, and never learns Swift or Kotlin.

### 1.2 Why the opportunity exists

Median.co has run this business since 2014 and is the category leader, but it has four exploitable weaknesses:

1. **Price shape.** A one-time activation fee of $229–$990 _plus_ a recurring annual fee of $179–$669 per app, per year, with native plugins metered by tier (3 plugins on Essential, 5 on Plus). For an agency with 20 client apps this is brutal. Their managed tiers start at $7,200 (SMB) and $18,000 (Enterprise).
2. **Artificial gating of zero-marginal-cost features.** Watermark removal, plugin count, team seats, and simulator session length are gated. All four cost Median approximately nothing per user. That is a pure pricing-power play, and it is the softest target in the entire category.
3. **Lock-in as a feature.** You configure in their studio, they own the build pipeline, and leaving means rewriting. Source export exists but is not the default posture.
4. **Thin ownership of the runtime.** Push, analytics, attribution, auth, and video are all third-party SDK injections (OneSignal, Braze, Firebase, AppsFlyer, Adjust, Auth0, Clerk, Twilio, Zoom). Median owns the shell and the bridge; it does not own the services. That leaves the entire "batteries included" position open.

### 1.3 The strategy in one paragraph

**Give away everything that costs nothing, charge for everything that costs something.** Every software capability — every plugin, watermark-free builds, unlimited team seats, full source export, unlimited Android builds and previews — is free forever. Revenue comes from things with real marginal cost or real labour: iOS build and simulator minutes beyond a generous free allowance, first-party push/analytics/OTA at volume, managed store publishing, agency multi-tenancy, and enterprise controls (MDM, SSO, SLA, private plugins, self-hosted runners). This inverts Median's model, is defensible because your cost base is genuinely lower than their price base, and it converts their strongest customers (agencies with many apps) into your best ones.

### 1.4 The three things that will actually be hard

1. **Operating a macOS build fleet.** ⚠️ iOS binaries can only be compiled on Apple hardware. This is a hardware-procurement and fleet-ops problem, not a coding problem, and it is the single largest cost and complexity driver.
2. **Custody of customer signing material.** You will hold Apple certificates, private keys, App Store Connect API keys, and Android upload keystores on behalf of strangers. A breach here is company-ending.
3. **⚠️ App Store Guideline 4.2 and 4.2.6.** Apple rejects "repackaged websites," and separately forbids app-generation services from submitting on a client's behalf. Both constrain the product design permanently. See §7.1 and §7.2.

### 1.5 Realistic scope

| Milestone       | Calendar (solo, focused) | What exists                                                          |
| --------------- | ------------------------ | -------------------------------------------------------------------- |
| Technical spike | 3–4 weeks                | Android shell builds from config on a Linux runner; APK downloads    |
| Private alpha   | 3–4 months               | Both platforms build; bridge v1; App Studio; own device testing only |
| Public beta     | 6–8 months               | Simulator preview, publishing assist, push, 15 plugins, billing      |
| Commercial v1   | 10–14 months             | Signing custody, OTA, offline engine, agency tier, SOC 2 readiness   |

This is not a weekend project. It is closer in scope to a small CI/CD company than to a website builder.

---

# Part I — Median.co Teardown

## 2. Business model

### 2.1 Two businesses in one

Median runs a **self-serve product business** and a **managed services business** side by side, and the pricing you quoted is only the first one.

| Track                     | Entry price                       | Who it's for                          | Delivery                              |
| ------------------------- | --------------------------------- | ------------------------------------- | ------------------------------------- |
| Self-Serve Developer      | $0 → $990 one-time + $179–$669/yr | Web developers building their own app | App Studio, DIY publishing            |
| Full-Service Agency (SMB) | from $7,200                       | Businesses with no mobile team        | Median's engineers build & publish    |
| Enterprise                | from $18,000                      | Large orgs, compliance requirements   | Security features, MDM, SLA, advisory |

**Key insight:** the self-serve product is largely a _lead generation and qualification funnel_ for the managed business. The free tier exists so you can prove the concept, then either convert at $229–$990 or escalate to a $7,200+ engagement. Your free tier should be designed with the same intent, but with a far higher ceiling before the paywall.

### 2.2 The licensing model

- Licensing is **per app**, not per account. Ten apps = ten licenses.
- Structure is **one-time activation fee + annual renewal starting at month 12**. The renewal is what buys continued rebuilds for OS compatibility — the real recurring value proposition.
- Plugins are **counted, not unlimited**: Essential gives 3 from the Essential library; Plus gives 5 from the Plus library.
- Plugins can be **trialled**, but a "This app was developed using Median" popup appears during trial.
- Some plugins additionally require the customer to hold a **third-party licence** (Scandit, JW Player, Zoom, Twilio, Sendbird, Intune).

### 2.3 What the annual fee actually buys

This is the part most competitors misunderstand. The annual fee is not rent on software. It buys:

- Rebuilds against new Xcode / Android SDK / target API levels
- Compliance with new store requirements (privacy manifests, data safety, age rating, DSA trader status, Android developer verification)
- Third-party SDK version bumps and breakage fixes
- Continued access to build infrastructure

**This is a genuine, recurring, unavoidable cost of doing business** — Apple and Google force a compatibility treadmill every single year. Any competitor that sells a one-time licence with no recurring revenue will die when the treadmill catches up. Do not repeat that mistake.

---

## 3. Pricing decomposed

### 3.1 The self-serve ladder as given

|                               | **Free**              | **Starter** | **Essential** (most popular) | **Plus** |
| ----------------------------- | --------------------- | ----------- | ---------------------------- | -------- |
| One-time activation           | $0                    | $229        | $590                         | $990     |
| Annual from month 12          | $0                    | $179/yr     | $399/yr                      | $669/yr  |
| Watermark removed             | ❌                    | ✅          | ✅                           | ✅       |
| Simulator session length      | 1 min                 | 1 min       | 3 min                        | 5 min    |
| Concurrent simulator sessions | 1                     | 1           | 2                            | 2        |
| Simulator sessions/day        | 30                    | 100         | 100                          | 100      |
| Team members                  | 1                     | 1           | 3                            | 5        |
| Essential plugins             | trial only            | trial only  | up to 3                      | included |
| Plus plugins                  | trial only            | trial only  | trial only                   | up to 5  |
| OneSignal push plugin         | ✅                    | ✅          | ✅                           | ✅       |
| Splash screen watermark       | ✅ shown              | removed     | removed                      | removed  |
| JS Bridge                     | ✅ (watermark widget) | ✅          | ✅                           | ✅       |
| iOS + Android cloud build     | ✅                    | ✅          | ✅                           | ✅       |
| Cloud backup of build files   | ✅                    | ✅          | ✅                           | ✅       |
| OS compatibility updates      | ✅                    | ✅          | ✅                           | ✅       |

### 3.2 Cost-to-serve analysis of each gate

This table is the strategic heart of the document. For each gate Median imposes, what does it actually cost them?

| Gate                               | Median's price for it | Their real marginal cost       | Verdict                                     |
| ---------------------------------- | --------------------- | ------------------------------ | ------------------------------------------- |
| Remove watermark                   | $229                  | **$0.00**                      | Pure rent. **Give away free.**              |
| Plugin count (3 vs 5 vs unlimited) | $361–$761             | **~$0.00** (build config flag) | Pure rent. **Give away free.**              |
| Team seats (1 → 3 → 5)             | bundled into tier     | **~$0.00** (a DB row)          | Pure rent. **Give away free.**              |
| Simulator 1 min → 5 min            | bundled into tier     | **real** (~$0.03–0.10/min)     | Legitimate cost. **Meter, but generously.** |
| Concurrent simulator sessions      | bundled               | **real** (device slots)        | Legitimate. Meter.                          |
| iOS cloud build                    | bundled               | **real** ($0.10–0.75/build)    | Legitimate. Meter.                          |
| Android cloud build                | bundled               | **~$0.01–0.03/build**          | Effectively free. **Unlimited.**            |
| Annual OS compat updates           | $179–$669/yr          | **real** (engineering payroll) | Legitimate. This is your recurring revenue. |
| Managed publishing                 | $7,200+               | **real** (human hours)         | Legitimate. High margin services.           |

**Four of the nine gates cost them literally nothing.** They represent roughly 70% of the price delta between Free and Plus. That is your wedge.

---

## 4. Complete feature inventory

Everything below is a capability Median ships today. Treat this as your **parity checklist** — you do not need all of it for v1, but you need to know what "complete" looks like. Column meanings: **Tier** = where Median gates it; **You** = P0/P1/P2 priority for your build; **MC** = your marginal cost to serve (○ = ~zero, ● = real).

### 4.1 Branding & appearance

| Feature                                                                 | Tier               | You | MC  |
| ----------------------------------------------------------------------- | ------------------ | --- | --- |
| App icon generation (all densities, adaptive icons, iOS marketing icon) | Free               | P0  | ○   |
| Splash / launch screen (static, with brand colour, safe-area aware)     | Free (watermarked) | P0  | ○   |
| Theme colours (primary, accent, nav bar, tab bar, dark variants)        | Free               | P0  | ○   |
| Status bar style (light/dark/translucent/hidden, per-URL)               | Free               | P0  | ○   |
| Dark mode (follow system / force light / force dark, CSS injection)     | Free               | P0  | ○   |
| Localization of app name and permission strings                         | Free               | P1  | ○   |
| iOS Liquid Glass adoption (iOS 26 design language)                      | Free               | P1  | ○   |

### 4.2 Interface & device control

| Feature                                          | Tier | You | MC  |
| ------------------------------------------------ | ---- | --- | --- |
| Screen brightness control from JS                | Free | P1  | ○   |
| Keep screen on / wake lock                       | Free | P1  | ○   |
| Full-screen / immersive mode                     | Free | P1  | ○   |
| Screen orientation lock (per-app or per-URL)     | Free | P0  | ○   |
| Native swipe gestures (back/forward, edge swipe) | Free | P0  | ○   |
| Pull-to-refresh (native, colour-matched)         | Free | P0  | ○   |
| Font scaling / respect system text size          | Free | P1  | ○   |
| WebView zoom enable/disable                      | Free | P0  | ○   |
| Maximum simultaneous windows                     | Free | P2  | ○   |
| Custom offline error page                        | Free | P0  | ○   |
| Service worker support                           | Free | P1  | ○   |
| iOS split view / Stage Manager support           | Free | P2  | ○   |
| Keyboard state tracking (show/hide events to JS) | Free | P1  | ○   |

### 4.3 Native navigation

| Feature                                                     | Tier | You | MC  |
| ----------------------------------------------------------- | ---- | --- | --- |
| Top navigation bar with dynamic titles from `<title>` or JS | Free | P0  | ○   |
| Nav bar action buttons (custom, icon or text, JS callbacks) | Free | P0  | ○   |
| Share button (native share sheet)                           | Free | P0  | ○   |
| Refresh button                                              | Free | P0  | ○   |
| Native search form in nav bar                               | Free | P1  | ○   |
| Sidebar / drawer navigation with visual editor              | Free | P0  | ○   |
| Dynamic sidebar menu items (driven by JS at runtime)        | Free | P1  | ○   |
| Bottom tab bar with visual editor                           | Free | P0  | ○   |
| Dynamic tab menu, per-URL tab selection                     | Free | P1  | ○   |
| Localized tab/menu labels                                   | Free | P2  | ○   |
| Custom icon upload for tabs and menus                       | Free | P0  | ○   |
| iOS contextual navigation toolbar                           | Free | P2  | ○   |
| "Auto new windows" (open matching URLs in modal windows)    | Free | P1  | ○   |

### 4.4 Link handling

| Feature                                                  | Tier | You | MC  |
| -------------------------------------------------------- | ---- | --- | --- |
| Internal vs external link rules (regex-based routing)    | Free | P0  | ○   |
| Universal Links (iOS) / App Links (Android) deep linking | Free | P0  | ○   |
| Custom URL scheme handling                               | Free | P0  | ○   |
| Deep-link validator tool (checks AASA / assetlinks.json) | Free | P1  | ○   |

### 4.5 Permissions & hardware access

| Feature                                        | Tier | You | MC  |
| ---------------------------------------------- | ---- | --- | --- |
| Camera access + file uploads from web forms    | Free | P0  | ○   |
| Photo library access                           | Free | P0  | ○   |
| Downloads directory management                 | Free | P0  | ○   |
| Location services (foreground)                 | Free | P0  | ○   |
| WebRTC audio & video (getUserMedia in WebView) | Free | P0  | ○   |
| Apple App Tracking Transparency prompt         | Free | P0  | ○   |
| Localized permission prompt strings            | Free | P1  | ○   |

### 4.6 Web overrides

| Feature                            | Tier | You | MC  |
| ---------------------------------- | ---- | --- | --- |
| Custom user-agent string           | Free | P0  | ○   |
| Custom HTTP headers on requests    | Free | P0  | ○   |
| Custom CSS injection               | Free | P0  | ○   |
| Custom JavaScript injection        | Free | P0  | ○   |
| Cookie persistence across launches | Free | P0  | ○   |

### 4.7 JavaScript Bridge

| Feature                                           | Tier                    | You | MC  |
| ------------------------------------------------- | ----------------------- | --- | --- |
| `median.*` JS API, promise-based                  | Free (watermark widget) | P0  | ○   |
| NPM package with TypeScript types                 | Free                    | P0  | ○   |
| SPA navigation hooks                              | Free                    | P0  | ○   |
| Device info (model, OS, app version, install ID)  | Free                    | P0  | ○   |
| App-usage detection (`isApp()` / user-agent flag) | Free                    | P0  | ○   |
| Clipboard read/write                              | Free                    | P0  | ○   |
| Native share sheet trigger                        | Free                    | P0  | ○   |
| App-resumed / foreground callback                 | Free                    | P0  | ○   |
| Programmatic file download                        | Free                    | P1  | ○   |
| Clear WebView cache                               | Free                    | P1  | ○   |
| Callbacks accessible from inside iframes          | Free                    | P2  | ○   |
| Google Tag Manager tag template                   | Free                    | P2  | ○   |

### 4.8 Push notifications

⚠️ Note: Median owns **none** of these. Every one is a third-party SDK injection. This is a strategic opening.

| Provider                                                                                   | Tier                  | You                     | MC  |
| ------------------------------------------------------------------------------------------ | --------------------- | ----------------------- | --- |
| OneSignal (full: consent mgmt, tagging, in-app messages, foreground handling, tap routing) | Free tier includes it | P0 (as integration)     | ○   |
| Firebase Cloud Messaging (raw)                                                             | paid                  | P0                      | ○   |
| Braze                                                                                      | paid                  | P2                      | ○   |
| Bloomreach                                                                                 | paid                  | P2                      | ○   |
| Cordial                                                                                    | paid                  | P2                      | ○   |
| Intercom                                                                                   | paid                  | P1                      | ○   |
| Iterable                                                                                   | paid                  | P2                      | ○   |
| Klaviyo                                                                                    | paid                  | P1                      | ○   |
| Customer.io                                                                                | paid                  | P2                      | ○   |
| Microsoft Dynamics                                                                         | paid                  | P2                      | ○   |
| MoEngage                                                                                   | paid                  | P2                      | ○   |
| Optimizely                                                                                 | paid                  | P2                      | ○   |
| Salesforce Marketing Cloud                                                                 | paid                  | P2                      | ○   |
| Sendbird                                                                                   | paid                  | P2                      | ○   |
| Xtremepush                                                                                 | paid                  | P2                      | ○   |
| **First-party push service (yours)**                                                       | ❌ _does not exist_   | **P1 — differentiator** | ●   |

### 4.9 Analytics plugins

| Provider                          | Tier                | You                     | MC  |
| --------------------------------- | ------------------- | ----------------------- | --- |
| Firebase Analytics                | paid                | P1                      | ○   |
| Firebase Crashlytics              | paid                | P0                      | ○   |
| Adjust                            | paid                | P2                      | ○   |
| AppsFlyer                         | paid                | P2                      | ○   |
| Branch.io                         | paid                | P1                      | ○   |
| Meta App Events                   | paid                | P2                      | ○   |
| **First-party analytics (yours)** | ❌ _does not exist_ | **P1 — differentiator** | ●   |

### 4.10 Authentication plugins

| Feature                                        | Tier             | You | MC  |
| ---------------------------------------------- | ---------------- | --- | --- |
| Face ID / Touch ID (iOS)                       | paid (Essential) | P0  | ○   |
| Android Biometric / fingerprint                | paid (Essential) | P0  | ○   |
| Passkey / WebAuthn native support              | paid             | P1  | ○   |
| Social login — Google Sign-In (native SDK)     | paid             | P0  | ○   |
| Social login — Sign in with Apple              | paid             | P0  | ○   |
| Social login — Facebook Login                  | paid             | P1  | ○   |
| Server-side redirect handling for social login | paid             | P0  | ○   |
| Auth0 native integration                       | paid             | P2  | ○   |
| Clerk native integration                       | paid             | P2  | ○   |

### 4.11 Scanning

| Feature                                                      | Tier             | You | MC  |
| ------------------------------------------------------------ | ---------------- | --- | --- |
| QR / barcode scanner (camera, native)                        | paid (Essential) | P0  | ○   |
| Document scanner (edge detect, perspective correct, PDF out) | paid (Plus)      | P1  | ○   |
| NFC tag scanner                                              | paid (Plus)      | P1  | ○   |
| iBeacon detection                                            | paid (Plus)      | P2  | ○   |
| Scandit enterprise scanning (requires Scandit licence)       | Enterprise       | P2  | ○   |

### 4.12 Native functionality

| Feature                                                     | Tier              | You | MC  |
| ----------------------------------------------------------- | ----------------- | --- | --- |
| Haptic feedback                                             | paid              | P0  | ○   |
| In-app purchases — Apple StoreKit                           | paid (Plus)       | P0  | ○   |
| In-app purchases — Google Play Billing                      | paid (Plus)       | P0  | ○   |
| RevenueCat integration                                      | paid              | P1  | ○   |
| App review prompt (SKStoreReview / Play In-App Review)      | paid              | P0  | ○   |
| Native contacts access                                      | paid              | P1  | ○   |
| Native calendar access                                      | paid              | P1  | ○   |
| Background location tracking                                | paid (Plus)       | P1  | ○   |
| Native datastore (key-value, survives cache clear)          | paid              | P0  | ○   |
| Native datastore offline mode                               | paid              | P1  | ○   |
| Offline download manager (queue files for offline)          | paid (Plus)       | P1  | ○   |
| Reader modal (in-app browser, reader mode)                  | paid              | P1  | ○   |
| Secure modal (screenshot-blocked view)                      | paid (Enterprise) | P1  | ○   |
| Share-into-app (register as a share target from other apps) | paid              | P1  | ○   |
| Health Bridge (HealthKit / Health Connect)                  | paid              | P2  | ○   |
| AgeSafety / age assurance                                   | paid              | P2  | ○   |

### 4.13 Media

| Feature                                         | Tier | You | MC  |
| ----------------------------------------------- | ---- | --- | --- |
| Background audio playback                       | paid | P1  | ○   |
| Native media player with lock-screen controls   | paid | P1  | ○   |
| Web screenshot capture (full page or by div id) | paid | P2  | ○   |
| JW Player                                       | paid | P2  | ○   |
| Kaltura                                         | paid | P2  | ○   |
| Twilio Video                                    | paid | P2  | ○   |
| Zoom SDK                                        | paid | P2  | ○   |

### 4.14 Enterprise & security

| Feature                                                   | Tier       | You | MC  |
| --------------------------------------------------------- | ---------- | --- | --- |
| Jailbreak / root detection                                | Enterprise | P1  | ○   |
| Microsoft Intune MAM/MDM wrapping                         | Enterprise | P2  | ○   |
| Enterprise security plugin (cert pinning, secure storage) | Enterprise | P1  | ○   |
| SOC 2 Type II compliance posture                          | Enterprise | P2  | ●   |
| MDM distribution advisory                                 | Enterprise | P2  | ●   |

### 4.15 Integrations & monetization

| Feature                                          | Tier | You     | MC  |
| ------------------------------------------------ | ---- | ------- | --- |
| AdMob native ads                                 | paid | P1      | ○   |
| Card.io card scanning                            | paid | P2      | ○   |
| Social share to Instagram Stories / Snapchat     | paid | P2      | ○   |
| External purchase link entitlement (reader apps) | paid | P1      | ○   |
| Master Lock (vertical-specific)                  | paid | ❌ skip | —   |
| Bubble.io plugin                                 | paid | P1      | ○   |

### 4.16 Build, test & publish

| Feature                                                        | Tier         | You                     | MC  |
| -------------------------------------------------------------- | ------------ | ----------------------- | --- |
| Browser App Studio (visual config)                             | Free         | P0                      | ○   |
| Cloud device simulators (streamed)                             | Free (1 min) | P0                      | ●   |
| iOS cloud build → IPA                                          | Free         | P0                      | ●   |
| Android cloud build → APK / AAB                                | Free         | P0                      | ●   |
| Cloud backup of build files                                    | Free         | P0                      | ●   |
| Source code export (build from source)                         | Free/paid    | **P0 — differentiator** | ○   |
| App Store Connect API connection (auto signing)                | Free         | P0                      | ○   |
| Test device registration                                       | Free         | P0                      | ○   |
| Signing config management                                      | Free         | P0                      | ●   |
| App Groups entitlement support                                 | Free         | P1                      | ○   |
| TestFlight upload automation                                   | Free         | P0                      | ○   |
| Google Play upload automation                                  | Free         | P0                      | ○   |
| Remote debugging tools (Safari/Chrome inspect on cloud device) | Free         | P1                      | ●   |
| CI/CD API integration (trigger builds from your pipeline)      | paid         | P1                      | ○   |
| Multiple app instances from one config (white-label)           | paid         | **P1 — differentiator** | ○   |
| `appconfig.json` as the config format                          | Free         | P0                      | ○   |

### 4.17 Website-builder integrations

Median publishes dedicated onboarding paths for AI/no-code builders. This is their newest growth vector and it works.

| Integration                                                 | You |
| ----------------------------------------------------------- | --- |
| Lovable                                                     | P1  |
| Base44                                                      | P1  |
| Replit                                                      | P1  |
| Bolt / v0 / Cursor-built apps                               | P1  |
| Salesforce Experience Cloud (LWC components + deep linking) | P2  |
| WordPress / WooCommerce                                     | P1  |
| Shopify                                                     | P1  |
| Webflow                                                     | P1  |
| Bubble                                                      | P1  |
| SharePoint                                                  | P2  |

---

## 5. Their architecture (confirmed + inferred)

Your original analysis was accurate. Here it is corrected and expanded with what the docs confirm.

### 5.1 Confirmed from public documentation

- The configuration artifact is **`appconfig.json`** — a declarative document describing branding, navigation, link rules, overrides, and enabled plugins.
- **Two real native codebases** are generated: an Xcode project (Swift/Obj-C, CocoaPods/SPM) and a Gradle project (Java/Kotlin).
- Customers can **build from source** on their own machines — meaning the generated projects are real, self-contained, and not obfuscated.
- **App Store Connect API integration** handles certificate and provisioning-profile automation.
- **Android App Bundle (AAB)** is the Play output format; APK for direct install.
- **CI/CD integration** exists as a paid feature, implying a build-trigger API.
- **"Multiple app instances"** exists — one config, many branded outputs. This is their white-label story.
- Plugins are **modular code extensions** injected at build time, gated by licence tier.
- The bridge is injected into the WebView and exposed via an **NPM package** with SPA navigation support.

### 5.2 The pipeline, reconstructed

```
┌──────────────┐
│  App Studio  │  Browser SPA. Visual editor → appconfig.json
└──────┬───────┘
       │  POST /apps/{id}/config
       ▼
┌──────────────────────────────────────────────┐
│            Control Plane (API)               │
│  • config validation + schema versioning     │
│  • licence & entitlement check               │
│  • plugin resolution (tier gating)           │
│  • build job enqueue                         │
└──────┬───────────────────────────┬───────────┘
       │                           │
       ▼                           ▼
┌───────────────┐         ┌────────────────────┐
│ Linux runners │         │  macOS runners     │
│ (Android)     │         │  (iOS) ⚠️ Apple HW │
│               │         │                    │
│ template repo │         │ template repo      │
│  + codegen    │         │  + codegen         │
│  + Gradle     │         │  + CocoaPods/SPM   │
│  + sign       │         │  + xcodebuild      │
│  → AAB/APK    │         │  + codesign        │
└───────┬───────┘         │  → IPA             │
        │                 └─────────┬──────────┘
        │                           │
        ▼                           ▼
     ┌──────────────────────────────────┐
     │   Artifact store (S3-like)       │
     └───────┬──────────────────┬───────┘
             │                  │
             ▼                  ▼
   ┌──────────────────┐  ┌───────────────────┐
   │ Streamed device  │  │ Store submission  │
   │ preview          │  │ (ASC API / Play   │
   │ (Appetize-like)  │  │  Developer API)   │
   └──────────────────┘  └───────────────────┘
```

### 5.3 The runtime shell

```
┌─────────────────────────────────────┐
│           Native App Shell          │
│                                     │
│  ┌───────────────────────────────┐  │
│  │  Native chrome                │  │
│  │  nav bar / tabs / drawer      │  │
│  └───────────────────────────────┘  │
│  ┌───────────────────────────────┐  │
│  │  WKWebView (iOS)              │  │
│  │  android.webkit.WebView (And) │  │
│  │                               │  │
│  │   ← your live website →       │  │
│  │                               │  │
│  │  injected: bridge.js          │  │
│  └───────────────┬───────────────┘  │
│                  │ postMessage /    │
│                  │ @JavascriptInterface
│  ┌───────────────▼───────────────┐  │
│  │  Bridge dispatcher            │  │
│  └───────────────┬───────────────┘  │
│  ┌───────────────▼───────────────┐  │
│  │  Plugin registry              │  │
│  │  biometric│scan│push│IAP│...  │  │
│  └───────────────────────────────┘  │
└─────────────────────────────────────┘
```

### 5.4 What you should copy, and what you should not

| Copy                                                  | Don't copy                                               |
| ----------------------------------------------------- | -------------------------------------------------------- |
| Declarative config file as the single source of truth | Gating plugin _count_ by tier                            |
| Real generated native projects (not a black box)      | 1-minute simulator sessions                              |
| Build-from-source escape hatch                        | Per-app licensing with separate activation + annual fees |
| Plugin-as-injection architecture                      | Depending on third parties for push and analytics        |
| Annual compatibility subscription                     | Watermarking the free tier's splash screen               |
| Managed publishing as high-margin services            | Charging for team seats                                  |

---

# Part II — Market

## 6. Competitive landscape

### 6.1 The map

| Player                                             | Model                         | Price shape                                                  | Strength                                                              | Weakness you exploit                                                           |
| -------------------------------------------------- | ----------------------------- | ------------------------------------------------------------ | --------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| **Median.co** (ex-GoNative)                        | Self-serve + managed          | $0–$990 one-time + $179–$669/yr per app; managed from $7,200 | Deepest plugin library, 12-year track record, enterprise trust, SOC 2 | Price, gating of free features, no first-party services, lock-in               |
| **MobiLoud**                                       | Fully managed only            | ~$200–$500/mo per app                                        | Approval guarantee, hand-holding, strong content marketing            | No self-serve, expensive for many apps, no DIY control                         |
| **GoodBarber**                                     | Template builder + webview    | ~€25–€200/mo                                                 | Beautiful templates, ecommerce focus                                  | Template lock-in, weaker JS bridge, less developer-oriented                    |
| **AppMySite**                                      | WordPress/WooCommerce focused | Freemium, ~$9–$99/mo                                         | Cheap, WP plugin distribution                                         | Shallow native features, quality complaints                                    |
| **WebViewGold**                                    | One-time source code sale     | ~$99–$299 one-time                                           | Buy the source, own it forever, no recurring                          | You maintain it yourself; no build service, no studio                          |
| **Natively / Native App AI / Code2Native / Twinr** | New wave, AI-builder focused  | ~$15–$60/mo                                                  | Targeting Lovable/Bolt/Replit users, cheap                            | Young, thin plugin libraries, unproven at scale                                |
| **Capacitor + Ionic Appflow**                      | Framework + CI                | Free OSS + paid CI                                           | Real open-source ecosystem, huge plugin community, no lock-in         | Requires actual dev skill, no visual studio, Appflow pricing is enterprise-ish |
| **DIY (Xcode + Android Studio)**                   | —                             | Developer time                                               | Total control                                                         | Certificates, provisioning, two toolchains, OS treadmill                       |

### 6.2 Where the market is moving

Three shifts matter, and all three favour a new entrant:

1. **AI web-app builders are creating a flood of new candidates.** Lovable, Bolt, v0, Replit, and Base44 users ship a working web app in a weekend and immediately want it in the stores. They have no mobile skills and no budget for $990. Median has already noticed this and built onboarding docs for each. This audience is huge, fast-growing, price-sensitive, and currently badly served.
2. **Store compliance is getting heavier, not lighter.** Privacy manifests, data safety declarations, DSA trader status, age ratings, and now Android developer verification. Every new requirement widens the gap between "I can write a website" and "I can ship an app," which increases the value of a platform that absorbs that complexity.
3. **⚠️ Android developer verification lands 30 September 2026** in Brazil, Indonesia, Singapore and Thailand, expanding globally through 2027. Every app installed on a certified Android device must be registered to an identity-verified developer, regardless of install source — Play, third-party store, or direct APK sideload. This kills the casual "just download the APK" distribution path that many small builders rely on, and creates immediate demand for a platform that walks people through verification and registration.

### 6.3 Your positioning statement

> **The web-to-native platform where every feature is free and you only pay for compute.**
>
> Every plugin, every seat, no watermark, full source export, unlimited Android builds — free forever. Pay only for iOS build minutes, cloud device time, and managed publishing. Your app, your code, your keys, your exit.

---

# Part III — The Gap

This is the section you asked for: **what Median (and the category) genuinely cannot handle.** They fall into three classes — structural walls that nobody can climb, technical ceilings inherent to the WebView approach, and commercial pain points that are simply choices they've made.

## 7. What Median structurally cannot do

These are ⚠️ **hard constraints imposed by Apple and Google**. Neither Median nor you can engineer around them. What you _can_ do is handle them more gracefully than Median does, and be honest about them where Median is vague.

### 7.1 ⚠️ Guideline 4.2 — Minimum Functionality

**The rule:** Apple states an app must include features, content, and UI that elevate it beyond a repackaged website. If it isn't useful, unique, or "app-like," it doesn't belong on the App Store. Sub-clauses 4.2.1/4.2.2 target apps that are "little more than a mobile website," and 4.2.3 targets web portals.

**Why nobody can fix it:** It is a _subjective human judgement_, applied by a different reviewer each time, with no appeal to objective criteria. Developer forum threads document apps rejected under 4.2 ten times running, and apps with genuinely rich native features (AlarmKit, Live Activities, Widgets, App Intents) still getting hit with it. It is enforcement-by-vibes.

**What Median does:** Sells native navigation, push, deep linking, biometrics, and haptics as the mitigation, and offers a paid managed publishing service with an approval guarantee — i.e., they price the risk rather than solve it.

**What you should do differently — this is a real product opportunity:**

- Ship a **Store Readiness Score**: a pre-submission analyser that scores the configuration against every known 4.2 trigger and refuses to let a user submit an obviously-doomed app.
- Ship **native-by-default scaffolding**: an app created in your studio has a bottom tab bar, offline page, push opt-in, native share, and a native settings/about screen _turned on by default_, not opt-in. Median's defaults produce a bare WebView; yours should not.
- Maintain a **public rejection knowledge base** with actual reviewer wording and the fixes that worked. Nobody in this category does this well, and it's the highest-trust content marketing asset available.
- Offer a **native "app-only" surface generator** — a small set of genuinely native screens (onboarding carousel, native settings, native profile, native offline library) that exist outside the WebView and cost the customer nothing to adopt. This is the single strongest 4.2 defence and it is entirely within your control.

**Rating: cannot be solved, can be substantially de-risked. High-value differentiator.**

### 7.2 ⚠️ Guideline 4.2.6 — Commercialized templates and app generation services

**The rule:** Apps created from a commercialized template or app-generation service will be rejected _unless submitted directly by the provider of the app's content_. Such services must not submit on behalf of clients; they must give clients tools to create customized apps. Guideline 5.2.1 reinforces this: the developer account must identify the app's actual owner.

**What this destroys:** the dream business model. You cannot run "one developer account, 500 client apps." Every customer must hold their own Apple Developer Program membership ($99/yr) and submit under their own account. Competitors like GoodBarber have publicly documented reviewing every client's developer account before publishing to enforce this.

**Second-order effects:**

- Your onboarding must include "get your own Apple Developer account" as a mandatory, friction-heavy step. This is where a large share of free users will drop off.
- You must handle **delegated access** rather than ownership: the customer invites you to their App Store Connect team, or issues you a scoped API key. This makes signing-material custody unavoidable (§18.2).
- **Apple's own escape hatch:** the aggregator or "picker" model — a single binary hosting all clients' content, like a restaurant-finder with an entry per restaurant. This is explicitly blessed. **Build this as a product line**: a "Marketplace App" mode for franchises, associations, school districts, church networks, and multi-location retail, where one binary you own serves N tenants selected at runtime. Median does not market this. It sidesteps 4.2.6 _and_ the $99/yr per client cost _and_ the per-app licence cost.
- Apple Developer **Enterprise** Program (in-house distribution) and Apple Business Manager **Custom Apps** are the other legitimate routes for internal/B2B apps, where 4.2 is applied far more leniently. Median sells this as Enterprise advisory; you can productize it.

**Rating: cannot be solved. Must be designed around. The picker model and the enterprise/MDM route are underserved and worth real money.**

### 7.3 ⚠️ Android developer verification (from 30 Sep 2026)

Google now requires every app installed on a certified Android device to be registered to an identity-verified developer — Play, alternative stores, and direct APK sideload alike. First enforcement is Brazil, Indonesia, Singapore and Thailand on 30 September 2026, expanding globally in 2027. Verification opened to all developers in March 2026 and roughly 98% of existing Play apps were auto-registered.

**Carve-outs that matter:**

- Local development via ADB/Android Studio is unaffected.
- Apps distributed through managed channels (Device Policy Controller / Managed Google Play) are exempt from the sideloading requirement.
- There is a lighter-weight free account tier for students and hobbyists.

**Implications for you:**

- Your "download the APK and install it" flow — the single best free-tier demo in this category — **stops working** for certified devices in those markets, then everywhere. Plan for this now.
- Build **verification onboarding** into the product: detect unverified developers, walk them through registration, register package names and signing keys automatically.
- For internal/enterprise apps, push customers toward **Managed Google Play private apps** — exempt, and a better experience anyway.
- Median has published guidance on this; nobody has _productized_ it. Be first.

**Rating: unavoidable. First-mover advantage available on tooling.**

### 7.4 ⚠️ In-app purchase mandate

Apple requires digital goods and subscriptions to use StoreKit (guideline 3.1.1). A website selling digital content that is simply loaded in a WebView will be rejected or forced to route payments through IAP at 15–30%.

This is why Median gates IAP behind their top tier, and why the "external purchase link" plugin (for reader apps) exists at all. You cannot change the rule, but you _can_:

- Make IAP and RevenueCat integration **free**, since gating it just makes rejection more likely for your users.
- Ship a **commerce classifier** in the readiness check that detects a payment flow in the WebView and warns the user before they submit.

### 7.5 The annual OS treadmill

Every year: a new iOS major, a new Android major, a new required target API level, deprecated APIs, changed permission semantics, new privacy declarations. Apps that aren't rebuilt eventually stop being accepted, then eventually break.

Median monetizes this correctly via the annual fee. **You must too.** A platform that sells a one-time licence and no recurring revenue is insolvent by year three. This is the strongest argument against a pure WebViewGold-style "buy the source once" model.

---

## 8. What Median technically struggles with

These are the WebView-approach ceilings. Some you can beat with engineering; a few you cannot.

### 8.1 Things that are genuinely hard and mostly unsolved by anyone

| #   | Problem                                        | Why it's hard                                                                                                                                      | Can you beat it?                                                                                                                                                                                         |
| --- | ---------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | **Cold start feel**                            | WebView init + network round trip before first paint. Native apps paint from a bundled bundle instantly.                                           | **Partially — big win available.** Ship a bundled offline shell + skeleton that paints in <100ms, then swap in the live page. Nobody in this category does this properly.                                |
| 2   | **True offline**                               | Service workers are unreliable in WKWebView; iOS evicts caches aggressively; there's no real background sync on iOS.                               | **Partially.** A native download manager + native key-value store + a bundled fallback bundle covers most real use cases. Full offline-first sync does not.                                              |
| 3   | **Scroll and gesture feel**                    | Web scroll physics differ from native; nested scroll containers fight the native gesture recognizer; iOS rubber-banding conflicts.                 | **Barely.** You can tune, not fix. Be honest about it.                                                                                                                                                   |
| 4   | **Widgets, Live Activities, Dynamic Island**   | These require real SwiftUI code and an app extension target. There is no way to drive them from a website.                                         | **Yes, with effort — major differentiator.** See §8.3.                                                                                                                                                   |
| 5   | **App Intents / Siri / Shortcuts / Spotlight** | Requires compiled intent definitions.                                                                                                              | **Yes, with a declarative intent schema.** See §8.3.                                                                                                                                                     |
| 6   | **watchOS / CarPlay / tvOS / visionOS**        | Entirely separate native apps.                                                                                                                     | **No.** Out of scope. Say so.                                                                                                                                                                            |
| 7   | **App Clips / Instant Apps**                   | Size budgets (~15MB iOS) that a WebView shell can meet but a website cannot.                                                                       | **Marginal.** P2 at best.                                                                                                                                                                                |
| 8   | **Background execution**                       | iOS gives you BGTaskScheduler with no guarantees. Android has Doze, App Standby, and OEM battery killers (Xiaomi, Huawei, Oppo are notorious).     | **No.** Nobody solves this. Manage expectations.                                                                                                                                                         |
| 9   | **WebView fragmentation**                      | Android System WebView version varies wildly across devices and OEMs; a CSS feature that works on your Pixel breaks on a 3-year-old budget device. | **Partially.** Ship a compatibility matrix + a minimum-WebView-version check + graceful degradation warnings.                                                                                            |
| 10  | **Accessibility**                              | VoiceOver/TalkBack traversal across the native-chrome ↔ WebView boundary is genuinely janky. Focus order breaks.                                  | **Partially — and it's a compliance requirement for public sector / EU customers.** Real differentiator for enterprise deals.                                                                            |
| 11  | **Cookie / session / SSO edge cases**          | WKWebView cookie partitioning, ITP, third-party cookie blocking, SameSite. SSO redirect chains through IdPs break in ways they don't in Safari.    | **Partially.** Native cookie persistence + `ASWebAuthenticationSession` for auth flows + custom header injection covers most of it. This is a top-3 support ticket category — solving it well is a moat. |
| 12  | **File upload/download quirks**                | Multi-file selection, camera capture, MIME handling, download interception, PDF viewing, and printing all differ per platform and OS version.      | **Yes, but it's grinding work.** Endless small bugs.                                                                                                                                                     |
| 13  | **Performance on low-end Android**             | 2GB RAM devices with an old WebView will jank on a heavy SPA regardless of your shell.                                                             | **No.** The website's fault, but you get the blame and the 1-star reviews.                                                                                                                               |

### 8.2 Things Median simply hasn't built

Not hard — just absent. Each is an opportunity.

| Gap                                                                                                                                                                                              | Opportunity size                                                                                                                   |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------------------- |
| **First-party push service.** Every push option is a third-party SDK. A user who wants push must sign up for OneSignal, configure APNs keys, and manage a second dashboard.                      | **Large.** Bundle push into the platform: one dashboard, one setup, free up to a real volume.                                      |
| **First-party analytics.** Same story — Firebase or nothing.                                                                                                                                     | **Large.** Ship sessions, retention, screen flow, crash-free rate out of the box.                                                  |
| **OTA web-bundle updates.** Median relies on the website being live. There is no signed, versioned, rollback-able bundle.                                                                        | **Large.** ⚠️ Legal under Apple's interpreted-code carve-out (§8.4) and explicitly permitted by Google for JS-in-WebView.          |
| **Config-as-code / GitOps.** `appconfig.json` exists but the studio is the primary interface. No git-tracked config, no PR review of app config, no diffing, no environments (dev/staging/prod). | **Medium — but it wins developer hearts.** A CLI + config-in-repo + `shellwright build --env staging`.                             |
| **Automated regression testing.** No screenshot diffing, no smoke tests on real devices, no "did the last web deploy break the app" check.                                                       | **Medium.** A nightly "does the app still boot and navigate" check per app is cheap and enormously reassuring.                     |
| **Store listing management.** Screenshots, descriptions, localizations, ASO — all manual.                                                                                                        | **Medium.** Auto-generate framed screenshots from the device preview across required sizes. That alone saves hours per submission. |
| **Alternative stores.** No Huawei AppGallery, Amazon Appstore, Samsung Galaxy Store, Xiaomi GetApps, or Microsoft Store.                                                                         | **Medium, regional.** AppGallery matters enormously outside the US/EU.                                                             |
| **PWA / TWA output.** No Trusted Web Activity or installable-PWA target.                                                                                                                         | **Small but cheap.** A free PWA/TWA export is a great free-tier gift and a legitimate answer for users who fail 4.2.               |
| **Desktop targets.** No macOS / Windows / Linux output.                                                                                                                                          | **Small.** But a Tauri or Electron target from the same config is a nice upsell.                                                   |
| **Multi-tenant agency console.** "Multiple app instances" exists but there's no genuine agency workspace with client sub-accounts, per-client billing, and bulk rebuild.                         | **Large.** Agencies are the highest-LTV segment and the most price-abused by per-app licensing.                                    |
| **Self-hosted / on-prem runners.** Enterprises with source-code policies cannot use a cloud build service at all.                                                                                | **Medium, very high price point.**                                                                                                 |

### 8.3 The "impossible" features you can actually ship

These are the ones that will make people switch. All three break the assumption that a WebView app can only be a WebView.

**A. Declarative native surfaces.**
A schema in `appconfig.json` that generates _real_ native screens with no WebView:

```jsonc
"nativeSurfaces": [
  { "type": "onboarding", "slides": [ /* image + title + body */ ] },
  { "type": "settings",   "sections": [ /* toggles bound to native datastore */ ] },
  { "type": "offlineLibrary" },
  { "type": "profile", "dataSource": "https://api.example.com/me" }
]
```

Compiled to SwiftUI and Jetpack Compose. This is the strongest possible answer to Guideline 4.2 and costs the customer zero effort.

**B. Declarative widgets and Live Activities.**

```jsonc
"widgets": [{
  "id": "orderStatus",
  "sizes": ["systemSmall", "systemMedium"],
  "dataSource": { "url": "https://api.example.com/widget", "refreshMinutes": 15 },
  "layout": { /* constrained DSL: text, image, progress, stack */ }
}]
```

Generated as a WidgetKit extension + Glance/AppWidget provider, fed by a timeline endpoint on the customer's server. **Nobody in this category offers this.** It is technically demanding but bounded, and it single-handedly refutes "it's just a wrapper."

**C. Declarative App Intents / Shortcuts.**

```jsonc
"intents": [{
  "name": "Check Order Status",
  "phrases": ["Check my {appName} order"],
  "handler": { "type": "deeplink", "url": "/orders/latest" }
}]
```

Generated as App Intents (iOS) and App Actions / shortcuts (Android). Cheap to build relative to widgets, high perceived value.

### 8.4 OTA web-bundle updates — the legal position

⚠️ Verify current agreement text before relying on this; policy wording moves.

The clause historically known as §3.3.2, relocated to §3.3.1(B) of the Apple Developer Program License Agreement, permits downloading and executing **interpreted** code, provided it runs in Apple's built-in WebKit/JavaScriptCore environment, does not materially change the app's primary purpose as reviewed, and does not introduce features or monetization the reviewer never saw. Google Play's equivalent explicitly excludes JavaScript in a WebView from its ban on downloading executable code.

A Capacitor-style app running its web layer in WKWebView sits squarely inside this carve-out — which is why CodePush, Ionic Appflow, EAS Update and Shorebird have operated openly for a decade. The guidelines that _do_ get enforced against OTA misuse are 2.3.1 (hidden/dormant features) and 2.5.2 (downloading code that changes functionality).

**Practical rules for your OTA feature:**

- Bug fixes, copy changes, layout tweaks, endpoint swaps, feature-flag defaults: safe.
- New feature areas, paywalls where none existed, changed pricing model, unhiding admin surfaces: **do not**, and build guardrails that make it awkward.
- Sign every bundle; version every bundle; support instant rollback; log every deployment for audit.
- Put a clear warning in the UI when a user pushes a bundle that changes navigation structure.

---

## 9. Commercial pain points you can exploit

| Median pain                                 | Evidence                                               | Your answer                                                            |
| ------------------------------------------- | ------------------------------------------------------ | ---------------------------------------------------------------------- |
| Per-app licensing punishes agencies         | $229–$990 + $179–$669/yr **per app**                   | Flat agency plan, unlimited apps, priced on build minutes              |
| Watermark on free tier                      | "Powered by Median.co" on splash + JS bridge widget    | **No watermark, ever, on any tier**                                    |
| 1-minute simulator sessions on free/Starter | Documented tier limit                                  | 15-minute Android sessions free, generous iOS allowance                |
| Plugin count caps (3 / 5)                   | Essential / Plus tiers                                 | **All plugins, all tiers, forever**                                    |
| Team seat caps (1 / 3 / 5)                  | Documented tier limit                                  | **Unlimited seats**                                                    |
| Trial watermark popup on plugin trials      | "This app was developed using Median" popup            | No trial concept — everything is just on                               |
| Third-party dependency sprawl               | OneSignal + Firebase + AppsFlyer accounts to configure | First-party push + analytics, one dashboard                            |
| Vendor lock-in                              | Studio-centric workflow                                | Source export + CLI + config-in-git, documented as a feature           |
| Opaque publishing                           | $7,200 managed tier or DIY                             | Free guided publishing wizard; paid only for hands-on-keyboard service |
| No usage-based option                       | Fixed tiers only                                       | Pay-per-build-minute for spiky users                                   |

---

# Part IV — Product Definition

## 10. Positioning & differentiation pillars

### 10.1 The five pillars

Every roadmap decision should trace to one of these. If a feature doesn't, cut it.

**Pillar 1 — Zero feature gating.**
Every plugin, every native capability, every seat, no watermark, on every plan including free. Marginal cost is zero; charging for it is rent. This is the headline and it is unmatchable by Median without destroying their own price structure.

**Pillar 2 — You own your app.**
One-click export of the complete, buildable Xcode and Gradle projects. Config lives in your git repo. A CLI that runs the same build locally. If the platform disappears, the customer still ships. **Counter-intuitively this increases retention** — people pay more willingly for something they aren't trapped in, and the operational value (build fleet, signing, OTA, push, compliance) is what actually keeps them.

**Pillar 3 — Native where it counts.**
Declarative native surfaces, widgets, Live Activities, and App Intents generated from config. The direct answer to Guideline 4.2 and the thing that makes the app stop feeling like a wrapper.

**Pillar 4 — Batteries included.**
First-party push, analytics, crash reporting, OTA bundles, and offline. One dashboard, one setup, no OneSignal/Firebase/AppsFlyer account sprawl.

**Pillar 5 — Compliance as a product.**
Store Readiness Score, rejection knowledge base, privacy manifest generator, data-safety form generator, Android developer verification onboarding, screenshot generator. The store is the hard part; own it.

### 10.2 Anti-positioning — what you will not be

- Not a website builder. You take an existing URL. Bring your own web app.
- Not a native app framework. If they want to write Swift, they should write Swift.
- Not a games platform.
- Not a "1-click app in 60 seconds" toy. That framing invites Guideline 4.2 rejections and low-value customers.

---

## 11. Personas & jobs-to-be-done

| Persona                                                                        | Volume                | Willingness to pay       | Primary job                                                         | What wins them                                                                                |
| ------------------------------------------------------------------------------ | --------------------- | ------------------------ | ------------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| **Indie / AI-builder** — shipped a Lovable or Bolt app, wants it in the stores | Very high             | Low ($0–$20/mo)          | "Get me into the App Store without learning Xcode"                  | Free tier that actually ships a real app; hand-holding through the store process              |
| **SaaS product team** — has a web app, needs a mobile presence                 | Medium                | Medium ($50–$300/mo)     | "Mobile parity without a mobile team"                               | Push, deep links, biometrics, SSO that works, OTA, staging environments                       |
| **Agency / white-label reseller** — 10–100 client apps                         | Low count, high value | High ($300–$2,000/mo)    | "Ship client apps profitably and rebuild them all when iOS updates" | Flat pricing regardless of app count, multi-tenant console, bulk rebuild, client sub-accounts |
| **Ecommerce (Shopify/Woo)**                                                    | High                  | Medium                   | "App-store presence + push for abandoned carts"                     | Push, deep links, IAP awareness, ecommerce templates                                          |
| **Enterprise / internal tools** — SharePoint, Salesforce, intranet portals     | Low                   | Very high ($10k–$50k/yr) | "Ship an internal app under MDM without a mobile team"              | SSO, cert pinning, Intune/MDM, self-hosted runners, SLA, audit logs, private distribution     |
| **Public sector / education**                                                  | Low                   | High                     | "Accessible, compliant, procurable"                                 | WCAG posture, accessibility audit, data residency, invoicing                                  |

**Land here first:** indie/AI-builder for volume and word-of-mouth, agencies for revenue. Enterprise is where the money is but the sales cycle will kill you before you have a track record.

---

# Part V — Feature Specification

## 12. Full feature catalogue

Organized by subsystem. `P0` = first paying customer, `P1` = within 6 months, `P2` = later. Everything in §4 is implicitly on this list as parity; below are the specifics of _your_ build, including the differentiators.

### 12.1 App Studio (the browser IDE)

| ID    | Feature                                                                                                                         | Pri |
| ----- | ------------------------------------------------------------------------------------------------------------------------------- | --- |
| ST-01 | URL ingestion + automatic site analysis (mobile-friendly check, meta theme colour, manifest.json, favicon, title, viewport)     | P0  |
| ST-02 | Icon generator — upload one 1024px source, output every iOS/Android density incl. adaptive foreground/background and monochrome | P0  |
| ST-03 | Splash screen designer (colour, logo, safe area, dark variant, iOS storyboard + Android 12+ SplashScreen API)                   | P0  |
| ST-04 | Theme editor with live preview (nav bar, tab bar, status bar, accent, dark mode variants)                                       | P0  |
| ST-05 | Navigation designer — drag/drop tabs, drawer items, nav bar buttons, per-item URL + icon + visibility rules                     | P0  |
| ST-06 | Link-rule editor (regex → internal / external browser / new window / block / deeplink) with a tester                            | P0  |
| ST-07 | Plugin catalogue with one-click enable, per-plugin config forms, and validation                                                 | P0  |
| ST-08 | Raw `appconfig.json` editor (Monaco) with schema validation, always in sync with the visual editor                              | P0  |
| ST-09 | Live device preview (streamed) with rotate, dark-mode toggle, locale switch, network throttle                                   | P0  |
| ST-10 | Build history with logs, artifacts, diff-vs-previous-config                                                                     | P0  |
| ST-11 | Environments (dev / staging / prod) with separate URLs and configs                                                              | P1  |
| ST-12 | Config diff & rollback                                                                                                          | P1  |
| ST-13 | Team workspace, roles (owner/admin/developer/viewer), audit log                                                                 | P1  |
| ST-14 | Agency mode: client sub-workspaces, per-client branding, bulk operations                                                        | P1  |
| ST-15 | Template gallery (ecommerce, SaaS, community, media, restaurant, education)                                                     | P1  |
| ST-16 | AI config assistant — "make this feel like a native shopping app" → proposes config diff                                        | P2  |

### 12.2 Native shell runtime

| ID    | Feature                                                                                       | Pri                             |
| ----- | --------------------------------------------------------------------------------------------- | ------------------------------- |
| RT-01 | WKWebView / Android WebView host with full config-driven chrome                               | P0                              |
| RT-02 | Bottom tab bar, top nav bar, drawer, all natively rendered                                    | P0                              |
| RT-03 | Multi-window / modal WebView stack with native transitions                                    | P0                              |
| RT-04 | Native pull-to-refresh, swipe-back gestures, edge gestures                                    | P0                              |
| RT-05 | Offline page + connectivity state events to JS                                                | P0                              |
| RT-06 | **Instant shell paint** — bundled skeleton renders <100ms before web content loads            | P0 (differentiator)             |
| RT-07 | Cookie persistence, custom headers, custom UA, CSS/JS injection                               | P0                              |
| RT-08 | `ASWebAuthenticationSession` / Custom Tabs for OAuth flows                                    | P0 (fixes the #1 support issue) |
| RT-09 | Universal Links / App Links + custom scheme routing                                           | P0                              |
| RT-10 | Native datastore (encrypted key-value, Keychain/Keystore-backed)                              | P0                              |
| RT-11 | Download manager with progress, resume, and offline queue                                     | P1                              |
| RT-12 | Secure modal (FLAG_SECURE / screenshot suppression)                                           | P1                              |
| RT-13 | Share-into-app (register as share target)                                                     | P1                              |
| RT-14 | Declarative native surfaces (onboarding, settings, profile, offline library)                  | P1 (differentiator)             |
| RT-15 | Declarative widgets / Live Activities                                                         | P2 (differentiator)             |
| RT-16 | Declarative App Intents / Siri Shortcuts / App Actions                                        | P2 (differentiator)             |
| RT-17 | OTA bundle loader with signature verification, staged rollout, instant rollback               | P1 (differentiator)             |
| RT-18 | Accessibility bridge — correct focus order across native↔web boundary, announced nav changes | P1 (differentiator)             |
| RT-19 | Minimum WebView version check with graceful in-app upgrade prompt (Android)                   | P1                              |

### 12.3 JavaScript Bridge SDK

| ID    | Feature                                                                                                                          | Pri                 |
| ----- | -------------------------------------------------------------------------------------------------------------------------------- | ------------------- |
| BR-01 | Versioned, promise-based API; `shellwright.*` namespace                                                                          | P0                  |
| BR-02 | NPM package with full TypeScript types, tree-shakeable                                                                           | P0                  |
| BR-03 | **Capability negotiation** — `await sw.capabilities()` returns exactly what this build supports, so web code degrades gracefully | P0 (differentiator) |
| BR-04 | Works in browsers as a no-op shim (write once, run on web and app)                                                               | P0 (differentiator) |
| BR-05 | Event bus: appResumed, appPaused, keyboardShown, connectivityChanged, pushReceived, deeplinkOpened, backPressed                  | P0                  |
| BR-06 | SPA navigation integration (history API hooks → native title/tab sync)                                                           | P0                  |
| BR-07 | Structured error objects with actionable codes, never silent failures                                                            | P0                  |
| BR-08 | Bridge inspector — a dev panel showing every bridge call live during preview                                                     | P1 (differentiator) |
| BR-09 | iframe-safe callbacks                                                                                                            | P2                  |
| BR-10 | React / Vue / Svelte hook packages                                                                                               | P1                  |

### 12.4 Plugin system

| ID    | Feature                                                                                                                  | Pri                 |
| ----- | ------------------------------------------------------------------------------------------------------------------------ | ------------------- |
| PL-01 | Manifest-driven plugin format (see Appendix A) — declares deps, permissions, config schema, bridge methods, entitlements | P0                  |
| PL-02 | Build-time injection: Gradle deps + manifest merge + Podfile/SPM + Info.plist + entitlements, all generated              | P0                  |
| PL-03 | Plugin conflict detection (duplicate SDKs, incompatible min-SDK, entitlement collisions) at config time not build time   | P0                  |
| PL-04 | Permission-string management with localization                                                                           | P0                  |
| PL-05 | Privacy manifest (`PrivacyInfo.xcprivacy`) fragments per plugin, merged automatically                                    | P0                  |
| PL-06 | Play Data Safety declaration fragments per plugin, merged into a submittable form                                        | P1                  |
| PL-07 | Private/custom plugins per tenant                                                                                        | P1                  |
| PL-08 | Public plugin SDK + docs so third parties can write plugins                                                              | P2 (ecosystem play) |

**Launch plugin set (all free, all tiers):** biometrics, QR/barcode scanner, document scanner, NFC, haptics, push (first-party + OneSignal + FCM), analytics (first-party + Firebase), Crashlytics, social login (Google/Apple/Facebook), IAP (StoreKit + Play Billing), RevenueCat, app review prompt, contacts, calendar, background location, background audio, native media player, AdMob, jailbreak/root detection, offline download manager, native datastore, share-into-app, secure modal, web screenshot.

### 12.5 Build service

| ID    | Feature                                                                        | Pri                 |
| ----- | ------------------------------------------------------------------------------ | ------------------- |
| BD-01 | Deterministic codegen: `appconfig.json` → complete Xcode + Gradle projects     | P0                  |
| BD-02 | Android build → APK (debug/unsigned) and AAB (release/signed)                  | P0                  |
| BD-03 | iOS build → IPA (development + ad-hoc + App Store)                             | P0                  |
| BD-04 | Signing: managed (you hold material) or BYO (customer uploads / delegates)     | P0                  |
| BD-05 | App Store Connect API key integration for automatic cert + profile management  | P0                  |
| BD-06 | Android keystore generation, storage, and — critically — **export**            | P0                  |
| BD-07 | Build logs streamed live to the browser                                        | P0                  |
| BD-08 | Reproducible builds: same config + same toolchain version = same artifact hash | P1 (differentiator) |
| BD-09 | Toolchain version pinning per app (choose Xcode 26.x, AGP version, target SDK) | P1                  |
| BD-10 | Full source export — zip of both projects, buildable offline, with README      | P0 (differentiator) |
| BD-11 | CLI: `sw build`, `sw preview`, `sw deploy`, `sw config validate`               | P1 (differentiator) |
| BD-12 | Build API + webhooks for CI/CD integration                                     | P1                  |
| BD-13 | Bulk rebuild (agency: rebuild all 40 client apps against new Xcode)            | P1 (differentiator) |
| BD-14 | Self-hosted runner agent for enterprises                                       | P2                  |
| BD-15 | PWA / TWA export target                                                        | P1                  |
| BD-16 | Desktop target (Tauri)                                                         | P2                  |

### 12.6 Preview & testing

| ID    | Feature                                                                                     | Pri                 |
| ----- | ------------------------------------------------------------------------------------------- | ------------------- |
| PV-01 | Streamed Android emulator in browser (WebRTC), interactive                                  | P0                  |
| PV-02 | Streamed iOS simulator in browser                                                           | P0                  |
| PV-03 | Remote web inspector attached to the preview device                                         | P1                  |
| PV-04 | QR code → install on your own physical device (TestFlight / APK / internal app sharing)     | P0                  |
| PV-05 | Automated smoke test: boots, first paint, navigates every tab, no JS errors                 | P1 (differentiator) |
| PV-06 | Screenshot generator — captures all required store sizes across devices, with device frames | P1 (differentiator) |
| PV-07 | Visual regression diff between builds                                                       | P2                  |
| PV-08 | Real-device cloud testing (partner integration)                                             | P2                  |

### 12.7 Publishing & compliance

| ID    | Feature                                                                                                | Pri                        |
| ----- | ------------------------------------------------------------------------------------------------------ | -------------------------- |
| PB-01 | Guided publishing wizard (Apple + Google), step-by-step, state-tracked                                 | P0                         |
| PB-02 | **Store Readiness Score** — analyses config against every known 4.2 trigger, blocks doomed submissions | P0 (differentiator)        |
| PB-03 | TestFlight upload automation                                                                           | P0                         |
| PB-04 | Play internal/closed/open track upload automation                                                      | P0                         |
| PB-05 | Privacy manifest generator (`PrivacyInfo.xcprivacy`)                                                   | P0                         |
| PB-06 | Play Data Safety form generator                                                                        | P0                         |
| PB-07 | Age rating questionnaire assistant                                                                     | P1                         |
| PB-08 | DSA trader-status guidance (EU)                                                                        | P1                         |
| PB-09 | Android developer verification onboarding + package/key registration                                   | P1 (timely differentiator) |
| PB-10 | Rejection knowledge base with searchable reviewer wording and fixes                                    | P1 (differentiator)        |
| PB-11 | Store listing management (descriptions, keywords, localizations)                                       | P2                         |
| PB-12 | Managed publishing service (human, paid)                                                               | P1 (revenue)               |
| PB-13 | Alternative store targets (AppGallery, Amazon, Samsung)                                                | P2                         |

### 12.8 First-party services

| ID    | Feature                                                                                       | Pri                 |
| ----- | --------------------------------------------------------------------------------------------- | ------------------- |
| SV-01 | Push service: APNs + FCM, segments, scheduling, deep-link payloads, delivery + open analytics | P1 (differentiator) |
| SV-02 | Analytics: sessions, DAU/MAU, retention cohorts, screen flow, custom events                   | P1 (differentiator) |
| SV-03 | Crash reporting with symbolication                                                            | P1                  |
| SV-04 | OTA bundle hosting + CDN + signed manifests + staged rollout + rollback                       | P1 (differentiator) |
| SV-05 | Remote config / feature flags                                                                 | P2                  |
| SV-06 | In-app messaging                                                                              | P2                  |
| SV-07 | App-open attribution / deferred deep linking                                                  | P2                  |

### 12.9 Platform / account

| ID    | Feature                                                                      | Pri |
| ----- | ---------------------------------------------------------------------------- | --- |
| AC-01 | Auth (email + OAuth), orgs, workspaces, roles                                | P0  |
| AC-02 | Billing (Stripe), metered usage, invoices, VAT/GST handling                  | P0  |
| AC-03 | Usage dashboard (build minutes, preview minutes, push volume, OTA bandwidth) | P0  |
| AC-04 | Quota enforcement with soft warnings before hard stops                       | P0  |
| AC-05 | Audit log                                                                    | P1  |
| AC-06 | SSO / SAML / SCIM                                                            | P2  |
| AC-07 | Status page + incident comms                                                 | P1  |
| AC-08 | Docs site with runnable examples                                             | P0  |

---

# Part VI — Architecture

## 13. System architecture

### 13.1 Context diagram

```
                 ┌──────────────────────────────────────────┐
                 │            USERS                          │
                 │  developers · agencies · enterprises      │
                 └───────────────┬───────────────────────────┘
                                 │ HTTPS
        ┌────────────────────────▼────────────────────────────┐
        │                    EDGE                             │
        │  Cloudflare: CDN, WAF, DDoS, R2, Workers            │
        └───────┬─────────────────────────────┬───────────────┘
                │                             │
    ┌───────────▼──────────┐      ┌───────────▼──────────────┐
    │   App Studio (SPA)   │      │   Docs / marketing site  │
    │   React + TS         │      │   Astro (static)         │
    └───────────┬──────────┘      └──────────────────────────┘
                │ REST + WebSocket
    ┌───────────▼──────────────────────────────────────────────┐
    │                    CONTROL PLANE                          │
    │  ┌────────────┐ ┌────────────┐ ┌────────────┐            │
    │  │  Identity  │ │  Config    │ │  Billing   │            │
    │  │  & Orgs    │ │  Service   │ │  & Quota   │            │
    │  └────────────┘ └────────────┘ └────────────┘            │
    │  ┌────────────┐ ┌────────────┐ ┌────────────┐            │
    │  │  Plugin    │ │  Build     │ │  Publish   │            │
    │  │  Registry  │ │  Orchestr. │ │  Service   │            │
    │  └────────────┘ └────────────┘ └────────────┘            │
    └───────┬───────────────┬──────────────────┬───────────────┘
            │               │                  │
   ┌────────▼──────┐ ┌──────▼────────┐ ┌───────▼──────────┐
   │  PostgreSQL   │ │ Temporal      │ │  Vault / KMS     │
   │  + Redis      │ │ (workflows)   │ │  (signing keys)  │
   └───────────────┘ └──────┬────────┘ └──────────────────┘
                            │ dispatch
   ┌────────────────────────▼───────────────────────────────┐
   │                     BUILD PLANE                         │
   │  ┌──────────────────┐        ┌──────────────────────┐  │
   │  │ Linux runners    │        │ macOS runners ⚠️      │  │
   │  │ (Docker, K8s)    │        │ Apple Silicon minis  │  │
   │  │ • Android build  │        │ Tart VMs (2/host max)│  │
   │  │ • Android emul.  │        │ • Xcode build        │  │
   │  │ • codegen        │        │ • iOS Simulator      │  │
   │  └────────┬─────────┘        └──────────┬───────────┘  │
   └───────────┼─────────────────────────────┼──────────────┘
               │                             │
   ┌───────────▼─────────────────────────────▼──────────────┐
   │              ARTIFACT & STREAM PLANE                    │
   │  R2 (artifacts, source exports, OTA bundles)            │
   │  LiveKit / Pion (WebRTC device streaming)               │
   └─────────────────────────────────────────────────────────┘
               │
   ┌───────────▼─────────────────────────────────────────────┐
   │              RUNTIME SERVICES (first-party)             │
   │  Push (APNs/FCM) · Analytics (ClickHouse) · OTA CDN     │
   │  Crash ingest · Remote config                           │
   └─────────────────────────────────────────────────────────┘
               │
   ┌───────────▼─────────────────────────────────────────────┐
   │              EXTERNAL                                    │
   │  App Store Connect API · Google Play Developer API      │
   │  Stripe · third-party plugin SDK registries             │
   └─────────────────────────────────────────────────────────┘
```

### 13.2 Control plane services

| Service                | Responsibility                                                         | Notes                                                                                   |
| ---------------------- | ---------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| **Identity & Orgs**    | Users, orgs, workspaces, roles, API tokens, audit                      | Multi-tenant from day one. Row-level tenant isolation.                                  |
| **Config Service**     | CRUD on `appconfig`, schema versioning, migration, validation, diff    | Configs are **immutable versions**; a build always references a config version hash.    |
| **Plugin Registry**    | Plugin manifests, versions, compatibility matrix, conflict rules       | Plugins are versioned artifacts, not code in the monolith.                              |
| **Build Orchestrator** | Enqueue, schedule, retry, cancel, stream logs, cache                   | Durable workflows (Temporal). A build can take 15 min; nothing may be lost on a deploy. |
| **Publish Service**    | ASC + Play API interaction, submission state machine, readiness checks | Long-lived, resumable, notification-driven.                                             |
| **Billing & Quota**    | Metering, Stripe sync, soft/hard limits, overage                       | Meter _before_ the expensive operation, not after.                                      |

### 13.3 Build pipeline in detail

```
1. VALIDATE
   ├─ schema-validate appconfig against version N
   ├─ resolve plugins → concrete versions
   ├─ conflict check (SDK dupes, minSDK, entitlements)
   ├─ entitlement/quota check
   └─ FAIL FAST — never burn a Mac minute on an invalid config

2. RESOLVE & CACHE KEY
   ├─ hash = H(config, plugin versions, toolchain versions, template version)
   └─ if artifact exists for hash → return cached  ★ major cost saver

3. CODEGEN  (Linux, cheap)
   ├─ clone template repo at pinned tag
   ├─ render templates (icons, colours, nav, Info.plist, AndroidManifest,
   │   build.gradle, Podfile/Package.swift, entitlements, privacy manifest)
   ├─ inject plugin sources + wire plugin registry
   ├─ generate native surfaces / widgets / intents from schema
   └─ output: project tarball → R2

4a. ANDROID BUILD  (Linux container)
   ├─ restore Gradle cache
   ├─ ./gradlew bundleRelease / assembleDebug
   ├─ sign (managed key from Vault, or unsigned for BYO)
   ├─ verify (apksigner verify, manifest sanity, size report)
   └─ upload AAB/APK + mapping.txt

4b. iOS BUILD  (macOS VM)  ⚠️ expensive
   ├─ restore SPM/CocoaPods + DerivedData cache
   ├─ fetch certs + profiles via ASC API (or customer-supplied)
   ├─ xcodebuild archive → exportArchive
   ├─ codesign + verify
   └─ upload IPA + dSYM

5. POST
   ├─ static analysis: size budget, permission audit, tracking-domain scan
   ├─ Store Readiness Score recompute
   ├─ optional: boot smoke test on emulator/simulator
   ├─ optional: screenshot generation
   └─ notify (websocket + webhook + email)
```

**Cost control rules, in priority order:**

1. **Validate on Linux before ever touching macOS.** Most build failures are config errors.
2. **Cache aggressively by config hash.** Icon-only change should not trigger a full rebuild if the hash logic is granular (split hashes: native-code hash vs asset hash — asset-only changes can be a resource-patch build).
3. **Batch Mac work.** Mac hosts have a 24-hour minimum billing window on AWS; owning hardware or using a monthly-billed provider avoids that trap entirely.
4. **Warm pools.** Cold-starting a macOS VM with Xcode is minutes. Keep VMs hot and reset them between jobs.

### 13.4 macOS fleet architecture ⚠️

This is the hardest infrastructure component. Options, honestly costed:

| Option                                              | Cost                                                                  | Pros                                                | Cons                                                                      |
| --------------------------------------------------- | --------------------------------------------------------------------- | --------------------------------------------------- | ------------------------------------------------------------------------- |
| **Own Mac minis in a colo**                         | ~$600–$1,400 capex each + ~$50–$100/mo colo                           | Cheapest per build at any real volume; full control | Capex, physical ops, remote hands, hardware failure                       |
| **MacStadium / Scaleway / Oxide-style Mac hosting** | ~$99–$250/mo per host                                                 | No hardware ops, monthly billing                    | Vendor dependency, less flexible                                          |
| **AWS EC2 Mac (mac2/mac-m4)**                       | ~$1.08–$1.30/hr, ⚠️ **24-hour minimum per dedicated host allocation** | Elastic-ish, AWS integration                        | ~$26 minimum every time you allocate; not autoscaling-friendly; expensive |
| **GitHub Actions macOS runners**                    | ~$0.062/min ≈ $3.72/hr                                                | Zero ops                                            | Most expensive per build; unsuitable as core infra                        |

**Recommendation:** start on a hosted Mac provider (1 host) for the alpha, move to **owned Apple Silicon Mac minis** as soon as you exceed ~150 iOS builds/day. Use **Tart** (or Anka) for VM isolation — ⚠️ Apple's macOS licence permits **at most 2 VMs per physical host**, which caps your density and must be factored into capacity planning.

**Fleet design:**

```
┌─────────────── Mac Host (M4 Pro, 48GB) ──────────────┐
│  macOS host OS + Tart                                │
│  ┌───────────────────┐  ┌───────────────────┐        │
│  │ VM 1: build       │  │ VM 2: simulator   │        │
│  │  Xcode 26.x       │  │  Xcode + simctl   │        │
│  │  ephemeral        │  │  streaming agent  │        │
│  └───────────────────┘  └───────────────────┘        │
│         ▲ max 2 VMs per host (Apple EULA) ⚠️          │
└──────────────────────────────────────────────────────┘
```

- Keep **golden VM images** per Xcode version. Restore from snapshot after each job (2–10s) rather than cleaning.
- Maintain **N–1 and N Xcode versions** simultaneously; Apple's submission deadlines force the whole fleet to move together.
- Health-check every host; auto-drain on failure. Xcode installs corrupt more often than you'd think.

### 13.5 Device preview architecture

```
Browser                    Gateway                 Device host
┌────────┐   WebRTC    ┌──────────────┐        ┌──────────────────┐
│ canvas │◄───video────│  LiveKit /   │◄──────►│ Android emulator │
│ +input │────input───►│  SFU + auth  │        │  (KVM, Linux)    │
└────────┘             │  + session   │        └──────────────────┘
                       │    manager   │        ┌──────────────────┐
                       │  + quota     │◄──────►│ iOS Simulator    │
                       └──────────────┘        │  (macOS VM)      │
                                               └──────────────────┘
```

- **Android:** Linux hosts with nested virtualization / bare metal + KVM. AOSP emulator images (no GMS by default — offer a GMS image for apps needing Play Services, noting licensing). Capture via the emulator's gRPC video stream or `scrcpy`, relay through a WebRTC SFU.
- **iOS:** macOS VM running `simctl`, capture via `simctl io ... recordVideo` or ScreenCaptureKit, same SFU.
- **Session manager** handles pooling, warm devices, idle timeout, per-plan concurrency and duration limits, and metering.
- **Build vs buy:** Appetize.io offers exactly this with an embed API, per-minute billing, a free allowance around 100 min/month, and an enterprise self-hosted option. **Buy for the alpha, build for margin later.** Android self-hosting is straightforward; iOS self-hosting is bound to your Mac fleet anyway.
- Latency budget: WebRTC adds roughly 40–120ms. Acceptable for config preview; do not promise it for performance testing.

### 13.6 Bridge protocol design

Do not repeat Median's shape. Design it properly once.

```
Web ──► window.__sw.postMessage(envelope) ──► native dispatcher ──► plugin
Web ◄── window.__sw.receive(envelope)    ◄── native dispatcher ◄── plugin
```

**Envelope:**

```jsonc
{
  "v": 1, // protocol version
  "id": "01J...", // ULID, for request/response correlation
  "type": "request", // request | response | event
  "plugin": "biometric",
  "method": "authenticate",
  "params": { "reason": "Unlock your account" },
  "meta": { "ts": 1756600000000 },
}
```

**Design rules:**

- **Versioned.** The shell declares the protocol version it speaks; the SDK adapts or fails loudly. Never silently no-op.
- **Capability negotiation first.** `await sw.capabilities()` returns the exact plugin+method set this binary supports. Web code branches on capability, not on user-agent sniffing. This is what makes "one codebase for web and app" actually true.
- **Promise-based with typed errors.** `{ code: "BIOMETRIC_NOT_ENROLLED", message, recoverable: true }`. Never resolve on failure.
- **Browser shim.** The npm package works in a plain browser as a no-op that returns `capabilities: {}`. Developers never write `if (isApp)` guards.
- **Origin allowlist.** ⚠️ Security-critical: only inject the bridge on origins declared in the config. Otherwise any page the user navigates to can call native APIs. Median-class platforms have historically been loose here; be strict.
- **Rate limiting and payload caps** per method, enforced natively.
- **Inspector mode**: in debug builds, mirror every envelope to a dev panel.

### 13.7 Plugin manifest architecture

A plugin is a **directory + manifest**, never a code change in the core shell. See Appendix A for the full schema. The build system consumes manifests to generate:

- `build.gradle` dependency lines + `AndroidManifest.xml` merge fragments + ProGuard rules
- `Podfile` / `Package.swift` entries + `Info.plist` fragments + `.entitlements` fragments
- `PrivacyInfo.xcprivacy` API-reason + tracking-domain fragments
- Play Data Safety declaration fragments
- Bridge method registration (generated dispatcher, no reflection)
- TypeScript type definitions for the npm SDK
- Documentation pages

**One manifest, seven outputs.** This is what makes 40 plugins maintainable by one person, and it is the single most important internal architecture decision in the whole system.

### 13.8 OTA bundle architecture

```
Customer web build ──► sw CLI ──► bundle.zip
                                    │
                            sign (Ed25519, per-app key)
                                    │
                                    ▼
                        R2 + CDN, manifest.json
                                    │
                    ┌───────────────┴──────────────┐
                    │  App checks manifest on      │
                    │  launch + resume             │
                    │  ├─ verify signature ⚠️       │
                    │  ├─ check compat range        │
                    │  ├─ download delta            │
                    │  ├─ stage, apply next launch  │
                    │  └─ rollback on boot failure  │
                    └───────────────────────────────┘
```

- Bundles are **signed**; the public key is compiled into the binary. An unsigned or mis-signed bundle is never executed.
- **Staged rollout** (1% → 10% → 50% → 100%) with automatic halt on crash-rate regression.
- **Automatic rollback** if the app fails to reach "first successful paint" twice in a row.
- ⚠️ Guardrails per §8.4 — warn loudly when a bundle changes navigation structure or introduces a payment surface.

### 13.9 Offline engine

Three layers, increasing capability:

1. **Shell offline** (P0) — bundled skeleton UI + branded offline page + connectivity events. Always on.
2. **Asset offline** (P1) — OTA bundle doubles as an offline fallback. If the network is down, serve the last known-good bundle from disk. This is the big one: it turns "white screen of death" into "app works, data is stale."
3. **Data offline** (P2) — native datastore + a sync queue API exposed over the bridge. The website enqueues mutations; the shell replays them on reconnect. Do not over-promise; this is where every hybrid platform's claims exceed reality.

### 13.10 Data model (core entities)

```
Org ──┬── Workspace ──┬── App ──┬── ConfigVersion (immutable, hashed)
      │               │         ├── Build ── Artifact
      │               │         ├── Release ── StoreSubmission
      │               │         ├── OtaBundle
      │               │         ├── SigningIdentity (ref → Vault path)
      │               │         └── UsageRecord
      │               └── Member (role)
      ├── Subscription ── Invoice
      └── AuditEvent

PluginManifest (global, versioned)
ToolchainVersion (global: xcode, agp, sdk, template)
```

**Key invariants:**

- A `Build` is immutable and references exactly one `ConfigVersion` and one `ToolchainVersion` set. This makes builds reproducible and makes "why did my app change?" answerable.
- `SigningIdentity` never stores key material in Postgres — only a Vault reference plus non-secret metadata (team ID, expiry, fingerprint).
- `UsageRecord` is written by the runner, not the API, so metering survives API outages.

---

# Part VII — Engineering

## 14. Tech stack

### 14.1 Recommended stack

| Layer                         | Recommendation                                                                                        | Why                                                                                                                                                                                          | Alternative if you disagree                                                          |
| ----------------------------- | ----------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| **App Studio frontend**       | React 19 + TypeScript + Vite + Tailwind + shadcn/ui + TanStack Query + Zustand + Monaco               | Fastest path to a polished config editor; Monaco gives you the raw-JSON view for free                                                                                                        | SvelteKit (smaller, faster)                                                          |
| **Marketing + docs**          | Astro + MDX, statically hosted                                                                        | SEO is a primary acquisition channel here; content marketing is how MobiLoud won                                                                                                             | Next.js                                                                              |
| **Control plane API**         | **ASP.NET Core 10 (.NET)** — minimal APIs, EF Core, MediatR-lite CQRS                                 | Plays to your existing .NET depth; excellent long-running-process story; strong typing across a large domain; genuinely fast                                                                 | NestJS/TypeScript if you want one language end-to-end; Go if you want small binaries |
| **Workflow engine**           | **Temporal** (.NET SDK)                                                                               | Builds are long, failure-prone, resumable, and must survive deploys. Rolling your own queue+retry+compensation logic is a 6-month tax. This is the highest-leverage dependency in the stack. | Hangfire (simpler, weaker), or Argo Workflows if you go K8s-native                   |
| **Primary DB**                | PostgreSQL 17                                                                                         | Row-level security for tenant isolation, JSONB for configs, mature everything                                                                                                                | —                                                                                    |
| **Cache / queue / locks**     | Redis (Valkey)                                                                                        | Session, rate limits, runner leases, live log fan-out                                                                                                                                        | —                                                                                    |
| **Analytics store**           | ClickHouse                                                                                            | First-party app analytics + your own build telemetry. Cheap at billions of rows.                                                                                                             | TimescaleDB if you want fewer moving parts                                           |
| **Object storage**            | **Cloudflare R2**                                                                                     | ⚠️ Zero egress fees. You will serve artifacts, source exports, and OTA bundles constantly. On S3 egress alone could exceed your compute bill.                                                | Backblaze B2                                                                         |
| **Secrets / signing custody** | **HashiCorp Vault** (Transit + KV v2) or AWS KMS + envelope encryption                                | Per-tenant encryption keys, audited access, no plaintext keys at rest or in logs                                                                                                             | —                                                                                    |
| **Linux runners**             | Docker containers on Hetzner/OVH dedicated or bare-metal, orchestrated by Temporal workers            | Android builds and emulators are cheap on owned metal; nested virt for KVM                                                                                                                   | K8s + Karpenter on AWS if you need elasticity more than margin                       |
| **macOS runners** ⚠️          | Apple Silicon Mac minis + **Tart** VMs; hosted (MacStadium/Scaleway) first, owned later               | Only legal path to iOS builds. Tart snapshots make resets seconds, not minutes. Max 2 VMs/host per Apple licence.                                                                            | Anka (commercial, more features, paid)                                               |
| **Device streaming**          | **LiveKit** (WebRTC SFU) + a capture agent per device                                                 | Battle-tested SFU, self-hostable, handles NAT/TURN                                                                                                                                           | Pion directly (more control, more work); Appetize.io (buy, don't build, for v1)      |
| **iOS shell**                 | Swift 6 + SwiftUI for native surfaces, UIKit for the WebView host                                     | SwiftUI for generated surfaces/widgets; UIKit for fine-grained WebView control                                                                                                               | —                                                                                    |
| **Android shell**             | Kotlin 2.x + Jetpack Compose for native surfaces, Views for WebView host                              | Same reasoning                                                                                                                                                                               | —                                                                                    |
| **Codegen**                   | Template engine over pinned template repos — **Scriban** (.NET) or Handlebars                         | Templates must be readable and diffable; avoid clever AST manipulation                                                                                                                       | —                                                                                    |
| **Store automation**          | **fastlane** (Ruby) wrapped by your orchestrator, + direct ASC/Play REST calls where fastlane is thin | fastlane solves a hundred edge cases you don't want to rediscover                                                                                                                            | Pure REST + `xcrun altool`, more control less coverage                               |
| **Push delivery**             | Direct APNs (HTTP/2, token auth) + FCM v1 API, your own fan-out service                               | Owning this is a core differentiator; the protocols are not hard, the scale engineering is                                                                                                   | —                                                                                    |
| **Billing**                   | Stripe (Billing + metered usage + Tax)                                                                | Metered usage is the whole model; Stripe Tax handles global VAT/GST                                                                                                                          | Paddle (merchant of record — worth it if you don't want to be the tax filer)         |
| **Observability**             | OpenTelemetry → Grafana stack (Loki/Tempo/Mimir), self-hosted                                         | Build logs are high-volume; hosted APM pricing will hurt                                                                                                                                     | Datadog if you'd rather pay than operate                                             |
| **Error tracking**            | Sentry (self-hosted or SaaS)                                                                          | For your platform _and_ as the basis of the crash-reporting product                                                                                                                          | —                                                                                    |
| **CI for your own code**      | GitHub Actions                                                                                        | —                                                                                                                                                                                            | —                                                                                    |

### 14.2 Language boundary decisions

- **.NET for the control plane, orchestration, and codegen.** One language, your strongest, and genuinely well suited to long-lived orchestration.
- **TypeScript for the studio and the bridge SDK.** Non-negotiable — the SDK must be a first-class npm package.
- **Swift and Kotlin for the shells.** These are the product. Hand-written, carefully, not generated.
- **Ruby only inside fastlane.** Don't let it spread.
- **Bash for runner glue, but keep it thin** — anything over 50 lines becomes a .NET tool.

### 14.3 Repository layout

```
shellwright/
├─ apps/
│  ├─ studio/              # React SPA
│  ├─ web/                 # Astro marketing + docs
│  └─ api/                 # ASP.NET Core control plane
├─ services/
│  ├─ orchestrator/        # Temporal workers
│  ├─ push/                # APNs + FCM fan-out
│  ├─ analytics-ingest/    # → ClickHouse
│  └─ stream-gateway/      # LiveKit session manager
├─ shells/
│  ├─ ios/                 # Swift template project  ← the product
│  └─ android/             # Kotlin template project ← the product
├─ plugins/
│  ├─ biometric/  { plugin.yaml, ios/, android/, web/, docs/ }
│  ├─ qr-scanner/
│  └─ ...                  # ~40 of these
├─ packages/
│  ├─ bridge-sdk/          # npm: @shellwright/bridge
│  ├─ cli/                 # npm: @shellwright/cli
│  └─ config-schema/       # JSON Schema, shared TS + C# codegen
├─ runners/
│  ├─ linux/               # Dockerfiles, Android SDK images
│  └─ macos/               # Tart image build scripts, Xcode provisioning
└─ infra/                  # Terraform / Pulumi, Ansible for Mac hosts
```

### 14.4 Non-obvious engineering decisions worth locking in now

1. **Config schema is versioned and migrated, forever.** You will change it 50 times. Write the migration framework in week one or regret it in month six.
2. **Builds are content-addressed.** `hash(config + plugins + toolchain + template)` is the cache key and the reproducibility guarantee.
3. **The template projects are real, hand-maintained apps** that build standalone with a default config. Never let the templates only be buildable through the generator — you'll lose the ability to debug them.
4. **Plugins never modify core shell code.** If a plugin needs a shell change, that's a shell feature with a capability flag. Enforce this ruthlessly or you get a combinatorial nightmare by plugin #15.
5. **Split the build hash.** Asset-only changes (icon, splash, colours) should trigger a fast resource-patch path, not a full compile. This is worth ~70% of your Mac minutes.
6. **Metering happens in the runner.** Never trust the API to have observed the work.
7. **Every generated project ships with its own README and a working `build.sh`.** Source export must be genuinely usable, not a technicality.

---

## 15. Services & third-party dependencies

### 15.1 Hard external dependencies

| Dependency                               | What breaks without it             | Mitigation                                                   |
| ---------------------------------------- | ---------------------------------- | ------------------------------------------------------------ |
| ⚠️ **Apple hardware**                    | No iOS builds at all               | Own spare hosts; N+1 capacity; hosted provider as overflow   |
| ⚠️ **App Store Connect API**             | No automated signing or submission | Manual fallback path documented; never make it the only path |
| ⚠️ **Google Play Developer API**         | No automated Play upload           | Same                                                         |
| **Apple Developer Program** (customer's) | Customer cannot ship at all        | Onboarding wizard; be explicit about the $99/yr              |
| **Google Play Console** (customer's)     | Same                               | $25 one-time; plus developer verification from 2026          |
| Cloudflare R2 + CDN                      | Artifacts and OTA unavailable      | Multi-region bucket; fallback origin                         |
| Stripe                                   | No revenue                         | —                                                            |
| APNs / FCM                               | Push dead                          | Both are free and highly available; queue and retry          |

### 15.2 Third-party SDKs you will integrate (as free plugins)

Push: OneSignal, Firebase Cloud Messaging, Braze, Klaviyo, Intercom, Customer.io, Iterable.
Analytics: Firebase Analytics, Crashlytics, Branch, AppsFlyer, Adjust, Meta App Events.
Auth: Google Sign-In, Sign in with Apple, Facebook Login, Auth0, Clerk.
Commerce: StoreKit 2, Google Play Billing, RevenueCat, AdMob.
Scanning: ML Kit / Vision (free), Scandit (customer licence).
Media: ExoPlayer/AVPlayer (first-party), JW Player, Zoom, Twilio Video (customer licence).
Enterprise: Microsoft Intune MAM.

⚠️ **Every third-party SDK is a maintenance liability and a privacy-manifest obligation.** Each one needs: a version-compatibility matrix entry, a privacy manifest fragment, a Data Safety fragment, and a test app. Budget ~2 days of ongoing maintenance per SDK per year. Forty SDKs ≈ 80 days/year of pure maintenance. **Ship 15 well rather than 40 badly.**

---

# Part VIII — Business

## 16. Unit economics & cost model

### 16.1 What each operation actually costs you

Figures assume owned/rented dedicated hardware at moderate utilization. They are estimates — validate before pricing.

| Operation                          | Cost at low scale     | Cost at scale | Notes                                                         |
| ---------------------------------- | --------------------- | ------------- | ------------------------------------------------------------- |
| Config save / validate             | ~$0.000               | ~$0.000       | Free forever                                                  |
| Codegen (Linux, ~20s)              | ~$0.001               | ~$0.0003      | Free forever                                                  |
| **Android build** (3 min, Linux)   | ~$0.01                | ~$0.003       | **Effectively free — make it unlimited**                      |
| **iOS build** (8 min, macOS)       | ~$0.30                | ~$0.02–0.05   | The real cost centre. Meter this.                             |
| Android emulator minute            | ~$0.005               | ~$0.0005      | **Effectively free — be generous**                            |
| iOS simulator minute               | ~$0.03                | ~$0.005       | Bound to Mac fleet. Meter.                                    |
| Artifact storage (100MB/build, R2) | ~$0.0015/mo           | same          | ⚠️ zero egress = source export is free                        |
| Source code export                 | ~$0.00                | ~$0.00        | **Free forever — and it's a headline feature**                |
| Push notification (per 1,000)      | ~$0.002               | ~$0.0005      | Nearly free. Be very generous.                                |
| Analytics event (per 1M)           | ~$0.15                | ~$0.05        | Nearly free                                                   |
| OTA bundle delivery (per GB)       | ~$0.00 egress on R2   | same          | **Nearly free — huge giveaway opportunity**                   |
| Managed publishing                 | **2–6 human hours**   | same          | ⚠️ The only genuinely expensive thing. Price accordingly.     |
| Support ticket                     | **0.5–2 human hours** | same          | Docs and readiness checks are cost control, not nice-to-haves |

### 16.2 The critical realization

**Roughly 85% of what Median charges for costs them nothing per user.** Their gating is entirely a pricing-power decision, not a cost-recovery decision. Your only genuinely metered resources are:

1. macOS compute (iOS builds + iOS simulator)
2. Human labour (managed publishing, support, enterprise onboarding)
3. Engineering payroll (the annual OS treadmill)

Everything else — every plugin, every seat, every watermark, every Android build, every push, every export — is a rounding error.

### 16.3 Fixed monthly cost estimate (early stage)

| Item                                              | Monthly      |
| ------------------------------------------------- | ------------ |
| 1 × hosted Mac host                               | $150         |
| 2 × Linux dedicated (build + emulator + services) | $120         |
| Postgres + Redis (managed or self-hosted)         | $50          |
| R2 storage + CDN                                  | $20          |
| ClickHouse (small)                                | $40          |
| Domain, email, Sentry, misc SaaS                  | $80          |
| Apple Developer Program (your own, for testing)   | $8           |
| Google Play Console (one-time $25)                | —            |
| **Total**                                         | **~$470/mo** |

At $39/mo average revenue per paying account, **12 paying customers cover infrastructure.** That is a very reachable break-even, and it is the strongest argument for building this.

Scaling: each additional Mac host (~$150/mo) supports roughly 200–400 iOS builds/day or ~1,000 simulator-hours/month. Add one per ~150 active paying apps.

---

## 17. Pricing & packaging — what to give away free

### 17.1 The giveaway list (your headline)

Everything below is free on every plan, forever, because it costs you essentially nothing and Median charges $229–$990 + annual fees for it.

| Given free                                                        | Median's price for it        | Your marginal cost      |
| ----------------------------------------------------------------- | ---------------------------- | ----------------------- |
| **No watermark, any plan**                                        | $229 activation              | $0.00                   |
| **All native plugins, unlimited count**                           | $590–$990 + annual           | $0.00                   |
| **Unlimited team seats**                                          | bundled in $590–$990 tiers   | $0.00                   |
| **Unlimited Android builds**                                      | metered by licence           | ~$0.003                 |
| **Unlimited apps per account**                                    | per-app licence, each        | $0.00                   |
| **Full source code export (iOS + Android)**                       | limited / paid               | $0.00                   |
| **CLI + config-as-code + git workflow**                           | not offered                  | $0.00                   |
| **First-party push, 100k notifications/mo**                       | not offered (3rd-party only) | ~$0.20                  |
| **First-party analytics + crash reporting**                       | not offered                  | ~$0.10                  |
| **OTA bundle updates, 25k MAU**                                   | not offered                  | ~$0.00 (R2 zero egress) |
| **PWA / TWA export**                                              | not offered                  | $0.00                   |
| **Store Readiness Score + rejection KB**                          | bundled in $7,200 service    | $0.00                   |
| **Privacy manifest + Data Safety generators**                     | not offered                  | $0.00                   |
| **Android developer verification onboarding**                     | blog post only               | $0.00                   |
| **Screenshot generator (all store sizes)**                        | not offered                  | ~$0.05                  |
| **Unlimited Android device preview (15-min sessions)**            | 1 min on free tier           | ~$0.075/session         |
| **Deep links, biometrics, QR, haptics, IAP, offline, native nav** | tier-gated                   | $0.00                   |

**Your one-line pitch writes itself:** _"Everything Median charges $990 plus $669 a year for, we give away. You pay only for iOS build minutes."_

### 17.2 What you charge for, and why it's defensible

| Charged                                                                | Justification                                             |
| ---------------------------------------------------------------------- | --------------------------------------------------------- |
| iOS build minutes beyond free allowance                                | ⚠️ Apple hardware costs real money, per minute            |
| iOS simulator minutes beyond free allowance                            | Same                                                      |
| Push/analytics/OTA above generous thresholds                           | Real infrastructure at volume                             |
| Managed publishing                                                     | Human labour, 2–6 hours per app                           |
| Agency multi-tenancy & bulk rebuild                                    | Operational value, high willingness to pay                |
| Enterprise: SSO, MDM, self-hosted runners, SLA, private plugins, audit | Real engineering + support commitment                     |
| Annual compatibility maintenance (bundled into subscription)           | ⚠️ The OS treadmill is a real, permanent engineering cost |

### 17.3 Proposed price ladder

|                             | **Free**                       | **Pro**    | **Team**   | **Agency**    | **Enterprise**     |
| --------------------------- | ------------------------------ | ---------- | ---------- | ------------- | ------------------ |
| Price                       | **$0**                         | **$39/mo** | **$99/mo** | **$299/mo**   | from **$1,500/mo** |
| Apps                        | 3                              | Unlimited  | Unlimited  | Unlimited     | Unlimited          |
| Seats                       | Unlimited                      | Unlimited  | Unlimited  | Unlimited     | Unlimited          |
| Watermark                   | **None**                       | None       | None       | None          | None               |
| Plugins                     | **All**                        | All        | All        | All           | All + private      |
| Android builds              | **Unlimited**                  | Unlimited  | Unlimited  | Unlimited     | Unlimited          |
| iOS builds/mo               | **15**                         | 150        | 500        | 2,000         | Unlimited          |
| Android preview             | **Unlimited, 15-min sessions** | Unlimited  | Unlimited  | Unlimited     | Unlimited          |
| iOS simulator/mo            | **60 min**                     | 600 min    | 2,000 min  | 6,000 min     | Unlimited          |
| Source export               | **Yes**                        | Yes        | Yes        | Yes           | Yes                |
| CLI + config-as-code        | **Yes**                        | Yes        | Yes        | Yes           | Yes                |
| Push notifications/mo       | **100k**                       | 1M         | 5M         | 25M           | Unlimited          |
| Analytics events/mo         | **1M**                         | 10M        | 50M        | 250M          | Unlimited          |
| OTA MAU                     | **25k**                        | 100k       | 500k       | 2M            | Unlimited          |
| Environments (dev/stg/prod) | 1                              | 3          | 3          | Unlimited     | Unlimited          |
| Client sub-workspaces       | —                              | —          | —          | **Unlimited** | Unlimited          |
| Bulk rebuild                | —                              | —          | Yes        | Yes           | Yes                |
| White-label studio          | —                              | —          | —          | Yes           | Yes                |
| SSO / SAML / SCIM           | —                              | —          | —          | —             | Yes                |
| Self-hosted runners         | —                              | —          | —          | —             | Yes                |
| MDM / Intune / cert pinning | —                              | —          | —          | —             | Yes                |
| Support                     | Community                      | Email 48h  | Email 24h  | Priority 8h   | SLA + Slack        |
| Managed publishing          | $399/app                       | $399/app   | $299/app   | $199/app      | Included ×3        |

**Overage (all plans):** iOS build $0.50 · iOS simulator minute $0.05 · push $0.30/100k · OTA $0.50/10k MAU. No overage on anything Android.

### 17.4 Why this beats Median on the numbers

**Case: agency with 20 client apps, 3 years.**

|                                 | Median (Essential tier)   | Shellwright (Agency)   |
| ------------------------------- | ------------------------- | ---------------------- |
| Year 1                          | 20 × $590 = **$11,800**   | 12 × $299 = **$3,588** |
| Year 2                          | 20 × $399 = **$7,980**    | **$3,588**             |
| Year 3                          | 20 × $399 = **$7,980**    | **$3,588**             |
| Publishing (20 apps)            | up to $7,200 each managed | 20 × $199 = $3,980     |
| **3-year total (self-publish)** | **$27,760**               | **$10,764**            |
| Plugin limit                    | 3 per app                 | Unlimited              |
| Seats                           | 3                         | Unlimited              |

**61% cheaper, with more features.** That is a slide, not a sentence.

**Case: solo indie shipping one app.**

|                                                   | Median         | Shellwright                                             |
| ------------------------------------------------- | -------------- | ------------------------------------------------------- |
| Ship with no watermark, biometrics, QR, push, IAP | $990 + $669/yr | **$0** (within free tier) or $39/mo if they build often |

### 17.5 Pricing psychology notes

- **Never charge an "activation fee."** It's Median's most disliked mechanic and it kills self-serve conversion. Subscription only.
- **Free tier must ship a real, publishable app.** A free tier that produces a watermarked toy converts poorly and generates support load without revenue. A free tier that ships to the App Store creates evangelists.
- **Meter the thing users understand.** "iOS builds" is intuitive. "Build minutes" invites anxiety about whether their app is slow to compile.
- **Warn at 80% of quota, never hard-stop mid-submission.** Allow a one-time grace overage. Losing a customer's store deadline over $2 is insane.
- **Annual discount: 2 months free.** Standard, improves cash flow, funds Mac hardware.
- **Grandfather aggressively.** Early customers should feel rewarded, not punished, when you raise prices.

---

# Part IX — Execution

## 18. Hardest engineering problems, ranked

Ranked by _risk to the business_, not by intellectual difficulty.

### 18.1 macOS build fleet operations — **Severity: critical**

**Why it's hard:** ⚠️ iOS binaries require Apple hardware. Apple's licence caps you at 2 VMs per physical host. Xcode is a 15–40GB install that corrupts, upgrades break things, and Apple's submission deadlines force the entire fleet to move to a new Xcode version on Apple's schedule, not yours. Hosts fail physically and you can't SSH a dead Mac mini back to life.

**Mitigations:**

- Golden VM images per Xcode version, snapshot-restore between jobs (seconds, not minutes).
- Always run N and N−1 Xcode. Migrate the fleet on a schedule, with a canary host first.
- N+1 hardware capacity; a spare host that does nothing but wait.
- Automated health checks; auto-drain and page on failure.
- Keep a hosted-Mac account as overflow even when you own hardware.
- ⚠️ Never use AWS EC2 Mac for bursty work — the 24-hour minimum dedicated-host billing window means every spin-up costs ~$26.

### 18.2 Custody of customer signing material — **Severity: critical**

**Why it's hard:** You will hold Apple distribution certificates and private keys, App Store Connect API keys, and Android upload/signing keystores for hundreds of strangers. Compromise means an attacker can ship malware signed as your customers. This is a company-ending, possibly legally-actionable event.

**Mitigations:**

- **Prefer delegation over custody.** Default flow: customer creates an ASC API key with the narrowest role and shares it; customer's Play service account gets the narrowest role. You never hold their account credentials.
- **Prefer Play App Signing.** Google holds the app signing key; you only ever touch the upload key. This dramatically reduces your blast radius on Android.
- Key material lives in Vault/KMS only, encrypted per-tenant, **never** in Postgres, never in logs, never in build logs (scrub aggressively — signing tools are chatty).
- Keys are injected into the build VM at job start and the VM is destroyed after. Never persisted on a runner.
- Every access to key material is audit-logged with actor, app, job ID, timestamp.
- Offer **BYO signing** as a first-class option: the customer signs locally with an unsigned artifact you produce. Enterprises will demand this; it is also a good default for the paranoid.
- Publish your key-handling model publicly. It becomes a sales asset.

### 18.3 ⚠️ Guideline 4.2 approval unpredictability — **Severity: critical (business model risk)**

Covered in §7.1. The commercial risk: if your users' apps get rejected at a higher rate than Median's, your reputation dies in month three, regardless of engineering quality.

**Mitigations:** native-by-default scaffolding, declarative native surfaces, Store Readiness Score with a hard block, rejection knowledge base, and a paid review-before-you-submit service. **Do not offer an "approval guarantee" until you have 100+ successful submissions of data.**

### 18.4 Plugin combinatorics and SDK conflicts — **Severity: high**

**Why it's hard:** With 40 plugins, there are 2⁴⁰ possible configurations. Two plugins can pull incompatible versions of the same transitive dependency (this is endemic with Firebase, Google Play Services, and anything using OkHttp or Kotlin coroutines). Entitlements collide. Minimum SDK levels conflict. A user hits a combination nobody has ever built.

**Mitigations:**

- Manifest-declared dependency constraints with **resolution at config time, not build time** — reject the combination in the studio with a clear message before the user waits 8 minutes for a failure.
- A **nightly build matrix** covering the top ~200 real-world plugin combinations plus all pairs.
- A single pinned "BOM" (bill of materials) for shared transitive deps (Firebase BoM, Kotlin, AndroidX) that all plugins must conform to.
- **Cap the launch set at ~15 plugins.** Depth beats breadth. Every plugin is a permanent liability.

### 18.5 WebView fidelity, cookies, and auth flows — **Severity: high**

**Why it's hard:** This will be your #1 support category. WKWebView cookie partitioning, ITP, SameSite, third-party cookie blocking, OAuth redirect chains that work in Safari and die in a WebView, SSO through corporate IdPs, session loss on app resume, and the fact that Android System WebView versions vary across a decade of devices.

**Mitigations:**

- Use `ASWebAuthenticationSession` (iOS) / Custom Tabs (Android) for OAuth flows — the WebView is the wrong container for auth and this fixes most of it.
- Native cookie persistence with explicit sync on background/foreground.
- Custom header injection for token-based schemes.
- A **diagnostics tool** in the studio: enter a URL, we load it in a real WebView and report cookie behaviour, mixed content, CSP violations, blocked resources, and viewport problems. This converts a support ticket into a self-service moment.
- Publish an explicit minimum Android WebView version with a graceful in-app upgrade prompt.

### 18.6 Device preview at low latency and low cost — **Severity: medium-high**

WebRTC adds 40–120ms; iOS simulators are pinned to Mac capacity; concurrency management, warm pools, idle reaping, and quota enforcement are all real work.

**Mitigation:** buy Appetize for v1, build Android streaming yourself once volume justifies it (it's the cheap half), keep iOS on the Mac fleet you already run.

### 18.7 The annual OS treadmill — **Severity: medium, permanent**

Every September/October: new iOS, new Xcode, new required SDK, deprecated APIs. Every year: a new Android target API requirement. Plus ad-hoc requirements like privacy manifests, Data Safety, DSA trader status, and Android developer verification.

**Mitigation:** budget ~25% of all engineering capacity, permanently, to compatibility. Price the subscription to fund it. Communicate proactively — a "we've already tested your app against iOS 27" email is worth more than any feature.

### 18.8 True offline — **Severity: medium**

Service workers are unreliable in WKWebView; iOS evicts caches; there is no dependable background sync. Every hybrid platform over-promises here.

**Mitigation:** ship layers 1 and 2 from §13.9 (shell offline + bundle fallback), which covers ~80% of real needs. Be publicly honest about layer 3. Honesty here is a differentiator — the whole category over-claims.

### 18.9 Accessibility across the native↔web boundary — **Severity: medium (blocking for public sector)**

VoiceOver/TalkBack focus order breaks at the boundary; native chrome and web content don't announce coherently.

**Mitigation:** invest early, publish a VPAT-style accessibility statement, and use it to win education, healthcare, government, and EU enterprise deals where competitors simply cannot answer the question.

### 18.10 Support load — **Severity: medium, underestimated**

The free tier will generate enormous support volume from users whose _websites_ are the problem, not your platform.

**Mitigation:** the site diagnostics tool, an exceptional docs site, a public forum (Median runs one for good reason), and a "your website has issues" report generated automatically at app creation. Community support only on free tier.

---

## 19. Security, legal & compliance

### 19.1 Security requirements (non-negotiable)

| Area                    | Requirement                                                                                                                                                                                              |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Tenant isolation        | Postgres row-level security + per-tenant encryption keys + separate R2 prefixes                                                                                                                          |
| Signing material        | Vault/KMS only; never in DB, logs, or build output; ephemeral injection; full audit trail                                                                                                                |
| Build isolation         | Every build runs in a fresh container/VM, destroyed after. Never reuse a VM across tenants.                                                                                                              |
| Bridge origin allowlist | ⚠️ Bridge injected **only** on configured origins. A user navigating to a third-party page must not expose native APIs.                                                                                  |
| Log scrubbing           | Signing tools print secrets. Scrub before storage, not on display.                                                                                                                                       |
| Supply chain            | Pin every dependency, verify checksums, SBOM per build, Dependabot + review                                                                                                                              |
| Uploaded assets         | Scan icons/splashes for malicious payloads; validate image dimensions and format strictly                                                                                                                |
| Rate limiting           | Per-org on builds, previews, API. Free tier is a DoS vector and a crypto-mining vector.                                                                                                                  |
| ⚠️ Abuse                | Someone will try to build a phishing app cloning a bank's website. **You need a takedown process and a URL reputation check at app-creation time before you have your first abuse incident, not after.** |

### 19.2 Compliance roadmap

| Stage     | What                                                              | When                                           |
| --------- | ----------------------------------------------------------------- | ---------------------------------------------- |
| Day 1     | Privacy policy, ToS, DPA template, GDPR basics, cookie compliance | Pre-launch                                     |
| Month 3   | Security whitepaper describing key handling                       | Beta                                           |
| Month 9   | SOC 2 Type I                                                      | Before first enterprise deal                   |
| Month 18  | SOC 2 Type II                                                     | ⚠️ Median has this; enterprise buyers will ask |
| As needed | ISO 27001, HIPAA BAA, data residency (EU)                         | Deal-driven                                    |

Note the architectural stance Median markets: apps connect directly to the customer's web infrastructure without routing user data through the vendor. **Adopt the same posture and say so loudly** — it is genuinely true of this architecture and removes an entire class of buyer objection.

### 19.3 Legal considerations specific to this business

- ⚠️ **Guideline 4.2.6** — you must not submit apps on customers' behalf under your own account. Build the product around delegated access. (§7.2)
- ⚠️ **Apple macOS licensing** — max 2 VMs per physical Apple host. Do not exceed this; it is a licence violation and it will surface during any enterprise security review.
- **Third-party SDK licences** — several plugins require the customer's own licence (Scandit, JW Player, Zoom, Twilio, Sendbird, Intune). Make this explicit in the studio _before_ they enable the plugin.
- **Liability limits** — you cannot guarantee store approval. Say so in the ToS, then over-deliver in practice.
- **Trademark** — "Shellwright" is a placeholder. Clear the name in your target markets and check npm/GitHub/domain availability before you print anything.
- **Your jurisdiction** — you're operating from Sri Lanka selling globally; think about the billing entity, Stripe availability, and whether a merchant-of-record like Paddle is worth the fee to avoid global VAT/GST filing.

---

## 20. Roadmap

### Phase 0 — Technical spike (weeks 1–4)

**Goal: prove the pipeline works before building any product around it.**

- [ ] Hand-write a minimal Android shell (WebView + bottom tab bar + config JSON read at runtime)
- [ ] Hand-write a minimal iOS shell (same)
- [ ] Write a codegen that takes a config and produces both projects
- [ ] Build both on a laptop, then on one Linux box + one rented Mac
- [ ] Publish one throwaway app to TestFlight and Play internal testing **yourself**, end-to-end, manually

**Exit criteria:** an app you configured with JSON is running on a real phone from a store track. If this takes longer than 6 weeks, reconsider the whole project.

### Phase 1 — Private alpha (months 2–4)

- [ ] Config schema v1 + validation + versioning framework
- [ ] Control plane: auth, orgs, apps, configs, builds
- [ ] Temporal build orchestration, Linux + one Mac runner
- [ ] App Studio: branding, navigation, link rules, raw JSON editor
- [ ] Bridge SDK v1 + npm package + capability negotiation
- [ ] 6 plugins: push (OneSignal), biometrics, QR scanner, haptics, share, native datastore
- [ ] Source export
- [ ] Artifact download + QR-to-device install
- [ ] **10 hand-picked alpha users. Watch them use it. Fix what breaks.**

### Phase 2 — Public beta (months 5–8)

- [ ] Device preview (buy Appetize)
- [ ] Publishing wizard + ASC/Play API automation + TestFlight/internal track upload
- [ ] Store Readiness Score v1
- [ ] Privacy manifest + Data Safety generators
- [ ] 15 plugins total, incl. IAP, social login, deep links, document scanner, background audio
- [ ] Billing + metering + quotas
- [ ] Docs site + rejection knowledge base
- [ ] CLI v1
- [ ] **Target: 100 apps built, 10 published to a store, 10 paying customers**

### Phase 3 — Commercial v1 (months 9–14)

- [ ] Managed signing custody (Vault) + BYO signing
- [ ] First-party push service
- [ ] First-party analytics + crash reporting
- [ ] OTA bundles with staged rollout and rollback
- [ ] Declarative native surfaces (onboarding, settings, offline library)
- [ ] Agency workspace + bulk rebuild + white-label studio
- [ ] Environments, config diff, audit log
- [ ] Screenshot generator
- [ ] Own Android emulator streaming (drop Appetize for Android)
- [ ] Android developer verification onboarding
- [ ] SOC 2 Type I
- [ ] **Target: 100 paying customers, $5k MRR**

### Phase 4 — Differentiation (months 15–24)

- [ ] Declarative widgets + Live Activities
- [ ] Declarative App Intents / Shortcuts
- [ ] Offline data sync layer
- [ ] Public plugin SDK + third-party plugin ecosystem
- [ ] Self-hosted runners
- [ ] SSO/SAML/SCIM, MDM/Intune
- [ ] Alternative stores (AppGallery, Amazon, Samsung)
- [ ] Aggregator/"picker" app product line (§7.2)
- [ ] SOC 2 Type II
- [ ] **Target: $30k MRR, first enterprise contract**

### What to explicitly defer or never build

- ❌ Website builder — not your business
- ❌ Games support
- ❌ watchOS / tvOS / CarPlay / visionOS
- ❌ Full offline-first sync engine as a marketing claim (build the primitives, don't over-promise)
- ❌ A managed agency service business in year one — it will consume all your time

---

## 21. Risk register

| #   | Risk                                                                   | Likelihood               | Impact | Mitigation                                                                                                                                  |
| --- | ---------------------------------------------------------------------- | ------------------------ | ------ | ------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | ⚠️ Apple tightens 4.2 further and webview apps become broadly unviable | Low                      | Fatal  | Native surfaces, widgets, intents make apps materially non-wrapper; pivot toward enterprise/MDM/picker apps where 4.2 barely applies        |
| 2   | Mac fleet costs exceed revenue at scale                                | Medium                   | High   | Aggressive build caching, split asset/code hashes, own hardware early, meter iOS honestly                                                   |
| 3   | Security incident involving customer signing keys                      | Low                      | Fatal  | §18.2 in full; prefer delegation and Play App Signing over custody                                                                          |
| 4   | Median cuts prices or opens their free tier                            | Medium                   | High   | Your differentiation is architectural (first-party services, source ownership, native surfaces), not just price. Price alone is not a moat. |
| 5   | Support load from free tier swamps a solo founder                      | **High**                 | High   | Diagnostics tooling, excellent docs, community-only free support, readiness checks that prevent tickets                                     |
| 6   | Solo founder burnout — this is a 2-year build                          | **High**                 | Fatal  | Ruthless scope control; ship Phase 1 narrow; consider a co-founder for infra ops                                                            |
| 7   | Abuse: phishing/scam apps built on your platform                       | Medium                   | High   | URL reputation checks at creation, takedown process, KYC for publishing features                                                            |
| 8   | Android developer verification breaks free-tier APK distribution       | **Certain** (Sep 2026 →) | Medium | Build verification onboarding now; push Managed Google Play for internal apps                                                               |
| 9   | A key third-party SDK (OneSignal, RevenueCat) changes terms or breaks  | Medium                   | Medium | First-party alternatives for push and analytics reduce dependency                                                                           |
| 10  | Cannot reach enterprise buyers without SOC 2 and references            | High                     | Medium | Start SOC 2 Type I early; land education/nonprofit first as reference customers                                                             |
| 11  | Legal exposure from a customer's app content                           | Low                      | Medium | Clear ToS, acceptable use policy, takedown process, no submission on their behalf                                                           |

---

## 22. Success metrics

| Stage | Metric                                    | Target       |
| ----- | ----------------------------------------- | ------------ |
| Alpha | Time from signup → first successful build | < 10 minutes |
| Alpha | Build success rate (valid configs)        | > 95%        |
| Beta  | Signup → app on a store track             | < 7 days     |
| Beta  | Free → paid conversion                    | > 4%         |
| Beta  | Store approval rate, first submission     | > 70%        |
| V1    | Median iOS build time                     | < 8 minutes  |
| V1    | Build cache hit rate                      | > 50%        |
| V1    | Support tickets per active app per month  | < 0.3        |
| V1    | Gross margin                              | > 75%        |
| V1    | Net revenue retention                     | > 100%       |
| V1    | Monthly logo churn                        | < 3%         |

**The single metric that matters most:** _store approval rate on first submission._ It drives word of mouth, support cost, and churn simultaneously. Instrument it from day one.

---

## 23. Open decisions

Decide these before writing production code.

| #   | Decision                  | Options                               | Lean                                                                                                                         |
| --- | ------------------------- | ------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| 1   | Control plane language    | .NET / TypeScript / Go                | **.NET** — your strength, and orchestration-friendly                                                                         |
| 2   | Build orchestration       | Temporal / Hangfire / custom          | **Temporal** — worth the learning curve                                                                                      |
| 3   | Mac strategy              | Own / hosted / cloud                  | **Hosted for alpha → owned at ~150 builds/day**                                                                              |
| 4   | Device preview            | Build / buy Appetize                  | **Buy for v1, build Android later**                                                                                          |
| 5   | Signing model default     | Custody / delegation / BYO            | **Delegation default, custody opt-in, BYO always available**                                                                 |
| 6   | Free tier ceiling         | Generous / restrictive                | **Generous — it's the entire strategy**                                                                                      |
| 7   | Managed publishing in v1? | Yes / no                              | **No** — it will eat all your time. Add in Phase 3.                                                                          |
| 8   | Launch plugin count       | 6 / 15 / 40                           | **15**                                                                                                                       |
| 9   | Open-source the shells?   | Yes / no / partial                    | **Partial** — open-source the bridge SDK and plugin format for ecosystem and trust; keep the shells and build service closed |
| 10  | Billing entity & tax      | Own entity + Stripe / Paddle MoR      | **Paddle initially** — avoid global VAT filing while solo                                                                    |
| 11  | Product name              | —                                     | Clear trademark, domain, npm scope before launch                                                                             |
| 12  | First vertical to target  | Indie/AI-builder / agency / ecommerce | **Indie/AI-builder for volume, agency for revenue — run both**                                                               |

---

# Appendices

## Appendix A — Plugin manifest schema (draft)

```yaml
id: qr-scanner
name: QR / Barcode Scanner
version: 1.2.0
category: scanning
tier: free # always free in our model; field retained for private plugins
description: Scan QR codes and barcodes using the device camera.

capabilities:
  - qrScanner.scan
  - qrScanner.scanContinuous

configSchema: # rendered as a form in App Studio
  type: object
  properties:
    formats:
      type: array
      items: { enum: [qr, ean13, ean8, code128, code39, pdf417, dataMatrix] }
      default: [qr, ean13, code128]
    beepOnScan: { type: boolean, default: true }
    torchButton: { type: boolean, default: true }

ios:
  minVersion: '15.0'
  dependencies:
    spm:
      - { url: 'https://github.com/...', from: '3.0.0' }
  frameworks: [AVFoundation, Vision]
  infoPlist:
    NSCameraUsageDescription:
      default: 'Scan QR codes and barcodes.'
      localizable: true
  privacyManifest:
    accessedAPITypes: []
    collectedDataTypes: []
  sources: ios/

android:
  minSdk: 24
  dependencies:
    gradle:
      - 'com.google.mlkit:barcode-scanning:17.3.0'
  permissions: [android.permission.CAMERA]
  features:
    - { name: android.hardware.camera, required: false }
  proguard: android/proguard-rules.pro
  sources: android/

dataSafety: # Play declaration fragment
  collectsData: false

web:
  typings: web/index.d.ts
  entrypoint: web/index.ts

conflicts:
  - plugin: scandit-scanner
    reason: 'Both register a camera scanning surface.'

docs: docs/index.md
```

## Appendix B — `appconfig.json` skeleton (draft)

```jsonc
{
  "$schema": "https://schema.shellwright.dev/v1.json",
  "schemaVersion": 1,
  "app": {
    "name": "Acme",
    "bundleId": "com.acme.app",
    "versionName": "1.4.0",
    "versionCode": 42,
    "initialUrl": "https://app.acme.com",
    "allowedOrigins": ["https://app.acme.com", "https://acme.com"],
  },
  "branding": {
    "icon": "assets/icon-1024.png",
    "splash": {
      "backgroundColor": "#0B1220",
      "logo": "assets/logo.svg",
      "dark": { "backgroundColor": "#000000" },
    },
    "theme": {
      "primary": "#2563EB",
      "navBar": "#FFFFFF",
      "tabBar": "#FFFFFF",
      "statusBar": "dark-content",
    },
    "darkMode": "system",
  },
  "navigation": {
    "topBar": { "enabled": true, "titleSource": "documentTitle", "actions": ["share", "refresh"] },
    "tabBar": {
      "enabled": true,
      "items": [
        { "label": "Home", "icon": "home", "url": "/", "activePattern": "^/$" },
        { "label": "Orders", "icon": "package", "url": "/orders", "activePattern": "^/orders" },
        { "label": "Account", "icon": "user", "url": "/account", "activePattern": "^/account" },
      ],
    },
    "drawer": { "enabled": false },
  },
  "linkRules": [
    { "pattern": "^https://app\\.acme\\.com", "action": "internal" },
    { "pattern": "^https://help\\.acme\\.com", "action": "readerModal" },
    { "pattern": ".*", "action": "externalBrowser" },
  ],
  "webOverrides": {
    "userAgentSuffix": "AcmeApp/1.4.0",
    "headers": { "X-Client": "mobile-app" },
    "injectCss": "assets/app.css",
    "injectJs": "assets/app.js",
    "persistCookies": true,
  },
  "offline": { "enabled": true, "fallbackBundle": "auto", "offlinePage": "assets/offline.html" },
  "nativeSurfaces": [
    {
      "type": "onboarding",
      "showOnce": true,
      "slides": [
        /* ... */
      ],
    },
    {
      "type": "settings",
      "sections": [
        /* ... */
      ],
    },
  ],
  "plugins": {
    "push": { "provider": "shellwright", "promptOnLaunch": false },
    "biometric": { "promptReason": "Unlock Acme" },
    "qr-scanner": { "formats": ["qr", "ean13"], "beepOnScan": true },
    "haptics": {},
    "iap": { "productsUrl": "https://app.acme.com/iap-products.json" },
  },
  "ota": { "enabled": true, "channel": "production", "rolloutPercent": 100 },
  "deepLinks": { "universalLinks": ["app.acme.com"], "customScheme": "acme" },
  "permissions": {
    "camera": true,
    "location": "whenInUse",
    "notifications": true,
    "contacts": false,
  },
  "build": { "toolchain": { "xcode": "26.1", "agp": "8.9", "targetSdk": 36 } },
}
```

## Appendix C — Bridge API surface (v1 draft)

```ts
import sw from '@shellwright/bridge';

// Environment
await sw.capabilities(); // { biometric: ['authenticate'], qrScanner: [...], ... }
sw.isNativeApp; // boolean, sync
await sw.device.info(); // { platform, osVersion, model, appVersion, installId, locale }

// UI
await sw.ui.setStatusBar({ style: 'light', color: '#000' });
await sw.ui.setOrientation('portrait');
await sw.ui.setTitle('Orders');
await sw.ui.setTabBadge('orders', 3);
await sw.ui.showToast({ message: 'Saved' });
await sw.ui.haptic('success');

// Navigation
await sw.nav.push('/orders/123');
await sw.nav.openModal({ url: '/help', style: 'sheet' });
sw.nav.onBackPressed(handler); // returns unsubscribe

// Device
await sw.share({ title, text, url });
await sw.clipboard.write('...');
await sw.clipboard.read();
await sw.browser.open(url, { presentation: 'reader' });

// Storage
await sw.storage.set('key', value, { secure: true });
await sw.storage.get('key');

// Plugins
await sw.biometric.authenticate({ reason: 'Unlock' });
await sw.qrScanner.scan({ formats: ['qr'] });
await sw.push.register();
await sw.push.setTags({ plan: 'pro' });
await sw.iap.purchase('pro_monthly');
await sw.iap.restore();

// Events
sw.on('appResumed', h);
sw.on('appPaused', h);
sw.on('connectivityChanged', h); // { online: boolean, type: 'wifi'|'cellular'|'none' }
sw.on('keyboardChanged', h); // { visible: boolean, height: number }
sw.on('pushReceived', h);
sw.on('deeplinkOpened', h);
sw.on('otaUpdateAvailable', h);
```

## Appendix D — Glossary

| Term        | Meaning                                                               |
| ----------- | --------------------------------------------------------------------- |
| **AAB**     | Android App Bundle — the required Play upload format                  |
| **AASA**    | apple-app-site-association — file enabling iOS Universal Links        |
| **ASC**     | App Store Connect (and its API)                                       |
| **Bridge**  | JS↔native messaging layer injected into the WebView                  |
| **IPA**     | iOS application archive                                               |
| **MAM/MDM** | Mobile Application / Device Management                                |
| **OTA**     | Over-the-air update of the web bundle, no store review                |
| **Shell**   | The native app that hosts the WebView and native chrome               |
| **Tart**    | Open-source macOS/Linux VM manager for Apple Silicon                  |
| **TWA**     | Trusted Web Activity — Android's Chrome-backed PWA container          |
| **4.2**     | Apple App Store Review Guideline on minimum functionality             |
| **4.2.6**   | Apple guideline on commercialized templates / app generation services |

---

## Final note

The three sentences that matter most in this document:

1. **Median's gating is pricing power, not cost recovery** — roughly 85% of what they charge for costs them nothing per user, and that is your entire wedge.
2. **⚠️ Guideline 4.2.6 means you can never publish on a customer's behalf** — design the product around delegated access from day one, and treat Apple's blessed aggregator/"picker" model as a separate product line.
3. **The macOS build fleet is the business.** Everything else is software you can write. That fleet is procurement, licensing, physical operations, and an annual forced migration — and it is the moat, because it is the part a competitor cannot clone in a weekend.

Build Phase 0 before you build anything else. If a JSON file can't put a working app on your own phone within six weeks, the rest of this document is premature.
