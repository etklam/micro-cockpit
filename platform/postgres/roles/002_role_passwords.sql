DO $allowlist$
BEGIN
  IF (SELECT count(*) FROM role_credentials) <> 5 OR EXISTS (
    SELECT 1 FROM role_credentials WHERE role_name <> ALL (ARRAY[
      'trade_diary_migrator','identity_service','journal_service','market_data_service','tool_service'
    ])
  ) THEN
    RAISE EXCEPTION 'Unexpected database role identifier';
  END IF;
END $allowlist$;

SELECT format('ALTER ROLE %I PASSWORD %L', role_name, convert_from(decode(encoded_password, 'base64'), 'UTF8'))
FROM role_credentials
\gexec
