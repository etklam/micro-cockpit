DO $allowlist$
BEGIN
  IF (SELECT count(*) FROM role_credentials) <> 14 OR EXISTS (
    SELECT 1 FROM role_credentials WHERE role_name <> ALL (ARRAY[
      'trade_diary_migrator','identity_service','journal_service','performance_service',
      'discipline_service','reminder_service','market_data_service','price_alert_service',
      'rotation_service','stock_research_service','partner_service','content_service','tool_service','operations_service'
    ])
  ) THEN
    RAISE EXCEPTION 'Unexpected database role identifier';
  END IF;
END $allowlist$;

SELECT format('ALTER ROLE %I PASSWORD %L', role_name, convert_from(decode(encoded_password, 'base64'), 'UTF8'))
FROM role_credentials
\gexec
