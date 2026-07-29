-- Runtime ownership is deliberately separate from the migration identity.
DO $ownership$
DECLARE schema_name text;
DECLARE object_record record;
BEGIN
  FOREACH schema_name IN ARRAY ARRAY['identity','journal','market','market_data_public','tool'] LOOP
    EXECUTE format('ALTER SCHEMA %I OWNER TO trade_diary_migrator', schema_name);
    FOR object_record IN
      SELECT c.relkind, c.relname
      FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname = schema_name AND c.relkind IN ('r','p','v','m','S','f')
    LOOP
      EXECUTE format(
        'ALTER %s %I.%I OWNER TO trade_diary_migrator',
        CASE object_record.relkind
          WHEN 'S' THEN 'SEQUENCE'
          WHEN 'v' THEN 'VIEW'
          WHEN 'm' THEN 'MATERIALIZED VIEW'
          WHEN 'f' THEN 'FOREIGN TABLE'
          ELSE 'TABLE'
        END,
        schema_name, object_record.relname
      );
    END LOOP;
    FOR object_record IN
      SELECT p.oid::regprocedure AS signature
      FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
      WHERE n.nspname = schema_name
    LOOP
      EXECUTE format('ALTER FUNCTION %s OWNER TO trade_diary_migrator', object_record.signature);
    END LOOP;
  END LOOP;
END $ownership$;

DO $revoke$
DECLARE role_name text;
DECLARE schema_name text;
BEGIN
  FOREACH role_name IN ARRAY ARRAY['identity_service','journal_service','market_data_service','tool_service'] LOOP
    FOREACH schema_name IN ARRAY ARRAY['identity','journal','market','market_data_public','tool'] LOOP
      EXECUTE format('REVOKE ALL ON SCHEMA %I FROM %I', schema_name, role_name);
      EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %I FROM %I', schema_name, role_name);
      EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %I FROM %I', schema_name, role_name);
      EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA %I FROM %I', schema_name, role_name);
    END LOOP;
  END LOOP;
END $revoke$;

GRANT USAGE ON SCHEMA identity TO identity_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity TO identity_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA identity TO identity_service;

GRANT USAGE ON SCHEMA journal TO journal_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA journal TO journal_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA journal TO journal_service;

GRANT USAGE ON SCHEMA tool TO tool_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA tool TO tool_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA tool TO tool_service;

GRANT USAGE ON SCHEMA market TO market_data_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON market.instruments, market.instrument_symbol_history, market.symbols, market.provider_runs, market.daily_bars TO market_data_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA market TO market_data_service;
GRANT SELECT ON market.published_symbols_v1, market.published_daily_bars_v1, market.published_provider_health_v1 TO market_data_service;
GRANT USAGE ON SCHEMA market_data_public TO market_data_service;
GRANT SELECT ON market_data_public.adjusted_daily_bars_v1, market_data_public.daily_bar_prices_v1 TO market_data_service;

ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA identity GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO identity_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA identity GRANT USAGE,SELECT ON SEQUENCES TO identity_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA journal GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO journal_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA journal GRANT USAGE,SELECT ON SEQUENCES TO journal_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA tool GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO tool_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA tool GRANT USAGE,SELECT ON SEQUENCES TO tool_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA market GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO market_data_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA market GRANT USAGE,SELECT ON SEQUENCES TO market_data_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA market_data_public GRANT SELECT ON TABLES TO market_data_service;

REVOKE CREATE ON DATABASE trade_diary FROM identity_service,journal_service,market_data_service,tool_service;
REVOKE ALL ON SCHEMA platform_migrations FROM identity_service,journal_service,market_data_service,tool_service;
REVOKE ALL ON platform_migrations.schema_history FROM identity_service,journal_service,market_data_service,tool_service;
