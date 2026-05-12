-- Policy baseline rules (customizable)
CREATE TABLE policy_rules (
    id          SERIAL PRIMARY KEY,
    rule_id     TEXT NOT NULL UNIQUE,
    severity    TEXT NOT NULL CHECK (severity IN ('critical','high','medium','low')),
    description TEXT NOT NULL,
    enabled     BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Seed default rules
INSERT INTO policy_rules (rule_id, severity, description) VALUES
('password.min_length',   'high',     'Password minimum length < 8'),
('password.complexity',   'high',     'Password complexity disabled'),
('password.max_age',      'medium',   'Password max age > 180 days or no expiry'),
('password.lockout',      'high',     'Account lockout threshold = 0 (no lockout)'),
('audit.logon',           'high',     'Logon/Logoff audit not including Failure'),
('firewall.disabled',     'critical', 'Firewall disabled on any profile'),
('defender.realtime',     'critical', 'Windows Defender real-time protection disabled'),
('uac.disabled',          'critical', 'User Account Control (UAC) disabled'),
('bitlocker.os_volume',   'high',     'OS volume (C:) not fully encrypted'),
('tls.weak_protocol',     'high',     'Weak TLS protocol enabled (TLS 1.0 or SSL 3.0)'),
('rdp.nla_disabled',      'high',     'RDP enabled without Network Level Authentication'),
('gpo.not_applied',       'medium',   'Expected GPO not applied'),
('agent.offline',         'medium',   'Agent has not checked in for > 24 hours')
ON CONFLICT (rule_id) DO NOTHING;
