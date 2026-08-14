import { readFileSync, writeFileSync, existsSync } from "node:fs";
import { join } from "node:path";
import type { LicenseConsent } from "@shared/protocol";

const CONSENT_VERSION = 1;
let consentPath = "";

export function initLicense(userDataPath: string): void {
  consentPath = join(userDataPath, "license-consent.json");
}

export function getConsent(): LicenseConsent | null {
  if (!existsSync(consentPath)) {
    return null;
  }
  try {
    const data = readFileSync(consentPath, "utf-8");
    const consent = JSON.parse(data) as LicenseConsent;
    // Re-show consent if version changed
    if (consent.consentVersion !== CONSENT_VERSION) {
      return null;
    }
    return consent;
  } catch {
    return null;
  }
}

export function acceptConsent(type: "nonprofit" | "commercial", licenseKey?: string): void {
  const consent: LicenseConsent = {
    consentVersion: CONSENT_VERSION,
    acceptedAt: new Date().toISOString(),
    type,
    licenseKey: licenseKey ?? null,
  };
  writeFileSync(consentPath, JSON.stringify(consent, null, 2), "utf-8");
}