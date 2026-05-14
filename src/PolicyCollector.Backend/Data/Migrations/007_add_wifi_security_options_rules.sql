-- Migration 007: policy rules for WiFi encryption and Security Options

INSERT INTO policy_rules (rule_id, severity, description, enabled) VALUES
  ('wifi.insecure_profile',         'critical', 'WiFi profile without proper encryption detected (Open/WEP/WPA v1) — traffic can be intercepted', true),
  ('security.wdigest_enabled',      'critical', 'WDigest authentication stores credentials in cleartext in LSASS memory (UseLogonCredential=1)', true),
  ('security.ntlm_level_weak',      'high',     'LAN Manager authentication level allows weak LM/NTLMv1 — vulnerable to relay and offline cracking', true),
  ('security.lsa_ppl_disabled',     'medium',   'LSA Protection (RunAsPPL) is not enabled — credential dumping tools can read LSASS without a kernel driver', true),
  ('security.smb_signing_disabled', 'high',     'SMB signing is not required on client or server — vulnerable to NTLM relay attacks (e.g. Responder)', true)
ON CONFLICT (rule_id) DO NOTHING;
