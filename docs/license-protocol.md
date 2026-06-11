# License Protocol

Product names:

- Chinese: `窗巡`
- English: `Window Sentinel`

## Local License Code

The app accepts an encrypted license code string:

```text
WML1.<base64url(nonce + tag + ciphertext)>
```

Encryption:

- AES-GCM
- 12-byte nonce
- 16-byte tag
- UTF-8 JSON payload
- Key is injected at build time with `-LicenseCryptoKeyBase64`

Payload example before encryption:

```json
{
  "licenseId": "LIC-202606110001",
  "licenseType": "yearly",
  "edition": "Professional",
  "deviceHash": "WM-0000-0000-0000-0000-0000-0000",
  "features": ["window-title", "ocr", "taskbar-flash", "notifications", "updates"],
  "issuedAt": "2026-06-11T00:00:00Z",
  "expiresAt": "2027-06-11T00:00:00Z",
  "nonce": "random-per-license"
}
```

Generate a local code for testing:

```powershell
.\tools\new-license-code.ps1 -MachineCode "WM-xxxx-xxxx-xxxx-xxxx-xxxx-xxxx" -LicenseType yearly
```

## Remote Validation

The validation URL is injected at build time:

```powershell
.\build.ps1 -Version 0.1.0 -LicenseValidationUrl "https://example.com/api/license/check"
```

The app sends a POST request every startup and once per hour:

```json
{
  "licenseCode": "WML1....",
  "machineCode": "WM-....",
  "nonce": "client-random-nonce",
  "clientVersion": "0.1.0.0",
  "product": "窗巡 Window Sentinel"
}
```

The service returns either plain encrypted text or `{ "response": "WML1...." }`.

Remote response JSON before encryption:

```json
{
  "nonce": "same-client-random-nonce",
  "serverUtc": "2026-06-11T00:00:00Z",
  "valid": true,
  "revoked": false,
  "expiresAt": "2027-06-11T00:00:00Z",
  "message": "ok"
}
```

Rules:

- The app first checks local license expiration from the decrypted license payload.
- If the local license is still usable, the app then asks the remote service whether the license is revoked or overridden.
- If the remote URL is unavailable, the app keeps the local validation result and does not show a remote failure state.
- If the remote service returns `valid=false` or `revoked=true`, all monitoring features are locked.
