-- Add network port exposure violation rule
INSERT INTO policy_rules (rule_id, severity, description) VALUES
('network.risky_port_exposed', 'high',
 'High-risk port (e.g. 135/139/445/3389/5985) is actively listening AND exposed via an inbound Allow firewall rule')
ON CONFLICT (rule_id) DO NOTHING;
