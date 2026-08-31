# Store submission dry run

Run before every release. ⚠️ Each item below has rejected a real app in this
category. Checking them costs minutes; a rejection costs a review cycle.

## Both stores

- [ ] Screenshots present at every required size
- [ ] App name is 30 characters or fewer and matches `app.name`
- [ ] Icon is 1024x1024, square, **no alpha channel**
- [ ] Age rating answered
- [ ] Support and privacy policy URLs resolve over https

## Apple

- [ ] ⚠️ Guideline 4.2: the app does more than reframe a website. Native
      navigation, at least one real capability, and at least one native screen.
      `CFG_NO_NATIVE_FEATURES` warns about this at config time — do not ship
      past it.
- [ ] ⚠️ Guideline 4.2.6: we are not submitting on a customer's behalf. The
      customer submits from their own account, using delegated access.
- [ ] Privacy manifest present and matches the plugins actually enabled
- [ ] Every permission string is specific about why, in the user's language
- [ ] Export compliance answered
- [ ] Sign in with Apple offered wherever another social login is
- [ ] App Tracking Transparency prompt shown before any tracking

## Google

- [ ] Data safety form matches what the plugins actually collect
- [ ] Target API level meets the current requirement
- [ ] AAB uploaded, not an APK
- [ ] Developer verification complete
- [ ] DSA trader status declared

## After submission

- [ ] Rejection reasons, if any, recorded in `docs/ops/store-rejections.md` —
      each one becomes a Readiness Score rule in Sprint 16
