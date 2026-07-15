-- Run as the database administrator before migrations.
DO $roles$
DECLARE role_name text;
BEGIN
  FOREACH role_name IN ARRAY ARRAY[
    'trade_diary_migrator',
    'identity_service','journal_service','performance_service','discipline_service',
    'reminder_service','market_data_service','price_alert_service','rotation_service',
    'stock_research_service','partner_service','content_service','operations_service'
  ] LOOP
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = role_name) THEN
      EXECUTE format('CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT', role_name);
    ELSE
      EXECUTE format('ALTER ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT', role_name);
    END IF;
  END LOOP;
END $roles$;

GRANT CONNECT, CREATE ON DATABASE trade_diary TO trade_diary_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM trade_diary_migrator;
